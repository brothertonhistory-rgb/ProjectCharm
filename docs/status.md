# Project Charm — Status Board

The living done/to-do board. **Read this FIRST when planning any session** (CONVENTIONS §6a),
and update it in the docs step of every session (CONVENTIONS §3). Rules:

- Edited **in place**, like design.md — this reflects *now*; journal.md holds history.
- An item leaves **Open** or **Parked** only by **shipping** or by an **explicit ruling**
  (which moves it to Closed-by-ruling) — never by fading out of memory.
- Keep it short. This is a checklist, not a third journal. One line per item, with the
  session/phase that owns the detail. The S73 migration ledger (journal S73) maps every
  pre-rebuild item to its home here.

Last updated: **Session 76** (2026-07-26; the minutes allocator — per-position depth charts, residual control, bounded cascades; the fatigue fence retired; season rebaselined ON PURPOSE).

## Current baseline

**The S76 page is the arc's recorded reference** (seed 20260720, world `stock-d1`, schedule
fingerprint `93d8c853…`, season SHA-256 `dfcda923…`): points 72.4, FG% 45.8,
3P% 35.9, FT% 70.5, PPP 1.0176, TO% 21.6, pace 71.1, fouls 20.23/team/game (6.47 shooting /
13.76 non-shooting), usage max/p90/median 42.0% / 19.5% / 5.0%, top-five share of floor time 69.7%,
cross-position occupancy 24.49%, census clean (4,511/4,511 drafted; 347/347 exact rosters; 347/347
protected coverage). Every calibration session diffs against that page, never against memory.

**Why it moved from S72 (deliberate, S76).** No dial was touched. The minutes allocator changed WHO IS ON
THE FLOOR, so every rate shifted slightly: PPP 1.0246 → 1.0176, points 72.8 → 72.4, FG% 45.9 → 45.8,
3P% 36.2 → 35.9, TO% 21.3 → 21.6. **This is the point of the session** — the S72 numbers described a league
where five men played 35 minutes, so they were never the right calibration target. Engine state: all Rolls
A–M real; the world drafts the Pass-3 two-plane budget cohort (S70 bridge); `PressureVolumeTaxScale` 0.30 is
the one calibrated dial (S72); the settings file and the config classes are name-parity-locked by Phase 71
(S74) — `config.json` SHA-256 `5094367e…`.

## Red blockers — resolve before major new work

- **R-1 (CLEARED, S76) — the rotation is built.** S75 removed the three things that made any rotation impossible: a 10-man roster
  (now 13 at 5G/4W/4B), a player-id ceiling that silently dropped the bench (now 1–13 / 14–26,
  asserted), and same-position-only substitution, which froze each position group at a 10.0-minute
  ceiling against a 13-man parity of 15.4 (now the one-step ladder — all observed lineup shapes reach
  15.4 exactly). The fatigue fence's thresholds are UNCHANGED, so starter share and the minutes
  distribution have not been fixed — that is S76. Measured on the S75 page: cross-position occupancy
  7.57% of floor time, usage-spread median 3.0%, deep bench barely playing.
  **S76 closed it.** The minutes allocator replaced the fence. Minutes by realized rotation rank now read
  31.8 / 29.9 / 27.9 / 25.9 / 23.9 / 20.0 / 16.1 / 12.1 / 8.1 / 4.2 against a placeholder ladder of
  32/30/28/26/24/20/16/12/8/4. **Top-five share 88% → 69.7%**; usage spread median 8.0% → 5.0%, p90
  24.3% → 19.5%, max 46.0% → 42.0%. Calibration may now be run against a league with a real rotation.
  The minute VALUES remain placeholders and the depth chart is PROVISIONAL pending O-6.

## Open — next-session candidates

- **O-1 — Intent-vs-touches ruling (S60).** The usage curve pays on the offense's intended
  share, pre-tilt/pre-denial; the denied big earns nothing. Rule this before touching the
  relief scale, or the scale absorbs the error.
- **O-2 — Relief-scale tuning (S60, quantified S60.2 as ~10× too quiet).** Behind O-1 and R-1.
- **O-3 — Usage architecture: the rail authors every star, HierarchyRank is mostly dead,
  and nothing takes the ball from a cold shooter (S59.2 + S60.2).** Both channels inside
  8–40% by design; the rail back to an emergency brake; volume pricing is the tax's job.
  Live evidence: usage max 46.0% on the real world.
- **O-4 — ★ The tax↔defensive-settings coupled pair (S72 ruling, MANDATORY revisit).**
  Attention multiplies the tax; when the defensive-settings layer lands, 0.30 gets re-walked.
