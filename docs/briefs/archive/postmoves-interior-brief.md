# PostMoves Interior Self-Creation — Design Brief

**Status: BUILT (v1) — Session 57 (2026-07-14).** Shipped, golden-parity green on
Emmett's machine. Magnitudes provisional-locked (shape fixed; the numbers are tuned
later on the season page with a real population).

---

## The problem (found S51)

The S51 interior-offense measurement found PostMoves buys a post player the ball — usage
climbs 12.7 → 23.9% across a full walk — but the extra volume **arrives at a totally
generic shot diet**. The mix never tilts inside, because the displacement diet bend reads
`OffenseRating(zone, shooter)` (Outside / Mid / Close / Finishing only) and PostMoves is
not in that map. The engine modelled *who gets the ball inside* but not that he then
*wants a specific kind of shot*, nor that he can *hold* his spot when the defense loads
up, nor that his baskets are *self-created* rather than fed. The interior analog of the
missing ball-dominance layer.

## What v1 does — three wires, one attribute

All three read the shooter's `PostMoves`, all anchored at 50, all upside-only. Shared lift:

```
postLift(PM) = max(0, (PM − 50) / 49)     // 0 at ≤50, 1 at 99
```

### 1. Diet tilt (Roll G — hunts inside)

`RollGGenerator.TiltInteriorDiet` (pure static). On the **coached** tendency vector, right
after `CoachingPull.Apply` and before the matchup bend:

```
interiorMultiplier = 1 + PostDietSpan × postLift
Rim   *= interiorMultiplier      // Rim and Short scaled by the SAME factor:
Short *= interiorMultiplier      //   authored Rim:Short ratio preserved
renormalize all five to sum 1    // Mid/Long/Three shed share proportionally
```

- **Never touches make%** — it lives on the shot diet before the bend and never calls
  `OffenseRating`. Roll G is location-only; the make door never reads PostMoves.
- **Multiplicative** — a zero authored Rim or Short stays zero. It amplifies existing
  interior intent; it does not invent a post game for a player who has none.
- **Transition-safe by construction** — the fast-break branch returns *above* the
  insertion point, so a running possession never post-hunts (Emmett's "unless it's in
  transition" — satisfied by placement, not a separate guard).
- Feeds **both** live paths (the zero-defender fallback and the normal bend). Both
  downstream consumers normalize by their own total, so the renormalize can't create a
  scale discontinuity vs the identity path.

### 2. Pressure resistance (Roll G — holds his spot)

`RollGGenerator.ResistPressureShift` (pure static). Inside `ApplyDietShift`, once the
bent-dominant zone is known:

```
if bentDomZone is interior (Rim idx 0 or Short idx 1):
    requestedShift *= (1 − PostPressureResistanceSpan × postLift)
```

Shrinks the *requested* usage-pressure diet shift (which lowers `absorbed`, and in step
`residual` — less demand to vacate = less spillover), **never the `intrinsicCapacity`
cap** (that read stays on the shooter's RAW authored tendencies — appetite is not
flexibility). The `[0,1]` span bound keeps the factor in `[0,1]`, so the resistance can
never go negative and add mass back onto the dominant zone.

### 3. Assist discount (Resolver — his buckets are self-created)

`MatchupConfig.PostAssistFactor`. At the one assist emitter, on the Rim + Short assisted
rate:

```
dampPass(pf) = DampFloor + (1 − DampFloor) × clamp((PfHi − pf) / (PfHi − PfLo), 0, 1)
postFactor   = 1 − PostAssistSpan × postLift × dampPass(pf)      // interior, PM>50
assistProb   = clamp(zoneBase × pf × postFactor, Floor, Ceiling)
```

where `pf` is the lineup passing factor already used in the assist math. His post buckets
are credited as assisted **less** often, and *how much* less depends on his teammates'
passing — **most** self-created next to a pass-dead lineup (`dampPass → 1`), **least**
beside elite passers (`dampPass → DampFloor`). Assistedness is jointly caused by scorer
AND passer, not the scorer alone. Mid / Long / Three are never discounted.

## Locked shape, provisional magnitudes

| Knob | Default | Home | Bound (Load) |
|---|---|---|---|
| `PostDietSpan` | 0.50 | RollG | ≥ 0 |
| `PostPressureResistanceSpan` | 0.50 | RollG | [0, 1] |
| `PostAssistSpan` | 0.50 | Matchup | [0, 1] |
| `PostAssistDampFloor` | 0.25 | Matchup | (0, 1] |
| `PostAssistPfLo` | 0.75 | Matchup | < PfHi |
| `PostAssistPfHi` | 1.25 | Matchup | > PfLo |

The approved archetype table (oracle `tools/post_assist_oracle.py`, golden
`tools/post_assist_golden.json`), assist rate % by PostMoves × passing factor:

```
Rim (base 0.4811):        weak pf0.80    avg pf1.00   elite pf1.20
  PM 50  (anchor)         38.5           48.1         57.7      (unchanged)
  PM 60                   34.9           45.0         55.8
  PM 85                   25.8           37.4         51.0
  PM 99                   25.0 (floor)   33.1         48.4
Short (base 0.3831):
  PM 85                   25.0 (floor)   29.8         40.6
  PM 99                   25.0 (floor)   26.3         38.5
```

## Design properties (all proven in the Phase 63 suite check)

- **Anchored at 50** — `postLift(50) = 0`, so all three wires reproduce today to the bit
  for a league-average or below post rating. The diet/resistance helpers return their
  inputs unchanged (no multiply, no renormalize); `PostAssistFactor` returns exactly 1.0.
- **Clean kill switches** — each span at 0 is byte-identical on its path. The assist site
  branches on `postFactor == 1.0` so the identity path runs today's exact two-factor
  expression with **no ×1 reassociation** (bit-exact, not tolerance-close).
- **Make% is untouched** — leak guard: `EffectiveRating(Rim)` and `(Short)` are
  bit-identical as PostMoves walks 0 → 99. The tilt raises the *count* of interior shots;
  the per-zone make% is unmoved.
- **Interior-only, upside-only** — Mid/Long/Three get factor 1.0 at every rating; below-50
  post ratings are identity everywhere.

## What it means for the game

A real post threat plays like one: he hunts his spots inside (more Rim/Short attempts,
none of it fake FG%), he's harder to bump off the block when the defense loads up, and his
post baskets read as self-created rather than assisted — the more so the weaker his team's
passing. A league-average post rating (50) changes nothing.

## The catch worth remembering

The flat-50 lab bench **cannot** show any of this — every bench player sits at PostMoves 50,
the exact identity point, so observation/stress aggregates are (correctly) unmoved. The
proof is Phase 63's golden parity and the end-to-end interior-share climb, not a bench
delta. The feature expresses only on a real population with a PostMoves spread, which is
also where the three spans get their magnitudes calibrated.

## Deferred / adjacent (logged, not scheduled)

- **SelfCreation perimeter assist discount** — v1 discounted interior (Rim/Short) only. The
  perimeter self-creation analog (a shot-creating wing whose pull-up threes are self-made)
  is its own wire, its own gap.
- **Magnitude calibration** — all three spans are shape-locked, number-provisional; tuned on
  the season page against a real population, never on the bench.
- **The committer/attribution and pressure/coaching layers** are untouched — the assist
  discount is credit-only; it does not change who *shoots* or who *turns it over*.
