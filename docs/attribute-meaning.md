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

> ## ⚠ DOCUMENT-WIDE RE-BASE (added 2026-07-21) — every make-percentage LEVEL below was measured on the PRE-RECENTER curve
>
> The 2026-07-21 make-curve recenter re-fit all five zone logistics to Emmett's ruled scenario
> anchors (design.md, "The make dial"): at effective 50 the zones now read Rim 61 / Short 43 /
> Mid 39 / Long 36 / Three 34 — roughly **+8–11pp above the old curve at the same rating** through
> the middle, with the elite end near-unmoved at Three and re-spread at Rim (77 at the pool's best
> finisher). Consequences for this document: every absolute FG%/3P%/zone-make **level** recorded
> below (the flat-50 anchors 34.33 FG% / 9.364 PTS, every walk's make column, every interaction
> row's shooting line) describes the old curve and is stale as a level. **Relative findings
> survive** — orderings, which-channel-moves-what, share-claiming vs creation, the walk SHAPES —
> because the recenter moved the curve every shot passes through, not any wiring. Do not re-run
> the families to refresh levels for their own sake (the S59.2 flat-50 caveat below already
> queues the re-measurement that matters); read levels here as pre-recenter currency.
>
> ## ⚠ DOCUMENT-WIDE CAVEAT (added S59.2, 2026-07-14) — every finding below the S59.2 section is a **FLAT-50-OPPONENT** finding
>
> **Read this before trusting any number in this document.** Until S59.2 the sweep bench
> **hardcoded the opponent at flat all-50** — there was no way to dial Team B, so every family
> from S45 to S55.1 was measured against average-everything competition. That is not a neutral
> choice. It caps the body gap between the swept player and his defender at about ±30 rating
> points, and **the engine's skill-muting mechanism does not meaningfully engage until the gap is
> larger than that.**
>
> S59.2 measured the same walk twice — once vs flat-50, once vs a real high-major roster — and the
> answers disagree badly. What an elite scoring edge is worth (FG%, elite minus average):
>
> | swept player's body | vs flat-50 | vs a real high-major team |
> |---|---|---|
> | 20 (small, slow) | +11.2 | **+6.0** |
> | 50 (even) | +11.6 | +10.6 |
> | 80 (freak) | +11.1 | +9.9 |
>
> Against flat-50 the rating's value is a **flat line** — which reads as "body mismatch does not
> mute skill." Against real talent it **nearly halves** for the small player. The mechanism was
> always there (the bounded logistic's tails compress rating differences once a big physical gap
> shoves a player toward the floor); flat-50 simply never pushed anyone far enough down the curve
> to show it. The same shape holds for rebounding (rating worth +2.7 vs flat-50 at body 20, +1.6
> vs a real front line).
>
> **So:** every pre-S59.2 finding here is true *as measured* and is the **even-bodied, average-
> competition** answer. None of them is the general answer. Any finding whose headline is "this
> rating is worth X" should be read as "worth X **against average competition**," and any finding
> that concluded a rating is **flat, inert, or unaffected by the body** is the one most likely to
> change on re-measurement — that is precisely the conclusion flat-50 manufactures. Re-measuring
> the eight families against dialed opponents is **Open** (see status.md); nothing below has been
> re-run yet. **Queued, not claimed.**
>
> The instrument was fixed in S59.2 (`opponentDials`, see design.md). The finding that the
> instrument had been lying by omission was Emmett's — he rejected "50s across the board" as
> not-real talent, and he was right.

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

1. **The height-over-defender make term — RULED, SHIPPED (S55), RE-MEASURED.** As first
   measured here (pre-S55 wiring), a 99-Height player gained +1.35pp FG%, all block-avoidance
   through LengthRating — shooting over a smaller defender had no wire. The wire now exists: a
   one-sided, zone-weighted (Rim 1.0 → Three 0.0), tanh-saturating reach advantage
   (reach = (Height+Wingspan)/2) added to the make door's EffectiveRating, on both the primary
   shot and the putback (Phase 61 golden parity; design.md make-door section). **The S50 curves
   in the table above predate the wire; the post-wire re-measurement — both length walks re-run
   on the live engine, 42,000 games each — landed exactly as designed:**
   - **The lift is real and one-sided.** Height FG% 33.6 → 34.3 → **36.3** (was 33.5 → 34.3 →
     34.8); Wingspan 33.5 → 34.3 → **36.0** (was 33.4 → 34.3 → 34.5). The wire adds **~+1.5pp at
     the 99-rung** on each, roughly doubling length's full-range scoring gain (+1.3 → +2.7pp
     Height, +1.1 → +2.5pp Wingspan).
   - **Gated at reach parity, exactly as built.** Below the 50-rung the new curves sit on the
     pre-wire curves (both 34.3% at 50); the entire +1.5pp lands *above* 50, where the swept
     player out-reaches the flat-50 opponent. The one-sided design (zero for equal/shorter) is
     visible directly on the walk.
   - **Concentrated at the rim, threes untouched.** 3P% stays flat across both walks (Height
     23.3 → 23.5, Wingspan 23.0 → 22.9) — the Three-zone weight of 0.0 confirmed on live
     outcomes; the FG% lift is the two-point/rim zones only.
   - **Cleanly isolated to scoring.** Rebounds, blocks, steals, and the defensive per-man line
     (opponent 2P% still falls as the swept player gets taller — Height 39.8 → 36.6) are all
     unchanged from the S50 curves. No side effects.
   The **S50 "length buys almost no scoring" finding is retired** — length now buys mismatch
   scoring. Whether +1.5pp at the top is the *right* magnitude is a calibration question (one
   dial, `MatchupConfig` MaxBonus 15.0) that waits for a real population per the standing rule.
2. **The block channel is modest even fully stacked** — FREAK blocks 1.2/game; a full length
   walk adds ~0.3 team blocks. Magnitude call; waits for a real population per standing
   rule, flagged as likely light.
3. **Wingspan feeds steals nothing.** ~~Whether long arms should buy deflections/steals is a
   wiring question.~~ **RULED AND SHIPPED (S58).** Long arms now buy deflections: the live
   steal-forcing floor (Roll F + Roll B) reads a **two-sided, perimeter-gated wingspan** term on the
   steal contest (long arms add, short arms cost a little; gated by postness so a short-armed big
   barely enters), and `StealerPicker` gained a slight wingspan tilt on who gets credited. Athleticism
   ((Quickness+FirstStep)/2) is the **primary** forcing input beside it, the steal rating secondary.
   See the S55-arc journal and design.md (Roll F/B disruption doors); this also made Steals partly a
   *create* dial at neutral (see Finding 3 below).
4. **Weight is cosmetic** — feed Strength-adjacent channels, or stay cosmetic until a
   body-contact layer exists?

### The generation-redesign feed (what body and athleticism actually buy)