- **O-5 — Residual channel is dead on the real population (S72 observation).** Mean ~0.0002;
  understand-or-retire, not a dial session.
- **O-6 — ★ RAISED PRIORITY (S76) — Scout-rank modernization (S63, re-based S70).** Three old-pool
  assumptions on the record: big-ATH +8 add-back, position-relative SIZE tails / guard-leaning board,
  stretch-big Outside invisible to SKILL. One ruled session. **S76 promoted this from cosmetic to
  consequential:** until now these shaped who got RECRUITED; from S76 they decide WHO PLAYS, because the
  depth chart orders the minute targets. Targets fall sharply within a group (32→8 for guards, 26→4 for
  bigs), so a single rank inversion is a large minute swing. The chart is labelled **provisional** in
  design.md until this lands.
- **O-7 — Drive gate Pass B: post-feed + usage diffusion (S59).** The "he gave it up" outcome —
  a denied drive currently always becomes his own contested jumper, never someone else's shot.
- **O-8 — Drive gate Pass C: the off-ball open-three lever (S59).**
- **O-9 — Pass A page-tuning, first item the level-neutrality finding (gate not neutral below
  rating ~48, a property of the locked spec) (S59).** Calibration — behind R-1.
- **O-10 — Re-measure the eight attribute families vs DIALED opponents / point something
  through `opponentDials` (S59.2 + S60.2).** Every S45–S55.1 finding carries the flat-50
  caveat; the instrument is built and still unused by any committed sweep.
- **O-11 — The random-vs-elite scoring spread is unexplained (S60.2).** 12-point PPG spread
  vs a nearly flat divisional ladder; do not assume benign.
- **O-12 — HelpDefense overhelp / rotation cost (recovered S60).** Feeds the
  rotation/defensive-settings design work.
- **O-13 — BasketballIQ's dead lower half — should low IQ actively hurt? (recovered S60).**
- **O-14 — DefenderPicker promotion / on-ball mismatch hunting (recovered S60).** Becomes
  real when the pick turns mismatch-hunting; this is the second door's second door.
- **O-15 — Remaining calibration queue (behind R-1): TO% 21.3 vs ~18.5 target; turnover-band
  placeholders (S37); the assist lever (10.1 vs ~13.5, S49); curve-steepness conversation;
  displacement magnitude (oracle-first only); the S48 FT-90 micro-flag.**
- **O-16 — The light block channel (S50 design question 2).** Magnitude call on a real
  population.
- **O-17 — Weight: feed Strength-adjacent channels or wait for a body-contact layer?
  (S50 design question 4; proven cosmetic today).**
- **O-18 — SelfCreation perimeter assist discount (S57 residue).** S57 did interior only.
- **O-19 — Refresh `docs/attribute-wiring-synthesis.md` when the attribute map moves (S60).**
- **O-20 — `game` demo: upgrade to real generators or retire (labeled as stub-driven on its
  banner at S73).** Micro-session, or rides any session touching Program.Game.cs.
- **O-22 — Opening-five selection is rank-blind (S75 measurement).** 93% of the league starts three
  guards (324/347 at 5/4/4) because `BuildOpeningFive` walks acquisition order under a quota floor,
  never rank or ratings. Evidence, not failure — but it belongs with coach-driven roster construction.
- **O-23 — Mismatch-hunting (S75 deferral, Emmett: "we can delay that for later").**
  `DefenderPicker` is slot-guards-slot and its own docs call it *"v1 logic"* with *"the eventual
  mismatch-hunting picker drops in here."* Consequence: a tall wing faces the opposing wing every
  possession and, in 93% of games, never sees a guard — so a big wing cannot exploit a three-guard
  lineup. The S75 ladder created the engine's FIRST cross-position matchups (7.57% of floor time,
  W→G at a +9.7 height gap); whether they are correctly priced is A11's open question.
- **O-24 — Role cost for out-of-position play (S75, measure before building).** The engine has no
  role layer: screening, off-ball movement, spacing, rotation duty and out-of-position ballhandling
  are absent rather than mispriced. S75 added no modifier by ruling. **Any future model must be
  gap-shaped** — see C-26.
- **O-26 — ★ Cross-position occupancy is 24.49% (S76 measurement).** Against S75's 7.57% and arithmetic
  floors of 5.0/5.0/14.0% by shape, the allocator reaches across position roughly twice as often as
  feasibility requires. Out-of-position play is currently free (O-24), so this is unpriced volume, not
  necessarily wrong volume. Page-only, never asserted. Understand before tuning.
