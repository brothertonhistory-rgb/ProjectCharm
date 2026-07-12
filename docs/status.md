# Project Charm — Status Board

The living done/to-do board. **Read this FIRST when planning any session** (CONVENTIONS §6a),
and update it in the docs step of every session (CONVENTIONS §3). Rules:

- Edited **in place**, like design.md — this reflects *now*; journal.md holds history.
- An item leaves **Open** or **Parked** only by **shipping** or by an **explicit ruling**
  (which moves it to Closed-by-ruling) — never by fading out of memory.
- Keep it short. This is a checklist, not a third journal. One line per item, with the
  session/phase that owns the detail.

Last updated: Session 46 (2026-07-11; **Rebounder-picker body floor SHIPPED** — the S45-diagnosed bug is fixed. Both rebounder pickers (`OffensiveRebounderPicker`, `DefensiveRebounderPicker`) now weight each player `Luck + Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier + BodyPull × max(0, ReboundPhysical − lineupMean) + FloorCeiling × tanh(max(0, ReboundPhysical − FloorReference) / FloorScale)` (ORB side × shooter nerf on the whole weight) — the block-picker's additive body shape. **Luck** (5.0, flat) replaced the retired floor-of-1; **body pull** (0.35, relative) rewards out-sizing your lineup; **body floor** (ceiling 4.0 / scale 40 / reference 22.5, absolute + saturating) rewards raw size vs a fixed reference so a big target claims more random loose balls, tanh-capped so a genuine big doesn't balloon. Result on the `sweep`: a freak with a zero rebounding rating grabs **0.2 → 4.86 boards/game**, and the mushy bottom of the zero-rating height ladder separates cleanly (5'8 ≈1.2 → 6'4 ≈2.2 → 7'3+ ≈4.9; average-no-hands 1.32 vs weakling-no-hands 0.67); elite anchors held (freak-elite 17.54, weakling-elite 8.65 by ruling, average-elite 12.27), controls uniform, team margins unchanged (freak-no-hands +2.1 — pure attribution). The 55/45 team split was NOT touched. Green on Emmett's machine (full suite + stress + three sweeps). **Next: resume the attribute-meaning family sweeps** — aim the bench at Body or Athleticism. Prior: Session 45 (2026-07-11; **Attribute-Meaning layer opened** — the general `sweep` findings bench is BUILT and proven on Rebounding. One harness instrument (`Program.Checks.AttributeSweep.cs`, token `sweep`) that pins a flat all-50 world, walks one rating up 0→99 on one player (or runs named stress rows), runs N seeded real games per rung, and prints rating → real outcome; a generalization of `sizetest`/`athtest`/`deftest` on the S24 lab-bench builder, aimed by a live-path text config. **Rebounding finding:** rebounding is a rating-gated skill the body *amplifies but does not grant* — a freak body with a zero rebounding rating grabs the same ≈0.2 boards/game a tiny weakling does, because the individual rebounder picker makes the body a multiplier on the rating (floored to 1 at rating 0), not a standalone term; blocks are the correct *additive* template. **Ruling: the 55/45 rebound team split stays** (the culprit is the picker, not the blend). Harness-only, engine untouched, three proof runs green on Emmett's machine with all control anchors holding. **Next: the rebounder-picker body-floor fix** (give the pickers additive height/wingspan/strength terms, the `BlockerWeight` shape) — this redirects the stale "aim at the next family" line. Prior: Session 44 (2026-07-09; C# port Phase 2 — the LIVE skill-first generator is built and proven, **standalone**: `src/Charm.Engine/Core/Sampling.cs` (Beta/Gaussian/Exponential on `IRng`; Marsaglia–Tsang k≥1 only) + `PlayerGenPass2Live.cs` (the 40-slot draw loop calling the Phase-1 transforms; `BuildCohort` returns Draws+Result pairs) + the pure `ComputeHeightShape` extraction in the locked transform (re-proven bit-for-bit by Phase 59 every run) + five dormant Player seats (latent/current/runway/arrival/class, outside `Validate()`) + the Phase 60 gate (sampler moments at N=200k against closed forms, all four live Beta pairs; then eight design-invariant bands + determinism on the canonical 46k cohort). Green on Emmett's machine — all moments OK, all bands OK ([B] 0.597, [E] +0.004, [C] 5 giants, [F3] PostMoves 5.89% vs OBD 6.02% inside the −0.5pp band, [G] 25,825 recruitable), Phase 59 still 0 failures / 0.0 deviation, Phases 54/55 unchanged, `ALL CHECKS PASSED`. **Scope reshaped at the S44 draft audit:** enforcer deletion + the season-pool swap + the season re-check are NOT a port — the skill-first cohort is positionless and the divvy is quota-based — so they moved to **Phase 3**, which opens with the positions-from-orientation design conversation. Ruled at the gate: class variation is legal (zero 7'3"+ players is an honest draw). Prior: Session 43 (2026-07-08; C# port Phase 1 — the deterministic MATH ported and proven exact against the S42.2 fixture: 57/57 constants, 306 players / 51,714 checks / 0 failures / 0.0 deviation at absolute 1e-9). Prior: Session 42.2 (replay fixture + reference reader committed); 42.1 (oracle re-locked after three bounded fixes); 42 (skill-first oracle locked as the port spec); 41 (assists 13.7 OK, steals 6.5 OK, rebound instrument audited). Post-S41 ruling: OT-LOW parked under the coaching / late-game-strategy layer.

---

## 1. Built and live

Every roll has a **real generator**, and every meaningful execution path (observation run,
stress test, season, calibration, gen bench) wires all of them. There are **no outstanding
stub generators.** The stub classes that remain exist only for isolated harness regression
baselines and the legacy `game` demo command (see Open).

| Roll | What drives it |
|---|---|
| A — Entry | Press decision + slot-weighted matchup (P15) |
| B — Halfcourt initiation | Pressure + steals-vs-handling matchup (P13) |
| C — Turnover classification | Context ticket (halfcourt / transition / entry-backcourt); flat within context **by ruling** (§2) |
| D — Foul flavor | Flat **by structural necessity** (§2) |
| E — Player selection | Attribute-driven usage hierarchy (P15/19) + defensive attention (P27) |
| F — Player action | Pressure + matchup (P12) |
| G — Shot location | Matchup bend on residualized gaps (P9 + S36 Route B), coaching nudge (P30), usage diet shift (P17), attention amplifier (P28), **matchup displacement** (S36), **fast-break diet bent per shooter + PaceBias tilt** (S38) |
| H — Make/miss | Matchup make/block/foul doors (P6/7/8), IQ term (P50), fatigue-discounted athleticism (P49) |
| I — Rebound | Two-touchpoint matchup model (P10) |
| J — Transition run-or-not | Coach pace + team athleticism gap (P28/30) |
| K — Offensive rebound | Attribute-driven putback tilt, per-zone (P32) |
| L — Free throws | Authored FT rating (P18) |
| M — FT glass | Roll I's model on the FT board population (P11) |
| Offensive-foul flavor | Frontcourt/backcourt context; flat within context **by ruling** (§2) |

**Systems live:** fouls/bonus + jump-ball arrow (**all defensive fouls feed the team total — shooting fouls included as of S40**, so the 7-foul bonus arrives on real schedule); end-of-half intent; OT; fatigue meter +
athleticism discount + fatigue-fence substitutions; Governor + Resolver walk with full
per-possession counters; **court-aware turnover clock** (S37 — turnovers draw a short
court-dependent band, not the shared possession clock; page splits length by court);
**fast-break shot diet** (S38 — the break sets a modern base diet bent per shooter, PaceBias
tilts the three share; page reads the realized transition three share; oracle v1, 14-vector
golden parity, Phase 58);
shooting-foul / steal / rebound / block / assist attribution.

**Layers live:** player generation Pass 1 + skill-derived tendencies (oracle v2, 19-vector
golden parity; **S39 era-profile volume retune — population three diagnostic ~35.5% → ~39.0%**);
divvy Pass 1.5 (national pool + prestige draft); world Pass 1 (347 schools,
32 conferences); season Pass 2 (schedule oracle-fingerprinted, standings, calibration page);
Roll G displacement (oracle v1, 10-vector golden parity, Phase 56); observation / stress /
bench instruments; **attribute-sweep findings bench** (S45, token `sweep` — walks one rating up
its range on one player through the real engine, team + per-player readout, live-path text config;
the attribute-meaning layer's instrument).
**Player generation Pass 2 — LIVE C# generator, standalone (S44):** samplers + 40-slot draw loop
over the fixture-proven Phase-1 transforms, Phase 60 statistical gate; produces honest positionless
46k cohorts on demand; NOTHING downstream reads it until the Phase-3 bridge (divvy/season still run
Pass 1 with its enforcers).
**Rebounder-picker body attribution — the S46 fix (LIVE, both pickers run every game):** `OffensiveRebounderPicker`
and `DefensiveRebounderPicker` credit the individual rebounder with a luck weight (flat, replaced the
floor-of-1) + a relative body pull (out-size your lineup) + a saturating absolute loose-ball floor (raw
size vs a fixed reference, tanh-capped) — the block-picker's additive shape. A freak body now corrals
boards on its own (0.2 → 4.86 boards/game at zero rating); the zero-rating height ladder rises cleanly.
The team battle (55/45 split) is unchanged. Details in Closed-by-ruling (the S46 shape) + `docs/attribute-meaning.md`.

## 2. Closed by ruling (looks unfinished — is not; do not "fix")

- **Turnover KIND stays flat.** Attributes drive how *often* a team turns it over (Rolls
  A/B/F own frequency), not what *kind* results. (Roll C docstring; the stub-era pressure
  parameter was retired on purpose.)
- **Roll D foul flavor stays flat.** Fires before Roll G — no zone stamped; slot may be
  null — no position context exists at its call time.
- **Offensive-foul flavor stays flat** beyond the frontcourt/backcourt split. Flavor is
  theater; nothing downstream reads it functionally.
- **The and-1 split (MafFraction) is per-zone, not matchup-aware.** Emmett's call.
- **Tendencies are deterministic in ratings** — same final ratings, same diet; the
  per-player style draw was rejected (S34). Volume differences are usage's job.
- **Held-ball losses stay off the turnover line** (S33 R1).
- **The tendency oracle's population-mean diet is a directional diagnostic only** — never
  a gate (S34/35).
- **DisplacementMaxMagnitude = 0 is ablation only** — it does NOT undo Route B; the
  residualized bend is ruled structure, not a dial (S36).
- **Turnover clock is court-aware bands, not a shifted center** (S37). The pace gap
  (65.5 → ~69) closed because turnovers stopped drawing a full possession's clock; the
  tempo Center stayed 17.0. Oracle-confirmed the center barely moves once the bands exist.
- **A possession that has offensive-rebounded is timed as a frontcourt turnover** (S37),
  regardless of the backcourt court-state flag transition / ball-advanced possessions
  carry — you cannot rebound in the backcourt. (`EffectiveTurnoverProfile`.)
- **The fast break sets a modern base diet bent per shooter, not a flat pie** (S38). The
  break dictates a base (Rim 0.57 / Short 0.08 / Mid 0.03 / Long 0.02 / Three 0.30) pulled
  toward the runner's own tendencies with a PaceBias three-share tilt; the null-shooter
  fallback is the flat base, NOT the half-court stub. The break reads RAW tendencies —
  coach shot-selection philosophy is deliberately NOT read on the break (only PaceBias
  tilts it). The `FastBreakMean{Zone}` denominators are pinned to the tendency oracle's
  population diagnostic, not free knobs. Locked oracle wins on any disagreement.
- **Roll K putbacks are excluded from fast-break FGA accounting** (S38). A transition
  possession's putback carries `FastBreak` forward but is rim-forced / putback-pie-resolved,
  so it never touched the fast-break diet; counting it would drag the reported transition
  three-rate below its true value. (`!c.Putback` guard.)
- **Three-point VOLUME is a generation/era-profile lever, not a runtime knob** (S39). The
  era profile's three multiplier + mid/long donors were retuned (Mid 0.50→0.44, Long 0.70→0.63,
  Three 2.10→2.44) to lift the population three diagnostic ~35.5% → ~39.0%, which landed the
  realized league **3PA rate 0.36 → 0.39 (OK)** and eased **aggregate FG% 45.3 → 44.9 (OK)** in
  one pass — a pure *selection* change, no make-curve touch. The `FastBreakMean{Zone}` denominators
  were re-synced to the new diagnostic (0.324/0.147/0.084/0.056/0.390) so the transition bend stays
  calibrated rather than double-counting the higher inclination into the break. Mid-range is the
  primary donor; rim is protected but not invariant. The oracle's multi-level structural gate now
  guards that archetype's identity, not a stale absolute three cap.
- **Shooting fouls count toward the team-foul total** (S40). A shooting foul now increments
  `FoulTracker` — a direct `_game.Fouls.Increment(state.Defense)` at the `ResolveShootingFreeThrows`
  resolution — so it feeds the opponent's bonus like every non-shooting defensive foul already did.
  Before S40 only non-shooting fouls counted, so teams crawled to the 7-foul bonus and the bonus-FTA
  bucket starved (FTA 15.8 / FT-rate 0.27, both LOW). Two increment sites (shooting here + non-shooting
  via `DefensiveFoulCharge`), **one** bonus-fork site (`DefensiveFoulCharge`); the paths are disjoint,
  so no double-charge. The shooter's own trip is never converted to a one-and-one (`oneAndOne: false`,
  never reads `BonusFor`) — the charge only moves future possessions' bonus reads. **Thresholds stay
  7/10** — the gap closed with foul *volume*, not an easier bonus. Proven by `ShootingFoulFeedsBonusCheck`
  (7th-foul boundary exact, no leak, no double). Payoff on the 347-page (seed 20260703, 5205 games):
  FTA 15.8 → 19.3 (t19.5, OK), FT-rate 0.27 → 0.34 (t0.34, OK), bonus share of FTA ~27% → ~42%;
  per-zone FG% all held in band, no shot-math regression.
- **The halfcourt turnover mix is 50/50 live/dead** (S41, Emmett's ruling — "to start", iteration
  anticipated). Base live .34→.50 (BadPassIntercepted .265 + LostBallLiveBall .235) and EntryBackcourt
  live .30→.50, dead menus scaled with shape preserved (each context sums to exactly 1.0); Transition
  was already 50/50, untouched. Weights-only, no Roll C code. Landed league **steals 4.5 → 6.5 (t6.2, OK)**;
  the realized `steals/turnovers` box proxy is ~47% (below the configured ~50% because Roll K live-ball
  turnovers are steal-less — the parked dilution, reported not fixed). Total turnover rate held at 13.8.
- **The assist lineup-passing midpoint tracks the generated population, not the scale midpoint** (S41).
  `AssistPassMidpoint` recentered 50 → **71.31** — the eligible-make-weighted mean lineup AssistWeight,
  solved by tanh zero-balance bisection so a league-average lineup earns factor 1.0. The placeholder 50
  assumed attributes center at 50; generated starters average ~71 (measured: mean factor 1.192 over 252k
  eligible makes), which had inflated assists to 17.4. The five `AssistedRate` zone bases were then trimmed
  a **uniform ×0.8909** (.88→.784 / .62→.5524 / .50→.4455 / .43→.3831 / .54→.4811), preserving the ordering;
  per-zone realized assisted shares stay near real-world (three ~78 / rim ~48). Landed **assists 17.4 → 13.7
  (t13.5, OK)** in one pass. The midpoint is a *relative-engine* dial: it follows the population it measures.
- **Player-generation Pass 2 is skill-first, no-gate, honest-draw** (S42, oracle locked; **re-locked S42.1**
  after three bounded fixes; the C# port is the next build). The locked oracle
  (`tools/gen_pass2_skillfirst_oracle.py`) is the port spec + calibration reference. Model rulings, hardened
  over five adversarial-review rounds (plus one S42.1 round) and now frozen:
  - **Three independent draws** (skill-quality, athletic-quality, specialization) + orientation; only three
    causal dependencies (orientation→height ceiling, size→athleticism landing, orientation→arrival/runway).
    Quality is body-independent: `corr(q, Height) ≈ 0`.
  - **Chosen-weapon specialization** (one identity weapon, glue skills excluded): broad at low s, spiked at
    high s (top1−top2 gap 34.6 vs 11.3).
  - **Weapon census offsets (S42.1 ruling — Option A):** the weapon is the argmax of
    `base[k] + WEAPON_CENSUS_OFFSET[k]` over the eligible set — the strongest eligibility-CORRECTED
    candidate. The S42 raw argmax had a census artifact (universally-eligible skills won
    disproportionately; "mid-range specialist" was the most common identity at 8.9%, post identity rarer
    than off-ball-defense identity). Offsets shift the argmax comparison only, never card math; the ruled
    default target is a near-flat identity census (~5.9% each; AFTER 5.70–6.02%, PostMoves ≥
    OffBallDefense). Proven by the [F3] paired-counterfactual census (both rules on identical pre-weapon
    states). **Rejected alternative: Option B, a random weighted lottery** (larger semantic change, not
    taken). The [F3] scaffolding (`weapon_raw`, `cf_player`) is oracle-audit-only and does NOT port.
  - **FT idiosyncrasy (S42.1 ruling):** ONE shared per-player draw, `gauss(0, FT_SIGMA=9.0)`, the SAME
    value in the latent-FT and current-FT derivations — a persistent shooter trait, not a second
    development axis. Restores the S29 "oddballs in the tails" ruling: the skilled low-FT hitch (Out≥70 &
    FT<50) runs 2–8 per 46k, the auto-line weak big (H≥71 & Out≤40 & FT>80) 13–22; rare-but-reachable
    passes, hitch-archetype judgment stays deferred to the season layer. [F4] audits are the proof.
  - **Population shape:** 7'3"+ giants ~3/cohort (Gaussian tail, not ~470); stretch bigs exist (~150 at
    S42.1 re-lock, was ~190), point-centers ~0 (shooting orientation-neutral, handle perimeter-locked);
    marquee unicorns rare (Wemby 1/6 sampled worlds, LeBron 5/6 at re-lock, was 3/6) — exact
    once-per-century framing deferred to the season layer.
  - **Baked potential / arrival / runway:** latent skill at birth, arrival suppresses current expression
    (guards developed, posts raw), runway = 21-skill latent−current vector.
  - **The recruiting line is an oracle-only downstream export proxy** (~25.7k recruitable at S42.1
    re-lock, was ~24.8k; six-seed mean 25,792, inside the 20–30k target with R_LINE untouched at 17.0),
    continuous and **orientation-weighted** (`perim_w=1−0.45·o`, `post_w=0.55+0.45·o`) — NOT a role table.
    A body or one weapon may amplify a pathway; neither substitutes for every tool. Rebounding is gated by
    interior skill (a rebound-only tall scrub stays below the line — demonstrated with printed rows in
    [G4] since S42.1, never asserted from prose); interior skill cashes by a height-access curve (~6'2
    inflection) plus a sub-6'0 taper (the whole 5'8"–5'9" tiny-post family clears zero times via the post
    path); Mid is access-gated (a lone midrange isn't a guard). Cross-path exceptions ~1.7%/1.1% (hybrid blur).
  - **Honest draw, no repair:** no redraws, competency repairs, rating floors, or role/position packages —
    the generator stays an honest cohort; the recruiting line is pure downstream selection.

- **The 55/45 rebound team split stays** (S45, Emmett's ruling; **the picker fix that it pointed to shipped in S46**). The `sweep` measurement proved
  rebounding is a genuine size-independent skill under the current blend (`ReboundSizeWeight` 0.45 /
  `ReboundSkillWeight` 0.55): a little-guy-with-great-hands out-rebounds a freak-body-no-hands 9.6 to
  0.2 at the individual grain. The freak-no-hands 0.2 was NOT a split problem — it was the individual
  rebounder picker flooring a zero-rating body to weight 1 (body a multiplier on the rating, not a
  standalone term). The split stayed; the picker was fixed in S46 (below). Full finding in `docs/attribute-meaning.md`.
- **Rebounder-picker body attribution — the S46 shape (Emmett's rulings across two design rounds).** Both
  pickers weight each player `Luck + Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier
  + BodyPull × max(0, ReboundPhysical − lineupMean) + FloorCeiling × tanh(max(0, ReboundPhysical −
  FloorReference) / FloorScale)`; the ORB side multiplies the **whole** weight by the shooter nerf. The
  rulings, all off the sign-off table (archetype table round 1, zero-rating height ladder round 2):
  (1) **`ReboundLuckWeight` = 5.0** — a flat, body-blind claim on random bounces, replacing the retired
  floor-of-1 (an inert player lands ≈0.7–0.9 boards/game, not 0.2). (2) **`ReboundBodyPullWeight` = 0.35,
  one-sided** — the *relative* body pull (out-size your lineup); a below-mean body gets zero, never a
  second penalty. A **signed** term was rejected (it dragged the weakling-elite below its window —
  double-penalty on the skilled small rebounder). (3) **The saturating loose-ball floor** (`ReboundBodyFloorCeiling`
  4.0 / `ReboundBodyFloorScale` 40.0 / `ReboundBodyFloorReference` 22.5) — an *absolute* body channel vs
  a **fixed** reference (not the lineup mean), tanh-saturated, added because the relative pull alone left
  an average body tied with a small one (both at their lineup mean). Gentle setting chosen over wider; a
  5'8 no-rebounder at ≈1.2 boards ruled correct (floor reference stays at the 5'2 extreme). (4) **On the
  offensive glass the shooter nerf multiplies the whole weight** (luck + body included) — the nerf models
  reduced availability after shooting, not a skill-specific penalty. (5) **Weakling-elite settling 9.0 →
  ≈8.65 is accepted** — the structural cost of letting average bodies compete (lifting them lets his
  average-bodied teammates take a sliver); every separating shape pays this, and 8.65 for a 5'6" elite
  rebounder is still elite. The **ORB→putback coupling** is accepted and noted (the picked offensive
  rebounder becomes the Roll K putback shooter — real basketball, a small second-order channel).
  Body composite is `ReboundPhysical` (same as the team battle). Full derivation: `tools/rebounder_body_floor_model.py`.

## 3. Open — next-session candidates

- **Attribute-meaning layer — the `sweep` bench is BUILT (S45); Rebounding is the first family
  measured and its picker bug is FIXED (S46); the other seven families are Open, the ACTIVE next track.** The instrument walks any rating up its range through the real
  engine (see Built + design.md). The remaining families to sweep, each following the rebounding
  template (each rating in isolation at 5-point steps + an interaction block where it plausibly fights
  another attribute): **A. Body** (Height, Wingspan, Weight) · **C. Athleticism** (7 ratings) ·
  **D. Scoring** (Close, Mid, Outside, Finishing, FreeThrow, FoulDrawing) · **E. Perimeter creation**
  (BallHandling, Passing, Playmaking, SelfCreation, OffBallMovement) · **F. Interior offense**
  (PostMoves, Screening) · **G. Defense** (PerimeterDefense, PostDefense, RimProtection, Steals,
  HelpDefense, OffBallDefense) · **H. Cognition** (BasketballIQ, Discipline). This layer unblocks the
  generation redesign (orientation channel for hybrids, height→skill and height→athleticism leans),
  which is blocked behind knowing what the ratings mean. Each family is a light "aim + record" session,
  not an instrument build. (Order is Emmett's call. The rebounder-picker fix that jumped the queue
  shipped in S46 — see Built + Closed-by-ruling — so the family sweeps are now the front-runner.)
- **Season calibration page — three-point thread CLOSED at S39 (seed 20260703, stock world,
  5205 games).** The full three-point arc is now done across three sessions: S38 fast-break diet
  (transition three 5% → 34.5%), S38.1 make-curve (all five per-zone FG% on target), **S39 volume
  via the era-profile retune — 3PA rate 0.36 → 0.39 (OK, on target) and aggregate FG% 45.3 → 44.9
  (OK, back in band), together, in one pass.** No make-curve touch; a pure population-selection change.
  Per-zone FG% all held OK (rim 61.2 / short 42.8 / mid 39.3 / long 36.1 / three 33.9); fast-break
  page line 34.9% transition three, sane (Means re-synced). FG% sits at the high edge of its band
  (44.9 vs 44.0) — a mix result, noted not chased.
  **Remaining open page gaps (S41 347-page run, seed 20260703) — three-point CLOSED at S39, FTA/FT-rate CLOSED at S40, assists + steals CLOSED at S41:**
  **HIGH** — turnovers 13.8 (t12.5) / TO% 19.6 (mostly a pace echo, its own read); blocks 4.1 (t3.5).
  **LOW** — rebounds 30.7 (t34.5) incl. offensive 8.8 (now an **understood definition gap**, not a mystery —
  S41 audited the credited↔public reconciliation and it degraded to diagnostic-only; see Parked, team-rebound
  line). Also a small routing drift: FG% 45.0 sits a hair over its +1.0 band edge (more live
  turnovers → more transition → slightly more efficient shots; noted, not chased). **The front-runner is now
  the turnovers-HIGH pace echo or the blocks-HIGH attribution.** Assists-HIGH (17.4) and steals-LOW
  (4.5) were prior front-runners and are **closed by S41** (assists 13.7 OK via the midpoint recenter + uniform
  trim; steals 6.5 OK via the 50/50 turnover mix). OT-LOW moved to Parked (diagnosed; needs the coaching layer).
  Pick the gap before drafting; the next-session prompt is its own audited pass.
- **C# port of the Pass-2 oracle — Phases 1+2 BUILT (S43/S44); Phase 3 (the divvy/season bridge) is the active next build.**
  **Phase 1 (S43, DONE):** the deterministic MATH in `src/Charm.Engine/Core/PlayerGenPass2.cs`, proven bit-for-bit
  against the committed S42.2 fixture by the Phase 59 gate (57/57 constants, 306 players / 51,714 checks / 0
  failures / 0.0 deviation at absolute 1e-9); re-proven on every harness run. **Phase 2 (S44, DONE):** the LIVE
  generator, standalone — `Sampling.cs` (Beta via Marsaglia–Tsang two-gamma k≥1-only, Gaussian via the untruncated
  ClockDraw Box-Muller core, Exponential via inverse-CDF), `PlayerGenPass2Live.cs` (the 40-slot draw loop in fixture
  order; ONE height-noise draw either branch; `LivePlayer` Draws+Result pairs; `BuildCohort`), the pure
  `ComputeHeightShape` extraction (Phase-59-protected), five dormant Player seats outside `Validate()`, and the
  Phase 60 gate: sampler moments (N=200k, closed forms, all four live Beta pairs) then eight design-invariant BANDS +
  determinism on the canonical 46k cohort — bands, never the oracle's seed numbers (different RNG stream by design);
  the 7'3"+ band is 0–40 (zero legal by ruling), the [F3] census band is PostMoves ≥ OBD − 0.5pp (near-parity by
  design; strict ≥ false-reds ~30% of honest cohorts). Green on Emmett's machine; Phases 54/55 untouched.
  **Phase 3 (OPEN, the next build) — the bridge, reshaped at the S44 draft audit:** the skill-first cohort is
  positionless (no position, role, leg, or quota) and the divvy is quota-based (80G/60W/60B, coverage roles,
  opening-five shape), so the swap requires a **positions-from-orientation apportionment design that does not
  exist yet**. Phase 3 opens with that oracle-side design conversation BEFORE any code, then: swap the season
  talent pool to the Pass-2 cohort, delete `GenEnforceFloors`/`GenEnforceLegHealth` (both call sites —
  `Program.Divvy.cs:239-240` and `Program.Gen.cs:883-884`; the honest-draw ruling made executable), and re-check
  the season page against the S39–S41 calibration (assist midpoint, steal mix, rebound instrument all measured on
  the Pass-1 population — expect movement, re-measure before re-tuning). Preserve the frozen contracts (see
  Closed-by-ruling, S42 + S42.1). The one unexercised clamp edge (Height == 99) still rides. The Phase-3 prompt
  is its own audited pass.
- **Generation-layer bridge — now crossed for Pass 2.** S39 was the first population-selection change (era
  profile); S42 locked the full Pass-2 skill-first oracle on the same proven oracle→archetype-table→
  golden-parity workflow. The C# port (above) is the build that lands it in the engine.
- **Turnover-band calibration** — the court-aware bands shipped with **placeholder**
  centers/spreads (backcourt ~5s, frontcourt ~14.5s); tune them off the season page's new
  turnover-length-by-court split. Open question flagged S37: single-period transition /
  ball-advanced turnovers currently draw the short backcourt band — fine for a bring-up
  strip, arguably too short for an already-across possession; decide once the split's size
  and mean are on the page. (S37)
- **Bridge #3 — shooting-foul rate dial (PARKED, no longer urgent)** — the S40 bonus fix closed
  FTA to target with foul *volume*, so a shooting-foul-rate bump is not needed for FTA. If ever
  opened it carries three items: (1) a modest per-zone shooting-foul-rate bump **with** the make-curve
  re-derive (standing condition — if Roll H block/foul baselines move, rim/short midpoints re-derive);
  (2) diagnose what `FoulDrawing` actually does and where shooting fouls are called per zone;
  (3) **late-game intentional fouling** — a trailing team hacking to extend the game, entirely unbuilt,
  a real separate FTA source. (S40)
- **Curve-steepness design conversation** — before any K moves; carries the finding that
  diminishing returns no longer exist inside the authored 0–99 range. (S32)
- **Displacement magnitude tuning** — only via the oracle-first flow (approve new oracle
  calibration → regenerate fixture → sync C# defaults + config → parity stays green). (S36)
- **`game` demo command** still stub-wired (self-documented) — upgrade to real generators
  or retire; micro-session or rides a session that touches Program.Game.cs anyway.

## 4. Parked — waiting on a named prerequisite

- **OT-LOW → the coaching / late-game-strategy layer** (parked 2026-07-06). Overtime runs ~2.8% vs
  the 4–8% target. **Diagnosed, not a mystery:** the end-of-game intent is a flat, *score-blind* pie
  (HoldShootLast 0.70 / ShootEarly / NoShot) triggered by time-left only — the Governor's own comment
  calls it "score-blind... a future score-aware layer." So none of the real behaviors that funnel close
  games into buzzer-ties exist: a trailing team never fouls to extend, it even milks the clock 70% of the
  time *while losing* (actively killing its own comeback), and a tied team doesn't reliably hold for the
  last shot. These are coaching-personality knobs (aggressive coach fouls/pushes; conservative coach bleeds
  clock), so the fix belongs to the coaching layer, not a standalone dial — bolting a score-aware layer on
  now would be rebuilt when coaching profiles land. This layer is also the parked intentional-fouling FTA
  source (bridge #3 item 3). Before building, size the prize with a cheap page diagnostic: "games within one
  possession (≤3) at ~1:00 left → fraction reaching OT" vs the real conversion.
- **Reconciled team-rebound line** (S41) — the credited-rebound LOW is an understood definition gap:
  public 34.5 includes uncredited team rebounds. S41's C0 audit proved the candidate dead-ball endings
  (OOB-off-offense, jump-ball arrows, loose-ball-foul-on-offense, MissOutOfBoundsLost) are individually
  rebound-opportunity-only but **cannot be reconciled page-only** — `JumpBallArrow` labels carry no
  rebound-origin provenance (jump balls feed from Rolls A/B/F/I/J/K/M). A true team-rebound line needs
  **rebound-provenance instrumentation** (a counter stamping which held-ball/OOB endings arose from a
  Roll I/M rebound scramble). Until then the page prints the candidates as a NOT-reconciled diagnostic only.
- **Player-generation Pass 2 — oracle LOCKED (S42/S42.1); C# port Phases 1+2 BUILT (S43/S44); Phase 3 (the bridge) is the active Open build** (see §3).
  The skill-first generation model is frozen in `tools/gen_pass2_skillfirst_oracle.py` and the S42
  Closed-by-ruling entry; the deterministic math is now ported and fixture-parity-proven (S43). Two design
  notes still ride Phase 2, not the oracle: the **tweener-post existence requirement** (guard/wing-sized
  players whose primary package is post play) is satisfied in principle by the skill-first orientation model
  and confirmed against real rosters at Phase 3+; **weakest-leg multiplicative development** belongs to the development/season
  layer, not generation. (Formal notes in memory + journal.)
- **Age/class population structure → the season layer** (parked 2026-07-07, S42.1). The oracle's age/class
  labels are a **placeholder projection of arrival** ("guards arrive developed" is currently implemented as
  "guards arrive old"; ready 18-year-olds ~0.03%). The season layer owns the real population structure, the
  one-class-vs-standing-pool question (deferred at S29), and the **ready-freshman existence requirement** —
  the ready freshman is a defining creature of modern college basketball and must exist there. Arrival is
  the ruled mechanism and ports as spec; the age/class labels are decoration on it and do NOT port as spec.
- **Roll G lineup-context bend** — teammate spacing/gravity as a *selection* effect
  (gravity/spacing attributes carried on Player, unread). Needs its own design conversation.
- **`Outside == 0` buzzer heave** at pie time — the only heave residual left after the
  universal capable floor (S35). Tiny.
- **Personality/timidity on the usage dial; strategy layers.** (S36)
- **Per-player attribution for held balls and Roll K turnovers** — records carry no
  slot/committer; aggregate-only today. (S33)
- **Press frequency / break rate game-level sentinels** — counter plumbing. (Named in the
  observation output's deferred block.)
- **Length-in-make% defender term** — a parked candidate wire, judged on its own merits,
  never a rescue knob. (S17/S40-era)
- **Per-zone location-blend weights** (top-3 blend varies by zone). (P9)
- **Corner vs above-the-break three split.** (P9)
- **Reference-card source pinning** — per line, when a tuning session needs a bullseye. (S31)
- **Multi-seed measurement** — blend seeds if per-zone drift proves material. (S31/32)
- **EqualShare centralization** — one shared constant across C1/C3/selection-tilt/Roll G. (P28)
- **Opening-five shape / lineup logic** — future lineup-or-coaching layer, not the divvy.
- **FT unattributed bonus-trip fallback** (pre-Roll-E trips use config MakeProbability 72%)
  — named loose end, ~0 volume on populated rosters.
- **Displacement "advantaged" observation bin** — empty on the even sentinel corpus by
  construction; revisit on a varied-population season page. (S36)
- **Code hygiene, parked:** WeightedAggregate duplication (A/B); harness `Mk`
  fixture-builder consolidation; RollEStubPieGenerator's internal double-build.
- **Long-term watches (design before the relevant layer ships, not now):** save-file
  schema versioning; end-to-end RNG/determinism review before the full season layer;
  the Player data layer at 21k+ actives; moddability. (working-with-emmett §7)