For the height→athleticism lean sizing (Phase 3 of the Pass-2 port): under the **S50
wiring these curves measured**, length was a possession engine (boards +~3/game, blocks
+~0.3 team/game across a full walk) and nearly not a scoring engine (FG% +1.1–1.4pp);
explosion is a modest efficiency engine (+1–2pp per rating, stacking to FREAK's +8.6pp);
the physical floor is punitive (−3.9 margin at 15s). **Design question 1 was ruled in and
shipped (S55)** — length now buys mismatch scoring through the height-over-defender term,
so the "height doesn't buy back scoring" premise no longer holds as written. The S48-S50
recommendation was followed exactly (Families F/G/H measured, synthesis run, size-should-
score ruled, the term shipped). **The queued re-measurement is now complete** (design question 1
above): post-wire, length's full-range scoring gain roughly doubled to +2.7pp (Height) / +2.5pp
(Wingspan), rim-concentrated and gated at reach parity — so length is now a possession engine
*and* a modest mismatch-scoring engine, no longer "possessions only." The height→skill lean can
be sized against measured post-wire curves rather than a pending flag.

### Parked out of this session

The time-sliced fatigue bench (Endurance's temporal shape); the JumpBall first-possession
counter (Wingspan's tip read); the four design questions above. All carried flags from
S48/S49 unchanged.

---

## Family F — Interior offense (PostMoves, Screening) — measured Session 51

The fourth family and the smallest: two ratings, two isolation walks, one seven-row
interaction block, all on slot 5 (the S48/S50 anchor seat — every Family F channel is
attribute-driven or an all-five aggregate, so slot choice buys direct comparison against
Scoring and Physical at no cost). 2,000 games/rung, 98,000 games total. Config-and-run;
the S47 full-box readout is the instrument, nothing in the engine moved.

**Determinism.** Both walks' 50-rungs and FLAT_50_CONTROL reproduce the anchor **9.4 PTS
/ 34.3 FG% / 49.8 FT%** — the world unmoved since S48.

### The wiring (what the source actually reads)

- **PostMoves — three gameplay reads.** (1) Roll E usage score: `(Close + PostMoves)/2 ×
  0.30` — the *raw* contribution is identical to Close's at equal weight. (2) The Roll E
  denial post-channel: `PostDefense − (Strength + PostMoves)/2`, blended at the postness
  weight — a second getting-the-ball channel that survives denial, the only rating measured
  so far with two stacked ball-getting doors. (3) Gravity postAccess: `(PostMoves +
  Strength)/2`, whisper-scale (two downstream consumers, both below noise). **No make read,
  no diet read.**
- **Screening — one gameplay read.** An all-five offensive aggregate (shooter included, no
  exclusions), halfcourt-only, bonus-only, lifting make% on **all five zones** (`makePct +=
  0.15 × (Σ Screening/100 / 5)²`). No usage, picker, defensive, or rebounding read anywhere.

### The two curves (isolated walks, swept player)

| Rating | Usage % (0 → 99) | Swept FG% | Team A FG% | Personal TO | Notes |
|---|---|---|---|---|---|
| **PostMoves** | 12.7 → **23.9** | 35.3 → 33.8 (−1.5) | ~34.3 flat | 1.9 → 2.7 | touches up, diet flat, no make term |
| **Screening** | **18.2 flat** | 33.2 → 35.5 (with team) | 33.2 → **35.5** (+2.3) | 2.3 flat | pure team make; individual line untouched |

### Finding 1 — Screening is the biggest team-wide single-rating channel on the bench

One player walking Screening 0→99 lifts **team A FG% +2.3pp** (33.2 → 35.5) and team points
+2.7 (51.4 → 54.1). Nothing measured prior comes close — S48's best gravity lift was under a
point. The lift reaches all five zones, threes included (3P% 22.0 → 24.4), confirming the
Phase 44 all-zone gate live. The swept player's own points rise 9.1 → 9.6 **purely through
his own FG% climbing with the team on flat 18% usage** — Screening buys him nothing
individually, only a slice of better team looks. Usage, steals, and blocks are dead-flat
across all 21 rungs (no wire can move them). Rebound ripple runs **down** (RebΔ −0.6 → +0.2:
fewer team misses to grab — the standing make-raiser signature).

The +2.3pp landed at the **top** of the pre-stated +1.7–2.3pp band, so the halfcourt share is
on the high side of the 0.60–0.75 dilution assumption. (A precise dilution number was
unavailable — the readout has no transition/fast-break-share column; reported at the gate,
banded not computed.)

### Finding 2 — PostMoves buys touches that arrive at a generic diet (the no-post-hunt gap)

PostMoves lifts usage **12.7% → 23.9%** — steeper than Close's S48 14%→22% at both ends,
because the two stacked ball-getting channels (usage score *and* denial survival) compound.
But **the shot mix never moves**: Rim 28.4 → 28.2, Short/Mid/Long/Three all flat across the
whole walk. **The engine has no way for a post player to hunt post shots through PostMoves.**
His extra possessions spread across the same generic diet everyone shoots, because the
displacement diet bend reads `OffenseRating(zone, shooter)` — Outside/Mid/Close/Finishing
only — and PostMoves is not in that map. This is the interior analog of S49's missing
ball-dominance layer, and the family's first-order design finding.

His FG% falls 35.3 → 33.8 (−1.5pp): no make term, so extra volume through flat zone skills
costs efficiency (the milder SelfCreation shape). Personal TO rises 1.9 → 2.7 (more touches,
more giveaways — the involvement channel; the postness *multipliers* stay pinned on this
bench, so the rise is pure volume). Steals and blocks flat.

POST_SCORER confirms the mechanism from the other side: it **does** tilt the diet inside
(Short 17.5 → 19.5), but through its Close/Finishing 90 — not its PostMoves. Skill hunts its
spots; PostMoves just delivers the ball there.

### Finding 3 — the tweener-post exists (a parked design requirement, closed affirmative)

TWEENER_POST — Height 15, Wingspan 20, Strength 45, PostMoves 90, Close 75, a guard-sized
body carrying a post skill package — scores **13.5 PTS on 35.4 FG% at 25.4% usage.** A
genuine interior threat whose skill overrides his size for both getting the ball and keeping
it. The missing size leg shows exactly where the divisional-sorting principle predicts:
rebounding collapses (7.7 → 5.8 REB), rim protection drops (BLK 0.7 → 0.5), efficiency barely
clears control (35.4 vs 34.3) — but none of it strands him. He functions as a D2/D3 big and,
with a modicum of perimeter skill, would unlock D1. **The generation redesign is now cleared
to produce him.** (Curiosity, classified noise: steals 1.6 vs 1.2 control, no wire connecting
his low height to steals, five dials moving — a CSV glance, not a bug lead.)

### The interaction block (seven rows)

| Row | Dials | PTS | FG% | Use % | A.PTS | Read |
|---|---|---|---|---|---|---|
| FLAT_50_CONTROL | — | 9.4 | 34.3 | 18.2 | 52.8 | anchor |
| POST_SCORER | PM90 Cl90 Fin90 Str75 | **17.6** | 42.9 | 28.0 | 57.4 | dominant interior line; tilts inside via Close/Fin |
| TWEENER_POST | H15 W20 Str45 PM90 Cl75 | 13.5 | 35.4 | 25.4 | 54.2 | **functions** — the marquee row |
| POST_HANDS_NO_FINISH | PM90 Fin10 Cl10 | 8.7 | 31.2 | 17.8 | 52.4 | interior Hack-a-Shaq, below control |
| SCREEN_BIG | Scr90 Str85 H85 ORB75 | 9.8 | 36.8 | 17.5 | 55.0 | pure team value; 10.8 REB, +2.2 margin |
| ALL_F_ELITE | PM85 Scr85 | 11.6 | 34.6 | 22.2 | 54.0 | both channels live |
| F_FLOOR_15 | PM15 Scr15 | 7.1 | 34.0 | 14.2 | 51.7 | both drag down modestly |

Readings: **POST_SCORER** is the block's biggest personal line, clearing S48's rim-only elite
(12.9) easily — both touch channels plus real interior finishing plus the heaviest gravity
levers, and it tilts the team diet inside (its Close/Finishing, not its PostMoves).
**POST_HANDS_NO_FINISH** scores *below* control (8.7 vs 9.4) — gets post position, bricks, and
the offense partly quarantines him (Rim 28.8 → 28.0); the interior twin of S48's Hack-a-Shaq.
**SCREEN_BIG** is read by his margin, not his line — 9.8 personal points but +2.2 team margin
and 10.8 boards (the S45 body amplifier on the OffReb 75).

### The design bundle this measurement feeds (nothing wired)

- **The no-post-hunt gap (new).** PostMoves gets a post player the ball but the engine cannot
  express "he hunts post shots" — his volume arrives at a generic diet. The interior analog of
  S49's missing ball-dominance layer. Whether a PostMoves→interior-diet wire should exist is a
  design conversation, logged for Emmett.
- **The tweener-post requirement — CLOSED affirmative** (Finding 3). Removed from the parked
  list; the generation redesign is cleared to produce guard-sized post-skill players.
- Sits alongside the S50 questions (the **height-over-defender make term is now SHIPPED — S55**;
  the block-channel/wingspan-steals/Weight questions remain open) and the S49 reshapers.

### The generation-redesign feed (what PostMoves and Screening buy)

PostMoves is a pure **touch-getter**: +11pp of usage across a full walk, zero diet shaping, a
mild efficiency tax, no make term. Its value is entirely downstream of the zone skills it
feeds touches into — POST_SCORER vs POST_HANDS_NO_FINISH is the whole story. Screening is a
pure **team-make multiplier**: +2.3pp team FG% fully stacked, nothing individual, a five-man
team good. Neither should be sized as a standalone scoring rating. Recommendation on the
record: size these after the synthesis pass, with the no-post-hunt gap ruled first.

## Family G — Defense (PerimeterDefense, PostDefense, RimProtection, Steals, HelpDefense, OffBallDefense) — measured Session 52

The fifth family through the S47 ruler, and the largest — six isolation walks (0→99 in 5-point
steps, 2,000 games/rung) plus **two** interaction blocks. Structurally different from A–F: every
prior family landed the swept player's effect on his own box or his own team's offense. Defense
is the mirror — a defender's job is to make the *other* team worse — so the primary signal for
most of Family G lives on **Team B's box** (B.PTS and the CSV's teamB zone FG%), not the swept
player's own line.

**Two slot blocks (Emmett's S52 ruling).** Six walks + a seven-row interaction block on **slot 1**
(reproducing the S49 slot-1 anchor: 11.6 PTS / 35.5 FG% / 21.7% use / 1.5 AST / 2.6 TO), because
Steals' one live team-turnover channel is guard-weighted and expresses on a guard seat, and every
make-door curve reads just as fully at slot 1 as slot 5 (on a flat-50 opponent every Team B slot
attacks the same generic diet regardless of the swept slot). Plus a four-row interior block on
**slot 5** (reproducing the S48/S50/S51 anchor: 9.4 / 34.3 / 49.8) where the interior-defense
archetypes carry real bodies — the body×interior-defense compound that slot 1 cannot show. Both
anchors held exactly. **274,000 games**, pure config-and-run — no engine file, no `config.json`,
no readout change, no Monte Carlo.

### The measurement wall this family hits (logged, not a bug)

The swept defender **guards one of the opponent's five shooters** (slot-guards-slot). So the three
"guard-your-man" ratings — PerimeterDefense, PostDefense, RimProtection — move the *team* FG%
about a fifth as much as they move his own man's shot. A lockdown perimeter defender takes real
points off the man he covers, but on the team scoreboard that shows up as a **whisper** (sub-half-
point on B.PTS). This is the ruled-as-is limitation: the instrument has no per-opponent-slot column,
and the on-ball make door is measured diluted. The clean reads for those three ratings live
elsewhere — in the block column and (for RimProtection) in second-chance defense.

By contrast, **HelpDefense and OffBallDefense are NOT diluted this way** — they are the four
off-ball defenders' aggregate on nearly every opponent shot, so their team-suppression reads
clean. And **Steals + RimProtection** print their real work on the swept player's own box (steals,
blocks) and on second-chance possessions, both undiluted.

### The six curves (opponent points = B.PTS, neutral 50 → 99)

| Rating | B.PTS 50→99 | What it buys |
|---|---|---|
| **RimProtection** | 52.9 → 51.6 (**−1.3**) | The biggest team effect of the six. Own BLK 0.7 → 0.9, opponent pushed off the rim (their rim attempt share 28.8 → 27.4). **Not a whisper** — the second-chance channel escapes the on-ball dilution. |
| **OffBallDefense** | 52.9 → 52.1 (**−0.8**) | Clean team-suppression channel, zero personal-box footprint. Suppresses the opponent's perimeter makes (Long/Three, Mid partial). |
| **HelpDefense** | 52.9 → 52.3 (**−0.6**) | Clean team-suppression channel, no personal footprint. Suppresses the opponent's interior makes (Rim/Short, Mid partial). Right in the predicted band. |
| **PostDefense** | 52.9 → 52.5 (**−0.4**) | Barely touches the scoreboard — but transforms **his own** box: REB 7.7 → 8.7, STL 1.3 → 0.9, TO 2.6 → 2.3. The engine reads him as a *bigger player* (see the postness note). |
| **PerimeterDefense** | 52.9 → 52.8 (**~0**) | Points whisper (the on-ball dilution) — but reshapes the opponent's diet: their 3PA share 20.4 → 19.2, rim share 28.8 → 30.0. Pushes his man off the arc toward the rim. |
| **Steals** | 52.9 → 52.8 (**~flat**) | Own STL climbs 0.1 → 2.1 — the cleanest, most monotone curve in the session — but **opponent points do not move.** He *claims* steals; he does not *force* new turnovers. |

### Finding 1 — RimProtection is the only "guard-your-man" rating with a real team effect, because it also does second-chance defense.

The other two interior/perimeter ratings are diluted to a whisper on the scoreboard. RimProtection
is not, and the mechanism is the reason: beyond contesting his own man's rim shot (diluted like the
rest), it deters and blocks **putback attempts after the opponent grabs an offensive rebound** — a
five-defender team read, not a one-on-one, so it is not diluted by the slot-guards-slot wall. B.PTS
falls a clean −1.3 across the walk, the opponent's rim attempt share falls 28.8 → 27.4, and his own
block line is the strongest own-box signal of any Group-1 rating. **This is the finding that most
corrects the going-in expectation:** RimProtection's team value is real and readable, where
PerimeterDefense/PostDefense's headline job is nearly invisible at team scale on this instrument.

### Finding 2 — PostDefense barely touches the scoreboard, but it is one-third of how the engine decides a player's size.

Walking PostDefense 0→99 leaves opponent points nearly flat (−0.4) but moves the swept player's own
box hard: rebounds 7.7 → 8.7, steals 1.3 → 0.9, turnover blame 2.6 → 2.3, usage 21.9% → 21.2%. None
of that is post defense working or failing — it is the engine **reclassifying him as a big.**
`Matchup.Postness` reads Height, PostDefense, and Strength in equal thirds, so raising PostDefense
alone raises his "postness," which the rebound-position, steal-share, and turnover-blame pickers all
read: bigs get better board position, a smaller slice of the team's steals, and less turnover blame.
**Consequence for the generation redesign: a "great post defender" rating currently reads more as
"is a big" than "stops post scoring."** Logged for synthesis — a design conversation, not a
this-session fix.

### Finding 3 — Steals is claim, not creation.

The Steals walk produces the cleanest single curve in the session — own STL 0.1 → 2.1, monotone
across all 21 rungs — while **opponent points stay flat and opponent turnovers do not rise.** The
one live team-turnover channel (Roll A, guard-weighted) barely moves the needle at neutral pressure;
what the rating actually buys is a bigger *share* of his team's existing steals (the StealerPicker
attribution weight), not more forced turnovers. BALL_HAWK confirms it from the archetype side (below).
This mirrors the S49 BallHandling finding on the defensive side: the disruption channels are
pressure-gated toward zero at neutral, so steal *rating* on this bench is an attribution dial, not a
turnover-forcing dial. The turnover-forcing side needs a non-neutral pressure setting to express —
recorded, parked to the coaching layer, same as BallHandling's pressure-dialed test.

