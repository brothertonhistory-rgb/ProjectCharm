# Project Charm — Status Board

The living done/to-do board. **Read this FIRST when planning any session** (CONVENTIONS §6a),
and update it in the docs step of every session (CONVENTIONS §3). Rules:

- Edited **in place**, like design.md — this reflects *now*; journal.md holds history.
- An item leaves **Open** or **Parked** only by **shipping** or by an **explicit ruling**
  (which moves it to Closed-by-ruling) — never by fading out of memory.
- Keep it short. This is a checklist, not a third journal. One line per item, with the
  session/phase that owns the detail.

Last updated: Session 39 (2026-07-06; three-point volume via era-profile retune — 3PA rate on target 0.39).

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

**Systems live:** fouls/bonus + jump-ball arrow; end-of-half intent; OT; fatigue meter +
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

## 3. Open — next-session candidates

- **Season calibration page — three-point thread CLOSED at S39 (seed 20260703, stock world,
  5205 games).** The full three-point arc is now done across three sessions: S38 fast-break diet
  (transition three 5% → 34.5%), S38.1 make-curve (all five per-zone FG% on target), **S39 volume
  via the era-profile retune — 3PA rate 0.36 → 0.39 (OK, on target) and aggregate FG% 45.3 → 44.9
  (OK, back in band), together, in one pass.** No make-curve touch; a pure population-selection change.
  Per-zone FG% all held OK (rim 61.2 / short 42.8 / mid 39.3 / long 36.1 / three 33.9); fast-break
  page line 34.9% transition three, sane (Means re-synced). FG% sits at the high edge of its band
  (44.9 vs 44.0) — a mix result, noted not chased.
  **Remaining open page gaps (S39 run), for a future ruling — NONE are the three-point thread:**
  **LOW** — FTA 15.8 (t19.5) and FT rate 0.27 (t0.34) (the standing whistle/foul-rate gap, the
  largest standalone miss); rebounds 30.7 (t34.5) incl. offensive 8.9, and steals 4.6 (t6.2)
  (credited-rebound / live-ball reconciliation); OT 2.8% (t4–8%). **HIGH** — assists 17.9 (t13.5;
  possibly the probabilistic attribution, not real over-assisting); turnovers 14.3 (t12.5) / TO%
  20.3; blocks 4.1 (t3.5). **The front-runner standalone miss is FTA-LOW / FT-rate-LOW** (a
  population-sensitive whistle tune, deliberately deferred past the generation build). Pick the
  gap before drafting; the next-session prompt is its own audited pass.
- **Generation-layer bridge opened.** S39 was the first change to the population-selection layer
  (era profile) — the smallest step toward player-generation Pass 2 (below). The oracle-first,
  archetype-table, golden-parity workflow now has a proven generation-side precedent.
- **Turnover-band calibration** — the court-aware bands shipped with **placeholder**
  centers/spreads (backcourt ~5s, frontcourt ~14.5s); tune them off the season page's new
  turnover-length-by-court split. Open question flagged S37: single-period transition /
  ball-advanced turnovers currently draw the short backcourt band — fine for a bring-up
  strip, arguably too short for an already-across possession; decide once the split's size
  and mean are on the page. (S37)
- **Whistle / FTA dial** — FTA LOW; the total-PF cumulative counter rides this session;
  standing condition: if Roll H block/foul baselines move, rim/short midpoints re-derive.
- **Steals vs dead-ball composition** — sharpen the live/dead turnover mix. (S33)
- **Curve-steepness design conversation** — before any K moves; carries the finding that
  diminishing returns no longer exist inside the authored 0–99 range. (S32)
- **Displacement magnitude tuning** — only via the oracle-first flow (approve new oracle
  calibration → regenerate fixture → sync C# defaults + config → parity stays green). (S36)
- **`game` demo command** still stub-wired (self-documented) — upgrade to real generators
  or retire; micro-session or rides a session that touches Program.Game.cs anyway.

## 4. Parked — waiting on a named prerequisite

- **Player-generation Pass 2** — tweener-post existence requirement; weakest-leg
  multiplicative development. (Formal notes in memory + journal, parked 2026-07-03.)
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