- **O-27 — Substitutions run 34–39 per team-game (S76 measurement)** against a real-basketball 20–25.
  Structural cause: there is no timeout model, so substitutions cannot clump at media breaks and spread
  evenly instead. Belongs with the coaching layer, not the allocator.
- **O-28 — The three zero-target men are inert until S77 (S76 named consequence, NOT a defect).**
  Emmett's ten-man ruling gives the bottom guard, wing and big a target of zero. Planned is always 0, so
  their residual can never reach a positive enter threshold and they cannot check in. Nothing in the game
  can call on them until foul-outs (S77) and injuries exist. Ruled knowingly; recorded so a future session
  does not read it as an accident.
- **O-21 — Normalize the three config loader shapes (S74 deferral).** Eighteen sections are sectioned
  `Deserialize`; `RollAConfig` is root-flat; `RollEConfig` is nineteen hand-written `GetProperty`
  assignments. The divergence is declared and asserted by Phase 71's registry, not hidden — but folding
  RollA/RollE into the common shape is its own session with its own drift audit. Not urgent: RollE's
  binding is now behaviourally proven.

## Parked — waiting on a named prerequisite

- **P-1 — Shooting-foul positional lean (~50.7/49.3) → the help-defense/rotation model (S62).**
- **P-2 — Steals' pressure-DIAL side → the coaching/pressure layer (S52, updated S58).**
- **P-3 — BallHandling's pressure-dialed test → the coaching layer (S49).**
- **P-4 — OT-LOW (~2.8% vs 6% target) → the coaching / late-game-strategy layer (post-S41).**
- **P-5 — Endurance's temporal shape → a time-sliced bench (S50).**
- **P-6 — Wingspan's jump-ball tip → a first-possession counter (S50).**
- **P-7 — PostDefense-as-size coupling → the synthesis pass (S52).**
- **P-8 — Age/class population structure → the season/recruiting layer (S42.1 ruling).**
- **P-9 — Roll G lineup-context bend (teammate spacing/gravity as selection) → its own
  design conversation; the attributes are carried on Player, unread.**