> **UPDATE (S58) — the neutral-pressure gate is now a live floor; Steals is partly a *create* dial.**
> The finding above described the *pre-S58* engine, where the Roll B/F disruption matchup was gated to
> zero at neutral. S58 replaced that gate with a pressure-**independent** steal-forcing floor: the
> steal-vs-ballcontrol contest (plus athleticism-primary and a perimeter-gated wingspan term) now forces
> turnovers at neutral, so a high steal rating **does** create some turnovers in a base defense, not only
> claim a share. What is *still* parked to the coaching layer is the pressure **dial** — a press that
> scales the whole disruption up; the floor is the base level, un-amplified. The attribution finding
> (the StealerPicker share) is unchanged and now also carries a slight wingspan tilt. See the journal/
> design.md for the floor; this measurement predates it and is left as the historical neutral-gate read.

### Finding 4 — HelpDefense and OffBallDefense are the clean team-suppression pair, and they are symmetric.

Both escape the slot-guards-slot dilution because they aggregate the four off-ball defenders on
nearly every opponent shot. HelpDefense suppresses interior makes (Rim/Short full, Mid ×0.30, zero
outside): B.PTS −0.6. OffBallDefense suppresses perimeter makes (Long/Three full, Mid ×0.30, zero
inside): B.PTS −0.8. Neither leaves any footprint on the swept player's own box — no steals, no
blocks, no rebounds move — exactly as wired. HELP_ANCHOR (below) is the marquee clean read.

**OffBallDefense's per-man denial channel is live but unmeasurable on this bench.** When the old
team-wide perimeter selection-squeeze was retired (Phase 46), it was replaced by a per-man version:
a defender with high OffBallDefense takes touches away from the specific man he covers. On a flat-50
opponent — five identical clones — taking a touch from one clone and handing it to another changes
nothing measurable. So this channel is real in the source and correctly invisible here; noted in the
completeness ledger, not the retired pile. (PostDefense has the same kind of invisible per-man denial
read, for the same reason.)

### Interaction block A — one defender, slot 1

| Row | B.PTS | Read |
|---|---|---|
| FLAT_50_CONTROL | 52.9 | The slot-1 anchor, exactly (11.6 / 35.5 / 21.7% use / 1.5 AST / 2.6 TO). |
| **HELP_ANCHOR** (HelpDef 90, OffBallDef 90) | **51.8 (−1.1)** | **The cleanest row in the session** — both team channels maxed, zero on-ball skill. One player running two off-ball channels = half of what a full elite defender is worth, spread across interior and perimeter zones. |
| **BALL_HAWK** (Steals 95, Hustle 85) | 52.8 (~0) | 2.2 personal steals, 8.7 rebounds — **opponent points unchanged.** Textbook confirmation of Finding 3: he stat-pads on steals and boards (Hustle claims share too), the opponent scores the same. |
| LOCKDOWN_POA (PerimDef 90, Steals 85, OffBallDef 75) | 52.3 (−0.6) | The point-of-attack stopper. Opponent perimeter diet compresses (3PA share 20.4 → 19.6), own STL up to 1.9, modest team-points dip — the on-ball dilution keeping it modest, as ruled. |
| **DEFENSIVE_LIABILITY** (all six at 10) | **55.0 (+2.1)** | One catastrophic defender lets the opponent score +2.1. He nearly vanishes from the box (0.4 STL, 0.4 BLK). |
| **ALL_G_ELITE** (all six at 85) | **50.6 (−2.3)** | One complete defender is worth ~2.3 opponent points. The terrible-to-elite swing (LIABILITY→ELITE) is ~4.4 points. |
| G_FLOOR_15 (all six at 15) | 54.6 (+1.7) | The near-floor mirror, milder than LIABILITY. |

### Interaction block B — the interior anchor, slot 5, with a real body

| Row | B.PTS | Own BLK / REB | Read |
|---|---|---|---|
| FLAT_50_CONTROL_S5 | 52.9 | 0.7 / 7.7 | The slot-5 anchor, exactly (9.4 / 34.3 / 49.8). |
| **RIM_PROTECTOR** (RimProt 90, HelpDef 85, Height 85, Wingspan 85) | **50.9 (−2.0)** | **1.2 / 10.6** | The marquee compound: against the bodiless RimProtection-90 walk row (−0.8, 0.9 BLK), the real frame adds ~1.2 points of suppression and +0.3 blocks. **The body×interior-defense compound is real and visible** — skill and length compounding, exactly the S50 body-amplifier thesis on the defensive side. |
| **INTERIOR_WALL** (RimProt 90, PostDef 90, HelpDef 90, Height 85, Wingspan 85, Strength 80) | **50.4 (−2.5)** | **1.3 / 11.8** | The full interior package: the biggest single-player suppression measured in the whole session. |
| POST_STOPPER (PostDef 90, Strength 80, Height 80) | 52.2 (−0.7) | 0.9 / 10.2 | The interior wall against the opponent's short/mid game; the big glass comes from the body plus the postness reclassification (Finding 2). |

