# Project Charm — Attribute Meaning (the findings layer)

**What this is.** A record of what each rating actually *does* across its 0–99 range, measured
by running the **real possession engine** — not asserted, not hand-calculated. Every number here
was produced by the `sweep` bench (see design.md, "The attribute-sweep findings bench"): a flat
all-50 world, one rating walked up its range on one player, N seeded games per rung, real
outcomes tabulated. This is the currency the generation-tuning questions ("is a 50 in this rating
good? are tall players too skilled?") had no way to answer, because "good" had no definition.

**How to read it.** Each family section records the finding in plain terms, the shape of the
curve (where the rating does nothing / kicks in / saturates), any engine flaw the measurement
exposed, and any ruling or generation-layer note that fell out. Findings are descriptive of the
engine *as it is now*; when a later session changes the engine, the affected finding is
re-measured and this doc is updated in place (journal.md holds the history).

**Method note — measurement, not tuning.** A sweep run never changes an engine constant. A "the
balance should move" conclusion is a finding recorded here for a future build session, not a
change made in the sweep session.

---

## Family B — Rebounding (OffensiveRebounding, DefensiveRebounding) — measured Session 45

The first family measured. Two ratings, and it carried a sharp test: **is rebounding made
useless by the physical attributes, or is it a real skill?** Run as two isolated sweeps
(OffReb 0→99, DefReb 0→99, 5-point steps, everything else pinned at 50) plus an eight-row
interaction block that deliberately moves body and rebounding together. 2,000 games per rung.

### The headline finding

**Rebounding is a rating-gated skill. The body amplifies it but does not grant it.** The swept
center's total boards per game:

| The center | Boards/game |
|---|---|
| 5'6" weakling (body 15), **no** rebounding instinct (rating 0) | 0.2 |
| Average body (50), **no** instinct (0) | 0.2 |
| 7-foot freak (body 99), **no** instinct (0) | **0.2** |
| 5'6" weakling, **elite** instinct (99) | 9.6 |
| Average body, **elite** instinct | 12.9 |
| 7-foot freak, **elite** instinct | 17.3 |
| Average body, average instinct (the flat-50 baseline) | 7.7 |

Two things to read off it:

1. **The rating is the switch.** The top three rows are identical at 0.2 — a 7-foot freak with no
   rebounding rating grabs the same near-nothing a 5'6" weakling does. Body alone gives a player
   almost no *individual* boards. The head-to-head Emmett asked for isn't close: little-guy-with-
   great-hands **9.6** vs freak-body-no-hands **0.2**.
2. **The body is a real amplifier once the rating is present.** Down the elite-instinct rows,
   9.6 → 12.9 → 17.3: a bigger frame converts the same rebounding rating into ~34% more boards
   (average → freak). With no rating there is nothing to amplify (0.2 either way). This is the
   multiplicative "weakest-leg" relationship the design aimed for, working: the rating decides
   whether you rebound at all; the body scales it once you do.

### The curves (isolated sweeps)

Both ratings pay off **continuously and near-linearly across the whole 0–99 range** — no dead
zone at the bottom, no saturation at the top. The swept center's own boards per game:

- **OffensiveRebounding:** 0.06 (at 0) → 2.20 (at 50) → 3.66 (at 99). Team offensive-rebound rate
  moves modestly (27.6% → 28.7%) because the swept player is one of five potential rebounders.
- **DefensiveRebounding:** 0.13 (at 0) → 5.49 (at 50) → 9.30 (at 99). Slightly steeper; team
  defensive-rebound rate 70.9% → 72.6%.

The near-zero floor at rating 0 (0.06 / 0.13, not literally 0) is the picker's floor-of-1 term,
explained below — it is the whole reason the freak-no-hands case is 0.2 and not higher.

### The flaw the finding exposed — FIXED in S46

A rebound resolves in **two engine steps**, and before S46 they behaved oppositely:

1. **Which TEAM gets the board** (`OffensiveReboundShare`): blends team-mean body (45%) and
   team-mean rating (55%). Here the freak's body works exactly right — a freak-body/zero-rating
   center lifts his team's rebound margin to **+2.1**. His body IS real in the team battle. (Unchanged
   by S46 — the 55/45 split was ruled correct and left alone.)
2. **Which of the five PLAYERS is credited** (`OffensiveRebounderPicker` /
   `DefensiveRebounderPicker`): *before S46* each player's pick weight was
   `max(1, Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier)` — the body entered
   **only as a multiplier on the rating**, so a zero-rating freak's product was zero, floored to 1, and
   he drew ≈1/200 of his team's boards. His body helped the team win the board but gave *him* no claim.
   That was the entire 0.2.