- **P-10 — Shooting-foul rate dial (bridge #3) → no longer urgent post-S40; a page question.**
- **P-11 — Reconciled team-rebound line → instrument work (S41; credited gap ~4.9/team/game
  is a definition gap, not a sim error).**
- **P-12 — The full personal-turnover attribution flip → the per-event attribution rework
  (S56 flattened the inversion; the committer-picker channel still climbs with usage).**
- **P-13 — Small parked tail (owner sessions in parentheses):** `Outside == 0` buzzer heave
  (S35); personality/timidity on usage (S36); per-player attribution for held balls / Roll K
  (S33); press-frequency sentinels; length-in-make% defender term (S17/S40-era); per-zone
  location-blend weights + corner-three split (P9); reference-card pinning + multi-seed
  (S31/32); EqualShare centralization (P28); opening-five/lineup logic; FT unattributed
  bonus-trip fallback (~0 volume on populated rosters); displacement "advantaged" bin (S36);
  code hygiene (WeightedAggregate duplication, Mk consolidation, RollE stub double-build).
- **P-14 — Long-term watches (design before the relevant layer ships, not now):** save-file
  schema versioning; end-to-end RNG/determinism review before the full season layer; the
  Player data layer at 21k+ actives; moddability. (working-with-emmett §7)

## Closed by ruling (looks unfinished — is not; do not "fix")

- **C-1 — Defensive ratings are the MAN-TO-MAN wire; a future scheme layer TOGGLES distinct
  wiring sets (S61 architecture ruling).**
- **C-2 — Team-aggression fouls belong to the coach/pressure layer, not Hustle (S61; the
  S45 Hustle→foul coupling retired S62, its dead dials deleted S73).**
- **C-3 — The ball-dominance / initiation layer is NOT built; assists attach after the
  fact, accepted as ornamental for now (S57.1).**
- **C-4 — Turnover KIND stays flat; attributes drive how often, not which flavor.**
- **C-5 — Roll D foul flavor stays flat (fires before Roll G — no zone stamped).**
- **C-6 — Offensive-foul flavor stays flat beyond the frontcourt/backcourt split.**
- **C-7 — The and-1 split (MafFraction) is per-zone, not matchup-aware.**
- **C-8 — Tendencies are deterministic in ratings; same ratings, same diet.**
- **C-9 — Held-ball losses stay off the turnover line (S33 R1).**
- **C-10 — The tendency oracle's population-mean diet is a directional diagnostic only.**
- **C-11 — DisplacementMaxMagnitude = 0 is ablation only; it does not undo Route B.**
- **C-12 — Turnover clock is court-aware bands, not a shifted center (S37); an
  offensive-rebounded possession times as a frontcourt turnover.**
- **C-13 — The fast break sets a modern base diet bent per shooter (S38); Roll K putbacks
  are excluded from fast-break FGA accounting (S38).**
- **C-14 — Three-point VOLUME is a generation/era-profile lever, not a runtime knob (S39).**
- **C-15 — Shooting fouls count toward the team-foul total (S40).**
- **C-16 — The halfcourt turnover mix is 50/50 live/dead, "to start" (S41).**
- **C-17 — The assist lineup-passing midpoint tracks the generated population (S41).**
- **C-18 — The 55/45 rebound team split stays; the picker was the culprit and was fixed
  (S45 ruling, S46 fix).**
- **C-19 — Rebounder-picker body attribution keeps the S46 shape (Emmett's rulings).**
- **C-20 — The recruiting line is first-past-the-line at R_LINE 17, "all of college";
  top-of-class selection is the divisional layer's job (S66, explicitly reversible).**
- **C-21 — Position follows the defensive plane, exact-count 40/30/30; height gets no
  vote; offensive role rides as flavor (S70).**
- **C-22 — The generated rebounding scale keeps, explicitly reversible (S70).**
- **C-23 — Divisional sorting: size/athleticism are the unofficial gates between divisions;
  skill overlaps heavily and is premium-overridable (Emmett ruling 2026-07-12).**
- **C-24 — Pass 2 (skill-first generator) is RETIRED and archived (S73); its oracle rulings
  (S42/S42.1) are historical record under `tools/archive/pass2/` and journal S42–S44.**
- **C-26 — Size is priced RELATIVELY, never absolutely, and any future role cost must be
  gap-shaped (Emmett ruling 2026-07-25).** *"You only get punished for size if the other team can
  punish it."* Verified against source: rebounding composes sizeShift/skillShift/hustleShift, each
  `GapFn(offense − defense)`, bent through tanh — equal teams get zero bend, so two five-guard teams
  get the same rebounding split as two five-big teams. Blocking is the same shape. There is no
  absolute size floor. A flat out-of-position penalty would be the first absolute physical term in
  the codebase and would break small-ball coherence.
- **C-27 — Positional eligibility is the ONE-STEP ladder, evaluated from the stored position and
  NOT transitive (Emmett ruling 2026-07-25).** G↔W and W↔B; a guard never reaches a big seat and a
  big never reaches a guard seat. Emmett: *"Every PG can play SG. Every SG can play SF, etc… not
  well, but there is real position flexibility baked into basketball."*
- **C-25 — A missing config key stays QUIET AT RUNTIME (compiled default applies, the game boots)
  and becomes LOUD AT TEST TIME via Phase 71 (Emmett ruling 2026-07-25).** Refuse-to-boot was
  considered and rejected: it would force every future dial into two places forever.

## Next approved candidate — exactly ONE

- **S76 — the minutes-target allocator.** Everything external review round 1 required of it carries
  forward: a feasibility assertion per team before simulating, a minimum stint and hysteresis band so
  a dead-ball target-chaser cannot thrash, target error reported as mean/p95/max per player rather
  than depth-rank averages, starter share gated at exactly 70% (140/200) inside a band declared
  BEFORE running, and "never played" defined as *zero players with a positive target logging zero
  possessions*. **Two S75 findings constrain it before it starts:** acquisition order is not a depth
  chart (A10 — index 1 is G297/W27/B23, index 13 is B218/W129), AND the scout rank underneath it is
  **position-normalised** (`DivvyScoutRank` excludes different attributes per position, scales size
  per position, and downshifts big athleticism), so a roster cannot be depth-ordered by sorting it
  either. S76's first job is defining what a depth chart actually is.

*(S75 shipped — roster 13, the player-id widening, the eligibility ladder, and the measured league;
historical detail in journal S75. Foul-outs moved to S77 on a source finding: committer selection is
post-hoc in the harness, so disqualification needs an RNG restructuring that must not share a diff
with a roster change.)*

*(S74 shipped — config key-name parity + Phase 71; historical detail in journal S74. Note the
"strict loader" framing it was originally given is retired: Emmett ruled quiet-at-runtime /
loud-at-test (C-25), and the scope narrowed to key-name parity plus RollE binding. The board's
motivating premise was also measurably wrong — zero orphan keys existed; the real gap ran the other
way, twelve properties with no key.)*
