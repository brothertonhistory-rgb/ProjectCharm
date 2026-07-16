# Project Charm — Attribute Wiring Synthesis (merged working reference)

*Merges the two independent synthesis reads of the completed measurement arc (Claude's + ChatGPT's),
with adjudications where they diverged. This is the **classification and diagnosis** doc for the
attribute layer — what each rating actually means in the live engine, and which problems are the
coefficient vs the missing subsystem. Supersedes the earlier `attribute-synthesis-read.md`.*

**Scope discipline (S60):** this document does **not** own the build order. `docs/status.md` owns
what is Open, Parked, and next — it is the board, it is edited every session, and it does the job
better. The sequencing section this doc used to carry was cut at S60 because it had gone stale and
duplicative. **What lives here and nowhere else is the map: §2's buckets and §3's structural read.**

*Last refreshed: S60 (2026-07-14). Written at S54; four of its five structural gaps have since been
resolved — see §3.*

---

## 0. DOCUMENT-WIDE CAVEAT — every bucket below was assigned off flat-50-opponent measurements

**Read this before trusting a single classification.** Every finding underneath came from the sweep
bench between S45 and S55.1, and until S59.2 that bench **hardcoded the opposing team at flat all-50
and could not dial it.** That caps the body gap at roughly ±30 rating points — and the engine's
skill-muting does not engage until well past that. So every read here is the **even-bodied,
average-competition** answer, presented at the time as the general one.

This is not a hypothetical. It flipped a headline in this very document:

- **§2 files rebounding under "clearly healthy."** Against flat-50, hands are worth +2.7 boards at
  body 20 *rising* to +3.7 at body 80. Against a real 6'10"-hyper-athlete front line the ordering
  **inverts**: +1.6 at body 20 vs +2.7 at body 80, and **perfect hands on a tiny body grabs 5.3 while
  average hands on a freak body grabs 7.8.** The freak wins by 47% with worse hands.
- Elite scoring skill reads a dead-flat ~+11pp FG% at every body against flat-50. Against real
  talent it is **+11.2 → +6.0** for a small/slow player — nearly halved.

The same S59.2 session established the rule that generalizes it: **the bench's flat baselines are a
convenience, not a neutral control.** Each flattened dial is an assumption that the measured thing
does not interact with what was flattened, and that assumption failed three times in one session.
**When a finding's headline is "X has no effect," suspect the baseline before believing it** — that
is precisely what a flattened control manufactures.

Re-measuring the eight families against dialed opponents is **Open** on the board and **nothing has
been re-run**. Treat §2 as the honest even-bodied map and expect the body-interaction column to be
missing everywhere.

---

## 1. The convergent diagnosis (both reads, independently)

The engine has a **well-developed model of who shoots and whether the shot goes in**, and a
**comparatively thin model of who controls the possession, who creates the advantage, who protects
the ball, and who makes the decision.** That asymmetry — not any single oversized coefficient — is
the central problem. It causes SelfCreation to carry too much of the offense, Passing/Playmaking to
feel smaller than they should, BallHandling to invert, PostMoves to make generic possessions, Steals
to become stat-attribution, and Discipline to go nearly decorative.

The core matchup engine does **not** need a rewrite. It needs the missing layer that connects player
skill to **offensive responsibility and possession authorship** — plus a handful of self-contained
wires. Fix those and several "weak" attributes start mattering on their own, without inflated draws.

> **S60 update — the diagnosis held, but its prescription did not survive contact.** Four of the five
> gaps in §3 have shipped, and the fifth was **ruled unnecessary** rather than built: Emmett's S57.1
> ruling killed the initiation layer this document called "the big one." The relevant behaviors turned
> out to be expressible without it (see §3). The read was right that the *possession-authorship* side
> was thin; it was wrong that a new layer was the only way to thicken it.

A recurring engine signature to keep in mind: **many ratings move *credit* (the box score) without
moving *outcomes* (the scoreboard)** — Steals, Hustle, Passing/Playmaking, and BasketballIQ's assist
all claim a share of a fixed pile rather than growing it. That's fine for an attribution rating; it's
a problem when the rating is *supposed* to produce. This signature is **still live** and is the
sharpest single lens in this document.

---

## 2. Attribute classification (merged buckets)