### The generation-redesign feed (what the six defensive ratings buy)

- **RimProtection** is the one guard-your-man rating with a real, readable team effect — through
  second-chance defense (deters and blocks putbacks), not the diluted on-ball door. Its own block
  line is the strongest individual signal. It compounds with the body (RIM_PROTECTOR vs the bodiless
  walk row).
- **HelpDefense + OffBallDefense** are the clean team-suppression pair — undiluted (four off-ball
  defenders), symmetric (interior vs perimeter), no personal-box footprint. HELP_ANCHOR ≈ −1.1 with
  two channels; a full defender ≈ −2.3.
- **Steals** is an attribution dial at neutral pressure — it claims a bigger share of the team's
  steals but does not force new turnovers. The turnover-forcing side is pressure-gated and needs the
  coaching layer to express.
- **PerimeterDefense + PostDefense** do their headline job (worse shots for the man they guard) real
  but **diluted to a whisper at team scale** on this instrument — their clean reads are the block-
  attribution column and (PerimeterDefense) the opponent's diet reshape. PostDefense additionally
  moves the player's size classification more than it moves opponent scoring (Finding 2).
- The full defensive package is worth ~2.3 opponent points for one perimeter defender and ~2.5 for
  one interior anchor — real and clean at the archetype level, even though two of the six ratings'
  headline jobs are diluted at the walk level.

### Completeness ledger (the honest note)

- **Steals + PerimeterDefense** — role-aligned at slot 1; Steals fully measured as an attribution
  dial, its turnover-forcing side parked to the coaching layer (pressure-gated).
- **HelpDefense + OffBallDefense** — broadly and cleanly measured (four-off-ball team suppression);
  OffBallDefense's per-man denial channel is live-but-unmeasurable on a flat-clone opponent.
- **RimProtection + PostDefense** — curves measured at slot 1, the body×interior compound added on
  slot 5. The on-ball per-opponent suppression stays diluted (no opponent-slot box exists), and the
  slot-5 rows are archetype points, not full body-conditioned curves.
- **No additional Family-G bench work is required before synthesis.** The two-slot design measured
  everything this instrument can show; the remaining questions (per-opponent-slot suppression, the
  pressure-dialed turnover-forcing side of Steals, the PostDefense-as-size coupling) are design
  conversations for synthesis and the coaching layer, not more sweeps.
