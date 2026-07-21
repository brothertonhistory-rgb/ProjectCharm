#!/usr/bin/env python3
"""
Session 62 — Discipline Effect B: the per-man NON-SHOOTING foul model. LOCKED ORACLE.

This is the sign-off reference for the reach-in propensity math. The C# engine ports
these formulas as named statics on Matchup; the golden fixture this script emits
(nonshooting_foul_golden.json) is replayed by the Phase 68 harness check, which binds
to the engine's OWN named statics and asserts parity at 1e-12 — never a formula copy.

Model (RULED at the S62 gate):

  discFactor(D)   = 1 - DiscSpan  * clamp((D-50)/49, -1, +1)   PRIMARY, symmetric about 50
  athFactor(A)    = 1 - AthSpan   * clamp((A-50)/49, -1, +1)   SMALL secondary
  perimOrient(P,m)= 0.5 - 0.5*tanh((P - m)/PostnessScale)      raw Postness -> [0,1] perim,
                                                               lineup-relative (m = mean)
  perimFactor(o)  = 1 + PerimSpan * (2o - 1)                   SLIGHT lean, o in [0,1]
  propensity      = LuckFloor + discFactor * athFactor * perimFactor   Base fixed at 1.0

  perManAggregate(five defenders) = sum(propensity_i) / (5 * refProp)
      refProp = propensity(D=50, A=50, o=0.5) = LuckFloor + 1   (= 1.0 at five-average)

  reach-in committer weight_i  = propensity_i          (full; incl. perimeter lean)
  situational committer weight_i = discFactor(D_i)     (candidate (b), Discipline-only)

Low Discipline -> MORE fouls (a hacker). LuckFloor keeps every propensity > 0 so no
defender is ever un-drawable. Magnitudes are page-tuned later, never suite-asserted.
"""

import json, math

# Signed-off placeholders (must equal the C# MatchupConfig defaults).
CFG = {
    "ReachInDiscSpan":     0.35,
    "ReachInAthSpan":      0.12,
    "ReachInPerimSpan":    0.10,
    "ReachInLuckFloor":    0.13,
    "ReachInPostnessScale": 25.0,
}

def clamp(x, lo, hi): return max(lo, min(hi, x))

def disc_factor(D):  return 1.0 - CFG["ReachInDiscSpan"] * clamp((D - 50.0) / 49.0, -1.0, 1.0)
def ath_factor(A):   return 1.0 - CFG["ReachInAthSpan"]  * clamp((A - 50.0) / 49.0, -1.0, 1.0)
def perim_orient(P, mean):
    return 0.5 - 0.5 * math.tanh((P - mean) / CFG["ReachInPostnessScale"])
def perim_factor(o): return 1.0 + CFG["ReachInPerimSpan"] * (2.0 * o - 1.0)

def propensity(D, A, o):
    return CFG["ReachInLuckFloor"] + disc_factor(D) * ath_factor(A) * perim_factor(o)

REF_PROP = propensity(50, 50, 0.5)   # = LuckFloor + 1

def per_man_aggregate(defenders):
    # defenders: list of (D, A, o)
    s = sum(propensity(D, A, o) for (D, A, o) in defenders)
    return s / (5.0 * REF_PROP)

# ── Golden rows ──────────────────────────────────────────────────────────────
rows = {"config": CFG, "refProp": REF_PROP}

rows["discFactor"]  = [{"D": D, "want": disc_factor(D)} for D in (0, 25, 50, 75, 99)]
rows["athFactor"]   = [{"A": A, "want": ath_factor(A)}  for A in (0, 25, 50, 75, 99)]
rows["perimFactor"] = [{"o": o, "want": perim_factor(o)} for o in (0.0, 0.25, 0.5, 0.75, 1.0)]
rows["perimOrient"] = [{"P": P, "mean": m, "want": perim_orient(P, m)}
                       for (P, m) in ((60, 45), (45, 45), (30, 45), (45, 30), (45, 60))]

# propensity archetypes (D, A, o)
prop_cases = [
    (0, 50, 0.0), (0, 50, 0.5), (0, 50, 1.0),      # hacker: post / neutral / perimeter
    (50, 50, 0.5),                                  # average
    (99, 50, 0.5), (99, 50, 1.0),                   # lockdown neutral / perimeter
    (0, 99, 0.5), (0, 0, 0.5),                      # hacker with hi / lo athleticism
    (25, 75, 0.25), (75, 25, 0.75),                 # mixed
]
rows["propensity"] = [{"D": D, "A": A, "o": o, "want": propensity(D, A, o)}
                      for (D, A, o) in prop_cases]

# per-man aggregate lineups (each a list of five (D,A,o))
AVG = (50, 50, 0.5)
HACK = (0, 50, 0.5)
LOCK = (99, 50, 0.5)
agg_cases = [
    ("five average -> 1.0",        [AVG, AVG, AVG, AVG, AVG]),
    ("one hacker + four average",  [HACK, AVG, AVG, AVG, AVG]),
    ("two hackers + three average",[HACK, HACK, AVG, AVG, AVG]),
    ("one lockdown + four average",[LOCK, AVG, AVG, AVG, AVG]),
    ("five hackers",               [HACK, HACK, HACK, HACK, HACK]),
    ("five lockdowns",             [LOCK, LOCK, LOCK, LOCK, LOCK]),
]
rows["aggregate"] = [{"label": lbl,
                      "defenders": [{"D": D, "A": A, "o": o} for (D, A, o) in lu],
                      "want": per_man_aggregate(lu)}
                     for (lbl, lu) in agg_cases]

# committer weights for a representative lineup (hacker + lockdown + 3 average)
committer_lineup = [HACK, LOCK, AVG, AVG, AVG]
rows["committerReachIn"]     = [{"D": D, "A": A, "o": o, "want": propensity(D, A, o)}
                                for (D, A, o) in committer_lineup]
rows["committerSituational"] = [{"D": D, "want": disc_factor(D)}
                                for (D, A, o) in committer_lineup]

with open("nonshooting_foul_golden.json", "w") as f:
    json.dump(rows, f, indent=2)

# ── Human-readable sign-off echo ─────────────────────────────────────────────
print(f"refProp (five-average propensity) = {REF_PROP:.6f}")
print("\npropensity archetypes:")
for c in rows["propensity"]:
    print(f"  D={c['D']:2} A={c['A']:2} o={c['o']:.2f} -> {c['want']:.6f}")
print("\nper-man aggregate (1.0 = today's rate):")
for c in rows["aggregate"]:
    print(f"  {c['label']:32} -> {c['want']:.6f}")
print("\nSUM property: one hacker RAISES the team aggregate above 1.0 (adds fouls,")
print("does not merely redistribute); one lockdown LOWERS it below 1.0.")
print("\nwrote nonshooting_foul_golden.json")
