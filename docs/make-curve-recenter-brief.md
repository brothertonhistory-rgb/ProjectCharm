# Make-Curve Recenter — Design Brief v2 (calibration arc, session 1)

Design conversation of 2026-07-21 (post-S63). **v2 folds the outside review** (fit
hierarchy defined; end-to-end constraint semantics made explicit; target provenance
labelled; shape audit + rating-0 guardrail added; season acceptance tightened). All
eight review findings were valid and folded; one footnote on the league-line
prediction's data limits (§8). Status: **rulings locked, oracle session not yet run.**
This brief is the governing record; the build prompt derives from it.

---

## 1. The rulings (Emmett, 2026-07-21 — all locked)

1. **An even matchup shoots normal basketball at every level.** D1/D2/D3 all land near
   real college percentages; absolute talent expresses through mismatches, recruiting,
   and who beats whom — never through whether an even game looks like basketball. (The
   engine already delivers the *flatness* — S59.2's ladder ran 53-53 / 55-55 / 56-56 /
   57-57 across four levels — the flat line just sits ~15 points too low.)
2. **The anchor: a 50-rated shooter, even bodies, even defense, shoots league average.**
3. **The spread from three: elite ~45% / average ~34% / poor ~27%** (vs typical
   defense). The top of today's curve is already right (S48: Outside 99 vs an average
   defender = 44.8%); the middle is what moves.
4. **Context pays on top of the anchor.** 34%-at-50 is *context-neutral* — average
   teammates, equal share. The existing channels (usage relief, gravity/spacing, the C4
   passing converter) raise it for a featured-role shooter on a good team. **The Korver
   requirement:** an elite shooter as a low-volume 5th option beside elite teammates
   should clear 50% from three once those channels run at design strength.

## 2. What this is and is not

**IS:** a re-anchoring of the five zone make curves (Roll H logistics) so the real
Pass-2 population produces real-basketball percentages at even matchups. Constants
move; no new wiring, no formula change, no new attribute reads.

**IS NOT:** a shot-diet fix (the 0.69 three-rate is its own session — even a perfect
curve caps league FG% near 39 under today's mix); not the FT curve (own session,
61.2 → 71); not the relief/gravity scale-up (gated behind the intent-vs-touches
ruling — this session RECORDS the Korver gap, the relief session closes it); not a
generation change (the transform stays oracle-locked; the population median Outside of
~27 is honest — it includes bigs and non-shooters, and the diet session is what routes
attempts to the capable).

## 3. The measured starting point (verified against the LIVE config.json, 2026-07-21)

The curve: `makePct = Floor + (Ceiling − Floor) / (1 + exp(−K × (effectiveRating −
Midpoint)))` per zone, reading the matchup-adjusted EffectiveRating.

At effective rating 50 vs the standing S50 anchors:

| zone  | today @eff50 | target @eff50 | today @eff0 | today @eff99 (raw curve) |
|-------|--------------|---------------|-------------|---------------------------|
| Rim   | 58.8%        | 61%           | 45.0%       | 76.1%                     |
| Short | 34.0%        | 43%           | 22.5%       | 48.8%                     |
| Mid   | 26.4%        | 39%           | 17.2%       | 40.0%                     |
| Long  | 25.7%        | 36%           | 20.7%       | 39.7%                     |
| Three | 23.5%        | 34%           | 18.0%       | 37.0%                     |

Context: the OLD pool ran league 3P% 33.8 because its median shooter sat near the old
midpoints; the same curve reads the new population at its floor — the whole story of
S63's 22.0%. The S48 elite figure (44.8% at rating 99) includes the live matchup gap
pushing effective rating above 99 — which is why P2/P3 below are end-to-end scenarios,
never raw curve points.

## 4. Constraint semantics — three pinned points per zone

- **P1 (the anchor)** — a raw curve point: the logistic at effective rating 50 equals
  the target column. Hard equality, every zone.
- **P2 (the elite)** — an **end-to-end scenario**: the elite archetype (a named real
  generated player, Outside 95+) vs a rating-50 defender at even bodies, run through
  the **live EffectiveRating path** (skill gap, physical, height term, discipline shave
  — all at their real values for the scenario), fitted against the final make output.
- **P3 (the poor)** — an **end-to-end scenario**: a rating-20 shooter, all other inputs
  neutral, vs a rating-50 defender at even bodies, through the same live path, against
  the poor target. (The *median generated shooter*, Outside ~27, is a separate
  archetype ROW in §6 — never conflated with the P3 pin.)

For every scenario the oracle records the triplet **(raw rating → computed effective
rating → final make%)** so raw-rating and curve-point targets can never be conflated.

**Target provenance — each pin is labelled, and the oracle treats the labels:**

| zone  | P1 @eff50 | P2 elite | P3 poor |
|-------|-----------|----------|---------|
| Three | **hard 34** (ruled) | **hard 45** (ruled) | **hard 27** (ruled) |
| Rim   | **hard 61** (S50 anchor) | provisional ~68 | diagnostic |
| Short | **hard 43** (S50 anchor) | provisional ~52 | diagnostic |
| Long  | **hard 36** (S50 anchor) | provisional ~44 | diagnostic |
| Mid   | **hard 39** (S50 anchor) | provisional ~46 | diagnostic |

*hard* = fit constraint. *provisional* = fit toward it, but Emmett confirms or moves
it at the table read — a provisional miss is reported, never silently absorbed.
*diagnostic* = computed and shown, not fitted.

