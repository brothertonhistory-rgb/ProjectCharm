# Project Charm — Status Board

The living done/to-do board. **Read this FIRST when planning any session** (CONVENTIONS §6a),
and update it in the docs step of every session (CONVENTIONS §3). Rules:

- Edited **in place**, like design.md — this reflects *now*; journal.md holds history.
- An item leaves **Open** or **Parked** only by **shipping** or by an **explicit ruling**
  (which moves it to Closed-by-ruling) — never by fading out of memory.
- Keep it short. This is a checklist, not a third journal. One line per item, with the
  session/phase that owns the detail. The S73 migration ledger (journal S73) maps every
  pre-rebuild item to its home here.

Last updated: **Session 81.3** (2026-07-29; off-ball help stops being measured against a permanent imaginary 50-rated player and is measured against the man actually shooting. Both arms move together onto the same two reads the on-ball duel already used, so the block door carries one rule instead of two. The same lineup now helps 21.17 against a D3-calibre shooter and 0.58 against a high-major one, where before it was one number forever. Block CREDIT deliberately did NOT move and is proved byte-identical against two independent witnesses. ★ THE BUILD PROMPT'S HEADLINE TABLE WAS TRANSPOSED — rim and three swapped, short and long swapped — so the session's expected direction was backwards: rim help RISES 7.0% and it is THREE-point help that falls 28.9%. League blocks therefore go slightly UP (45,320 -> 45,734 credited, 4.4/team unchanged against a 3.5 target) and NOTHING was tuned. Closed O-44. Opened O-53. Suite `ALL CHECKS PASSED` and the season page verified on Emmett's machine.)

## Current baseline

**The S78 page is the arc's recorded reference — and it is PROVISIONAL pending a calibration session**
(seed 20260720, world `stock-d1`, schedule fingerprint `93d8c853…` unchanged): points 68.5, FG% 42.9,
3P% 34.9, FT% 69.9, PPP 0.9651, TO% 22.2, pace 71.0, rebounds credited 31.4, fouls **17.95/team/game
(6.31 shooting / 11.64 non-shooting)**, blocks 4.1, steals 7.5, assists 9.9, usage max/p90/median
36.9% / 18.0% / 6.2%, top-five share of floor time 69.7%, cross-position occupancy 24.44%, census
clean (4,511/4,511 drafted; 347/347 exact rosters; 347/347 protected coverage). Every calibration
session diffs against that page, never against memory. *(Previous reference: the S77 page — points
72.4, FG% 45.8, 3P% 35.9, FT% 70.5, PPP 1.0176, TO% 21.6, pace 71.1, fouls 20.23 = 6.47/13.76.)*

**Why it moved at S78, and why NOTHING was chased.** Seventeen calibration verdicts read HIGH or LOW.
`PrintCalibrationReadout` is a `void` print — page-only, never asserted — and no dial was touched.
The page moved because the POPULATION was corrected: `Discipline` went ~17 → ~53 and S62 wired it
into reach-in foul propensity, so fouls fell 20.23 → 17.95, FTA fell with them, and free points went
with that. Defense also strengthened league-wide, which outran Roll H's IQ make-bonus finally
switching on (`clamp((IQ − 50)/49)` — dead at the old league mean of ~17).

**S77 changed no simulated number.** The page grew from 493 lines to 627 by appending the stat section;
the 493 pre-existing lines are **byte-identical** to S76.1, proven by line diff, not by hash. Only the
fingerprint moved (`38ec0e9f…` → `96eb2c3a…`). New on the page: 4,511 player-seasons, 1,018 of whom never
took the floor; qualifiers `>=100 3470 | >=250 2810 | >=500 2082 | >=900 439`; league per-game medians
points 4.4, rebounds 2.1, assists 0.6, minutes 16.1.

**Why it moved from S76 (S76.1, one line).** No dial, no engine path, nothing simulated. `AttributeGame`'s
per-slot shooting block still carried the literal `>= 20` guard S75 replaced everywhere else, so stamped ids
21–26 logged no FGA/FGM/3PA/3PM/FTA/FTM at all — 56,714 shot attempts a season, ~10% of the league, every
road game. Attribution is post-hoc, so **492 of 493 page lines are byte-identical**; only the box-sourced
usage line moved, 42.0/19.5/5.0 → 38.8/18.1/6.3. Journal S76.1 carries the measurement and the reason it
survived two sessions.

**Why S76 moved from S72 (deliberate, S76).** No dial was touched. The minutes allocator changed WHO IS ON
THE FLOOR, so every rate shifted slightly: PPP 1.0246 → 1.0176, points 72.8 → 72.4, FG% 45.9 → 45.8,
3P% 36.2 → 35.9, TO% 21.3 → 21.6. **This is the point of the session** — the S72 numbers described a league
where five men played 35 minutes, so they were never the right calibration target. Engine state: all Rolls
A–M real; the world drafts the Pass-3 two-plane budget cohort (S70 bridge); `PressureVolumeTaxScale` 0.30 is
the one calibrated dial (S72); the settings file and the config classes are name-parity-locked by Phase 71
(S74) — `config.json` SHA-256 `5094367e…`.

## Shipped since the last board update

- **S81.3 — the help arm compares to the shooter (engine + oracle + fixtures).** `Matchup.BlockHelpThreat`
  and `Matchup.BlockHelpShiftVsShooter` are new beside the untouched neutral pair, so the rate/credit wall is
  visible in the function names: the RATE reads the shooter, CREDIT keeps the neutral bar (R2). No nullable
  shooter and no no-shooter overload — either would preserve two rate semantics indefinitely. No new config
  keys; the bar is no longer a number, it is the man shooting. Golden schema splits `rate_vs_shooter` from a
  frozen `credit_neutral` section, two new fixtures keep the credit half a regression test rather than a
  snapshot, and a negative control proves every run that the credit comparison still rejects a mis-wire.
  Three findings recorded and NOT tuned toward: league blocks rise slightly (the opposite of the prompt's
  prediction), rim help rises 7.0% because real rim shooters are tall but poor finishers, and the per-game
  blocks leaderboard is still led by a 6'4" guard on minutes (O-49's artifact, not a block-door defect).

