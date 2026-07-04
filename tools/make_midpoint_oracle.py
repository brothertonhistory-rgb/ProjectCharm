#!/usr/bin/env python3
"""
make_midpoint_oracle.py — Session 32's per-zone midpoint proposal.

WHAT THIS IS (and is not): a FIRST-ORDER PROPOSAL derived from the observed
per-zone FG% gap, pushed through the exact current logistic curve and the exact
box-score carve. It is NOT a recovery of a "population effective rating" — the
live make path applies a stack of post-logistic adjustments (openness bonus,
spacing/gravity penalty, usage tax + residual, passing bonus, screening bonus,
help-defense and off-ball-defense suppression, fast-break hustle suppression,
proportional IQ bump) and matchup-bends the block/foul rates on live shots.
None of that is modeled here; it is the declared first-order error, and the
bounded run-2 derive-apply iteration is the convergence mechanism. The output
is a predicted DIRECTION with an empirical landing, never a guaranteed one.

INPUTS (no constant is hard-coded in this file):
  - Curve parameters (Floor/Ceiling/K/Midpoint per zone) and carve baselines
    (Block/Foul/MafFraction per zone) are read DIRECTLY from config.json —
    the canonical source as of Session 32.
  - The five observed/target FG% pairs arrive as explicit command-line
    arguments, copied from the season calibration readout's per-zone block
    (whose static printer table is the sole committed home of the targets).

USAGE:
  python tools/make_midpoint_oracle.py <path-to-config.json> \
      rim=OBS/TGT short=OBS/TGT mid=OBS/TGT long=OBS/TGT three=OBS/TGT

  e.g.  python tools/make_midpoint_oracle.py src/Charm.Harness/config.json \
          rim=67.7/61.0 short=51.7/43.0 mid=51.7/39.0 long=49.6/36.0 three=50.1/34.0

ALGORITHM per zone (mirrors RollHConfig.MakeProbability and BuildRealPie's
box-score identity constant-for-constant):

  logistic:  makePct(r) = floor + (ceiling - floor) / (1 + exp(-K * (r - midpoint)))

  box-score identity (MissFouled is NOT an FGA; MadeAndFouled is FGA and FGM;
  Blocked is an attempt):
    observedFG% = [makePct*(1-block-foul) + foul*mafFraction]
                  / [1 - foul*(1-mafFraction)]

  1. Invert the identity: observed FG% -> the clean makePct producing it.
  2. SOLVABILITY STOP: that clean makePct must lie strictly inside the curve's
     (floor, ceiling) open interval, else the inversion has no solution and the
     zone cannot be responsibly recentered from aggregate FG% alone.
  3. Numerically invert the logistic (bisection on the exact curve — no
     linearization; the gaps are large and the slope varies across them):
     clean makePct -> the rating at which the CURRENT curve yields it.
  4. Same inversion for the target. shift = rating(observed) - rating(target).
     newMidpoint = oldMidpoint + shift.
  5. RATING-DOMAIN STOP: a proposed midpoint outside [0, 120] is a
     STOP-and-report result, not a value to commit. (Session 32 ruling: the
     bound is the EFFECTIVE-rating domain, not the authored 0-99 range —
     matchup shifts are uncapped by design and the observed data itself
     implies league-average effective ratings well above the authored scale.)

DECLARED CAVEATS, printed with every run:
  - Rim is a MIXED-SAMPLE HEURISTIC: the rim line pools ordinary attempts and
    putbacks, whose carve context differs (putbacks use the flat rim foul
    baseline and a stacked five-defender block), and the possession record
    carries no putback tag to split them. Pass 1 treats all rim attempts as
    ordinary and leans on the run-2 iteration; if rim alone fails to converge
    in two passes, that finding is the deliverable.
  - Carve context here is the CONFIG BASELINE per zone; live shots matchup-bend
    block and foul, which is part of the declared first-order error.
"""

import json
import math
import sys

ZONES = ["rim", "short", "mid", "long", "three"]
ZONE_KEY = {"rim": "Rim", "short": "Short", "mid": "Mid", "long": "Long", "three": "Three"}

# Session 32 ruling: commit range for a proposed midpoint (effective-rating
# domain, not the authored 0-99 attribute range). Outside this -> STOP.
MIDPOINT_COMMIT_RANGE = (0.0, 120.0)

BISECTION_BRACKET = (-500.0, 500.0)   # generous; logistic is monotone in r
BISECTION_TOL = 1e-10


def logistic(r, floor, ceiling, k, midpoint):
    """Mirrors RollHConfig.MakeProbability constant-for-constant."""
    return floor + (ceiling - floor) / (1.0 + math.exp(-k * (r - midpoint)))


def observed_from_clean(make_pct, block, foul, maf_fraction):
    """Mirrors BuildRealPie via the box-score identity."""
    fgm_share = make_pct * (1.0 - block - foul) + foul * maf_fraction
    fga_share = 1.0 - foul * (1.0 - maf_fraction)
    return fgm_share / fga_share


def clean_from_observed(observed, block, foul, maf_fraction):
    """Exact inversion of the identity above (linear in makePct)."""
    fga_share = 1.0 - foul * (1.0 - maf_fraction)
    return (observed * fga_share - foul * maf_fraction) / (1.0 - block - foul)