## 5. The fit hierarchy (deterministic — no hidden optimizer choices)

Per zone, in order:

1. Hold Floor and Ceiling at today's values. Fit **(Midpoint, K)** with **P1 as a hard
   equality**, minimizing the combined error across P2 and P3 (equal weights; for
   provisional/diagnostic pins, weight P2 only).
2. If P2 or P3 misses by more than **±0.5pp**: unlock **exactly one** additional
   parameter — **Floor when the poor-end miss dominates, Ceiling when the elite-end
   miss dominates** — and refit.
3. Unlock both only if one extra degree of freedom still cannot satisfy the pins.
4. **Every moved constant is reported with the miss that forced it.** A fit that moves
   Floor or Ceiling without a recorded forcing miss is a spec violation.

## 6. The archetype table (the sign-off medium)

Named REAL generated players from the S63 pool (pool ids recorded so rows replay),
before → after, vs a rating-50 defender at even bodies, context-neutral except where
the row says otherwise:

- the median shooter (Outside ~27) — today ~21% from three → expected ~29–30
- the 50-rated shooter — 23.5 → 34 (the anchor row)
- the p99 shooter (Outside ~76) — today ~30 → ~42
- the elite (Outside 95+, one of the pool's six) — ~44 → ~45 (near-unmoved, by design)
- a rim-running big at his zones (Rim/Short rows)
- **the Korver row (acceptance, DEFERRED-CLOSE):** elite shooter, ~10% share, elite
  teammates, gravity on — today's stacked output (expected ~46%) vs the ≥50%
  requirement. The gap is reported **in both percentage points and relative terms**
  (e.g. "+5.0pp / +11% relative") and recorded in status.md as the relief/gravity
  session's entry criterion. NOT a pass/fail for this build.
- **the mirror row:** the same elite shooter as a heavy-share first option on a bad
  team (taxed, no gravity) — should sit low-40s. Guards against stars becoming free.

**The shape audit (beside the table, per zone):** fitted make% at effective ratings
**0 / 10 / 20 / 50 / 76 / 95 / 99**, plus interval gains **20→50, 50→76, 76→95,
95→99** in a before/after Δ table — so an awkward shoulder or a p99≈elite collapse is
visible at sign-off, not discovered on the page. **Rating-0 guardrail:** 0 is
diagnostic, never pinned, but the oracle must FLAG any zone where make(0) lands within
3pp of the poor pin or where the 0→20 interval gain falls below a third of the 20→50
gain — a true non-shooter must remain visibly worse than a poor shooter when he does
shoot (diet decides how often; the curve still decides how well).

## 7. Assumptions to disprove at the build session (adversarial preamble seeds)

- **A1 — the constants live in config.json's RollH block and nowhere else
  load-bearing.** The RollHConfig.cs code defaults are stale decoys (the S55 catch).
  Enumerate every consumer of the five-zone constants.
- **A2 — which suite fixtures EMBED curve outputs.** Phase 61/66/67 goldens carry
  absolute makePct values computed under the current curve; a curve change means
  regenerating those goldens from their oracles under the new config. Ratio-based
  checks (e.g. Phase 67's flat-across-zones) survive untouched. The build greps every
  golden for curve-derived absolutes and lists regenerate-vs-survives before touching
  a constant.
- **A3 — the flat-50 bench anchors all move.** S48's control (9.364 PTS / 34.33 FG%)
  and every family-sweep baseline shift; attribute-meaning.md gets a document-level
  note (the FINDINGS survive; the absolute levels are re-based).
- **A4 — the observation corpus and stress buckets move a lot** (the corpus should
  land near real D1). Expected movers enumerated with predicted directions before the
  run, per the drift-audit discipline.
- **A5 — the EffectiveRating path.** The oracle must reproduce the live gap/shift math
  so "50 vs 50" really enters the curve at 50 and the P2/P3 scenarios really flow
  through what the engine does. Verified against Matchup source at draft time,
  re-verified at the gate.

## 8. Acceptance (what green means)

- **Oracle:** the fit hierarchy honored (every moved constant has a recorded forcing
  miss); hard pins hit within ±0.5pp; provisional pins reported with their misses;
  shape audit clean or flags surfaced; archetype table signed by Emmett.
- **Build:** goldens regenerated where A2 says so; full suite green; drift audit shows
  ONLY the predicted movers.
- **Page (observational, never the curve's pass/fail):** the controlled matchup probes
  are the curve's test. The season page is a population- and diet-weighted observation,
  compared against an **oracle-predicted league line** computed from the S63 zone
  attempt mix plus the pool's rating distribution routed through the committed tendency
  oracle — an approximation, stated as such (the page does not record per-shot shooter
  identity). League 3P% is NOT expected to reach 34 until the diet session; the
  predicted line in the what-to-watch note is what the page is held to.
- **The Korver gap** (pp and relative) recorded in status.md as the relief/gravity
  session's entry criterion.

## 9. Sequencing (the arc)

1. **This session** — the recenter (oracle → table → Emmett → build).
2. **The FT curve** — same shape, one curve, 61 → 71.
3. **The shot diet** — the 0.69 three-rate vs the era profile; routes attempts to the
   capable.
4. **Intent-vs-touches ruling → relief + gravity scales** — closes the Korver gap.
5. **The usage tax to ~0.7** (S60.2) — the star's price becomes real.

After each: season page diffed against the recorded S63 table, never memory.
