"""
Unforced-turnover handling curve — reference oracle (LOCKED constants).

The neutral-pressure turnover base is flat today: a butterfingers handler and a
sure-handed one turn it over at the same rate until the defense presses. This term
makes the *base* handling-aware by multiplying each door's own flat base share by a
dimensionless factor g(handling), anchored so that:
  * g(50) = 1.0            -> a league-average handler reproduces today's rate exactly;
  * SpanFrac = 0 -> g == 1 -> kill switch reproduces today bit-for-bit at any handling;
  * bad hands raise the base, good hands lower it, on ONE continuous anchored curve;
  * diminishing returns above ~80 (a 99 still turns it over sometimes);
  * the elite "floor" is the curve's own asymptote (~0.72x), NOT the safety clamp.

Same curve for BOTH doors (Roll B team-initiation TO, Roll F individual-action TO);
each door supplies its own flat base, so the effect is proportional and exposure-free.

This file is the authoritative reference the C# port proves golden-parity against.
It is NOT provisional — magnitudes are provisional-locked (tunable later on the season
page), the shape is fixed.

Emits:
  * a human-readable archetype table (both doors, the handling walk) in plain rates;
  * full-precision golden JSON (tools/unforced_turnover_golden.json) for the C# fixture,
    so the fixture is generated, never hand-transcribed from rounded display output;
  * helper-level assertions (anchor, span-0 identity, monotonicity, diminishing returns,
    floor clamp inactive in-range but active out-of-range, exposure-free on a uniform team).
"""
import math, json
from pathlib import Path

# ---- LOCKED curve constants ------------------------------------------------------
MID        = 55.0     # UnforcedMid
SCALE      = 18.0     # UnforcedScale
SPAN_FRAC  = 0.443    # UnforcedSpanFrac -> g(99) ~ 0.7218, the approved near-floor value
FLOOR_FRAC = 0.72     # UnforcedFloorFrac -> SAFETY RAIL below the in-range minimum (0.7218);
                      # the clamp does NOT activate for any authored 0-99 rating.
ANCHOR     = 50.0     # league-average handler; g(ANCHOR) == 1 by construction

# ---- Flat base shares, computed from the LIVE config numerators (not transcribed) ----
# Read from src/Charm.Harness/config.json 2026-07-13.
# actionMass = BaseProceed + BaseFoul + BaseDeadBallTurnover (B); Shot + TO + NSFoul (F).
ROLLB = {"num": 0.03, "action_mass": 0.845 + 0.12 + 0.03}   # 0.995
ROLLF = {"num": 0.09, "action_mass": 0.855 + 0.09 + 0.05}   # 0.995
DOORS = {
    "RollB": ROLLB["num"] / ROLLB["action_mass"],   # 0.030150...
    "RollF": ROLLF["num"] / ROLLF["action_mass"],   # 0.090452...
}

# ---- The curve --------------------------------------------------------------------
def lift(h):
    # falls from ~1 (bad hands) toward ~0 (elite hands); midpoint MID.
    return (1.0 - math.tanh((h - MID) / SCALE)) / 2.0

def raw_g(h, span_frac):
    # anchored form: g(ANCHOR)=1 for any span; g==1 for all h at span 0.
    return 1.0 + span_frac * (lift(h) - lift(ANCHOR))

def g(h, span_frac):
    return max(FLOOR_FRAC, raw_g(h, span_frac))

def final_share(door, h, span_frac):
    return DOORS[door] * g(h, span_frac)

# ---- Fixture rungs ----------------------------------------------------------------
HANDLINGS   = [0, 20, 35, 50, 70, 85, 99]
SPAN_FRACS  = [0.0, SPAN_FRAC]
BOUNDARY_H  = 140     # synthetic out-of-range row to exercise the floor clamp (NOT authored)

def golden_rows():
    rows = []
    for door in ("RollB", "RollF"):
        flat = DOORS[door]
        for sf in SPAN_FRACS:
            for h in HANDLINGS:
                rows.append({
                    "door": door, "flat_base_share": flat, "handling": h,
                    "mid": MID, "scale": SCALE, "spanFrac": sf, "floorFrac": FLOOR_FRAC,
                    "lift_at_h": lift(h), "lift_at_50": lift(ANCHOR),
                    "raw_g": raw_g(h, sf), "clamped_g": g(h, sf),
                    "final_share": final_share(door, h, sf),
                })
        # one synthetic out-of-range boundary row per door: exercises the clamp
        rows.append({
            "door": door, "flat_base_share": flat, "handling": BOUNDARY_H,
            "mid": MID, "scale": SCALE, "spanFrac": SPAN_FRAC, "floorFrac": FLOOR_FRAC,
            "lift_at_h": lift(BOUNDARY_H), "lift_at_50": lift(ANCHOR),
            "raw_g": raw_g(BOUNDARY_H, SPAN_FRAC), "clamped_g": g(BOUNDARY_H, SPAN_FRAC),
            "final_share": final_share(door, BOUNDARY_H, SPAN_FRAC),
            "note": "boundary test — out-of-range rating, clamp active; NOT an authored value",
        })
    return rows