def invert_logistic(target_make, floor, ceiling, k, midpoint):
    """Bisection on the exact curve: which rating yields target_make?"""
    lo, hi = BISECTION_BRACKET
    if not (logistic(lo, floor, ceiling, k, midpoint) < target_make
            < logistic(hi, floor, ceiling, k, midpoint)):
        raise ValueError("bisection bracket does not straddle the target make")
    while hi - lo > BISECTION_TOL:
        mid = 0.5 * (lo + hi)
        if logistic(mid, floor, ceiling, k, midpoint) < target_make:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def main():
    if len(sys.argv) != 7:
        sys.exit("usage: make_midpoint_oracle.py <config.json> rim=O/T short=O/T "
                 "mid=O/T long=O/T three=O/T   (values in percent, e.g. rim=67.7/61.0)")

    config_path = sys.argv[1]
    pairs = {}
    for arg in sys.argv[2:]:
        try:
            zone, rest = arg.split("=", 1)
            obs_s, tgt_s = rest.split("/", 1)
            pairs[zone.strip().lower()] = (float(obs_s) / 100.0, float(tgt_s) / 100.0)
        except ValueError:
            sys.exit(f"cannot parse argument '{arg}' — expected zone=OBSERVED/TARGET")

    # Validate exactly the five expected zones, no more, no less.
    if set(pairs) != set(ZONES):
        sys.exit(f"expected exactly the five zones {ZONES}, got {sorted(pairs)}")

    with open(config_path) as f:
        rh = json.load(f)["RollH"]

    def cfg(name):
        if name not in rh:
            sys.exit(f"config.json RollH section is missing '{name}' — the 20 "
                     "per-zone logistic keys must be present (Session 32).")
        return rh[name]

    print("=== make_midpoint_oracle — Session 32 per-zone midpoint proposal ===")
    print(f"config: {config_path}")
    print("nature of output: first-order empirical proposal; the post-logistic")
    print("adjustment stack and live matchup bending of block/foul are UNMODELED")
    print("(declared error, absorbed by the bounded run-2 iteration).")
    print()

    stops = []
    proposals = {}

    for zone in ZONES:
        key = ZONE_KEY[zone]
        floor = cfg(f"{key}Floor")
        ceiling = cfg(f"{key}Ceiling")
        k = cfg(f"{key}K")
        old_mid = cfg(f"{key}Midpoint")
        block = cfg(f"Block{key}")
        foul = cfg(f"Foul{key}")
        maf = cfg(f"MafFraction{key}")
        observed, target = pairs[zone]

        print(f"--- {zone.upper()} ---")
        print(f"  curve: floor {floor}  ceiling {ceiling}  K {k}  midpoint {old_mid}")
        print(f"  carve baseline assumed: block {block}  foul {foul}  mafFraction {maf}")
        print(f"  observed FG% {observed*100:.1f}   target FG% {target*100:.1f}")

        clean_obs = clean_from_observed(observed, block, foul, maf)
        clean_tgt = clean_from_observed(target, block, foul, maf)
        print(f"  implied clean make%: observed -> {clean_obs*100:.2f}   target -> {clean_tgt*100:.2f}")

        # Solvability stop: both clean makes must sit strictly inside (floor, ceiling).
        bad = [("observed", clean_obs), ("target", clean_tgt)]
        bad = [(lbl, v) for lbl, v in bad if not (floor < v < ceiling)]
        if bad:
            for lbl, v in bad:
                print(f"  STOP: {lbl} clean make {v*100:.2f}% is at/outside the curve's "
                      f"open interval ({floor*100:.2f}%, {ceiling*100:.2f}%) — no inversion "
                      f"exists; this zone cannot be responsibly recentered from aggregate FG% alone.")
            stops.append(zone)
            print()
            continue

        r_obs = invert_logistic(clean_obs, floor, ceiling, k, old_mid)
        r_tgt = invert_logistic(clean_tgt, floor, ceiling, k, old_mid)
        shift = r_obs - r_tgt
        new_mid = old_mid + shift
        print(f"  rating inversion on the current curve: observed -> {r_obs:.2f}   target -> {r_tgt:.2f}")
        print(f"  proposed shift: {shift:+.2f}   midpoint {old_mid} -> {new_mid:.4f}")

        lo, hi = MIDPOINT_COMMIT_RANGE
        if not (lo <= new_mid <= hi):
            print(f"  STOP: proposed midpoint {new_mid:.2f} is outside the commit range "
                  f"[{lo:.0f}, {hi:.0f}] — diagnostic evidence, not a value to commit.")
            stops.append(zone)
            print()
            continue

        direction = "DOWN toward target" if shift > 0 else "UP toward target"
        print(f"  predicted direction: this zone's observed FG% moves {direction} "
              f"(landing is empirical, never guaranteed).")
        if zone == "rim":
            print("  caveat: MIXED-SAMPLE HEURISTIC — this line pools ordinary rim attempts")
            print("  and putbacks (flat rim foul baseline; stacked five-defender block on")
            print("  putbacks; no putback tag on the record to split them). Pass 1 treats")
            print("  all rim attempts as ordinary and leans on the run-2 iteration.")
        proposals[zone] = (old_mid, new_mid, shift)
        print()

    print("=== SUMMARY ===")
    if stops:
        print(f"STOP-and-report zones: {stops} — do not commit these; the finding is the deliverable.")
    for zone in ZONES:
        if zone in proposals:
            old_mid, new_mid, shift = proposals[zone]
            print(f"  {ZONE_KEY[zone]}Midpoint: {old_mid}  ->  {new_mid:.4f}   ({shift:+.2f})")
    print()
    print("caveats carried by every proposal: post-logistic stack unmodeled; carve")
    print("context is the config baseline (live shots matchup-bend block/foul); rim")
    print("pools putbacks. One derive-apply iteration after run 2 if a zone lands")
    print("outside its band; two misses means something structural is unmodeled.")


if __name__ == "__main__":
    main()