- **Adjacent flags carried:** the S50 wingspan→steals wiring question was open at this pass (Steals
  read as an attribution dial, not a deflection engine) — **now CLOSED, wired in S58** (the live
  steal-forcing floor gives long arms a two-sided perimeter-gated deflection term and makes the steal
  contest live at neutral; see Finding 3's S58 update above); the S49 unforced-turnover reshaper
  (shipped S56) sits adjacent to the Steals attribution machinery.

### The undiluted reruns (S54 — the per-man readout + lineup stacks)

The slot-guards-slot dilution above (the on-ball ratings reading as a whisper on B.PTS because the
swept defender guards only ~1/5 of the opponent's shots) was an **instrument artifact, not the
ratings' meaning**. S54 added a per-man readout — each Team B slot's own attributed shooting line —
and reran the diluted walks through it. The covered man is the **mirror-slot opponent** (slot-guards-
slot; his primary shots and his own putbacks are both contested by his mirror), read as a primary-
matchup proxy that includes all his attributed attempts. On *his own line* the reads are clear:

| Rating (0 → 99 on the swept defender) | Covered man's line | Reading |
|---|---|---|
| **PerimeterDefense** | FGA **12.8 → 12.8** (flat); FG% **37.8 → 34.9**; 3P% **29.5 → 19.4**; 2P% 40.0 → 38.7 | On-ball contest. Volume never moves — the offense does not hunt the on-ball mismatch — but his looks get harder, hardest on the three. |
| **RimProtection** | FGA 12.8 (flat); 2P% **40.5 → 36.7**; 3P% ~23 (flat) | Kills interior makes, not volume or the three. (Plus the S52 second-chance/putback team channel.) |
| **PostDefense** | FGA **13.7 → 12.0**; 2P% **41.2 → 37.1** | The one that takes **both** — it is a *denial* rating (touch redistribution via postness), so it lowers his volume as well as his efficiency. |
| **Discipline** | FTA **3.7 → 3.6** (essentially flat); nothing else moves | **Was measured on the WRONG meter (S61 correction).** FTA is shooting-fouls-only, vs a flat-50 shooter — its foul wire IS live (moves the shooting-foul rate ~5% vs an average drawer, more vs a magnet). Make-% was one unbuilt half; **the Discipline make-shave SHIPPED S61 (Effect A)**. The **non-shooting foul half SHIPPED S62 (Effect B)** — Discipline now gates each defender's own reach-in rate PER-MAN (hacker 1.48 / average 1.13 / lockdown 0.78 propensity), and every non-shooting foul carries his name. Now wired on THREE channels. See Family H. |

**Finding — perimeter defense is point-neutral without a rim deterrent (the S54 design result).**
Stacking PerimeterDefense at 99 across 1 → 2 → 3 → 5 slots confirmed the mechanism and surfaced a
bigger truth. The opponent's shot-diet shift strengthens then **saturates after three slots** (Team B
three-share 20.4 → 19.2 → 17.7 → 16.8 → 16.8; rim-share 28.8 → 33.0), and **uncovered men never gain
volume** (slots 3–5 hold 12.1 / 10.7 / 10.6 FGA at every rung — the offense-doesn't-hunt-the-mismatch
result, a design confirmation). **But B.PTS barely moves and even ticks up mid-ladder** (52.9 → 52.8 →
53.2 → 53.3 → 52.5): perimeter pressure pushes the opponent off the three and into higher-percentage
twos, and it nets out even. **Perimeter defense alone is point-neutral; it is a diet-shaper that pays
only when a rim deterrent is waiting to punish the shots you funnel inside.** Do not size perimeter-
defense value in isolation. This is exactly why the balanced defense works and a pure perimeter wall
does not.

> **STATUS (S59, 2026-07-14) — the wire that answers this has LANDED; the curve above is NOT yet
> re-measured.** The finding's *mechanism* diagnosis drove the three-pass rim-access design, and
> **Pass A shipped (S59/P65)**: `Matchup.ApplyDriveGate`, a per-man drive gate in Roll G that lets the
> matched on-ball defender remove part of a walled perimeter driver's rim/short access and re-route it
> to a contested Long/Three (diet only — no make% wire). On the real divvy'd population it moves
> league-scale rim 36.3% → 33.8% and three 36.0% → 38.7%. **But the numbers in this section were
> measured on the pre-gate engine and stand as-is until the PerimeterDefense stack walk is re-run** —
> the two S54 sub-results most likely to move are "B.PTS barely moves" (the gate now funnels toward
> *worse* shots, not better ones) and "uncovered men never gain volume" (which is Pass B's job, not
> Pass A's, and so should still hold). **Retirement requires a re-measurement, not a wire** — the exact
> S50→S55→S55.1 pattern (S55 shipped the height term; the "length buys almost no scoring" finding was
> retired only after S55.1 re-ran the sweeps and diffed the curves). Queued, not claimed. Two further
> caveats for whoever re-measures: Passes B and C are still unbuilt, so this is a partial answer; and
> the gate is **not level-neutral below rating ~48** (see status.md Open — an absolute unlock anchor
> inside a relative comparison), which will bias any low-rated bench world downward.

**Help defense compounds cleanly.** The help stack lowers the make itself rather than relocating the
shot, so it compounds **roughly linearly** with no point-neutral problem: B.PTS 52.9 → 52.3 → 51.6 →
50.9 → **49.5** at five helpers, covered men's interior FG% falling every rung.

**The headline number — a real defense is worth ~10.5 points.** BALANCED_DEFENSE (two lockdown POA +
a rim protector + a help anchor who also denies off-ball) holds Team B to **50.5** (covered men into
the low 30s, three-share 18.6%); SIEVE_5 (all six defensive ratings at 10 on all five) bleeds
**61.0** (everyone ~42% from everywhere). A sensibly-built defense is worth roughly **−10.5 opponent
points** against a sieve — undiluted, now readable man-by-man.

**A pre-existing bench property, now visible.** The five flat-50 Team B men are *not* identical on the
per-man surface — guards (slots 1–2) take ~12.8 FGA, bigs (slots 4–5) ~10.6, from the bench's usage
curve. Not an S54 artifact; read each ladder as each man vs *his own* flat-50 line. And the per-man
line's putback dilution (a putback reads rim-protection, not the swept perimeter rating) is why the
covered-man perimeter curve bottoms near 34.9% rather than lower — isolating his rim shooting would
need a per-slot-per-zone engine emission (out of scope). **These four reads are now measured undiluted;
synthesis follows S54.**

## Family H — Cognition (BasketballIQ, Discipline) — measured Session 53

The sixth and final family through the S47 ruler, and the smallest — two isolation walks (0→99 in
5-point steps, 2,000 games/rung) plus one seven-row interaction block, all on **slot 1**. **98,000
games**, pure config-and-run — no engine file, no `config.json`, no readout change, no Monte Carlo.
The slot-1 anchor held exactly (both 50-rungs and FLAT_50_CONTROL reproduced 11.6 PTS / 35.5 FG% /
21.7% use / 1.5 AST / 2.6 TO). **This section closes the measurement arc: every rating on the Player
has now been walked through the ruler.**

The two ratings live on opposite surfaces. **BasketballIQ is an offensive read on the swept player
himself** — his own make%, his own assist share — and reads clean and undiluted on his own box.
**Discipline is a defensive read** — the defender's light tap in the shooting-foul contest — and is
slot-guards-slot diluted exactly like the Family G on-ball ratings.

### The wiring (what the source actually reads)

- **BasketballIQ — three source reads, all confirmed at the gate.** (1) The make-door bonus
  (`RollHGenerator.cs`, Phase 50): the last make% term, a proportional sprinkle on the settled make%,
  `bump = makePct × IqMakeSensitivity(0.08) × zoneWeight × iqProgress`, where
  `iqProgress = clamp((IQ − 50)/49, 0, 1)`. Zero at/below IQ 50, zero at the Rim (zone weight 0.0),
  full at Three/Long (1.0), partial Mid (0.7) / Short (0.3). Driven by the shooter's **own** IQ,
  absolute. (2) The assist weight (`AssistPicker.cs`): `0.5·Passing + 0.35·Playmaking + 0.15·IQ` —
  IQ the smallest of the three inputs. (3) The iqFactor (`AttentionGenerator.cs`, 0.85→1.15), which
  multiplies effective playmaking.
- **Two of BasketballIQ's reads have small team-level tails, not purely own-box** (flagged at the
  gate, both confirmed as whispers by the data): the assist weight also feeds `LineupPassingFactor`,
  so a higher-IQ lineup mean lifts the *team* assisted rate a hair (creation, not just credit); and
  the iqFactor read routes IQ → effective playmaking → the team's conversion-quality → Roll H's C4
  passing-converter, a *team* halfcourt make bonus. Both move on the flat bench because the swept
  player is the only non-clone — but both compute to whispers (team points moved 52.8 → 53.0 across
  the whole walk), consistent with S49's finding that even *Passing* at the higher 0.5 weight left
  team FG% dead flat.
- **Discipline — one source read.** The defender-light half (weight 0.2) of the shooting-foul contest
  (`Matchup.cs`): `0.8·(FoulDrawing − 50) − 0.2·(Discipline − 50)`, saturating toward a per-zone foul
  ceiling/floor. It reads on the specific man the swept defender guards (`DefenderPicker`) — the
  slot-guards-slot wall. No other Discipline read exists anywhere in the tree.

### The two curves (isolated walks, swept player)

| Rating | Signal | Shape |
|---|---|---|
| **BasketballIQ** | own FG% 35.5 → 35.5 → **36.6**; own 3P% 22.2 → 22.3 → **24.1** (+1.8pp); own AST 1.3 → 1.5 → **1.7**; PTS 11.6 → 11.9 | **A one-sided hockey-stick.** The make bonus is dead flat across the entire 0→50 half, then rises above the midpoint — and it is **perimeter-only**: the shot mix never moves, there is no rim gain, so this is "a smart player finishes the good look he already had," not more or different shots. The assist claim is the exception to the gate — it rises **gently across the whole range** (the assist weight is linear in IQ, not clamped at 50), a small share-claim, the weakest of the three assist inputs as designed. |
| **Discipline** | own box dead flat (FG% 35.5–35.6, no curve); Team B points flat at 52.9 across all 21 rungs | **Below the instrument's resolution.** The read is real in the source, but the swept defender bends the foul rate only on the ~1/5 of Team B's shots his own man takes, and the readout has no Team B FTA column — the only proxy (Team B points) is flat. See the ledger: the effect is source-real and arithmetic-bounded, not bench-measured. |

### Finding 1 — BasketballIQ is a perimeter make-finisher plus a small assist claim, and the gate splits the two cleanly.

The make bonus does exactly what its clamp says: nothing from IQ 0 to 50 (own FG% pinned at 35.5),
then a clean rise to 36.6 by IQ 99, concentrated on the perimeter (3P% +1.8pp, no rim movement). It
rewards a good look proportionally and is a rounding error on a bad one — a genius-IQ poor shooter is
still a poor shooter. The assist read is a genuine *claim*: his own assists rise only 1.3 → 1.7
across the full walk, and — unlike the make bonus — the claim is ungated, drifting up steadily on
both halves of the range. Nothing fired below the IQ-50 knee (the trace trigger that correctly did
not fire), no rim bonus appeared (zone weight 0.0), and the AST swing never approached a Passing-sized
slope (IQ is weight 0.15, not 0.5).

### Finding 2 — the MAESTRO/IQ_ONLY pair proves the split: passing creates the assists, IQ finishes the shots.

`HIGH_IQ_MAESTRO` (IQ 95 with elite passing and playmaking behind it) posts 2.3 assists — the assist
jump comes almost entirely from the 85 passing/playmaking. `IQ_ONLY` (IQ 95, nothing else) moves
assists just +0.2 off control (1.5 → 1.7) — the pure 0.15 claim with no passing skill to distribute.
Both rows carry the **same** make-% lift (FG 36.7 vs 36.5, 3P 24.3 vs 23.9) because both have IQ 95.
So the two IQ effects are orthogonal and separable: elite passing/playmaking does the assist-creating;
IQ only claims a slightly bigger slice of it, while independently finishing the perimeter looks. The
two team-level whisper tails stayed whispers throughout (team points 52.8 → 53.2 at most).

### Finding 3 — Discipline is wired and real, but below this instrument's resolution at team scale (the honest gap).

The swept player's own box is dead flat across the whole Discipline walk (FG% 35.5–35.6, no curve —
correctly, since Discipline is a defensive read with no offensive wire; a monotonic own-FG curve here
would have been the trace trigger, and it did not appear). The effect lives on Team B's foul rate,
and the readout has no Team B FTA column. From the live parameters, the per-man effect is a source-real
whisper: on the man he guards, the shooting-foul rate bends by roughly +0.7pp (Rim) at Discipline 0
down to −0.14pp (Rim) at Discipline 99 — **asymmetric**, because the foul ceiling sits far above the
base while the floor is just beneath it, so an undisciplined defender moves more than a disciplined
one. Diluted 5:1 to the man he guards, that lands as roughly **−0.02 (Disc 99) to +0.08 (Disc 0)
FTA/game** on Team B. Team B points staying flat at 52.9 is **consistent** with a whisper of that
size — it is not a measurement of one. This is the same limitation family as the Family-G on-ball wall
and the S51 transition-share gap: the per-man effect is real, the instrument dilutes and hides it.
(Note: `DISCIPLINED_STOPPER`'s Team B shot-mix shift — Rim 28.8 → 29.3, 3PA 20.4 → 20.0 — is the
PerimeterDefense 80 doing its S52 job, not the Discipline.)

### Finding 3 CORRECTED and FULLY ANSWERED (S61 + S62): Discipline was quiet on the WRONG meter; now wired on three channels.

The S53/S54 "near-inert" read measured **the wrong meter twice over**, confirmed by an S61 audit
against live source + a real run:
- **The foul wire is LIVE, not dead.** `Matchup.FoulRate` reads `defender.Discipline` and is called only
  from the **shooting-foul** door (`RollHGenerator.cs:601`). A 0→99 Discipline walk moves the *shooting-foul
  rate* ~5% relative against an average foul-drawer, and up to ~37% against a foul-magnet — a two-sided
  drawing-vs-restraint contest, so the swing rides on where the shooter's FoulDrawing sits on the tanh
  curve. The "FTA 3.7 → 3.6" read looked flat because **FTA is shooting-fouls-only** (35% of defensive
  fouls) **and** the bench shooter was flat-50 (a small swing on an already-small base). Quiet by tuning
  (`DefenseFoulWeight` 0.2), not absent.
- **The make-% shave was the genuinely-unbuilt half — and it SHIPPED (S61, Effect A).** Discipline touched
  make% nowhere (confirmed by grep of the make chain, `EffectiveRating`, and the Roll G blend). S61 added
  `RollHGenerator.ApplyDisciplineShave`: a small, ABSOLUTE, FLAT-across-zones reduction on the man's make%,
  read off the defender's own Discipline, symmetric about 50 (lockdown shaves, average neutral, liability
  gives up a cleaner look). This is the **man-to-man** wire (per the S61 scheme-toggle ruling). Modest by
  design (~1.5% relative at max Discipline); magnitude page-tuned. See design.md "Session 61" and Phase 67.