**The S46 fix (the block-picker's additive shape) gave the body two standalone channels.** The pick
weight is now `Luck + Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier
+ BodyPull × max(0, ReboundPhysical − lineupMean)
+ FloorCeiling × tanh(max(0, ReboundPhysical − FloorReference) / FloorScale)` (the ORB side × the
shooter nerf on the whole weight). **Luck** (5.0) is every slot's equal claim on random bounces — it
replaced the retired floor-of-1, so an inert player collects the garbage boards a body-blind floor
should give (weakling-no-hands ≈0.7, average-no-hands ≈1.3). **Body pull** (0.35, *relative*) rewards
out-sizing your own lineup; **body floor** (ceiling 4.0 / scale 40 / reference 22.5, *absolute* and
*saturating*) rewards raw size against a fixed reference — a big target loose balls find regardless of
teammates — tanh-capped so a genuine big doesn't balloon. The absolute floor was added (S46b) because
the relative pull alone left an average body tied with a small one (both sit at their lineup mean and
earn nothing from a relative term); the floor un-flattens the mushy bottom of the zero-rating height
ladder into a clean rise (5'8 ≈1.2 → 6'0 ≈1.6 → 6'4 ≈2.2 → 7'3+ ≈4.9).

**The head-to-head, after S46 (swept center total boards/game, `sweep` interaction, 2,000 games/rung):**

| The center | S45 (before) | S46 (after) |
|---|---|---|
| 7-foot freak, **no** instinct (rating 0) | 0.2 | **4.86** |
| average body, **no** instinct | 0.2 | 1.32 |
| 5'6" weakling, **no** instinct | 0.2 | 0.67 |
| 7-foot freak, **elite** instinct | 17.3 | 17.54 |
| average body, **elite** instinct | 12.9 | 12.27 |
| 5'6" weakling, **elite** instinct | 9.6 | 8.65 |

The freak's body now earns him boards on its own (0.2 → 4.86); a bigger body separates cleanly from a
smaller one even at zero rating; and the elite anchors held (the weakling-elite slipped 9.6 → 8.65 by
ruling — the structural cost of letting average bodies compete). Team margins are unchanged (freak-
no-hands still +2.1) — the fix was pure individual attribution, not team-total movement. This was the
one and only per-player selector where a body attribute that should confer standalone credit was wired
multiplicatively; the cross-selector audit below confirmed no other picker needs it.

### The cross-selector audit (all seven pickers, S45)

The floor-of-1 shape (`max(1, base × multipliers)`) repeats, but the shape is only *wrong* when the
thing being multiplied is a body attribute that should stand on its own. That is true for exactly
one stat.

| Counting stat | Should a pure freak body earn this individually? | Body given standalone pull in the selector? | Verdict |
|---|---|---|---|
| **Rebounds** (O + D) | **Yes** — a giant corrals boards at the rim | **No** — body only multiplies the rating; zero rating floors out | **Broken** |
| **Blocks** | Yes — length + hops swat shots | **Yes** — Height, Wingspan, Vertical are *added in* on their own | Correct (**the template**) |
| Steals | No — hands and anticipation, not size | n/a — base is the steal rating, no body term denied | Correct |
| Assists | No — passing, not size | n/a — passing-driven | Correct |
| Turnovers (lost ball) | No — handling; a *blame* stat | n/a | Correct |
| Interior turnovers | Body already *is* the driver (Strength-based) | Yes — Strength is the base | Correct |

**Blocks are the correct template.** `BlockerWeight` is additive:
`BlkHeight·Height + BlkWingspan·Wingspan + BlkVertical·Vertical + BlkRimProtection·RimProtection + …`
— so a 7-foot freak with a zero shot-blocking rating still swats shots on body alone. Blocks and
rebounds are the two stats where a big body should earn standalone individual credit, and the engine
wired them **oppositely**. The other selectors look structurally identical but aren't a problem,
because steals/assists/turnovers *shouldn't* hand a giant free credit for being tall.

### Rulings and notes that fell out

- **Ruling: the 55/45 rebound team split stays.** It delivers the design goal — rebounding is a
  genuine skill regardless of size. The culprit was the picker's body-as-multiplier, not the team
  blend. (Recorded in status.md, Closed-by-ruling.)
- **The fix shipped in S46 (single and well-scoped):** the two rebounder pickers now carry the block
  picker's additive body shape plus a luck weight and a saturating loose-ball floor (see the FIXED
  section above). Validated on the `sweep` bench — freak-no-hands 0.2 → 4.86, elite anchors held,
  average-no-hands (1.32) cleanly above weakling-no-hands (0.67). The signed sign-off was against the
  archetype table (round 1) and the full zero-rating height ladder (round 2, the S46b floor).
- **Generation-layer corollary — RE-EVALUATE after S46 (the passive-floor picture changed).** The
  S45 note said a tall player's board count lives or dies on his rebounding rating, so the generation
  redesign should give tall players a rebounding-rating floor. **S46 changed this:** a body now confers
  standalone individual rebounding pull independent of rating (the additive pull + the saturating floor),
  so a big with a low rebounding draw no longer rebounds like a guard on body alone. Re-measure the
  passive-floor picture on the live pickers **before** sizing any generation-layer rebounding floor —
  it may now be smaller than the S45 note assumed, or unnecessary.

---

## Family D — Scoring (Close, Mid, Outside, Finishing, FreeThrow, FoulDrawing) — measured Session 48

The first measurement taken with the S47-generalized full-box ruler, and the proof that "every
future family is pure config-and-run" is real: seven text configs, zero engine/readout/code
changes, 266,000 real games (six isolation walks 0→99 in 5-point steps + a seven-row interaction
block, 2,000 games/rung, swept slot 5, everything else frozen at 50). SelfCreation was ruled OUT
at the gate — its only direct engine read is the Roll E bonus-FT-putback shooter picker, no
make-curve role — so it is measured with Family E (perimeter creation), not here.

**The grain (S47 ruling, applies to every number below).** The swept player's aggregate FG%,
3P%, FT%, FTr, and PossessionUse are *his*; the Rim/Short/Mid/Long zone FG% lines are **Team A
level** — a directional proxy diluted by four flat-50 teammates, informative because only the
swept slot is dialed but never individual attribution. His personal zone curves are therefore
steeper than the team lines shown.

### The gates — the instrument re-proved itself beyond spec

- **Determinism, seven ways.** All six walks' 50-rungs and FLAT_50_CONTROL are identical to three
  decimals (9.364 PTS / 34.33 FG% / 49.75 FT%) — seven independent runs, one answer.
