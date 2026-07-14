#!/usr/bin/env python3
"""
PostMoves interior self-creation — ASSIST DISCOUNT oracle.

Reference implementation of wire (3): a passing-scaled discount on the Rim + Short
assisted rate, reading the shooter's PostMoves. Interior buckets are credited as
assisted less often when a high-PostMoves creator scores from the post — most when
the team can't pass, least beside elite passers.

This oracle is the source of truth for the C# golden-parity test
(Program.Checks.Shooting / assist-discount rows). C# must match every emitted
assistProb within |Δ| <= 1e-12.

Math (locked, matches the build prompt §C wire 3):
    postLift(PM)  = max(0, (PM - 50) / 49)                      # 0 at <=50, 1 at 99
    dampPass(pf)  = DampFloor + (1 - DampFloor) *
                    clamp((PfHi - pf) / (PfHi - PfLo), 0, 1)     # 1 at pf<=PfLo, DampFloor at pf>=PfHi
    postFactor    = 1 - PostAssistSpan * postLift * dampPass(pf) # interior zones, PM>50, span>0
    assistProb    = clamp(base(zone) * pf * postFactor, Floor, Ceiling)

Identity path (PM <= 50 OR span == 0 OR zone not in {Rim,Short}):
    assistProb    = clamp(base(zone) * pf, Floor, Ceiling)       # today's exact two-factor expression
"""

import json

# --- Provisional parameters (shape locked; magnitudes tuned later on the season page) ---
POST_ASSIST_SPAN = 0.50
DAMP_FLOOR       = 0.25
PF_LO            = 0.75
PF_HI            = 1.25

# --- Zone base assisted rates (MatchupConfig defaults, S41) ---
BASE = {"Rim": 0.4811, "Short": 0.3831}

# --- Final-probability clamp (MatchupConfig AssistRateFloor / Ceiling) ---
FLOOR   = 0.25
CEILING = 0.95


def clamp(x, lo, hi):
    return lo if x < lo else hi if x > hi else x


def post_lift(pm):
    return max(0.0, (pm - 50.0) / 49.0)


def damp_pass(pf):
    t = clamp((PF_HI - pf) / (PF_HI - PF_LO), 0.0, 1.0)
    return DAMP_FLOOR + (1.0 - DAMP_FLOOR) * t


def assist_prob(zone, pm, pf, span=POST_ASSIST_SPAN):
    base = BASE[zone]
    interior = zone in ("Rim", "Short")
    lift = post_lift(pm)
    if interior and span > 0.0 and lift > 0.0:
        post_factor = 1.0 - span * lift * damp_pass(pf)
        return clamp(base * pf * post_factor, FLOOR, CEILING)
    # identity path — today's exact two-factor expression
    return clamp(base * pf, FLOOR, CEILING)


def main():
    pms  = [30, 50, 60, 85, 99]
    pfs  = [0.80, 1.00, 1.20]
    zones = ["Rim", "Short"]

    # Clean 30-row grid (2 zones x 5 PostMoves x 3 passing factors) at the DEFAULT span.
    # Naturally covers: PM<=50 identity (30/50), the active discount (60/85/99), and the
    # two accepted floor-saturation corners (Short PM 85 & 99 at pf 0.80 both clamp to 0.25).
    # The span=0 kill switch is an exact-bit identity test in the harness, not a golden row.
    rows = []
    for zone in zones:
        for pm in pms:
            for pf in pfs:
                rows.append({
                    "zone": zone,
                    "pm": pm,
                    "pf": pf,
                    "assistProb": assist_prob(zone, pm, pf),
                })

    out = {
        "params": {
            "PostAssistSpan": POST_ASSIST_SPAN,
            "DampFloor": DAMP_FLOOR,
            "PfLo": PF_LO,
            "PfHi": PF_HI,
            "BaseRim": BASE["Rim"],
            "BaseShort": BASE["Short"],
            "Floor": FLOOR,
            "Ceiling": CEILING,
        },
        "rows": rows,
    }
    with open("tools/post_assist_golden.json", "w") as f:
        json.dump(out, f, indent=2)

    # Human-readable archetype table (percentage points) for sign-off.
    print("Assist rate (%) — baseline base*pf vs adjusted, by PostMoves x passing factor\n")
    for zone in zones:
        print(f"{zone} (base {BASE[zone]}):")
        print(f"{'PM':>4} | {'weak pf0.80':>18} | {'avg pf1.00':>18} | {'elite pf1.20':>18}")
        for pm in pms:
            cells = []
            for pf in pfs:
                baseline = clamp(BASE[zone] * pf, FLOOR, CEILING) * 100
                adj      = assist_prob(zone, pm, pf) * 100
                cells.append(f"{baseline:5.1f} -> {adj:5.1f}")
            print(f"{pm:>4} | {cells[0]:>18} | {cells[1]:>18} | {cells[2]:>18}")
        print()


if __name__ == "__main__":
    main()