- **S81.2 — what a vertical leap is worth (engine + config).** `Matchup.VerticalShift` (the convex
  defender-relative primitive, weight OUTSIDE `GapFn`) applied at exactly two sites — ordinary Rim at
  `RimVerticalWeight 0.40` and putbacks at `PutbackVerticalWeight 0.80` — deliberately NOT inside
  `EffectiveRating`, which runs for every zone. `Matchup.ReboundVerticalMultiplier` joins the skill
  product in both rebounder pickers (`ReboundVerticalSwing 0.20`, teammate-relative), and a team-vs-team
  gap joins `totalShift` in `OffensiveReboundShare` (`ReboundVerticalTeamWeight 0.05`). Reach weights
  retuned to 0.45 / 0.40 / 0.15 with an ordering guard ADDED on top of the kept sum-to-one guard.
  Null-defender fallbacks preserved byte-identical on both make paths. New Phase 75 (isolation sweep
  first, three neutral points kept separate, strict picker monotonicity with full preconditions, all
  five config guards). **Acceptance test:** the tall long man out-protects the rim over the short
  explosive one, 12.38 vs 7.45, skill/readiness/assignment/shooter held identical. **Within one fixed
  body more hops STILL helps** — 12.16% → 13.97% — the slope halves (4.54 → 1.82 pts), the sign does not
  move. Stage-1 population gate run before any scale was trusted: rim term is a tail effect (6.5% of
  shots move >2 pts), putback broader (23.5%). Rebound gain decomposed on a four-way grid — no
  interaction, essentially all of it the individual layer. **Also closed a hole in Phase 74's fixture
  guard**: the golden declared the three reach weights and never checked them, so a reach change would
  have sailed past it with all 1,210 rows agreeing with a wrong engine.

- **S81 — rim help is gated by assignment (engine).** `Matchup.BlockSpacing` /
  `BlockAssignmentGate` / `BlockEffectiveGate` / `BlockAssignedMan`, consumed in exactly two places
  (`BlockHelpSum` for the rate, `BlockCreditWeights` for the credit) through one named assignment
  lookup, so a coaching layer replaces slot parity without touching the gate formula. Score is a
  logistic on Outside (midpoint 45, scale 14); floor 0.30; influence Rim 1.00 / Short 1.00 / Mid 0.50
  / Long 0.20 / Three 0.00. Transition exempt (`offense = null` on a fast break reproduces the S79 tree
  bit-for-bit). Putbacks untouched and now proven byte-exact. Oracle locked with 18 self-checks before
  any C# was written; golden 1,210 rows / 1,000 gated / worst |Δ| **0.0E+000**. Phase 74's existing
  assertions classified, then re-armed against the gated tree; Phase 36 gained a realistic opponent and
  a new sub-check 11 that fails loudly if the gate is ever inverted.
  **Per situation it is large and exact** (a 6'11" rim protector dragged onto a sniper: 39.4% → 18.9%
  of his team's rim blocks, 8.821 → 3.618 blocks per 100). **League-wide it is small** (helper share
  68.4% → 63.8%; height composition ~2 points). **It is not a block-total correction** — see O-40.

- **S79.3 — the leaderboards became percentages (page-only).** Four exact on-floor counters on
  `SeasonPlayerRecord` (`OffensiveCredits`, `OpponentTwoPaOnFloor`, `SecuredBoardsOnFloor`,
  `OffensiveTeamFgmOnFloor`), staged in the existing `NoteOccupancy` record walk and drained in its
  existing roll-up. Five rate boards — BLK%, REB%, AST%, STL%, PTS/100 — plus five distribution rows on
  the qualified pool and `off poss | def poss` on the minutes board. Phase 73 gained **twelve** gates
  (four league identities, five feasibility bounds, three zero-consistency) and two honest diagnostics.
  Every existing page block byte-identical, proven by line diff against the committed build at the same
  seed. **The defect it fixes:** the per-game block board still reads a 6'4" guard first and its worst
  per-minute blocker ninth; BLK% is ten bigs. Denominators counted, never estimated — a `Credits / 2`
  estimate was measured to reorder the points top ten. No engine, config, generator, fixture or dial.

- **S79.2 — the position census (page-only instrument).** `PrintS80PositionCensus` in
  `Program.Season.Calibration.cs`: rostered players split G/W/B, sixteen skills grouped by which
  ones compete for the same family budget, each with median / p10 / p90 / share <=10 / share <=20.
  Sits beside the S78 body-band census, which is UNCHANGED so S78's recorded numbers stay
  comparable. Validated by reproducing the S79.1 census exactly — G RimProtection 27 / 34.6% <=20,
  G PostMoves 14 / 72.0% <=20. No engine math, no config, no fixture, no assertion.
  This is the readout S80 is ruled and validated on.

- **S79 — the block help arm + contribution credit.** `Matchup.BlockWeightWithHelp` composes the matched
  duel with a zone-weighted help arm in pre-tanh shift space; `BlockCreditWeights` /
  `PutbackBlockCreditWeights` replace `BlockerWeight`. Help depth is **body-only** (height + strength),
  NOT `Postness` — sharing `PostDefense` with the threat term let a better defender lower his own team's
  block rate on 8,237 of 40,000 matchups. New: `tools/block_help_oracle.py`, `tools/block_help_golden.json`
  (210 rows), Phase 74, Phase 36 sub-checks 8–10, one page-only season readout. Closes O-29.

