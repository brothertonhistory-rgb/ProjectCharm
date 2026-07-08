# Project Charm — Status Board

The living done/to-do board. **Read this FIRST when planning any session** (CONVENTIONS §6a),
and update it in the docs step of every session (CONVENTIONS §3). Rules:

- Edited **in place**, like design.md — this reflects *now*; journal.md holds history.
- An item leaves **Open** or **Parked** only by **shipping** or by an **explicit ruling**
  (which moves it to Closed-by-ruling) — never by fading out of memory.
- Keep it short. This is a checklist, not a third journal. One line per item, with the
  session/phase that owns the detail.

Last updated: Session 42.2 (2026-07-08; replay-fixture micro-session — the deterministic math-replay fixture + reference reader for the Pass-2 C# port are committed and locked: `tools/gen_pass2_replay_fixture_s42_2.json` (306 players, seed 20260706) + `tools/gen_pass2_replay_check.py`; a pure recording side-channel, so the default oracle run is byte-identical to S42.1 and NO generation math changed; Python only, design.md untouched; verified green on Emmett's machine — 306 players / 51,714 field checks / 0 failures, recorder cross-check 33,795 players / 0 mismatches). Prior: Session 42.1 (2026-07-07; oracle repair micro-session — the Pass-2 oracle re-locked after three bounded fixes: weapon-census offsets on the argmax, ONE shared FT idiosyncrasy draw, age/class labeled a placeholder; Python only, design.md untouched); Session 42 (2026-07-07; Player-generation Pass 2 — the skill-first generation oracle is locked as the C# port spec, `tools/gen_pass2_skillfirst_oracle.py`, after five adversarial-review rounds; oracle-only, design.md untouched, C# port is next); Session 41 (2026-07-06; scoreboard — assists 13.7 OK, steals 6.5 OK, rebound instrument audited). Post-S41 ruling (2026-07-06): OT-LOW parked under the coaching / late-game-strategy layer.

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
bench instruments.

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

## 3. Open — next-session candidates

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
- **C# port of the Pass-2 skill-first generation oracle — the named next build (S42; oracle re-locked
  S42.1; validation reference committed S42.2).** The oracle is
  locked at `tools/gen_pass2_skillfirst_oracle.py`; the port is the live-generator rewrite that adds Player
  fields for latent/current/runway/arrival/class, deletes the retired body/package-gate floors
  (`GenEnforceFloors`/`GenEnforceLegHealth`), and proves **math parity** by replaying the **committed
  deterministic fixture** `tools/gen_pass2_replay_fixture_s42_2.json` (S42.2 — 306 players, seed 20260706;
  raw draws + named intermediates + frozen constants, RNG factored out) through the recompute obligations
  the reference reader `tools/gen_pass2_replay_check.py` demonstrates (integers exact, floats ≤ 1e-9; the
  reader reimplements every formula from scratch and tripwires its constants against the fixture echo). The
  port validates against this committed fixture rather than generating a fresh one at port time. One edge is
  unexercised by the fixture on the canonical seed and is noted for the port: `height_high_clamp`
  (Height == 99, ~4.5σ on the post branch; recorded as `NONE` in the fixture header). Preserve the frozen
  contracts (see Closed-by-ruling, S42 + S42.1): three independent draws;
  only
  orientation→height, size→athleticism, orientation→arrival dependencies; chosen-weapon specialization
  **via the S42.1 offset argmax** (the offset table is part of the spec; the [F3] counterfactual
  scaffolding is not); the shared FT idiosyncrasy draw; the age-placeholder non-porting rule (arrival ports
  as mechanism, the age/class labels do not port as spec);
  current/latent/runway separation; full 33-key emission; honest cohort → downstream recruitable export;
  orientation-weighted continuous pathway selection; no generator repair/rejection/redraw/role-package.
  `design.md` is edited **at the port session** (not the oracle session), against real ported code.
  Do not reopen oracle calibration unless the port reveals a fidelity mismatch or the first real rosters
  produce a concrete population problem (reviewer's standing condition, applied once already at S42.1).
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
- **Player-generation Pass 2 — oracle LOCKED (S42), re-locked (S42.1); the C# port is now an active Open build** (see §3).
  The skill-first generation model is frozen in `tools/gen_pass2_skillfirst_oracle.py` and the S42
  Closed-by-ruling entry. Two design notes still ride the port, not the oracle: the **tweener-post
  existence requirement** (guard/wing-sized players whose primary package is post play) is satisfied in
  principle by the skill-first orientation model and confirmed at the port; **weakest-leg multiplicative
  development** belongs to the development/season layer, not generation. (Formal notes in memory + journal.)
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