- **FT linearity, exact.** FT% tracks the rating to within 0.74pp at worst across all 21 rungs
  (0 → 0.0%, 50 → 49.8%, 99 → 99.0%).
- **Control parity.** Team A ≈ Team B at control (52.8 / 52.9 PTS).
- **Rebound ripple, right direction everywhere.** Total boards drift down as any make rating
  climbs (makes end possessions), including the FreeThrow signature: at FT=0 the opponent's
  total-rebound line is *elevated* (39.3 vs 38.7 baseline) because every bricked free throw is a
  live rebound chance; at FT=99 it falls to 37.9.
- **Nothing leaked to defense.** STL/BLK flat under every scoring rating; Team B's box flat under
  every walk.

### The curves (0 → 50 → 99)

| Rating | His PTS/g | Direct line | His use share |
|---|---|---|---|
| **Close** | 7.4 → 9.4 → 13.2 | Team Short FG% 32.7 → 34.2 → 40.5 | 14.4% → 22.3% |
| **Mid** | 7.6 → 9.4 → 12.8 | Team Mid FG% 27.1 → 27.9 → 34.5 | 14.5% → 22.2% |
| **Outside** | 7.2 → 9.4 → 15.1 | **Personal 3P% 17.2 → 23.2 → 44.8**; team Long 24.8 → 31.1 + Three 22.8 → 28.7 (one rating, two zones, as wired) | 14.1% → 22.8% |
| **Finishing** | 8.0 → 9.4 → 12.5 | Team Rim FG% 52.8 → 54.5 → 59.7 | 16.3% → 19.9% |
| **FreeThrow** | 7.9 → 9.4 → 11.0 | Personal FT% ≈ rating (pure linear) | flat 18.2% |
| **FoulDrawing** | 9.2 → 9.4 → 9.8 | FTA 2.65 → 3.11 → 4.20; FTr 0.246 → 0.291 → 0.406 | flat |

Per-rating notes:

- **Outside is the biggest single lever** (7.2 → 15.1 PTS) because it owns two zones. The make
  curve's floor is visible at the bottom: a 0-rated shooter still converts ~17% of threes against
  even defense — rating 0 means bad, not hopeless, consistent with the standing "open-only, not a
  non-shooter" ruling.
- **FreeThrow is the one true zero.** The FT make is pure `rating/100`, so FreeThrow 0 made
  literally 0.0% over 2,000 games. Every make curve has a floor; FT does not. The full linear
  line is now charted for the parked FT-curve calibration ruling (see status.md).
- **FoulDrawing buys trips, never makes** — and the fouled-miss mechanism is visible in the raw
  counts: his FGA *falls* 10.75 → 10.34 across the walk (a `MissFouled` shot is excluded from
  FGA, a fouled missed three from 3PA), which is exactly why recorded FG%/3P% tick up ~+0.5pp
  compositionally with the make probability untouched.
- **Finishing** also carries the putback-finish channel and shows the smallest usage response
  (16.3 → 19.9 vs ~8pp for the other zone skills) — plausibly because rim volume partly flows
  through putbacks and transition rather than half-court selection. Recorded as an observation;
  the selection site was not opened this session.

### The interaction block (seven rows, all dials on the one swept player)

| Row | Dials | PTS | FG% | 3P% | FT% | FTr | Use |
|---|---|---|---|---|---|---|---|
| FLAT_50_CONTROL | — | 9.4 | 34.3 | 23.2 | 49.8 | .291 | 18.2% |
| ELITE_SHOOTER | Out 90, Mid 70 | 14.8 | 41.4 | 39.2 | 49.4 | .253 | 23.6% |
| ELITE_RIM | Fin 90, Close 70 | 12.9 | 41.3 | 25.0 | 50.0 | .287 | 21.4% |
| FREE_POINTS | FD 90, FT 90 | 11.3 | 34.5 | 23.5 | 89.8 | .394 | 18.3% |
| FOULS_BUT_BRICKS | FD 90, FT 20 | **8.6** | 34.9 | 23.1 | 20.4 | .361 | 18.3% |
| ALL_SCORING_ELITE | all six 85 | 21.9 | 46.3 | 37.4 | 84.8 | .324 | 29.3% |
| COMPOSITE_NONSCORER | all six 15 | 3.7 | 30.0 | 20.6 | 14.6 | .306 | 9.9% |

Readings:

- **Hack-a-Shaq emerged.** FOULS_BUT_BRICKS scores *below* control (8.56 vs 9.36) — drawing fouls
  you can't cash is a net negative, because each fouled trip replaces a shot worth ~0.75 expected
  points with two 20% free throws worth 0.4. Nobody authored this; it fell out of the math.
- **ELITE_SHOOTER's FTr *dropped*** (.253 vs .291 control) despite more FTA — his extra attempts
  are perimeter-tilted, and threes draw far fewer whistles (FoulThree 0.015 vs FoulRim 0.20). FTr
  is a zone-mix composite, not a FoulDrawing readout.
- **The usage tax is real.** ALL_SCORING_ELITE's turnovers rise 2.3 → 3.1/g with his 29.3% load —
  more ball, more giveaways. The non-scorer's fall to 1.7.
- **The offense routes around a non-scorer** — 3.7 PTS on a 9.9% use share, with his own ORB the
  highest of any row (2.34): he is almost never the shooter, so the picker's shooter-nerf almost
  never touches him.

### Mechanisms traced — why cross-stat movement is design, not bugs

Three channels were traced to named source this session; every "suspicious"-list signal
dispositioned.