## Red blockers — resolve before major new work

*None open.* **R-1 (the rotation) shipped in S76** — the minutes allocator replaced the fatigue fence and
took top-five share of floor time from 88% to 69.7%; historical detail in journal S76. Calibration may now
be run against a league with a real rotation, though the minute VALUES remain placeholders and the depth
chart is PROVISIONAL pending O-6.

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
  design.md until this lands. **S78 relocated the risk precisely:** the accepted pool is drafted
  ONE-FOR-ONE (4,511 → 4,511), so draft-level masking is structurally impossible — the rank cannot stop a
  player making a roster. What it still decides is which school and which depth-chart slot, i.e. **minutes**
  (O-33). Any future "did the scout rank hide my players?" question is a stage-3 question, not stage-2.
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
- **O-28 — The three zero-target men are inert, and the session that was meant to change that never came
  (S76 ruling; re-dated S77).** Emmett's ten-man ruling gives the bottom guard, wing and big a target of
  zero, so their residual can never reach a positive enter threshold and they cannot check in. Ruled
  knowingly. **The "until S77" in the original wording is void** — see O-30. Now VISIBLE rather than
  inferred: the S77 page shows 1,018 of 4,511 player-seasons with zero games played.

- **~~O-29~~ — BLOCK CREDIT IS FLAT PER MINUTE. ★ SHIPPED S79** (moved to Built). `BlockerWeight` retired
  and deleted with its 30 config keys; credit is now the defender's positive blocking contribution over a
  luck floor. S79 also found the half of the defect S78's diagnosis missed: the block RATE consulted one
  defender, so an unmatched rim protector moved the team block rate by zero. Both fixed. The board went
  from a top-ten spanning 1.2–1.0 (nine guards) to bigs at 2.9/3.1 against guards' 0.3–0.5.

- **★ O-33 — MINUTES ARE SKEWED AGAINST BIGS (S78 ladder finding; split out of O-29).** Bigs average ~24
  mpg against guards' ~32, and every per-game leaderboard is minutes-weighted, so the better man loses the
  board on playing time. Measured: the big at rebound rank 2 takes **0.44 boards/min** against the
  leader's **0.34** and still finishes second. Unlike O-29 this is NOT a credit problem — the rebounding
  credit tracks the right men — it is who the depth chart puts on the floor. **★ CAUSE FOUND (post-S79):
  it is NOT O-6's territory either.** 321 of 347 schools open 3G/1W/1B, so four bigs share one seat's 40
  minutes while five guards share three seats' 120. The board only orders men within a group that was
  already capped by the shape. **This is O-42**, and O-33 closes when O-42 ships.

- **O-34 — ★ THE ANTI-TARGET GATE IS PAGE-ONLY, ON LOAN (S78 ruling, named end date).** The "no elite
  recruit is flat" rule was an EXACT gate that threw; S78 demoted it to a printed number because Glue was
  silently doing the work of the guaranteed hole (96 of the top 347 cards had a Glue skill as their card
  minimum) and the rule was written when the intangibles were dead. It fires at 1–3 now. **This is a
  recorded LOOSENING, proposed by Claude and accepted:** re-rule it once the season page shows what a flat
  card actually plays like. It does not leave Open by fading.

- **O-35 — The intangibles CENTRE is a placeholder (S78, explicitly UN-RULED).** `INT_A`/`INT_B` put
  BasketballIQ / Discipline / HelpDefense at mean ~53.5. Emmett's position: it cannot be judged before
  there are stats. The SHAPE is locked (shared component + idiosyncratic, [8,99], current == latent,
  runway zero); only the centre is open. Revisit after the first season's numbers.

- **O-36 — The intangibles have no DEVELOPMENT (S78, provisional by design).** All three are written
  identically to latent and current, so runway is exactly zero and they cannot grow. A named comment marks
  it. Belongs to an intangible-development session, not to a generator session.

- **O-37 — The fixture declares a tolerance the C# never reads (S78, small).** The Pass-3 fixture header
  carries `float_tolerance: 1e-9` and `Program.Checks.GenPass3.cs` uses its own `GenPass3Tol` const. They
  agree today; nothing asserts they must. One line to close, worth closing because S78 just exercised the
  fixture contract.

- **O-38 — "Elite rim protector, 5'9"" reads as a bug on a card (S78, presentation).** A consequence of
  the S78 ruling that the generator states capability and the engine prices expression. The honest fix is
  presentation — show expressed production beside the rating, or scout language that accounts for body —
  and there is no presentation layer yet, so nothing breaks by waiting. Recorded so it is not
  re-discovered as a generator bug.

- **O-30 — ★ FOUL-OUTS HAVE NO SCHEDULED HOME (orphaned, found S77).** S75 deferred foul-outs to "S77" and
  S77 became the stat page, so nothing is scheduled to build them. The deferral itself is still correct —
  committer selection is post-hoc in the harness, so disqualification needs an RNG restructuring that must
  not share a diff with a roster change. It needs a real session number. Blocks O-28.

- **O-31 — Per-GAME retention: game logs, home/away and conference splits, streaks (S77 deferral,
  EMMETT'S CALL).** S77 keeps season totals only. Cheap to add later at the same seam (`Accumulate` already
  sees one game at a time); the open questions are what the finished game should show a player and the
  save-size arithmetic at career scale. Also the only way to re-derive the S76 per-RANK minutes ladder by
  identity, which season totals cannot do.

- **O-32 — The season page should print its OWN fingerprint (proposed S77, not ruled).** The recorded
  season SHA-256 comes from a recipe that exists nowhere in the repo; the sandbox cannot reproduce it under
  any line-ending convention, so the reference can only come from Emmett's machine via a hand-run shell
  command. The page already prints `Schedule fingerprint:` — a self-computed page fingerprint would be
  reproducible anywhere, immune to line endings and console encoding, and would retire the copy-paste step.
