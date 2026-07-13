# Unforced-Turnover Channel — Design Brief

**Status: BUILT (v1) — Session 56 (2026-07-13).** Shipped, golden-parity green on
Emmett's machine. Magnitudes provisional-locked (shape fixed; the numbers are tuned
later on the season page with a real population).

---

## The problem (found S49)

At neutral pressure the turnover base was a flat constant — a butterfingers point guard
and a sure-handed one coughed it up at exactly the same rate until the defense pressed.
BallHandling only entered through a *defense-relative* matchup that the pressure gate
zeros at neutral, so at a normal defensive setting handling had no effect on turnovers at
all. Worse, the S49 walk read **inverted**: better handlers accumulated *more* personal
turnovers, because the committer picker attributes team/bad-pass turnovers by usage (a
good handler holds the ball more) while the protection channel that should offset it was
gated to zero. Handling carried turnover *blame* but not turnover *protection*.

## What v1 does

A single dimensionless factor `g(handling)` multiplies each door's own **flat** neutral
turnover base share:

- **Roll B** (team initiation): `base × g(teamBallHandlingAggregate)`
- **Roll F** (individual action): `base × g(handler.BallHandling)`

```
lift(h) = (1 − tanh((h − Mid) / Scale)) / 2
g(h)    = 1 + SpanFrac × (lift(h) − lift(50)),   clamped at FloorFrac
```

**Locked constants:** `Mid 55`, `Scale 18`, `SpanFrac 0.443`, `FloorFrac 0.72`.

Design properties, all proven in the Phase 62 suite check:

- **Anchored at 50** — `g(50) = 1` exactly, so a league-average handler reproduces
  today's rate to the bit. `SpanFrac = 0` is a clean kill switch: `g ≡ 1` for every
  rating, byte-identical to the pre-change engine on the turnover path.
- **One continuous curve, not one-sided** — bad hands *raise* the base, good hands
  *lower* it, pivoting at 50 (g(20) ≈ 1.153, g(99) ≈ 0.722).
- **Diminishing returns above ~80** — Emmett's ruling: "even a 99 guy can screw up, but
  above 80 there have to be diminishing returns." 50→70 moves g ~0.21; 85→99 moves it
  only ~0.012.
- **The elite floor is the curve's own asymptote (~0.72×), not the clamp.** `FloorFrac`
  is a safety rail set just *below* the in-range minimum (0.722), so it never activates
  for any authored 0–99 rating; only an out-of-range rating trips it.
- **Multiplicative on each door's own base** → proportional and exposure-free on a
  uniform-handling lineup: no per-door allocation, no exposure measurement. One curve,
  two bases.

`actionMass` is held flat (built from the config constants); only the turnover *numerator*
is scaled. Proceed (Roll B) / ShotAttempt (Roll F) absorbs the change via
`1 − toShare − foulShare`, exactly as pressure already does. The forced/pressure matchup
math, the committer picker, and Roll A / Roll K were not touched.

## The honest finding — it FLATTENS the inversion, it does not fully FLIP it

The pure handling effect is now correctly signed and anchored: against the kill switch,
the swept player's turnovers rise for bad hands and fall for good hands, pivoting exactly
at 50 (elite ≈ −0.5 TO/game, butterfingers ≈ +0.2). That part works perfectly.

But the *absolute* personal-TO count on the S49 walk still drifts **up** with handling
(≈1.8 → 2.8 across 0→99), where today's engine drifts up harder (≈1.6 → 3.2). The fix
**flattens** the inversion (walk slope +1.6 → +1.0) rather than **flipping** it negative.
The reason is deliberate scope: the dominant personal-TO channel for a high-usage guard is
the usage-weighted attribution of team/bad-pass turnovers through the committer picker and
Roll C — and those were left untouched this session. The rate is now honest; the
attribution still climbs with usage.

**Fully flipping the count negative requires the deferred per-event attribution rework**
(richer touch/initiation attribution) — out of scope for v1, and the scope wall was right
to leave it. What v1 delivers is the correct *rate* and a substantial reduction of the
inversion, not its reversal.

## Deferred / parked

- **Full inversion flip** — needs the per-event attribution rework so bad-pass/lost-ball
  blame follows the weaker handle rather than raw usage. Parked (touch/initiation model).
- **Pressure-dialed interaction** — v1 measures press vs neutral deltas as a *finding*, not
  an asserted invariant; the pressure/coaching layer that would dial forcing is still
  dormant.
- **Magnitudes** — `SpanFrac`/`FloorFrac` are provisional; the calibration read happens on
  the season page with a real population, never on the flat bench.

## Where it lives

- Curve: `Matchup.UnforcedFactor(handling, cfg)` in `src/Charm.Engine/Core/Matchup.cs`.
- Call sites: `RollBGenerator` (team aggregate), `RollFGenerator` (named handler).
- Config: `MatchupConfig.UnforcedMid/Scale/SpanFrac/FloorFrac` + `config.json` `Matchup.*`,
  range-validated at Load.
- Oracle of record: `tools/unforced_turnover_oracle.py` → `tools/unforced_turnover_golden.json`.
- Suite check: Phase 62 (`Program.Checks.UnforcedTurnover.cs`) — 30-case golden parity at
  1e-12, helper tests, config guards.