1. **Usage follows skill (measured; the other half of the standing ruling).** Raising any zone
   make skill lifts the swept player's possession-use share from ~14% to ~22–23%; FreeThrow and
   FoulDrawing move it not at all. The offense hunts shooting skill, not line-trips. The
   quarantine corollary: team zone lines barely dent at the bottom of a walk (Close=0 costs team
   Short only −1.5pp while Close=99 adds +6.3pp) because a bad shooter takes few shots (8.2 FGA/g
   at rating 0 vs 13.3 at 99) — the offense quarantines weakness and amplifies strength
   automatically. Micro-fingerprint: his own ORB falls as skill rises (2.25 → 2.12) because he is
   the shooter more often and the rebounder-picker's shooter-nerf engages.
2. **The shot-diet tilt is the S36 displacement bend, on purpose.** Each zone skill tilts the
   team attempt mix toward its own zone (Short 16.7 → 19.4 under Close; Mid 15.6 → 18.6; Rim
   27.5 → 30.5 under Finishing; both Outside zones up; FT/FoulDrawing mixes dead flat).
   Traced: `DeriveDisplacement` Stage 2 computes per-zone gaps via `OffenseRating(zone, shooter)`
   — the same zone→skill map the make curve uses — and Stage 5 bends the diet by the residualized
   gap; the Rim/Short inward gates (Finishing/Close) stack on top. **This corrects the S48 draft
   prompt's claim:** "no scoring rating feeds shot frequency" is true of the Roll G *base
   weights* only; the bend reads zone skills by design. Skilled players hunt their spots.
3. **Gravity verified live — a byproduct finding.** Every make skill faintly lifts the *other*
   zones' Team-A FG% at the high end, and the lift order matches the `GravityContribution`
   weights exactly: Finishing (0.35 of gravity) → biggest glow (+2.0–2.4pp at 99); Close (0.25)
   next; Mid (0.10) small; Outside (0.05) ≈ nothing. Team B flat throughout — an offense-side
   relief effect, which is what the gravity → attention → relief chain
   (`Player.GravityContribution` → AttentionGenerator → Roll H C1 relief) is supposed to be. It
   also explains ELITE_RIM out-lifting ELITE_SHOOTER on *team* points (56.0 vs 55.5 A.PTS)
   despite fewer personal points: rim threat carries 60% of gravity, perimeter threat 5% — the
   engine believes an interior monster warps a defense hardest, and the measurement agrees.
   **Two boundary lines:** "verified" means the wiring fires correctly — whether +2.4pp of
   teammate lift is the right *size* is a calibration question deferred to a real population; and
   only gravity (the saturating top-threat composite) was exercised — **spacing** (the
   accumulating twin) stayed quiet as designed, since one player can't move a team environment,
   and gets its day in a perimeter-family or team-composition test.

### Open flags (recorded, not chased)

- **The FTA gap.** At identical FoulDrawing 90, FREE_POINTS drew 4.08 FTA/g vs FOULS_BUT_BRICKS'
  3.77 — ~8%, too large for noise at 2,000 games/row, mechanism untraced (candidate: missed-FT
  rebound/possession composition differences). Chase only if a later family moves it.
- **3P% at the rating-0 end of the Close and Mid walks sits ~1–1.5pp above baseline** (25.1 /
  24.6 at rating 0 vs 23.2 at 50), fading by mid-walk. Borderline against the ~0.7pp per-rung
  noise; no mid-walk trend. Watch item only.

### Rulings and generation-layer notes

- **SelfCreation belongs to Family E** (ruled at the S48 gate) — no make-curve role; measured
  with perimeter creation.
- **What "50" means (interpretation note for every family that follows).** Rating 50 is *not*
  the league-average player. The make-curve recentering (S31-era, Emmett's ruling: recenter,
  never compress) anchored the *league's average shooter* — the generated population's effective
  level, which sits well above 50 — to the real per-zone targets (rim 61 / short 43 / mid 39 /
  long 36 / three 34). The flat-50 bench player is therefore a below-D1-average shooter facing a
  defense with no weak link and no mismatch to hunt; his 34.3% FG coexists coherently with the
  season page's calibrated 44.9%. The bench anchor is a lab reference, not a world prediction —
  read every curve here as *shape and mechanism*, and read "is this level right?" only on the
  season page with a real population.
- **Generation-redesign feed (height→skill-quality).** The redesign's height-conditioned skill
  leans should be sized against the population's effective average, not the scale midpoint —
  "average" is already defined at the season page by the recentering ruling, which is closed. A
  "50 scorer" in generation terms is a below-average one; the leans shift *distributions*, and
  the season page (not the bench) judges the result.
- **Gravity magnitude calibration** is deferred until a real population is on the season page;
  **spacing verification** is pending a test where team composition varies.

---

## Family E — Perimeter creation (BallHandling, Passing, Playmaking, SelfCreation, OffBallMovement) — measured Session 49

The second family through the S47 full-box ruler, and the first measured on **slot 1 (the point
guard)** rather than slot 5. Five isolation walks (0→99 in 5-point steps, 2,000 games/rung,
everything else frozen at 50) plus an eight-row within-player interaction block — 232,000 real
games, pure config-and-run (no engine file, no `config.json`, no readout change, no Monte Carlo).
SelfCreation, ruled out of Family D at the S48 gate, is measured here.

**Why slot 1, and the one thing it buys.** The check-in audit found that in the flat-50 lab
world, four of the five ratings are **slot-blind** — the assist picker, the usage score, the
denial contest, and the gravity/spacing terms are all attribute-driven, so BallHandling on slot 2,
3, or 5 would read the same. The **one** exception is BallHandling's team-turnover channel: it
flows through guard-heavy position weights (slot 1 = 0.35, slot 5 = 0.08), so it only reads on a
guard's seat. (A second reason — a post-vs-guard attribution penalty — was investigated and
**withdrawn**: in a flat-clone world every player has the same body, so nobody counts as more of a
post, and that penalty is inert here.) The cost of moving off slot 5 is that the S48 scoring
control does **not** carry; the slot-1 flat-50 box is the new Family-E anchor and is not comparable
to the slot-5 numbers.