- **O-21 — Normalize the three config loader shapes (S74 deferral).** Eighteen sections are sectioned
  `Deserialize`; `RollAConfig` is root-flat; `RollEConfig` is nineteen hand-written `GetProperty`
  assignments. The divergence is declared and asserted by Phase 71's registry, not hidden — but folding
  RollA/RollE into the common shape is its own session with its own drift audit. Not urgent: RollE's
  binding is now behaviourally proven.

- **★ O-39 — THE BLOCK CONTEST UNDER-WEIGHTS RIM-PROTECTION SKILL AT THE RIM (S79 finding; RE-SCOPED
  AND RE-SEQUENCED at S79.2).** `BlockContestWeights` prices skill at Rim **0.40** — identical to Three —
  against a schedule of Rim 0.40 / Short 0.45 / Mid 0.50 / Long 0.42 / Three 0.40, an inverted U peaking at
  Mid. The source comment beside those weights says skill should count for MORE near the rim; the numbers
  do not.
  **S79.2 measured the lever and it works:** on real generated players, an elite rim protector's share of
  his lineup's rim-block credit against a chase-down wing's runs 43.2% vs 33.4% at the current 0.40, 46.5%
  vs 25.9% at 0.55, and 49.3% vs 16.7% at 0.70.
  **But the weight is not why length wins, so this item is now the SECOND half of a two-part fix.** See
  O-44: the two arms are measured against the same neutral point of 50 while sitting on entirely different
  distributions (length median 59.7, rim-defence median 33.2), so the length arm pays out to 86.5% of the
  league and the skill arm to 17.1%. Raising the skill weight works by making more of the league a
  non-threat (positive-threat share falls 53.8% → 37.1%) rather than by making rim protection matter.
  **Sequencing ruled at S79.2: this waits for S80.** Rim-defence median moves 33.2 → 25.9 (linear) or 17.6
  (convex) under S80's candidates, so any neutral point set today is wrong the moment S80 lands. The length
  half (O-44) does NOT move under S80 and can go first. Emmett's Phase 7 anchor; still his call.

- **O-40 — BLOCK RATE CALIBRATION (S79, expected).** Blocks read 4.2 against the 3.5 target, up from S78's
  4.1. The help arm only ever adds, so this was predicted in the prompt and recorded rather than chased.
  Dials: `BlockHelpShare{Zone}` and `BlockHelpPositionalSwing`. Belongs to a calibration session against a
  real generated population, not to the session that shipped the mechanism.

- **O-41 — THE C6 MAKE-DOOR HELP AGGREGATE IS UNWEIGHTED (S79 audit finding, RULED OUT of S79).** Roll H's
  C6 aggregates the four off-ball defenders' HelpDefense on a fixed denominator of 4.0 with **no positional
  weighting** — a point guard's help suppresses the make rate as much as a centre's — and it never reads
  `RimProtection`. (Roll E carries a parallel all-five/5.0 aggregate into selection.) Ruled out of S79
  because moving make% through a second door in the same session makes the season page unattributable.
  Own session. Measured size for scale: swapping an ordinary lineup for a menace lineup moves C6's
  make-shave by 0.12 percentage points, against the S79 block door's 3.6.

- **★ O-42 — LINEUP SHAPE NEVER VARIES, AND IT IS THE REAL CAUSE OF O-33 (design conversation after S79).**
  Every roster is exactly 5G/4W/4B, the seating floor is 2G/1W/1B, and the fifth seat goes to best
  available — so **321 of 347 schools open 3G/1W/1B**. Four bigs and four wings each compete for ONE seat
  (40 minutes); five guards share three seats (120). That is the whole 24-vs-32 minute gap, and it is NOT
  the scout rank: the board only decides *which* big gets the 40, the shape already decided there are only
  40. Compounding it, defenders are assigned by **slot parity**, so identical shapes league-wide mean a
  wing is always guarded by a wing — no size mismatch can ever occur, nothing punishes three guards, and
  the loop is self-sealing.
  **★ Emmett's ruling (2026-07-27):** a good wing with size should have a field day against three shorter
  guards, and that should force the other team to answer with its own wing — "a great wing is too big for a
  guard and too quick for a post." **The engine already prices both halves** (the S55 height-over-defender
  make bonus and the athleticism gap in `EffectiveRating`); they never fire because the matchup never
  happens. **Shape variety alone lights the fuse under the existing man-to-man wiring** — two teams in
  different shapes produce a wing-on-guard through slot parity, with no matchup-assignment layer needed.
  **★ Build-shape ruling: a team must OWN A SET of deployable lineups and initially deploy one**, not merely
  get a smarter opening five. Same visible result; the difference is that in-game looks and matchup counters
  later become *choosing a different member of a set that already exists* rather than a rewrite.
  **★ THE STANDING ACCEPTANCE TEST for this whole arc, in Emmett's words:** *a 6'10" plodding big who
  averages 4 minutes plays 14 against the one team in the conference with a 6'11" center.* Nothing short of
  that counts. Four things stand between here and it: (1) shape is not a concept anywhere in the code;
  (2) minute targets are per-season, identical every game, and blind to the opponent; (3) slot parity cannot
  express "I brought him in to guard their guy"; (4) nothing observes the game in progress, so "if I don't
  feel like my lineup can function" has no input.