def print_table():
    print(f"{'='*76}")
    print(f"  Unforced-turnover oracle — LOCKED (Mid={MID}, Scale={SCALE}, "
          f"SpanFrac={SPAN_FRAC}, FloorFrac={FLOOR_FRAC})")
    print(f"  g(50)=1 (anchor); good hands lower, bad hands raise; one anchored curve.")
    print(f"{'='*76}")
    print(f"\n  {'handling':>9}{'g(H)':>10}{'RollB share':>14}{'RollF share':>14}")
    for h in [99, 85, 70, 50, 35, 20]:
        gg = g(h, SPAN_FRAC)
        print(f"  {h:>9}{gg:>10.4f}{final_share('RollB',h,SPAN_FRAC):>14.6f}"
              f"{final_share('RollF',h,SPAN_FRAC):>14.6f}")
    print(f"\n  (anchor row H=50: shares == today's flat {DOORS['RollB']:.6f} B / "
          f"{DOORS['RollF']:.6f} F)")

def helper_asserts():
    # anchor: g(50) == 1 for any span
    assert abs(g(50, 0.0) - 1.0) < 1e-15
    assert abs(g(50, SPAN_FRAC) - 1.0) < 1e-15, "anchor must hold at live span"
    # span-0 identity: g == 1 for every handling
    assert all(abs(g(h, 0.0) - 1.0) < 1e-15 for h in range(0, 100)), "span-0 must be identity"
    # strictly decreasing across the walk (bad hands -> more, good hands -> fewer)
    walk = [g(h, SPAN_FRAC) for h in [0, 20, 35, 50, 70, 85, 99]]
    assert all(a > b for a, b in zip(walk, walk[1:])), "curve must be strictly decreasing"
    # elite never below the floor; and in-range the CLAMP never fires (asymptote is the floor)
    assert g(99, SPAN_FRAC) >= FLOOR_FRAC
    min_raw_inrange = min(raw_g(h, SPAN_FRAC) for h in range(0, 100))
    assert min_raw_inrange > FLOOR_FRAC, "floor clamp must NOT activate for any 0-99 rating"
    # diminishing returns above ~80: 85->99 improvement < 50->70 improvement
    assert (g(85, SPAN_FRAC) - g(99, SPAN_FRAC)) < (g(50, SPAN_FRAC) - g(70, SPAN_FRAC))
    # boundary: out-of-range rating drops raw below floor -> clamp engages
    assert raw_g(BOUNDARY_H, SPAN_FRAC) < FLOOR_FRAC
    assert g(BOUNDARY_H, SPAN_FRAC) == FLOOR_FRAC, "clamp must pin out-of-range to the floor"
    # exposure-free on a uniform-handling team: same factor on every door -> team scales by g(H).
    # Two different B/F exposure mixes give the same 99/50 ratio == g(99).
    def team(mixB, mixF, h):   # mix = fraction of exposure through each door
        return mixB * final_share("RollB", h, SPAN_FRAC) + mixF * final_share("RollF", h, SPAN_FRAC)
    def ratio(mixB, mixF):
        return team(mixB, mixF, 99) / team(mixB, mixF, 50)
    assert abs(ratio(0.7, 0.3) - ratio(0.2, 0.8)) < 1e-12, "uniform team must be exposure-free"
    assert abs(ratio(0.7, 0.3) - g(99, SPAN_FRAC)) < 1e-12, "ratio must equal g(99)"
    print("\n  helper asserts: PASS (anchor, span-0 identity, monotone, diminishing returns,")
    print("                        floor inactive in-range / active out-of-range, exposure-free)")

if __name__ == "__main__":
    print_table()
    helper_asserts()
    rows = golden_rows()
    payload = {
        "constants": {"UnforcedMid": MID, "UnforcedScale": SCALE,
                      "UnforcedSpanFrac": SPAN_FRAC, "UnforcedFloorFrac": FLOOR_FRAC},
        "flat_base_shares": {"RollB": DOORS["RollB"], "RollF": DOORS["RollF"]},
        "tolerance": {"final_share": 1e-12},
        "cases": rows,
    }
    out = Path(__file__).with_name("unforced_turnover_golden.json")
    with out.open("w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
    print(f"\n  wrote {out} — {len(rows)} cases (2 doors x [7 handlings x 2 spans + 1 boundary])")