**The grain (applies to every number below).** (1) The swept player's box is *his*; team-zone and
team-total lines are Team-A-level, diluted by four flat teammates. (2) **The flat-50 bench is a
below-average *passing* world, by the same logic as Family D's "what 50 means" note.** The assist
system's lineup-passing pivot was recentered to **71.31** in S41 to track the real generated
population (whose starters average ~71 assist-weight), and the zone assisted-rates were calibrated
to the season page (13.7 assists) at that pivot — so a flat-50 lineup sits 21 points *below* the
pivot and runs its assist existence at ~0.80× normal. Read the passing/playmaking curves here for
**shape and attribution**; the assist *level* is judged on the season page, never on this bench.

### The gates

- **Determinism.** All five walks' 50-rungs and FLAT_50_CONTROL are the same box (11.6 PTS /
  35.5 FG% / 21.7% use / 1.5 AST / 2.6 TO) — five runs, one answer at the new slot.
- **Control parity.** Team A ≈ Team B (52.8 / 52.9 PTS).
- **Nothing leaked to the defense.** STL/BLK flat under every walk; Team B's box flat everywhere.

### The curves (0 → 50 → 99)

| Rating | His headline line | Read |
|---|---|---|
| **SelfCreation** | PTS 7.3 → 11.6 → 18.6; **use 13.4% → 21.7% → 36.2%**; FG% 37.8 → 35.5 → 33.0 | The steepest usage curve of any rating measured. The FG% slide is the designed volume tax. |
| **OffBallMovement** | PTS 10.8 → 11.6 → 12.3; use 20.3% → 21.7% → 23.0%; FG% flat 35.5 | Getting open lifts his shot volume with no shooting-skill read behind it. Moderate — a fraction of SelfCreation's slope. |
| **Passing** | AST 0.9 → 1.5 → 2.1; everything else flat | Pure assist attribution — more credit, zero extra shots or made-shot lift. |
| **Playmaking** | AST 1.1 → 1.5 → 1.9; everything else flat | Same, slightly gentler (lower attribution weight than Passing). |
| **BallHandling** | **TO 1.6 → 2.6 → 3.2**; everything else flat | **Inverted** — the better the handle, the *more* turnovers he is charged. See Finding 4. |

The BallHandling `use%` line rises 20.3 → 22.6 across its walk, but that is the *turnover term* of
the possession-use proxy (`FGA + 0.44·FTA + TO`) climbing with his turnover count — his FGA and
shot mix are dead flat. BallHandling does **not** feed the shooter-selection score, confirmed.

### The interaction block (eight rows, all dials on the swept point guard)

| Row | Dials | PTS | FG% | AST | TO | Use% |
|---|---|---|---|---|---|---|
| FLAT_50_CONTROL | — | 11.6 | 35.5 | 1.5 | 2.6 | 21.7 |
| PURE_POINT | Pass 90, Play 90, BH 85 | 11.7 | 35.7 | **2.3** | 3.1 | 22.3 |
| ISO_ALPHA | SC 90, BH 70 | **17.6** | 33.5 | 1.3 | 3.8 | **34.1** |
| MOVEMENT_MAN | OBM 90 | 12.1 | 35.5 | 1.5 | 2.7 | 22.6 |
| BUTTERFINGERS_VISIONARY | Pass 90, Play 90, BH 10 | 11.5 | 35.5 | **2.3** | **1.8** | 20.6 |
| WEAK_LINK | Pass 5 | 11.5 | 35.4 | 0.9 | 2.6 | 21.6 |
| ALL_E_ELITE | all five 85 | 17.4 | 33.8 | 1.9 | 3.9 | 33.9 |
| COMPOSITE_NONCREATOR | all five 15 | 7.1 | 37.6 | 0.8 | 1.3 | 12.4 |

### Findings

**Finding 1 — SelfCreation is the volume king, exactly as wired.** It carries the single largest
usage coefficient in the engine (0.35 on one rating), and it shows: use share climbs 13% → 36%,
points 7 → 19, with FG% sliding 38 → 33 (the designed volume tax on high-usage shot-hunting) and
his own offensive boards falling 2.30 → 2.00 as the shooter-nerf engages. The shot diet tilts off
the rim toward mid/perimeter as he creates more of his own looks (rim attempt share 28.8 → 26.9).
This inherits the whole S48 usage bundle as expected secondaries — nothing suspicious. The gravity
whisper (SelfCreation feeds the perimeter-access term of `GravityContribution`, ~+4 points at 99)
is far too small to separate from noise here, as predicted.

**Finding 2 — OffBallMovement is getting-open, no make read.** Raising it lifts his surviving usage
share (20% → 23%) purely by beating the Roll E denial contest (`OffBallDefense − OffBallMovement`),
with flat FG%. Real, moderate, and clean — a smaller mirror of SelfCreation's volume channel with
no shooting-skill component behind it.