*Subject to §0 in full. Entries carry a **[S5x]** tag where a later session moved them.*

**Clearly healthy — leave the wiring alone.**
OffensiveRebounding, DefensiveRebounding, Close, Mid, Finishing, FreeThrow, FoulDrawing,
OffBallMovement, RimProtection, OffBallDefense.
*Caveat: the two rebounding ratings' "healthy" read is the one §0 shows inverting against a real
front line. The rating still works; its value **relative to a body** is the part that was measured
wrong.*

**Healthy but potentially too influential — magnitude/stacking watch (calibrate on a real population,
don't rewire).**
- **Screening** — biggest single team-FG% channel (+2.3pp at 99, all five zones), an all-five
  aggregate with a *squared* term → lineup-stacking risk. First rating under the magnitude microscope.
- **SelfCreation** — steepest usage curve in the engine (13→36% use); the closest thing the engine has
  to ball-dominance. **[S57.1]** Originally flagged as "carrying responsibilities that belong to the
  missing initiation layer" — but that layer was ruled unnecessary, so this is now simply *what
  SelfCreation is*, not a symptom. **[S59.2]** The real usage problem turned out to be elsewhere: a
  genuinely more-skilled star is pinned by `UsageRail` before the depth chart ever speaks.
- **Outside** — biggest pure scoring lever because it owns **two** zones (long two + three).
- **Complete physical packages** — FREAK (+6.4 margin on zero skill) is the strongest single-player
  team effect measured; the danger is *compounding*, controlled at generation (rarity), not by
  nerfing each physical rating. **[S59.2]** This is now understood as a *feature*, not a risk: the
  body-sets-the-ceiling gate is the divisional sorter, measured and confirmed live.
- **HelpDefense (stacked)** — compounds nearly linearly (52.9→49.5 across five helpers). Real, but
  should face **overhelp/rotation diminishing returns** once spacing is exercised. **[S60]** Now a
  named Open item on the board.

**Meaningful but conditional or misleading in isolation — right, but only in combination.**
- **PerimeterDefense** — a *funnel* rating: it suppresses the three but redirects to higher-% interior
  shots, so it read **point-neutral without a rim deterrent behind it**. **[S59]** The drive gate now
  walls a beaten driver's rim access and re-routes it to a contested three — the finding is answered
  (Pass A of three; Passes B and C are Open).
- **PostDefense** — reads more as *"the engine now thinks he's a big"* than *"he stops post scoring"*
  (it's ⅓ of Postness). Identity problem — the one §3 gap still standing.
- **Height / Wingspan / Strength** — buy possessions, boards, blocks; almost no scoring. **[S55/S55.1]
  RETIRED.** The height-over-defender make term shipped; length now buys mismatch scoring (~+1.5pp at
  the top, rim-concentrated, threes flat, gated at reach parity), roughly doubling its full-range gain.
- **Vertical / Speed / Quickness / FirstStep** — modest efficiency doors (~+1–2pp each). **[S59]**
  FirstStep gained a second, larger job: it is 0.62 of the drive-tools composite that beats an on-ball
  defender.
- **Hustle** — a legitimate loose-ball/effort claimer, but a *share* rating; must be valued by impact,
  not counting stats, or it inflates box scores without team effect.
- **PostMoves** — a touch-getter with no offensive *intent*: usage 12.7→23.9% but the diet stayed
  generic. **[S57] RETIRED.** Three wires shipped (interior diet tilt, pressure resistance, assist
  discount); the post player now hunts inside.

**Under-rewarded or incomplete — the layer is missing, not the coefficient.**
- **Passing / Playmaking** — read as capped by the missing initiation layer; could only reshuffle a
  fixed assist pie (top ~2.3 AST). **[S57.1]** Ruled: **this is accepted.** Assists attach after the
  fact via the picker; a high-passing guard already raises team make odds *and* draws more assist
  credit, so the two behaviors that matter are expressed. **Assists are accepted as largely ornamental
  in this system** — possibly a flaw, fine for now. Do not re-pitch the initiation layer.
- **Steals** — at neutral pressure it was an attribution dial (own STL 0.1→2.1, opponent TO flat).
  **[S58] MOSTLY ANSWERED.** The steal-forcing floor is now live at neutral instead of gated to zero,
  so Steals is partly a *create* dial too. The pressure *amplification* still waits on the coaching
  layer.
- **Endurance** — +0.2pp on whole-game aggregates, but **instrument-limited** (fixed lineup, no
  time-slices); expected-flat, not proven-weak. Time-sliced bench before any change. Still parked.
- **BasketballIQ** — healthy but **narrow**: nothing 0→50, a perimeter make bonus 50→99, small assist
  claim. Half the range is inert by design — open question whether *low* IQ should actively hurt (bad
  shot selection, bad passes, poor late-clock reads), not just "no bonus." **[S60]** Now a named Open
  item on the board.

**Fundamentally problematic — the wiring is wrong or absent.**
- **BallHandling** — was *inverted*: the better handler charged *more* turnovers (1.6→3.2). Blame
  without protection. **[S56] HALF-FIXED.** The unforced-turnover channel makes the *rate*
  handling-aware, which **flattens** the inversion (walk slope +1.6→+1.0, pivoting exactly at 50) but
  does not **flip** it — the committer picker still assigns blame by usage, so the best handler still
  eats the most. The flip waits on the per-event attribution rework. **[S59]** BallHandling also
  gained a second job: the multiplicative *unlock* gate on drive tools (below 28 no drive exists at
  all; above 48 it adds exactly nothing).
- **Discipline** — *near-inert even undiluted* (covered man's FTA 3.7→3.6 across the whole walk). A
  rating called Discipline should touch shooting/reaching fouls, biting on fakes, illegal screens,
  offensive fouls, closeout quality — currently it barely earns roster space. **Unchanged; still the
  most clearly under-wired rating in the engine.**
- **Weight** — *cosmetic*: zero gameplay reads, 21 byte-identical rungs. Must be explicitly ruled
  metadata-or-gameplay; a visible 0–99 gameplay-looking rating that does nothing is the worst option.
  **Unchanged; still the only true no-op.**

---

## 3. The structural gaps — four of five resolved

Almost every "problematic / incomplete" rating above is a symptom of one of these. Status as of S60:

1. **Ball-dominance / initiation layer** — **RULED NOT NEEDED (S57.1).** This document called it the
   big one and the highest-value addition. Emmett ruled otherwise and the ruling stands: don't build
   it. The behaviors it was meant to unlock already exist by other routes (see Passing/Playmaking in
   §2). **Closed by ruling — do not re-pitch.**
2. **Unforced-turnover channel** — **SHIPPED S56 (v1).** `Matchup.UnforcedFactor(handling)` scales each
   door's neutral turnover base, anchored at 50. **Residual parked:** the *attribution* half (unforced
   blame should lean to the *weaker* handle; forced blame stays on the high-usage handler) is untouched
   and rides the per-event rework.
3. **Offensive-intent / diet-hunt routing** — **SHIPPED S57.** PostMoves now tilts the interior diet
   without touching make% (Close/Finishing still convert). **Logged, not built:** the SelfCreation
   *perimeter* assist discount — the exact analog on the other side of the floor.
4. **Height-over-defender make term** — **SHIPPED S55, re-measured S55.1.** It gated the generation
   height→skill lean; that gate is now open and the lean can be sized against real post-wire curves.
5. **Dormant pressure/coaching layer** — **STILL PARKED, and still the largest prerequisite.** S58
   removed *Steals* from behind it by building a floor that is live at neutral; BallHandling's
   protection likewise moved to the S56 rate channel. What remains behind it is the *amplification* —
   a dialed-up press driving the forced side the rest of the way. Nothing else in the engine is blocked
   on it today.

**Still a real gap, never built:**
- **PostDefense ↔ Postness coupling.** Decouple *physical/positional* postness (Height/Strength/maybe
  Weight) from *post-defensive skill* (PostDefense). A stocky technician can defend the post; a big can
  be a poor post defender. Using D-skill to set body-role blurs both. **The last survivor of the
  original five-item Tier 1.** Parked on the board.

**Not gaps, but they change how a rating should be valued / wired:**
- **Disruption / deflection channel** — **PARTLY BUILT (S58).** Long arms now feed the steal contest
  (two-sided, perimeter-gated) and tilt attribution slightly. The *other* behaviors this named —
  passing-lane deflections, tipped balls, entry denial, contest quality as distinct outcomes — do not
  exist. Whether they should is open.
- **On-ball mismatch hunting** — the offense doesn't shift volume toward a soft *on-ball* defender
  (S54: covered FGA flat). Off-ball feeding *already works* (S54 DEFENSIVE_LIABILITY: the man guarded
  by a weak off-ball defender took 12.8→15.1 FGA). This is the deferred `DefenderPicker` → carried
  `PossessionState.DefenderSlot` promotion; S59 noted the drive gate is the second door that would
  justify it, and declined, because the pick is still a pure deterministic slot map. **[S60]** Now a
  named Open item on the board.

---

## 4. Adjudications (where the two reads differed)

*Preserved as written at S54 — this is the record of how the merged read was settled, not a live
plan. Later sessions may have overtaken individual items; §2 and §3 carry the current state.*

- **HelpDefense overhelp** — ChatGPT right, folded: near-linear stacking should meet diminishing
  returns (open shooters, rotation conflict, spacing punishment). Caveat we both land on: the flat
  bench has no spacing to punish it, so this is a *test-with-spacing* item, not a confirmed too-strong.
- **BasketballIQ dead lower half** — ChatGPT right, folded as an open design question (should low IQ
  actively harm?).
- **PostDefense decouple recipe** — ChatGPT's split (physical postness vs post-D skill) adopted into
  the gap list.
- **Disruption channel** — ChatGPT's synthesis of Steals+Wingspan adopted. *(S58 built the steal half.)*
- **"Conditional in isolation" tier** — ChatGPT's taxonomy is better than Claude's "modest"; adopted.
- **"Matchup hunting too muted"** — *partial.* On-ball hunting is deliberately deferred (a named
  DefenderPicker seam), and off-ball feeding already fires. Logged as a future promotion, not a gap.
- **"Add a separate Disruption/Steals-creation rating"** — *premature.* The forcing channel exists
  (Roll A, pressure-gated). Test Steals under non-neutral pressure first; a new rating may be overkill.
  *(S58 vindicated this: the existing channel was un-gated rather than replaced. No new rating.)*
- **"Screening should not help all five zones equally"** — a design *opinion*, Emmett's call, not a
  correction. The magnitude/stacking concern is shared and real.
- **"Endurance is suspicious"** — overstated; a flat full-game aggregate is *expected* on a fixed-
  lineup, time-slice-blind bench. Same conclusion (time-slice test first), gentler read.

---

## 5. Generation implications (what waits on the engine work)

- **Do not inflate the weak ratings' draws to make them feel relevant.** Passing, Playmaking,
  BallHandling, Discipline, Steals, PostMoves, Endurance were under-rewarded because a *layer* was
  missing — higher numbers would create inflated-looking attributes whose on-court value still fails
  the label. Fix the engine meaning, then size. **[S60] Mostly discharged:** PostMoves, BallHandling
  (rate), and Steals now have their wires, and Passing/Playmaking were ruled complete-as-is. The three
  still genuinely under-wired at generation time are **Discipline, Weight, and Endurance.**
- ~~Rule the height-over-defender term before sizing any height→skill lean.~~ **[S55/S55.1] DONE** —
  the term shipped and the curves were re-measured. The lean can now be sized against real numbers.
- **Control the physical package with a latent body/athleticism model, not independent generous draws**
  — the FREAK compounding should be generational-rare, and the model should produce the honest splits
  (tall-but-slow, long-but-weak, quick-but-no-first-step, durable-but-ground-bound). **[S59.2]
  Reinforced and sharpened:** the body-sets-the-ceiling gate is measured and live, so the body model
  *is* the divisional sorter. Same skill, two bodies = a D3 star vs a below-average high-major player.
- **The tweener-post is confirmed producible today** (S51) — the divvy's skill-overrides-size principle
  has engine support now.
- **Rebounding needs no generation floor for tall players anymore** — post-S46 a body confers passive
  board value on its own. **[S59.2] Now measured against a real front line, and the earlier flat-50
  read was backwards** (see §0): against real bigs, hands are worth *less* on a small body, and a freak
  body with average hands beats perfect hands on a tiny one. **A rebounding floor is not needed and the
  evidence for that is now stronger, not weaker.**
