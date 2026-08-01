#!/usr/bin/env python3
"""
S88 — emit tools/transition_defense_golden.json from the LOCKED oracle.

This script imports transition_defense_oracle.py and does not redefine a single
formula, so the fixture cannot drift from the spec Emmett signed off. The oracle
file itself is locked and is never edited by this.

What it writes, per case, is the RAW NUMERIC inputs the C# primitives take —
not player dictionaries — so golden parity binds to TransitionDefense's own
entry points rather than to a transcription of them.

    python3 tools/transition_defense_golden_emit.py > tools/transition_defense_golden.json
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import transition_defense_oracle as o   # noqa: E402

ZONE_NAME = {None: None, "rim": "Rim", "short": "Short",
             "mid": "Mid", "long": "Long", "three": "Three"}


def main():
    A = o.AVG_OFFENCE
    opp_mean = sum(m["post"] for m in A) / 5
    mid = A[2]                      # the opponent used by the shooter/make/block families
    cases = []

    # ── got-back vs a varying opponent post-ness (R2, the depth axis) ──────
    for s in [20, 35, 50, 65, 80, 95]:
        for op in [25, 45, 65, 85]:
            d = o._p(s, 50, 50, 50)
            cases.append(dict(
                kind="gotback", speed=s, hustle=d["hustle"],
                oppPostness=float(op), oppMean=opp_mean, zone=None,
                expected=o.got_back(d, o._p(50, op, 50, 50), opp_mean)))

    # ── got-back with the shooter-zone multiplier on his seat (R7) ─────────
    for s in [20, 35, 50, 65, 80, 95]:
        for z in [None, "rim", "short", "mid", "long", "three"]:
            d = o._p(s, 50, 50, 50)
            cases.append(dict(
                kind="gotback", speed=s, hustle=d["hustle"],
                oppPostness=float(mid["post"]), oppMean=opp_mean, zone=ZONE_NAME[z],
                expected=o.got_back(d, mid, opp_mean, z)))

    # ── the break make rate (R4 + R5/job 3) ───────────────────────────────
    for s in [20, 35, 50, 65, 80, 95]:
        for rp in [25, 50, 75, 95]:
            d = o._p(s, 50, rp, 50)
            g = o.got_back(d, mid, opp_mean)
            for agg in [0.85, 1.00, 1.25]:
                cases.append(dict(
                    kind="make", rimprot=float(rp), gotBack=g, aggregate=agg,
                    expected=o.break_make_pct(d, g, agg)))

    # ── the chase-down block rate (R3) ────────────────────────────────────
    for s in [20, 35, 50, 65, 80, 95]:
        for lg in [30, 50, 70, 90]:
            for rp in [25, 50, 75, 95]:
                d = o._p(s, 50, rp, lg)
                g = o.got_back(d, mid, opp_mean)
                cases.append(dict(
                    kind="block", rimprot=float(rp), length=float(lg), gotBack=g,
                    expected=o.break_block_pct(d, g)))

    doc = {
        "schema": "s88-1",
        "source": "tools/transition_defense_oracle.py (LOCKED)",
        "float_tolerance": 1e-9,
        "note": ("Absolute 1e-9, deliberately NOT bitwise: a bit-exact cross-platform "
                 "fixture is not portable (Math.Pow / tanh differ by 1-3 ULPs between "
                 "Windows and Linux libm) and shipping one produced a red suite at S81.3 "
                 "with nothing wrong in the engine."),
        "constants": {
            "TransitionGotBackLuckFloor":   o.LUCK_FLOOR,
            "TransitionLegsSpan":           o.LEGS_SPAN,
            "TransitionDepthSpan":          o.DEPTH_SPAN,
            "TransitionEffortSpeedShare":   o.EFFORT_MIX,
            "TransitionPostnessScale":      o.POSTNESS_SCALE,
            "TransitionArrivalSpan":        o.ARRIVAL_SPAN,
            "TransitionContestDiscount":    o.TRANSITION_DISCOUNT,
            "TransitionBaseBreakMake":      o.BASE_BREAK_FG,
            "TransitionBaseBreakBlock":     o.BASE_BREAK_BLK,
            "TransitionRimProtectionSwing": o.RIMPROT_SWING,
            "TransitionTeamPresenceSwing":  o.TEAM_PRESENCE,
            "TransitionChaseSwing":         o.CHASE_SWING,
            "TransitionChaseLengthWeight":  o.CHASE_LENGTH_W,
            "TransitionChaseRimProtWeight": o.CHASE_RIMPROT_W,
            "TransitionChaseSpeedSwing":    o.CHASE_SPEED,
            "TransitionShooterZoneRim":     o.ZONE_SHOOTER["rim"],
            "TransitionShooterZoneShort":   o.ZONE_SHOOTER["short"],
            "TransitionShooterZoneMid":     o.ZONE_SHOOTER["mid"],
            "TransitionShooterZoneLong":    o.ZONE_SHOOTER["long"],
            "TransitionShooterZoneThree":   o.ZONE_SHOOTER["three"],
        },
        # The A6b denominator fixture, computed from the oracle rather than transcribed
        # from prose. Three defenders and three opponents in offensive slots 3/4/5, using
        # the oracle's own average archetypes. The occupied-count aggregate and the
        # would-be fixed-five aggregate are BOTH emitted: the test asserts the first and
        # asserts that the port does NOT produce the second.
        "denominator_fixture": {},
        "cases": cases,
    }

    # slots 3,4,5 of the average offence, on both sides
    trio = [A[2], A[3], A[4]]
    trio_mean = sum(m["post"] for m in trio) / len(trio)
    w = [o.got_back(trio[i], trio[i], trio_mean) for i in range(3)]
    doc["denominator_fixture"] = {
        "description": "offensive slots 3/4/5 of AVG_OFFENCE on both sides; oppMean over the three occupied seats",
        "slots": [3, 4, 5],
        "players": [dict(speed=m["speed"], hustle=m["hustle"], post=m["post"]) for m in trio],
        "oppMean": trio_mean,
        "weights": w,
        "aggregate_occupied_count": sum(w) / (len(w) * o.REFERENCE_GOTBACK),
        "aggregate_fixed_five": sum(w) / (5 * o.REFERENCE_GOTBACK),
    }

    json.dump(doc, sys.stdout, indent=1)
    sys.stdout.write("\n")


if __name__ == "__main__":
    main()