**Finding 3 — Passing and Playmaking are almost pure assist-attribution ratings right now, and
the attribution is passing-dominant (correct).** Both raise the swept player's *share* of the
team's assists monotonically (Passing 0.9 → 2.1; Playmaking 1.1 → 1.9), and Passing moves it more
because it carries the larger attribution weight (0.5 vs 0.35) — which is correct basketball (the
pass that creates the bucket is the assist skill). **The predicted asymmetric make-conversion dip
did not appear.** Team FG% is dead flat across the Passing walk (35.4 / 35.5 / 35.6) and at
WEAK_LINK (Pass 5 → 35.4). The RollH passing-conversion bonus wire is real in source, but on this
instrument it is invisible for two stacked reasons: the below-pivot passing discount (the grain
note), and — the larger one — the bonus is **gated on gravity/spacing openness**
(`opportunityGate = lerp(floor, 1, BaseOpenness)`), which is near-zero in a flat world with no
mismatch to punish. So the conversion channel, like spacing in Family D, needs a
team-composition/openness test to fire and is **parked** with it. Net: on the bench, passing and
playmaking reshuffle assist *credit* but do not measurably make teammates shoot better.

**Finding 4 — BallHandling is inverted: it currently carries the *blame* for turnovers but not the
*protection* against them.** The clearest evidence is one triple in the interaction block, all
identical except the handle:

| Ball-handling | Turnovers/game |
|---|---|
| BUTTERFINGERS_VISIONARY (BH 10) | **1.8** |
| FLAT_50_CONTROL (BH 50) | 2.6 |
| PURE_POINT (BH 85) | 3.1 |

