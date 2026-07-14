#!/usr/bin/env python3
"""
Steal-forcing FLOOR oracle (live at neutral) — DESIGN sign-off + golden source.
Emits stealFloorShift (the exact quantity C# golden-parity will lock) AND a relative
FORCING-PROPENSITY proxy for the readable table. The proxy is NOT credited steals — it
does not simulate Roll B/F/C or the StealerPicker; credited steals are proven separately
in the integration harness (§E). Magnitudes provisional; SHAPE is the sign-off.
"""
import math

# --- provisional floor knobs (shape locked; numbers calibrated on the season page) ---
ATH_STEEP, ATH_EXP     = 0.85, 1.4
STEAL_STEEP, STEAL_EXP = 0.38, 1.4
WING_WEIGHT, WING_SCALE, WING_REF = 0.45, 22.0, 50.0
GAP_SCALE = 25.0
# --- continuous perimeter gate (matches C#: postness = (H+PD+St)/3) ---
POST_PIVOT, POST_RANGE, PERIM_FLOOR = 50.0, 28.0, 0.20
# --- proxy-only (engine constants, for the readable relative table) ---
TANH_DIV = 1.2
CEIL_MULT, FLOOR_MULT = 1.64, 0.18

def gapfn(gap, steep, exp): return (1 if gap>=0 else -1)*steep*(abs(gap)/GAP_SCALE)**exp
def postness(p): return (p["H"] + p["PD"] + p["St_def"]) / 3.0
def perimW(p):
    postUnit = max(0.0, min(1.0, (postness(p) - POST_PIVOT) / POST_RANGE))
    return 1.0 - (1.0 - PERIM_FLOOR) * postUnit

def steal_floor_shift(d, h):
    ath_gap   = (d["Q"]+d["FS"])/2 - (h["Q"]+h["FS"])/2
    steal_gap = d["Steals"] - h["BH"]
    wing_signed = (d["W"] - WING_REF) * perimW(d)   # TWO-SIDED: short arms cost a little, perimeter-gated
    return (gapfn(ath_gap, ATH_STEEP, ATH_EXP)
          + gapfn(steal_gap, STEAL_STEEP, STEAL_EXP)
          + WING_WEIGHT * math.tanh(wing_signed / WING_SCALE))

def rel_propensity(d, h):
    s = math.tanh(steal_floor_shift(d, h) / TANH_DIV)
    return 1.0 + (CEIL_MULT-1.0)*s if s>=0 else 1.0 - (1.0-FLOOR_MULT)*(-s)

# defenders carry the Postness attributes (H, PD, St_def); guards low, bigs high
def P(kind, Q=50, FS=50, Steals=50, W=50, BH=50, H=None, PD=None, St_def=None):
    if H is None:  # default bodies by kind
        H, PD, St_def = ({"guard":(48,45,48),"wing":(58,55,58),"big":(80,75,80)}[kind])
    return dict(kind=kind, Q=Q, FS=FS, Steals=Steals, W=W, BH=BH, H=H, PD=PD, St_def=St_def)

AVG   = P("guard", BH=50)
ELITE = P("guard", BH=90, Q=70, FS=70)
PLOD  = P("guard", BH=40, Q=35, FS=35)
rows = [
 ("AVERAGE guard vs AVERAGE handler",          P("guard"),                        AVG),
 ("QUICK LONG GUARD vs PLODDING handler",      P("guard",Q=85,FS=85,Steals=80,W=80), PLOD),
 ("QUICK LONG GUARD vs ELITE handler",         P("guard",Q=85,FS=85,Steals=80,W=80), ELITE),
 ("PURE ATHLETE, mediocre steals vs AVERAGE",  P("guard",Q=90,FS=90,Steals=40,W=55), AVG),
 ("HIGH STEALS but SLOW vs AVERAGE",           P("guard",Q=40,FS=40,Steals=90,W=55), AVG),
 ("LONG-ARMED GUARD, avg quicks vs AVERAGE",   P("guard",Q=55,FS=55,Steals=55,W=88), AVG),
 ("ATHLETIC LONG BIG vs AVERAGE handler",      P("big",  Q=72,FS=72,Steals=60,W=88), AVG),
 ("SLOW LONG BIG vs AVERAGE handler",          P("big",  Q=35,FS=35,Steals=50,W=88), AVG),
 ("SLOW SHORT-ARMED GUARD vs AVERAGE",         P("guard",Q=38,FS=38,Steals=45,W=42), AVG),
 ("ISOLATED: avg guard, SHORT arms (W40) vs AVG", P("guard",W=40), AVG),
 ("ISOLATED: avg guard, LONG arms (W88) vs AVG",  P("guard",W=88), AVG),
 ("ISOLATED: SHORT-armed BIG (W40) vs AVG",       P("big",W=40),   AVG),
]
print(f"{'Matchup (defender vs handler)':<44}{'floor shift':>12}{'  propensity':>13}  perimW")
print("-"*84)
for n,d,h in rows:
    print(f"{n:<44}{steal_floor_shift(d,h):>12.4f}{rel_propensity(d,h):>11.2f}x   {perimW(d):.2f}")

# ── Boundary/slide coverage for the golden fixture (proves the gate + clamp order) ──
print()
print("BOUNDARY COVERAGE (perimeter slide + wing-ref zero):")
print(f"{'case':<44}{'postness':>9}{'perimW':>8}{'floor shift':>13}")
def big_body(post):  # solve a body that yields a target postness (H=PD=St_def=post)
    return dict(H=post, PD=post, St_def=post)
def Pb(post, W=88, Q=60, FS=60, Steals=60, BH=50):
    d=dict(kind="x", Q=Q, FS=FS, Steals=Steals, W=W, BH=BH); d.update(big_body(post)); return d
bcases=[
 ("perimW=1  : postness at pivot (50)",        Pb(50)),
 ("perimW mid: postness pivot+range/2 (64)",   Pb(64)),
 ("perimW=floor: postness pivot+range (78)",   Pb(78)),
 ("below pivot clamp: postness 30",            Pb(30)),
 ("above range clamp: postness 99",            Pb(99)),
 ("wing == ref (W50): zero wing term",         Pb(50, W=50)),
]
for n,d in bcases:
    print(f"{n:<44}{postness(d):>9.1f}{perimW(d):>8.2f}{steal_floor_shift(d,AVG):>13.4f}")
