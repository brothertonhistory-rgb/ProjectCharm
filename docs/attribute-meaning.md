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