The better handler is charged *more* turnovers, because the only live BallHandling turnover
channels on this bench are the committer-attribution picker (`weight ∝ BallHandling` — the ball is
in his hands, so he catches more of the team's giveaways) and the team-aggregate matchup, which is
muted (below). The half that should make a good handler *lose it less* — the halfcourt
protection matchup — is pressure-gated to zero at the default neutral-pressure setting. So
BallHandling reads as a net negative. **This is not a bug to patch this session; it is the headline
design finding** (see the two reshapers below).

### The two reshapers this measurement surfaced

**Reshaper A — the missing ball-dominance / initiation layer (the big one).** The engine models
who *shoots* (the usage score) but not who *initiates* the possession or *holds the ball*. This
single gap explains two of this family's disappointments at once:

- **Passing magnitude is capped.** A great passer can only claim a bigger *slice* of a small,
  fixed assist pie — he cannot grow the pie by running the show, because he gets no more touches
  than his four flat teammates. Even PURE_POINT (Passing + Playmaking both 90) tops out at 2.3
  assists. Real floor generals rack up assists by *dominating the ball*.
- **Ball-handling protection has nowhere to live.** A good handler should reduce turnovers by
  keeping the ball on the string possession after possession — but without a touches/initiation
  layer there is no offense-side, defender-independent place for handle-protects-ball to act.

Both point at one absent subsystem: **ball-dominance / who initiates**. Building it would let
passing manufacture real assists *and* let ball-handling protect the ball. Recommended as the
highest-value design conversation this whole measurement pass has produced. It also intersects the
coaching layer (heliocentric vs egalitarian ball distribution).

**Reshaper B — the missing unforced-turnover channel.** The engine's base turnover rate is a flat
config constant, identical for a maestro and a butterfingers; BallHandling only enters through a
*defense-relative* matchup (your handle vs their steals), which needs a defender to push against
and is muted at neutral pressure. There is no **offense-only, defender-independent** "lost it
yourself" rate — the travel, the ball off the foot, the pass sailing out of bounds. The engine
already *names* the two categories it would split across (dead-ball turnover = self-inflicted;
live-ball turnover = stripped/steal), so the fix has a natural home: low BallHandling should raise
the **dead-ball** rate directly, offense-side, no defender required, while the forced/live-ball
channel stays the pressure matchup. **Attribution sub-call for Emmett when this is built:** the
current committer picker pins *more* blame on the *best* handler (usage proxy) — correct for forced
steals, arguably backwards for unforced dead-balls, which usually belong to the *worse* handle.
Recommendation on record: lean unforced blame toward the weaker handles; keep forced blame on the
high-usage handler.

### Muted channels (recorded, not fixed — this is measurement)

- **BallHandling is measured at neutral pressure — PARTIAL, not closed.** Two of its three
  turnover-rate channels are pressure-gated to zero because the bench's `HomePressure` /
  `AwayPressure` sit on the neutral pivot (5.0): the halfcourt individual matchup (Roll F) and the
  halfcourt-entry aggregate (Roll B). Only the transition/entry aggregate (Roll A, fixed
  `StandardGate`) reads it live, which is why team turnover *rate* barely moved while attribution
  climbed. **Prerequisite before BallHandling's full meaning is claimed:** the pressure-dialed test
  (parked to the coaching layer), which lights up Roll B and Roll F. Note also this corrects the
  S49 draft's mechanism: the mute is the config pressure scalar equalling neutral, **not** a
  null-coach fallback — there is no live coach-pressure read in the tree yet (`CoachProfileFor` is a
  comment-only migration path; `CoachProfile` has no `Pressure` property).
- **The passing make-conversion bonus and team spacing both need a team-composition/openness test**
  (they are openness-gated and cannot fire in a flat world) — parked together.
- **Slot-weight sensitivity** is why this family read on slot 1; a full picture of BallHandling's
  team-security channel across the five position weights is a future refinement, not required here.

### Rulings and generation-layer notes

- **SelfCreation's Family-E placement holds** — no direct make-curve read anywhere; its only reads
  are the usage-score top coefficient and the gravity perimeter-access term. (The S48 "bonus-FT
  putback picker reads SelfCreation" line is retired: the fresh audit found no such read in the
  tree — SelfCreation has exactly two engine reads.)
- **Generation-redesign feed.** Two perimeter-creation ratings are currently *under-rewarded* by
  the engine and one is *inverted* — Passing/Playmaking (capped by the missing initiation layer)
  and BallHandling (blame without protection). Do **not** over-size these ratings in the generation
  redesign to compensate for engine gaps; the correct fix is the ball-dominance and unforced-
  turnover layers, not inflated draws. Record the two reshapers as engine work that must land
  before perimeter-creation ratings can be judged "correctly valued."

---

## Physical package — Families A + C (Height, Wingspan, Weight, Strength, Speed, Quickness, FirstStep, Vertical, Endurance, Hustle) — measured Session 50

Combined into one package by Emmett's S49 close-out ruling: the engine mixes body and
athleticism in shared composites (the five-member Athleticism mean; LengthRating;
ReboundPhysical; Postness), and they feed the same generation decision (the
height→athleticism leans). Ten isolation walks (0→99 step 5, 2,000 games/rung, **slot 5** —
the S48 anchor reclaimed) plus a nine-row interaction block; 438,000 games, pure
config-and-run. Eleven configs in `tools/sweep/`.

### The gates

FLAT_50_CONTROL and every walk's 50-rung reproduced the S48 anchor **exactly** — 9.364 PTS /
34.33 FG% / 49.75 FT% to the third decimal, twelve runs one answer; the world unmoved since
S48. Team A ≈ Team B at control. Nothing on the suspicious list survived: no usage-shaped FGA
curve under any body rating (Height FGA 10.65 → 10.74, flat), Hustle never touched FG%
(34.3 across all 21 rungs), the explosion trio's divergence fully explained by documented
private channels, and the Weight walk flat with the dial proven applied.

Three draft-map corrections made at the check-in gate, before any walk was read: Roll A's
slot-weighted entry aggregate reads **both** athleticism and LengthRating (two muted whispers
at slot 5, not one); displacement's physical term is **one** channel reading **raw**
athleticism (not fatigue-discounted — Endurance does not flow through the diet bend); and the
denial-blend postness wake-up is **invisible under Height on a flat bench** (both skill gaps
are zero, so shifting the mix moves nothing) — it expresses only under Strength, which moves
the post gap itself.

### The curves (0 → 50 → 99)

| Rating | PTS | FG% | REB | BLK | STL | TO | What it is |
|---|---|---|---|---|---|---|---|
| **Height** | 9.2 → 9.4 → 9.5 | 33.5 → 34.3 → 34.8 | 6.5 → 7.7 → 9.5 | 0.57 → 0.70 → 0.88 | 1.59 → 1.25 → 0.91 | 2.55 → 2.30 → 2.07 | Possessions + designed post-shaping |
| **Wingspan** | 9.2 → 9.4 → 9.5 | 33.4 → 34.3 → 34.5 | 6.6 → 7.7 → 9.4 | 0.55 → 0.70 → 0.89 | flat | flat | The purest length rating |
| **Weight** | flat | flat | flat | flat | flat | flat | **Cosmetic — zero gameplay reads** |
| **Strength** | 9.2 → 9.4 → 9.5 | 33.8 → 34.3 → 35.7 | 6.4 → 7.7 → 9.5 | flat | 1.60 → 1.25 → 0.92 | 2.49 → 2.30 → 2.09 | Boards + post getting-open |
| **Vertical** | 9.2 → 9.4 → 9.7 | 33.3 → 34.3 → 35.5 | ~flat | 0.62 → 0.70 → 0.84 | flat | flat | Best athletic rating for a scorer |
| **Speed** | flat | 34.1 → 34.3 → 35.3 | flat | flat | flat | 2.3 → 2.2 | Composite door + gravity whisper |
| **Quickness** | 9.2 → 9.4 → 9.6 | 34.0 → 34.3 → 35.1 | flat | flat | flat | flat | Composite door |
| **FirstStep** | flat | 34.1 → 34.3 → 35.4 | flat | flat | flat | 2.3 → 2.2 | Composite door + gravity whisper |
| **Endurance** | flat | 34.3 → 34.3 → 34.5 | flat | flat | flat | flat | **A whisper — see verdict** |
| **Hustle** | flat | flat | 6.6 → 7.7 → 8.7 | flat | 1.1 → 1.2 → 1.4 | flat | Share-claiming through three pickers |

**Weight — proven cosmetic, the session's headline finding.** All 21 rungs byte-identical in
every stat column while `meta_swept_value` steps 0→99 (dial applied; `BuildSweepTeam` throws
on unknown fields, so a silent drop is impossible). Source story confirmed by whole-tree
grep: read only by `Player.Validate()`, the Pass-2 generator, and roster/bench copies.

**The blocks triple — length both creates and claims.** Under Height, Wingspan, and Vertical,
team blocks rise (3.47 → 3.75/3.76/3.79) *and* the swept share rises (20% → 23/24/22%): the
length door creates blocks and the blocker picker hands the length more of them. Strength
correctly touches neither. Magnitude note for the design bundle: a full 0→99 length walk adds
~0.3 team blocks/game.

**The postness suppressions — two designed, one newly observed on a walk.** Height and
Strength (postness coefficients 1/3 each, confirmed in config at the gate) suppress both
turnover blame (committer picker: TO falls monotonically, magnitudes above — computed from
the coefficients at the gate and confirmed on the walk) **and steal share** (StealerPicker,
same tanh shape: share 26% → 14–15% while team steals stay dead flat at ~6.3). The steal
suppression was predicted for the archetype rows but not listed for the walks — it fired by
the same designed machinery and is classified direct-by-design. Strength's on-paper
interior-picker double-up (base weight × rising postness multiplier) **lost its tug-of-war**:
the general suppression wins and Strength's TO falls smoothly.

**The explosion trio (Speed / Quickness / FirstStep)** — each buys ~+1.2pp FG% across the
full range, the predicted one-fifth-strength composite doors, directionally identical. The
only divergence: Speed and FirstStep see usage *dip* at the top (18.2% → 17.6%) while
Quickness stays flat — their gravity-access term draws defensive attention, which taxes the
denial contest. Explained by the documented private channel; residual divergence none.

**Endurance — the magnitude verdict is made: a whisper.** 0 vs 99 = +0.2pp FG%, nothing
else. The channel is live (fatigue accrues every possession on the sweep path, confirmed at
the gate; fixed lineups mean the swept player never sits and carries his full drain
difference), it just doesn't cash out on whole-game aggregates. **Aggregate-only by
construction:** the readout has no time-slice columns, so *when* in the game fatigue lands is
unmeasurable on this instrument — a time-sliced bench is the recorded prerequisite for the
temporal-shape question.

**Hustle — textbook share-not-creation.** Boards 6.6 → 8.7 and steals 1.1 → 1.4 while team
totals stay flat: he claims a bigger slice of the same pile through the three picker tilts.
First swept player of the entire pass to move his own defensive box. The team-mean nudges
(Roll F disruption, foul cost) are below noise with one player moving the mean ~+9.8.

**Wingspan's JumpBall read: wired-but-unmeasured.** The opening tip is won on max team
wingspan (source-confirmed), but no first-possession column exists and one tip/game is
hopelessly confounded with Wingspan's own block/rebound effects. Recorded, not inferred.

### The interaction block (nine rows)

| Row | PTS | FG% | REB | BLK | Margin | The verdict |
|---|---|---|---|---|---|---|
| FLAT_50_CONTROL | 9.4 | 34.3 | 7.7 | 0.7 | −0.1 | The anchor, exact |
| **FREAK** (both halves 90) | 12.0 | 42.9 | 11.9 | 1.2 | **+6.4** | Body alone laps everything |
| **STIFF_GIANT** (length, no explosion) | 9.1 | 34.0 | 11.7 | 0.9 | +1.5 | Scores *below average*; lives on possessions |
| **POCKET_ROCKET** (explosion, no length) | 10.3 | 38.1 | 5.8 | 0.6 | +2.0 | Outscores the giant |
| MOTOR_MAN (Hustle 95) | 9.4 | 34.4 | 8.7 | 0.7 | +0.1 | The Hustle walk in one row |
| MARATHON_MAN (End 95) | 9.4 | 34.5 | 7.7 | 0.7 | 0.0 | Indistinguishable from GASSED |
| GASSED (End 5) | 9.4 | 34.3 | 7.7 | 0.7 | −0.2 | — |
| ALL_PHYS_ELITE (all ten 85) | 11.5 | 41.5 | 12.6 | 1.1 | +5.6 | Weight-85 contributes nothing |
| PHYSICAL_FLOOR_15 (all ten 15) | 8.0 | 29.4 | 4.3 | 0.4 | **−3.9** | The body floor is a real team liability |

**The marquee answer: explosion buys points, length buys possessions.** POCKET_ROCKET
outscores STIFF_GIANT (10.3 on 38.1% vs 9.1 on 34.0%), but the giant wins games anyway
(+1.5) on 11.7 boards, block share, and the lowest turnover blame on the board — two
different, both-viable physical archetypes, which is the relative-engine thesis working.
FREAK is the strongest single-player team effect measured on this bench to date (+6.4 margin
on zero skill). Both designed postness effects showed on cue in the post-shaped rows (FREAK
and STIFF_GIANT steal/TO fall).

### The design-question bundle (Emmett's ruling — logged with evidence, nothing touched)

Opened by Emmett's in-session read ("height and wingspan don't impact blocks or steals
enough and should probably result in higher FG%, especially for a center — a 99 guy should
be scoring over guys"), sharpened by the numbers:

1. **No height-over-defender term exists in the make chance.** A 99-Height player gains
   +1.35pp FG%, *all of it* block-avoidance through LengthRating. Shooting over a smaller
   defender — real efficiency near the rim and in the post — has no wire to live in. A
   missing wire, not an undersized one. **This is the question that gates the
   height→skill lean sizing** (below).
2. **The block channel is modest even fully stacked** — FREAK blocks 1.2/game; a full length
   walk adds ~0.3 team blocks. Magnitude call; waits for a real population per standing
   rule, flagged as likely light.
3. **Wingspan feeds steals nothing.** Whether long arms should buy deflections/steals is a
   wiring question.
4. **Weight is cosmetic** — feed Strength-adjacent channels, or stay cosmetic until a
   body-contact layer exists?

### The generation-redesign feed (what body and athleticism actually buy)

For the height→athleticism lean sizing (Phase 3 of the Pass-2 port): under **current
wiring**, length is a possession engine (boards +~3/game, blocks +~0.3 team/game across a
full walk) and nearly not a scoring engine (FG% +1.1–1.4pp); explosion is a modest
efficiency engine (+1–2pp per rating, stacking to FREAK's +8.6pp); the physical floor is
punitive (−3.9 margin at 15s). So today's engine prices tall players out of very little
scoring — the height→skill negative lean would be taxing something height doesn't buy back.
**But design question 1, if ruled in, changes that materially.** Recommendation on the
record: run Families F/G/H and the synthesis pass, rule on size-should-score, *then* size
the leans.

### Parked out of this session

The time-sliced fatigue bench (Endurance's temporal shape); the JumpBall first-possession
counter (Wingspan's tip read); the four design questions above. All carried flags from
S48/S49 unchanged.