- **★ O-43 — THE ON-BALL CONTEST SHOULD BLEND THE OTHER FOUR DEFENDERS (Emmett's design, 2026-07-27).**
  Today the make contest reads `DefenseRating(matched defender)` alone. Ruled shape: read roughly
  **80% matched man + 20% the aggregate of the other four's ON-BALL defense** — and down the line make that
  blend a **coaching switch setting** (a switch-everything coach moves toward 50/50, and a switching team
  with poor defenders gets burned for it).
  Why this shape and not the alternative: Emmett first proposed a positional **bleed** (~10% toward each
  neighbouring slot) and then **rejected it himself** in favour of this. Correctly — bleed would make slot
  order *spatial*, and the source is explicit that slot 1–5 is a list index with no floor meaning, so
  adjacency would be a load-bearing architectural commitment everywhere at once (and ~24% of floor time is
  already someone playing out of position, who would inherit a seat's neighbours along with the seat).
  The blend needs none of that.
  Three properties that make it cheap: it is **not a new effect** — same single wiring site, different
  input, so nothing double-counts; the other four contribute their **on-ball** ratings, NOT HelpDefense,
  which keeps it from colliding with the help door (O-41); and because the contest already weights perimeter
  vs post defense **by zone**, a switch-everything team whose bigs cannot guard the perimeter should get
  burned *specifically on threes* with nothing extra wired. **Verify that last one before relying on it.**
  It also delivers what the possession-interior idea was reaching for — an elite perimeter defender is no
  longer glued to one man — **without** modelling time inside a possession, which was by far the largest
  build discussed.

- **O-44 — CLOSED, SHIPPED IN S81.3 (2026-07-29).** The fixed yardstick is gone: help is measured against
  the man shooting, so the bar now moves with the matchup and the question this item asked stops existing.
  Closure was made conditional on the realized post-build report, and that condition was met on Emmett's
  machine — the concentration collapse this item warned about does **not** occur (best defender's
  probability-weighted conditional credit share 49.3% -> 49.0%; matched/helper route split 46.0/54.0 ->
  45.8/54.2). **Its stale "blast radius is three call sites" line was wrong and is corrected here:**
  `PutbackDefenderShift` is a separate function, not a caller of `BlockDefenderThreat`, and the putback door
  was already approximately shooter-relative by another route — see O-53. Two measurements from this item
  survive it and are carried forward: the length arm paid out to nearly everyone while skill paid one man in
  six, and **the obvious fix alone would have made it worse** (raising the bar flattens the board onto the
  luck floor). Both are why the fix had to move the bar and re-price what sits above it together, which
  comparing to the shooter does automatically.

- **★ O-45 — THE MAKE DOOR HAS NO BODY GATE ON INTERIOR-DEFENCE SKILL (S79.2, Emmett asked for this
  item).** Blocking is the smaller half of what `RimProtection` does. It is also 65% of
  `DefenseRating(Rim)` (`PostDefense` the other 35%; `PerimeterDefense` contributes **zero** at the rim),
  which feeds `EffectiveRating`'s skill shift and therefore rim make percentage directly — plus
  `DefensiveResistance`'s top-3 blend, which moves shots away from the rim before anyone shoots.
  **The defect, worked example:** Pool_85 (Robert Morris, seed 20260720) is **5'8" with a 43 wingspan and
  RimProtection 96 / PostDefense 88**. His rim defensive rating is **93.2** against **37.0** for the 6'9"
  big beside him, and he takes **35.5%** of his team's rim-block credit to the big's 32.6%.
  *(S79.3 note: he led the nation's ninth-most blocks PER GAME on the old board; on the S79.3 BLK% board
  he is nowhere near the top ten. The rate boards fixed the presentation half. This item is the OTHER
  half and is untouched by that — his rating still suppresses rim FG% through the make door.)* **He is not rare** — of the 49 rostered men with RimProtection >= 80,
  **12 are under 6'0" and only 7 are 6'6"+**.
  **★ Why the engine does not disbelieve him, precisely:** the two doors read his body differently and the
  make door reads it CORRECTLY. `Reach = (Height + Wingspan)/2` = **41.5** for him and deliberately excludes
  Vertical, so a tall shooter collects nearly the full `HeightOverDefenderShift` bonus. `LengthRating =
  (Height + Wingspan + Vertical)/3` = **52.0**, above the 50 bar — his 73 vertical launders his arms. The
  exclusion is deliberate per the `Reach` source comment, so whether shot-blocking should credit vertical at
  one-third weight is a live basketball question, not an oversight.
  **The open question, Emmett's to rule:** the make door damps him through the shooter's height bonus but
  still consumes his 93.2 in the skill channel with no body gate at all. Should a small man's elite interior
  rating stop suppressing rim FG%, and if so by what mechanism? **Neither S80 nor O-44 covers this** — S80
  makes him rarer, O-44 stops the block board believing him, and this is the third piece.
  Context that matters: S78 removed the old hard height cap (`34 + 65·clamp((h−46)/28)`, which pinned a
  5'8" man at 34) **deliberately**, on the recorded ruling *the generator says what a man can do; the engine
  says whether it is felt*, and named "elite rim protector, 5'9"" as the accepted counterweight. The
  generator half shipped; the engine half never did. **This item is that missing half — it is not a
  regression and the answer is not to restore the cap.**

- **★ O-46 — NO PLAYER IN THE ENGINE RUNS AN OFFENCE (S79.3 measurement; documentation only, NOT acted on).**
  The AST% board S79.3 shipped makes it readable for the first time. League median AST% is **9.52**; the
  league's **best passer sits at 26.3%** (Pool_1079, 71 of 270). Elite real-world college point guards
  reach **35–40%**. So the middle looks ordinary and **the elite tail is simply absent** — the engine has
  no player who is the reason his team scores. Note the shape of the miss: it is not that assists are
  globally too low (the calibration page reads assists 9.8 against 13.5, LOW but not absurd) — it is that
  the DISTRIBUTION has no top. Whether this is a generation question (nobody is generated with the
  passing/playmaking package a primary creator needs) or a Roll C question (the assist door cannot
  concentrate credit on one man however good he is) is **unresolved and is the first thing to determine**.
  Belongs to neither S80 nor O-44/O-45. Emmett's call when to take it.

- **★ O-47 — THE DRIVE GATE IS ANCHORED IN ABSOLUTE RATING POINTS, AND THE POPULATION SITS UNDER IT
  (S79.3 conversation; Emmett's read that the league is undertalented, given a mechanism).**
  `Matchup.DriveTools` (`Matchup.cs:1427-1436`) is `(FirstStep, Quickness composite) × unlock`, where
  `unlock = clamp((BallHandling − 28) / (48 − 28), 0, 1)`. **Below BallHandling 28 the unlock is exactly
  zero, so drive tools are zero however fast the player is**, and `ApplyDriveGate` compares
  `gap = DriveTools − matched.PerimeterDefense` with only the wall side firing — so that player eats
  full rim suppression. **Measured on the S79.3 census: median BallHandling is G 25 / W 20 / B 16.**
  More than half the guards in the league — the players whose job is beating a man off the dribble —
  generate nothing at this gate. Bigs are largely shielded by the orientation term (a post-up is not a
  drive), so it bites guards and wings hardest, which is backwards.
  **Where it lands:** rim FG% **51.8 against a 61.0 target**, the largest single miss on the calibration
  page. **Not new, and that is the point:** design.md records this as a known SPEC property from S59
  (*"the gate is NOT level-neutral below rating ~48"*), ruled **ship-as-is** at the time because no real
  population existed to measure against, and names it **"the first item for the Pass A tuning pass."**
  There is now a real population and it sits below the anchor.
  **★ The distinction that decides which session fixes it:** whether the RATINGS are too low is a
  generation question; whether the ENGINE'S ANCHORS are calibrated for the ratings it actually receives
  is a tuning question. This item is the second kind. Related evidence pointing the same way, none of it
  yet connected: guard Outside median 42 against the oracle's own recorded **arc target ~50**; AST% has
  no elite tail (O-46); rim-defence median 33.2 with 83% of the league below the block bar (O-44).
  **Sequencing:** S80 moves this the RIGHT way on its own — freed interior budget flows to perimeter
  skills, BallHandling among them, pushing more guards over 28 — so take this AFTER S80, measured
  against the moved population, not before.

- **★ O-48 — TRANSITION HAS NO ASSIGNMENT MODEL (S81 ruling; Emmett: its own session).**
  On a fast break nobody is matched up — defenders sprint back and pick up whoever is closest — but slot
  parity is the engine's only assignment model. S81 therefore **exempts** transition entirely
  (`BlockerPicker.ResolveOffensiveLineup` returns null when `state.FastBreak`), which makes every gate
  1.0 and preserves Emmett's ruling that transition is one of the two ways guards legitimately get
  blocks. That is the correct conservative call and it is not the answer: a break has *some* structure
  (who is ahead of the ball, who is trailing), and nothing in the engine models it.
  **Scope note:** this is not block-specific. Any future rule that reads "who is this defender
  guarding" inherits the same hole.

- **★ O-49 — THE RARE-EVENT LEADERBOARDS USE A MINUTES FLOOR WHERE THEY NEED AN OPPORTUNITY FLOOR
  (S81 measurement; belongs with the S79.3 percentage-board work).**
  At the 100-minute floor the BLK% board's top four read **10 of 91**, **10 of 92**, **12 of 117**,
  **13 of 129**, all at ~4 mpg. At a true 4% rate over 91 attempts, posting 10% is ordinary luck. The
  actual best shot blocker in the league (Pool_4503, **59 of 642**, 24 mpg) sat **eighth**. At the
  500-minute floor he is **first** and every man on the board carries 596–823 opportunities.
  **REB% at the same floor is fine** — its leaders carry ~1,100 opportunities — because rebounds are
  common and blocks are not. So the defect is not the floor's value, it is the floor's *unit*: BLK% and
  STL% want a denominator threshold (opportunities faced), not a minutes threshold.
  **Not S81's defect and not S81's fix.** Purely a reporting question; the simulation is byte-identical
  across both floors (blocks 4.3, location 89.7%/10.3%, 44,821 credited blocks), which confirms S79.3's
  reporting-only design held.

- **★ O-50 — MORE THAN HALF OF EVERY SHOT IN THE ENGINE IS AT THE RIM (S81 measurement; documentation
  only, NOT acted on).**
  League mean shot diet, measured on the generated population through the locked tendency derivation:
  **Rim 52.7 / Short 5.9 / Mid 7.4 / Long 4.0 / Three 30.1.** Real college basketball is roughly a third
  at the rim. The engine's own credited-block location agrees the shots are there (89.7% of blocks at
  Rim+Short).
  **Why it matters here:** blocks read HIGH (4.3 against 3.5) and the rim is where blocks happen, so a
  rim-heavy diet may be a substantial part of that miss — which would make O-40 partly a *diet* problem
  rather than a *block-door* problem. Do not calibrate the block door against a diet this skewed.
  **Second consequence, and the reason the S81 score is what it is:** the same derivation is bimodal per
  player — arc share reads p25 6.5 / p75 70.5 with nothing between, ~67% of the league pinned at one end
  or the other. That is why S81's spacing score is a logistic on Outside rather than the more direct
  shot-diet read, which classified better (AUC 0.970 vs 0.944) but has no middle to grade.
  **When this settles, revisit S81's spacing score** — the arc-share read becomes correct and drops in
  behind the same lookup with no call-shape change.

- **O-51 — CLOSED, SHIPPED IN S81.2 (2026-07-29).** Both questions ruled and built. Wingspan now
  outweighs height (0.45 / 0.40) and the leap keeps 0.15 of reach — a small strictly positive share, so
  within any fixed body more hops still helps a defender block; what falls is the slope. The leap's real
  value moved to its own terms: the rim (0.40), the second jump (0.80), and the glass in two layers.
  Sequenced BEFORE the shooter-relative change rather than inside it, reversing S81.1's plan line, so
  the help arm is measured against corrected reach.

- **★ O-52 — THE GENERATOR MAKES ALMOST NO LONG FLAT-FOOTED BIGS (S81.2 measurement; documentation
  only, NOT acted on).** Scanning the 4,511-man drafted population for the two archetypes S81.2's change
  most affects found **61 springy small men** (hops 85+, small frame) and **7 long flat-footed bigs**
  (hops ≤35, big frame). `PlayerGenPass3.SIZE_COEF["Vertical"] = -0.02` — hops are essentially
  height-independent in generation, so a 7'0" with a 20-inch vertical is nearly as rare as one with a
  40-inch vertical. **The archetype S81.2 most rewards barely exists yet**, which caps how much of the
  change the league can express. Belongs with the generation arc, not the block door. Emmett's call.


- **★ O-53 — THE TWO BLOCK DOORS REACH SHOOTER-RELATIVITY BY DIFFERENT ROUTES (S81.3 finding; Emmett's call).**
  The located-shot door now compares the helper to the man shooting, on both arms, at every zone. The PUTBACK
  door does something adjacent but not the same: `PutbackBlockRate` subtracts a finisher-resistance term
  measured against the midpoint, so it is *approximately* shooter-relative by a different shape, and
  `PutbackDefenderShift` still reads the neutral bar. Whether the two should be unified is a real basketball
  question and not tidiness: a go-back-up is contested by the whole interior in a scramble, which may
  legitimately justify a different shape from a located jumper contested by one man with help. Not scoped, not
  urgent, and NOT a bug — recorded so the divergence is a decision rather than an accident.

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

- **C-28 — A big who cannot guard the perimeter is as legitimate as a guard who cannot protect the
  rim; the shared defensive bid pair is INTENDED (Emmett's ruling, 2026-07-27).** `PlayerGenPass3`
  drives `pref["PerimDefense"]` and `pref["InteriorDefense"]` from ONE pair of constants
  (`DEF_BID_LO` / `DEF_BID_SPAN`, `:440-441`), mirrored about the defensive plane. S79.2's check-in
  flagged this as a scope contradiction in the S80 prompt, which claimed to change interior defence
  and "nothing else in generation". Emmett ruled the mirror is the design: **perimeter-defending
  bigs should become rare in the same fashion that rim-protecting guards do.** So S80 changes both
  sides together and does NOT split the constants. Consequence, recorded so it is not read as
  collateral damage: a big's `PerimeterDefense`, `Steals` and `OffBallDefense` low tail widens by
  exactly as much as a guard's interior tail. The census (S79.2) prints the two families adjacent
  so the mirror is inspectable.
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

**S81.3 — the help arm compares to the SHOOTER.** Ruled in full at S81.1 (see journal and design);
the basketball is settled and only the build remains. **Renumbered from S81.2**, which took the reach
composite first so this is measured against corrected reach.

**The defect:** `BlockDuelShift` (on the ball) reads both terms as differences against *this shooter*.
`BlockDefenderThreat` (every helper) reads both terms against `AttributeMidpoint`, a **constant**. The
on-ball contest is a matchup; the help arm is a rating against a fixed yardstick. That is the no-scalar
wall breached in the one place it is hardest to see, and it means a D3 seven-footer helps identically in
D3 and in the Big Ten.

**The change delivers three separately-requested behaviours from one edit:** level-relativity with no
level flag; the shooter's HEIGHT reducing help block odds; the shooter's FINISHING reducing help block
odds independently of make%. Rulings 2 and 3 need no extra work — `OffenseRating(Rim)` is already
Finishing and `LengthRating(shooter)` is already computed on the on-ball side.

**O-51 is CLOSED — it shipped in S81.2, ahead of this rather than inside it.** S81.1 reasoned that the
reach questions were only answerable once the comparison moved; the reversal was ruled explicitly, on
the argument that the help arm should be measured against a corrected reach composite rather than the
other way round. **Consequence for this session: its baseline is the S81.2 tree, not the S81 one.**
Reach now reads 0.45 wingspan / 0.40 height / 0.15 vertical, the league's length median sits at 60.9
rather than 59.7, and 87.6% of the league is above the fixed bar — so the fixed-midpoint defect this
session fixes is slightly WIDER than O-44's original numbers describe.

**Absorbs O-44 outright** — do not schedule it separately.

**Why it displaces S80:** S80 changes what ratings players are BORN with; this changes what a rating is
ALLOWED TO DO once he has it. This one is contained to the block door, touches no generation, waits on no
moving population, and closes an open item. S80 stays approved and moves to second.

**Expected side effect, name it in the prompt before measuring:** league blocks read **4.3 against a 3.5
target** and S81 explicitly could not move that (the entire help arm is worth ~1.3 points of a ~2-point
matchup effect on a 12% base). This change reaches the quantity that actually drives block totals and
should move it. **If it does not, that is a finding about the block door's base rate**, not about this
change — and it points at O-50 (52.7% of shots are at the rim) as the remaining suspect.

**Guard the build against a size nerf.** Measured today, a 7'0"/7'5"-span defender with a ZERO
rim-protection rating helps at 1.6x an ordinary 6'8" and outhelps a 6'0" guard with a 99. **That is
correct basketball and must survive** — Emmett's D3 big is dominant because he is the tallest man out
there, and the change must only make that advantage shrink when the floor gets bigger, never invert.

---

**Second: S80 — the interior-defence bid.** Emmett ruled the basketball at S79.2 (C-28, the symmetric
mirror), so the design question is settled and only the build remains. **The S80 prompt as written must be
redrafted first** — S79.2's check-in found five defects (below), and the §6a/§6b passes are already done,
so the redraft is cheap.

**What the redraft must fix, all verified against source at S79.2:**
1. **§4's scope claim was unachievable** — one constant pair drives both defensive bids. Resolved by C-28:
   the mirror is intended, both sides move, no constant split.
2. **★ Every symmetric candidate breaks Phase 70's `line-17` band** (79.5% ±1.5pp): baseline 80.17 →
   81.20 / 81.62 / 81.93 / 82.96 across the four candidates, and the hardest also breaks
   `ceiling-pressure`. `Band()` throws. **The band needs re-ruling as part of S80, or the build fails the
   harness.** Mechanism: freed interior budget raises perimeter value, so more of the stream clears Rscore 17.
3. **R4's endpoint anchor does not protect the elite ceiling.** It pins the bid at dplane 1.0; the bigs'
   realized median is 0.737 and p90 0.905. Measured: max RimProtection falls 98 → 79, p99 80 → 66.
4. **R3 is wrong** — `INTANGIBLES` is three (BasketballIQ, Discipline, HelpDefense). `OffBallDefense` is a
   spend skill in `PerimDefense` (`:159`), NOT locked by `INT_LO`, and is directly in the blast radius.
5. **§3's `tools/real_d1_block_reference.csv` is not in the repo**, and §5b's census did not exist in the
   shape described (height bands, means only) — that half is now shipped.

**Also settled at S79.2 and load-bearing for the prompt:** guards do NOT reach the bid floor — guard dplane
median is **0.207**, p90 0.324, only 10.8% below 0.05 — so a lower floor is nearly cosmetic and **convexity
does the work**. Draw order does NOT change (the mapping consumes no RNG), so fixed draws are free and the
fixture's `draws` block stays byte-identical; only the constants echo and the checkpoints move. And the
candidate populations are not re-allocations of the same men: 139–281 of the 4,511 drafted are different
people, so comparisons are distributional, never paired.

**Direction of travel, worth knowing before the build:** rim FG% reads **51.8 against a 61.0 target**, the
largest miss on the calibration page. S80 cuts guards' interior defence, which moves that miss the right
way. It also lowers the individual block ceiling, which moves O-39's ceiling gap the WRONG way — those two
are in tension and O-39/O-44 are where it gets resolved, not S80.

**The counter-candidate, if the appetite is for a contained engine win instead: O-44, the block door's
neutral point.** Population-independent on its length half, three call sites, and it is the part of the
block-board fix S80 cannot invalidate. Do NOT take O-39 before S80 — sequencing ruled at S79.2.

*Also live, not competing for this slot: O-43 (on-ball blend — one wiring site, pairs with O-41);
O-6 (scout-rank modernization, RAISED PRIORITY, feeding O-33's minutes skew — it is what makes bigs
play 24 minutes against guards' 32); O-47 (the drive gate's absolute anchor — explicitly sequenced
AFTER S80). Detail on each is under Open.*

**Not yet: O-40, block-rate calibration.** Blocks read high, but calibrating against a population that is
still moving is calibrating against a fiction. **S81 sharpened this rather than closing it:** the assignment
gate moved blocks 4.4 → 4.3 against a 3.5 target, and measurement showed why it could not do more — deleting
the ENTIRE help arm moves the rim block rate only 14.32% → 13.02%, so the whole help arm is worth ~1.3 points
of a ~2-point total matchup effect on a 12% base. **The block total is not primarily a help-arm quantity.**
Two other suspects now carry more of it: O-50 (52.7% of shots are at the rim, against roughly a third in real
college basketball) and O-44 (the neutral point, where 83% of the league sits below the bar). Take O-50's
measurement before touching a block dial.

**Recommended NOT next:** any calibration session. Seventeen verdicts are red and the page is explicitly
provisional, which makes chasing them now the exact wrong move — several will move on their own when O-29
and O-6 land. **Also NOT next:** O-30 (foul-outs), unchanged reasoning — an RNG restructuring is a poor
thing to run immediately after a rebaselined page.

*(S78 shipped — the body wall down (`BodyCap` ≡ 99), the interior/rebounding bid re-based, the small-body
handle/shoot lean softened, and Glue out of the budget with the three intangibles on an isolated per-player
stream. Suite `ALL CHECKS PASSED`; Phase 69 bit-identical (0.000E+000 across 35,092 checks); one Phase 70
band re-ruled (ceiling-pressure 23%±3 → 26.5%±2.5, from a fixed five-seed panel); the old body-cap gate
replaced by a ceiling-provenance gate; the anti-target gate demoted to page-only (O-34). Historical detail
in journal S78. Six items opened: O-33 through O-38; O-29 narrowed and O-6 relocated.)*

*(S77 shipped — per-player season records keyed by the person, the three readouts, Phase 73's twenty-two
gates. No engine file touched; the 493 pre-existing page lines byte-identical. Historical detail in
journal S77. Five items opened: O-29 through O-32 plus the O-28 re-dating.)*

*(S76.1 shipped — the sixth silent-drop site; the last literal-20 player-id ceiling in the tree, which had
been dropping 56,714 shot attempts a season. Season rebaselined on exactly one line. Historical detail in
journal S76.1.)*

*(S76 shipped — the minutes allocator, per-position depth charts, residual control, bounded cascades, the
fatigue fence retired; R-1 closed. Historical detail in journal S76. Two flags raised and not resolved:
cross-position occupancy 24.49% against arithmetic floors of 5/5/14% (O-26), and substitutions 34–39 per
team-game against a real-basketball 20–25 (O-27) — both coaching-layer gaps, neither an allocator defect.)*

*(S75 shipped — roster 13, the player-id widening, the eligibility ladder, and the measured league;
historical detail in journal S75.)*