- **Effect B (the "fouls committed" half) SHIPPED (S62).** 65% of defensive fouls are non-shooting (reach-ins
  / off-ball grabs), and were authored as an anonymous team rate in Roll B/F and charged with no name — Discipline
  read nothing there. S62 rebuilt that: each defender carries his own reach-in propensity, Discipline PRIMARY
  (`discFactor = 1 − ReachInDiscSpan·clamp((D−50)/49,−1,1)`, symmetric about 50, low D → more fouls; propensity
  hacker 1.48 / average 1.13 / lockdown 0.78, a ±31% swing), with a small athleticism secondary and a slight
  lineup-relative perimeter lean. The five propensities set the team rate through an aggregate that is exactly
  1.0 at five-average (today's rate preserved; one hacker → 1.062, adds not redistributes; stacking linear), and
  every non-shooting foul carries a committer's name — drawn ∝ propensity for reach-ins, ∝ the Discipline factor
  for situational fouls. The Hustle→foul coupling was retired to the coach layer. **So Discipline is no longer a
  "candidate wiring gap": all three channels are live** — the shooting-foul contest (quiet by tuning), the make-%
  shave (S61), and the per-man non-shooting foul rate (S62). See design.md "Session 62" and Phase 68. What the
  bench still cannot show: the per-man spread needs Discipline to VARY across a lineup (the flat-50 bench seats it
  at 50 everywhere, so the aggregate sits at 1.0 and the box reads flat) — the spread is proven in Phase 68's
  draw test with deliberately varied Discipline, and shows in the box score's new NSF column on a real population.

### The interaction block (seven rows, slot 1)

| Row | Dials | Read |
|---|---|---|
| FLAT_50_CONTROL | — | The slot-1 anchor, exactly (11.6 / 35.5 / 22.3 3P / 1.5 AST / 2.6 TO). |
| **HIGH_IQ_MAESTRO** | IQ 95, Pass 85, Play 85 | PTS 11.9, FG 36.7, 3P 24.3, **AST 2.3**, A.PTS 53.2. The floor general — assists jump (from the passing/playmaking) *with* a perimeter make lift (from the IQ). |
| **IQ_ONLY** | IQ 95 | PTS 11.8, FG 36.5, 3P 23.9, AST 1.7. The clean IQ look: +1.0 FG / +1.6 3P from the make bonus, +0.2 AST from the pure claim, rim flat. |
| DISCIPLINED_STOPPER | Disc 95, PerimDef 80 | 11.6 / 35.6, B.PTS 52.9. Team B points flat; the visible Team B diet shift (fewer threes, more rim) is the PerimeterDefense 80, not the Discipline. |
| UNDISCIPLINED | Disc 5 | 11.6 / 35.6, B.PTS 52.9. The hack-happy defender leaves no readable footprint on this instrument (no FTA column, diluted). |
| ALL_H_ELITE | IQ 85, Disc 85 | 11.8 / 36.4 / 23.8, AST 1.6. Both at once — the perimeter make lift shows, the Discipline half does not. |
| H_FLOOR_15 | IQ 15, Disc 15 | 11.6 / 35.5 / 22.1, AST 1.4. IQ 15 is below the make gate, so make% is already at floor (no lift); the assist claim is at its smallest (1.4 < 1.5). |

### The generation-redesign feed (what the two cognition ratings buy)

- **BasketballIQ** is a small, proportional **perimeter make-finisher on good looks above the
  midpoint** (own 3P% +1.8pp at the top, nothing at/below IQ 50, nothing at the rim) plus a **minor
  assist claim** (the weakest of the three assist inputs — it credits, it does not create). Its two
  team-level channels (conversion-quality make bonus, lineup-passing assist rate) are real in the
  source but whisper-scale. It should be sized as a modest efficiency-and-credit rating for perimeter
  players, not a volume or creation driver.
- **Discipline** is a **two-channel defensive-restraint rating** (as of S61, and no longer a "candidate
  wiring gap"): (1) a defender-light tap in the **shooting-foul** contest (live, quiet at
  `DefenseFoulWeight` 0.2 — the S53/S54 "near-inert" read measured this on shooting-fouls-only FTA vs a
  flat-50 shooter and missed it), and (2) the **make-% shave** shipped S61 (Effect A) — a small, absolute,
  flat-across-zones reduction on the man's make%, symmetric about 50, man-to-man. Its third intended channel
  — reduced **non-shooting** fouls committed (Effect B, 65% of defensive fouls) — is a ruled per-man
  foul-model build, not yet in. Size it as a modest, small-magnitude defensive rating: worthwhile stacked
  across five defenders, barely a game from one. Magnitudes page-tuned, never suite-asserted.

### Completeness ledger (the honest note)

- **BasketballIQ** — measured clean on its own box (the make bonus and the assist claim both read
  directly on the swept player's line). Its two team-level tails (the conversion-quality make channel
  and the lineup-passing assist-rate channel) were flagged at the gate and confirmed as whispers by
  the walk (team points 52.8 → 53.0).
- **Discipline** — the S53/S54 "3.7 → 3.6 near-inert" read was **corrected in S61**: it measured
  shooting-fouls-only FTA (35% of defensive fouls) against a flat-50 shooter, hiding a live-but-quiet foul
  wire (the shooting-foul rate moves ~5% vs an average drawer, more vs a magnet). The genuinely-unbuilt half
  — make% — was **shipped in S61 (Effect A, the make-% shave)**, and the "fouls committed" half (Effect B,
  non-shooting) has a ruled per-man build path. **No longer a candidate wiring gap.** (See Finding 3
  CORRECTED above.)
- **No additional Family-H bench work is required before synthesis**, and no additional bench work of
  any kind remains — **the measurement arc is complete.** The remaining Discipline question
  (per-opponent-slot foul suppression) is the same instrument gap logged for Family G, a design/
  readout conversation, not another sweep.
- **The synthesis pass is DONE, and its first ruling is SHIPPED.** This document was read end-to-end
  (Emmett's S49 standing ruling, satisfied after S54) and produced the cross-family wiring reading
  (`attribute-wiring-synthesis.md`, Emmett's project files). Its top-ranked item — the S50
  height-over-defender make term, the one that most gated the height→skill lean sizing — **shipped in
  Session 55** (see the Physical package section above and design.md's make-door section). The
  remaining wiring track, in the synthesis's order: the ball-dominance/initiation layer (next), the
  unforced-turnover channel, the S51 no-post-hunt diet gap, the S54 defensive findings (perimeter-
  defense point-neutrality; Discipline near-inert), and the remaining S50 physical questions. The
  pressure-dialed channels (BallHandling, Steals turnover-forcing) stay coaching-layer, scoped out.

---

## Cross-cutting — the BODY-GAP GATE and divisional sorting — measured Session 59.2 (2026-07-14)

**The first sweep run against a DIALED opponent** (the `opponentDials` upgrade shipped this
session — see design.md). Not one attribute family: the question underneath all of them, and the
one the world/divvy layer is downstream of. Emmett's framing: *"this is how virtually every
attribute that counts stats should work — when athleticism and/or size are major advantages, skill
advantages get muted hard; when the bodies equalize, skill is felt hardest. It's the size and
athleticism which dictate where you can play, more than anything else."*

### The headline finding

**The gate is real, it works, and it was already built.** Same player, same elite scoring rating,
walked up four levels of competition. Real level-appropriate rosters on BOTH sides (his teammates
too — an earlier cut of this bench left his teammates at flat-50 and had to be thrown out).

**The relative-engine control — this had to hold first:**

| level | a typical player at that level | team score |
|---|---|---|
| D3 | 11.6 pts / 35.3% | 53.6 – 53.4 |
| D2 | 12.1 / 36.9% | 54.7 – 54.6 |
| Mid-major | 12.2 / 38.8% | 56.0 – 56.0 |
| High-major | 12.3 / 38.9% | 56.9 – 56.7 |

A typical player scores ~12 at **every** level and every game is even. **The relative engine is
verified end-to-end across four levels of competition** — "the same mathematical drama at D3 as at
D1" is not an aspiration, it is measured.

**The gate — one player (elite scoring skill 85), two bodies, four levels:**

| level | typical | **6'1", slow** (body 30) | **big, athletic** (body 78) |
|---|---|---|---|
| D3 | 11.6 / 35.3% | **19.6 / 44.5%** — star | **27.6 / 57.6%** — monster |
| D2 | 12.1 / 36.9% | **17.8 / 42.8%** — very good | **24.1 / 53.8%** |
| Mid-major | 12.2 / 38.8% | **14.0 / 38.0%** — ordinary, already shooting below average | **19.8 / 48.9%** |
| High-major | 12.3 / 38.9% | **10.7 / 32.0%** — BELOW average | **16.6 / 44.0%** — still a star |

**Identical skill ratings. The body alone sets the ceiling.** The small player tops out around D2
and is a below-average high-major player; the big one is a star in any league. His shooting decays
44.5 → 42.8 → 38.0 → 32.0 as he climbs — that decay *is* the muting. **Every part of the divisional
thesis checks out: bodies sort divisions, skill decides among equals, and the D3 star is stranded
when he plays up.**

### The mechanism (and why it was missed twice)

It is **not** a gating multiplier in the rating math. Every door composes skill and body
**additively** — make (`baseline + skill + physical + height`), diet (`skillLevel + physLevel`),
block (fixed per-zone weights × skill + × length). Nothing reaches over and shrinks the other's
term. The muting is delivered **by the bounded logistic that converts rating → probability**: a
large body gap shoves a player toward the curve's floor, where the same rating difference buys a
much smaller make% difference. design.md's Phase-4 additive-vs-multiplicative ruling named this
explicitly ("a monster physical edge crushes a player toward the floor, where the make%-distance
between him and a scrub **compresses** — athleticism 'wipes out the skill gap' in the box score").
**The design intended this from the start and the engine delivers it.**

Two ways this was mis-called during the S59.2 conversation, both recorded because both are
instructive: (1) reading the additive rating math and concluding "no muting exists anywhere" —
wrong, because the muting lives one stage later, in the curve, not the arithmetic; (2) measuring it
against a flat-50 opponent, getting a flat line, and concluding the mechanism was absent — wrong,
because flat-50 caps the gap at ~±30 and the compression does not bite until well past that. See
the document-wide caveat at the top.

### Rebounding — the same shape, on Emmett's own example

His test case: *"an extremely skilled 6'4", 190, weak-athlete rebounder just doesn't do much against
a front line of 6'10" hyper athletes, sort of regardless of how good their rebounding is."*
Rebounds/game, swept slot 5, vs a dialed high-major front line (bodies ~80, hands 68):

| his body | avg hands (50) | elite (85) | perfect (99) | what hands are WORTH |
|---|---|---|---|---|
| 20 — his archetype | 3.2 | **4.8** | 5.3 | **+1.6** |
| 50 | 5.0 | 7.2 | 8.0 | +2.2 |
| 80 | 7.8 | 10.5 | 11.4 | **+2.7** |

**Perfect hands + tiny body = 5.3 boards. Average hands + freak body = 7.8.** The freak wins by 47%
with worse hands — the ruling holds. Against flat-50 the same player got 8.1 boards and the
rating's value *rose* monotonically with body (+2.7 → +3.7, never collapsing): the flat-50 bench
inverted the finding.

**Note the tension with the S45 finding above** (rebounding is rating-gated; the body amplifies but
does not grant). The two are **compatible, not contradictory**: S45/S46 fixed the body being a pure
*multiplier* on the rating (a freak with zero rating grabbed nothing), giving the body an additive
floor so it pays unconditionally. This finding is about the *opponent's* body gap muting the
rating. Body unconditional + rating conditional-on-parity is one coherent model, and both are live.

### The open magnitude question (NOT closed here)

Emmett's ruling is "not much, *sort of regardless* of rating." Today the small player's rating still
buys him +1.6 boards and +6.0 FG% at the extreme. Is that "not much," or should it be nearer zero?
**Deliberately not answered**: that is a magnitude question, and per the page-only calibration
principle it waits for a real generated population. Emmett's call (2026-07-14): *"until we get an
actual variety of real players, I'm not sure we get much from this"* — the synthetic bench had
already produced three wrong readings, and a fourth would not have taught us anything.

### Side finding — athleticism raises three-point shooting

Walking body alone (rating held at 50), 3P% climbs 20.6% → 27.4%. The physical shift is applied
**zone-independently**, so a pure athletic edge helps from 25 feet exactly as it helps at the rim.
Recorded, not judged. May be correct, may be a wiring question; it has never been ruled on.

---

## HierarchyRank / usage — the dial is broken and the RAIL is doing the design's job — measured Session 59.2 (2026-07-14)

Reached by chasing one number: the D3 star above scored 19.6, and Emmett wants D3's best scorers
around 25. He diagnosed it immediately — *"no coaching strategy as well as 50s across the board
tilting usage."* Half right, and the half that was wrong is the interesting half: the teammates were
NOT flat-50 (they were level-appropriate). **`HierarchyRank` was pinned at its baseline 5 for every
player the bench has ever built**, so no sweep in this document has ever had a go-to guy.

### Finding 1 — the top half of the depth chart is dead

Teammates frozen at rank 5, only the star's rank walked (D3, skill 85, body 30):

| rank | usage | pts |
|---|---|---|
| 5 | 29.1% | 19.6 |
| 6 | 40.4% | 27.3 |
| 7 | **45.1%** | 30.7 |
| 8 | **45.1%** | 30.9 |
| 9 | **45.2%** | 31.0 |
| 10 | **45.2%** | 31.0 |

**Ranks 7, 8, 9 and 10 are the same player** — identical to a tenth of a point. A coach cannot
distinguish a first option from a franchise player. Proven to be `RollE.UsageRail` (0.52) by lifting
it to 0.95 and re-running: 7/8/9/10 then separate to 48.6 / 54.3 / 54.3 / 54.4 — i.e. they **hit a
second wall** at 54%, which is `RollE.UsageFloor` (0.09) × 4 teammates guaranteeing 36%, capping any
one player at 64% of selections. Two hard clamps stacked.

### Finding 2 — one rank step is worth ELEVEN usage points

Rank 5 → 29.1%, rank 6 → 40.4%. **There is no setting that produces the ~33% a 25-point D3 scorer
needs**; the dial steps straight over it. And rank 6 (40.4%) is already past what real college
basketball produces (~32–36% for its most ball-dominant players), so the scale spends its entire
working range at or beyond realism before the rail even catches it.

**The whole depth chart, identical players, chart 10/7/5/3/1** (so this is the dial alone):

| slot | rank | usage |
|---|---|---|
| 1 | 10 | 45.7% |
| 2 | 7 | 22.0% |
| 3 | 5 | 12.4% |
| 4 | 3 | 10.1% |
| 5 | 1 | 9.9% |

One star and four ghosts. **Slots 3/4/5 are mashed against the 9% floor and indistinguishable** —
the middle of the roster does not exist. Lowering `HierarchyExponentNeutral` (1.0 → 0.7 → 0.5 →
0.35) moves the top (45.7 → 42.6 → 38.9 → 35.5) and **cannot move the bottom at all** (#4/#5 stay
welded at ~9.9 at every exponent).

### Finding 3 (the structural one) — the star's usage is not designed, it is CLAMPED

On identical players, exponent 0.5 lands the #1 at 38.9% and looks like a fix. **On a realistic
roster it does nothing**: with talent 85/64/58/55/52 aligned to chart 10/7/5/3/1, the #1 posts
**44.8% at exponent 1.0 and 44.8% at exponent 0.5** — unchanged, because *skill alone* already
pushes him past the rail before the depth chart speaks. Only at exponent 0.35 does he drop below it
(42.6%).

**So `UsageRail` — designed as participation protection, an emergency brake — is the thing actually
authoring every real star's usage.** Two channels (skill and depth chart) compound past realism, and
a hard clamp catches the result at ~45%. Nobody ever decided he should be at 45%; that is simply
where the fence is. This is also *why* the top of the dial is dead: everyone from rank 7 up is
already against the fence. Emmett's 40% ceiling is therefore **not a tuning job** — dropping the
rail to ~0.46 would hit 40% while leaving the dial just as dead, one notch lower.

### Finding 4 — volume is FREE at today's calibration (the tax exists; it is tuned to a whisper)

> **[S60.2] MEASURED ON A REAL POPULATION — this finding is no longer a flat-50 inference.**
> 3,000 games, 100 teams drawn from the live Pass-2 cohort (top 500 of 46,000 by the generator's
> own `Rscore`). The result is worse than "free" — **hoarding is a FREE LUNCH:**
>
> | | USG% | FG% | PPP | PPG |
> |---|---|---|---|---|
> | chucker (top usage decile) | 39.2% | 39.6% | **0.853** | 26.4 |
> | even-share (median decile) | 17.7% | 37.1% | **0.775** | 10.8 |
>
> **The man taking 39% of the shots is MORE efficient than the man taking 18%, at 2.2× the load.**
> The engine currently advises: always feed one guy. And **reshaping usage across the entire range
> costs the league 0.4 points a game** (`UsageExponent` 2.0 → 1.0: max usage 47.9% → 35.6%, league
> FG% 38.3% → 37.9%). The arithmetic matches the dial exactly — pressure 0.28 × scale 0.12 = a 3.4%
> haircut ≈ 0.4pp league-wide. **`PressureVolumeTaxScale` = 0.12 is doing precisely what 0.12
> predicts.** Walking it to **1.00** closes the chucker's edge to +0.001; **~0.7** leaves a real star
> a small edge and makes a 48%-usage man pay ~6pp of FG%. Calibration, deferred by Emmett's ruling
> (wiring first). Full record: journal S60.2.

| rank | usage | **FG%** |
|---|---|---|
| 5 | 29.1% | 44.5 |
| 6 | 40.4% | 44.7 |
| 7 | 45.1% | 44.9 |
| 10 | 45.2% | **45.8** |

Usage climbs 16 points and **shooting goes UP**. This blocks Emmett's ruling directly
(2026-07-14): *"I'm not against, in extreme situations, it being higher than that, but it should
come with a pretty hefty efficiency hit."* That player cannot exist at these settings.

**CORRECTED (S60): this is a CALIBRATION failure, not a missing mechanism.** The tax has existed
since Phase 17 and is live on every above-share shooter: `makePct *= (1 − pressure ×
PressureVolumeTaxScale)` with the scale at **0.12** — a whisper by choice — plus a residual channel
(`PressureResidualPenaltyScale` 2.0) that *can* bite hard, but only for a forced specialist under
defensive attention. The earlier phrasing here and in status.md — *"the mechanism is missing, not
mis-tuned"* — was **correct on the low side and wrong on the high side**. The honest statement: the
curve was **half-built, and the built half is tuned to a whisper**. S60 built the missing half (see
Finding 7); the tax's magnitude remains a page question, deliberately untouched under Emmett's
anchor ruling.

### Finding 5 — the dial is dead at BOTH ends, and its authority collapses next to good players

The role-player probe (below) exposed what the star-only walk could not: **ranks 1, 2 and 3 are also
the same player** (13.0/13.1/13.1% usage, 34.5% FG, identical to a tenth of a point) — all floored.
So the scale is dead 1–3, dead 7–10, and only **4/5/6/7 do anything: four working settings out of
ten.** Worse, on a roster of *elite* teammates (skill 90) the dial stops working almost entirely:
ranks 1 through 7 all produce 13.5% usage and 37.1% 3P%, and even rank 10 only reaches 20.3%,
because the teammates' skill channel drowns the hierarchy weight. **`HierarchyRank`'s authority is
not merely coarse — it is a function of the surrounding talent**, and next to good players a coach
cannot make a specialist a high-usage option at all.

### Finding 6 — ROLE does nothing; TEAMMATE GRAVITY does everything (the decomposition)

Emmett's design claim (2026-07-14): *"an underathletic but really good shooter, even in a Big 12
environment, would still shoot okay if he was truly the fourth or fifth option and allowed to just
shoot open threes. But forced to be the top option, that percentage might be right. In general,
lower usage results in higher efficiency."* Measured on exactly that archetype — Outside 90, body
30, everything else ordinary — on a high-major roster vs high-major opponents. **The two halves
separate completely.**

**Teammate gravity (his role held fixed at low usage) — LIVE and strong:**

| his teammates' skill | his 3P% | his FG% |
|---|---|---|
| 55 | 27.7 | 30.9 |
| 68 | 32.1 | 33.7 |
| 80 | 34.1 | 36.3 |
| 90 | **37.1** | 38.3 |
| 97 | **37.7** | 39.2 |

**+10 points of three-point shooting from teammate gravity alone.** The S48
gravity→attention→relief channel is real and substantial, and it delivers Emmett's player: the
underathletic sniper hits **37.1% from three in a Big 12 environment** as a low-usage option beside
real talent.

**His role (elite teammates held fixed) — DEAD:**

| rank | usage | 3P% | FG% |
|---|---|---|---|
| 1 | 13.5% | 37.1 | 38.3 |
| 2 | 13.5% | 37.1 | 38.3 |
| 4 | 13.6% | 37.1 | 38.3 |
| 5 | 13.6% | 37.1 | 38.3 |
| 7 | 13.7% | 36.7 | 38.0 |
| 10 | 20.3% | 36.1 | 36.9 |

**He shoots 37.1% whether he is the first option or the fifth.** On a skill-68 roster the same
decomposition gives 34.5% at 13% usage vs 31.7% at 43% usage — **a thirty-point usage drop buys 2.8
points of shooting**, roughly 0.1pp per usage point, where real basketball is three to five times
that.

**So the ruling "lower usage results in higher efficiency" is NOT wired.** What is wired is "better
teammates → higher efficiency." **Context changes a player's value; role does not.** Emmett's
role-player misfits therefore half-exist: the good-team half works, the reduced-role half does not.

**This is the SAME hole as Finding 4, from the other side** — but the two sides are not the same
kind of problem, and S60 separated them. Scarcity had **no reward at all** (no term existed: every
below-share player read exactly 0). Volume has a price that is **tuned to a whisper** (the tax is
live; `PressureVolumeTaxScale` is 0.12). One flat usage↔efficiency curve, two impossible players —
but one half was *missing* and the other merely *quiet*. **Emmett's anchor ruling (2026-07-14): the
~31.7% at 43% usage is plausibly CORRECT; it is the low-usage end that is wrong.** So the fix
anchors the top and lifts the bottom — it is not a re-tune of the star. **Finding 7 records what
shipped.**

### Finding 7 — the relief half SHIPPED (S60), and it pays on INTENT, not on touches

`UsageRelief = max(0, equalShare − finalShare)` is now stamped by Roll E beside the existing
pressure and paid by Roll H as a multiplicative make% bonus, `makePct × (1 + relief ×
UsageReliefBonusScale)`, applied after the C3 penalty block and before the C4 passing converter.
Magnitude (`UsageReliefBonusScale` = 1.0) is a **placeholder**; the shape is what was signed off.
Emmett's sniper now exists: at 13.5% usage his 3P goes 37.1 → ~39.5 at the placeholder. Full design
record in `design.md` (Phase 17 + Session 60) and journal S60.

**The finding that changes how this family is described.** The `shares` array both halves of the
curve read is **post-floor/rail but PRE-tilt and PRE-denial**. Roll E stamps pressure and relief,
and *then* the pie is bent twice more — by the Phase 27 attention tilt and the Phase 46 per-slot
denial. So the curve pays on the share a team's offense **intends** to give a man, not the touches
he actually gets. Measured on the frozen corpus through the live generator (S60):

| player | share the offense INTENDS | realized FGA share | relief | bonus |
|---|---|---|---|---|
| Marcus Webb | 20.06% | 22.7% | 0.0000 | none |
| DeShawn Pryor | 23.89% | 26.8% | 0.0000 | none |
| Trey Holloway | 22.76% | 23.6% | 0.0000 | none |
| Javon Okafor | **18.45%** | **13.5%** | 0.0155 | **+1.6%** |
| Cory Baptiste | 14.84% | 13.2% | 0.0516 | **+5.2%** |

**Okafor and Baptiste take the same ~13% of the shots and are treated completely differently.**
Okafor's offense intends 18.45% for him — a hair under even — and the defense strips the other five
points off him; he earns relief for 1.5 points of shortfall, not the 6.5 he actually plays, and his
+1.6% vanishes into sampling noise on ~7,900 attempts (measured: 52.2 → 52.2, while Baptiste went
42.2 → 44.0 and Thornton 43.7 → 46.1). Three of the five starters read exactly zero relief.

**So the correct sentence for this family is NOT "low-usage players shoot better." It is "players
their own offense does not feature shoot better."** They come apart precisely for the man the
defense takes away. This is symmetric with the tax (which has always read the same basis, so the
two can never disagree about the pivot) and is therefore not a defect — but it is a real design
question, logged Open for the calibration page.

**League effect at the placeholder:** FG% 43.73% → 44.03% (+0.30pp), PPP 0.9755 → 0.9814.

**[S60.2] MEASURED ON A REAL POPULATION — the wire is live and roughly 10× too quiet.** FG% by
usage decile (lowest usage → highest), same field, only `UsageReliefBonusScale` moving:

| scale | d1 | d2 | d3 | d4 | d5 | d6 | d7 | d8 | d9 | d10 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0.0 | 35.8 | 39.5 | 37.8 | 36.4 | 37.3 | 36.8 | 36.9 | 38.2 | 37.5 | 39.9 |
| **1.0 (today)** | **37.0** | 40.6 | 39.9 | 37.8 | 39.0 | 35.8 | 36.1 | 38.4 | 38.0 | 39.6 |
| 4.0 | **45.3** | 44.7 | 42.0 | 42.8 | 40.9 | 36.6 | 37.1 | 38.0 | 37.4 | 40.4 |

At 0 the curve is flat; at 4 it is the intended shape (open men shoot 45.3%, ball-hogs 40.4%). **At
1.0 the bottom decile gains 1.2pp — invisible.** It is also quieter than *designed*: decile 1
averages 9.1% usage, so relief should be 0.109 → ~+3.9pp, and it delivers +1.2pp. **The
intent-vs-touches Open above is the likely cause** — low-usage men are exactly who the defense sags
off, so the tilt strips them below what the books say they carry. **Rule intent-vs-touches BEFORE
tuning this scale**, or the scale absorbs the error and a later fix makes it twice too strong.

### Loose thread — RESOLVED (S60)

*Original (S59.2):* with the rail artificially lifted, rank 10 posted 39.0 FG% while rank 9 at
*identical* usage (54.3 vs 54.4) posted 45.1, suggesting the usage-pressure penalty might read what
a player *wants* rather than what he *gets* — a clamped star paying for shots he never takes.

*Verdict (S60, from source):* **half right.** The pivot reads **post-floor/rail** shares — so the
rail's clamping IS reflected and a railed star is **not** paying for shots the rail took away. But
it reads **pre-tilt and pre-denial**, so the *defense's* denial is invisible to both sides of the
curve. The rank-9/10 anomaly is the residual+attention compound, not a wants-vs-gets bug. The
surviving half of the question is Finding 7's intent-vs-touches item, now Open.

### Not calibrated here

Emmett's spec (2026-07-14): usage above ~40% unrealistic, below ~8–10% unrealistic. `UsageFloor` at
0.09 already matches the lower bound. **No constant was changed** — all rail/exponent numbers above
came from temporary edits to the *build-output* config, reverted, with the committed `config.json`
verified byte-identical afterward. This is a findings entry for the Roll E usage session (Open in
status.md), not a tune.
