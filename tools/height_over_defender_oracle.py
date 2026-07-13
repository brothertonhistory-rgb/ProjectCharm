"""
Height-over-defender make term — reference oracle (LOCKED / signed constants).

Signed off on the archetype table (Session 54 design conversation; make%% column regenerated
against the LIVE recentered curve at Session 55 — rating shifts unchanged, deltas within ~0.4pp): one-sided, zone-weighted,
saturating reach advantage added to Matchup.EffectiveRating. This file is the authoritative
reference the C# port proves golden-parity against — it is NOT provisional.

Emits:
  * a human-readable archetype table (5 archetypes x 5 zones, incl. Long);
  * full-precision golden JSON (tools/oracle/height_over_defender_golden.json) for the C# fixture,
    so the fixture is generated, never hand-transcribed from rounded display output;
  * helper-level assertions (reach incl. an odd-sum case, one-sided clamp, zone ordering, saturation).
"""
import math, json
from pathlib import Path

# ---- Current make-chain constants (read from live source, unchanged by this term) ----
SKILL_STEEP, SKILL_EXP = 6.0, 2.0
PHYS_STEEP, PHYS_EXP   = 11.5, 1.75
REF_SCALE = 25.0

# Per-zone logistic make-curve — the LIVE curve, read from src/Charm.Harness/config.json
# ("RollH" section) at Session 55, NOT the RollHConfig.cs code defaults. The live midpoints
# carry the recenter arc (S31+); the code defaults are the stale pre-recenter values and the
# original v1 oracle was wrongly built against them (caught at S55 build, Emmett-ruled fix:
# regenerate against live). Floors/ceilings/K are identical in both. (floor, ceil, K, midpoint)
CURVE = {
    'Rim':   (0.3582, 0.9527, 0.024666,  68.7559),
    'Short': (0.1316, 0.7045, 0.021592,  75.9276),
    'Mid':   (0.1042, 0.6447, 0.021592,  90.3196),
    'Long':  (0.1934, 0.6034, 0.034190,  99.3440),
    'Three': (0.1608, 0.6328, 0.029646, 106.5661),
}
ZONES = ['Rim', 'Short', 'Mid', 'Long', 'Three']

def gapfn(gap, steep, exp, scale):
    return math.copysign(1, gap) * steep * (abs(gap)/scale)**exp if gap != 0 else 0.0

def make_prob(zone, rating):
    f,c,k,m = CURVE[zone]
    return f + (c-f)/(1.0 + math.exp(-k*(rating-m)))

# ---- The height-over-defender term (LOCKED constants) ----
HEIGHT_MAX_BONUS   = 15.0     # rating points at full saturation & zone weight 1.0
HEIGHT_REF_SCALE   = 18.0     # length-points; tanh saturation speed
HEIGHT_ZONE_WEIGHT = {'Rim':1.0, 'Short':0.8, 'Mid':0.3, 'Long':0.05, 'Three':0.0}

def reach(H, W):
    return (H + W) / 2.0                 # float divide — 85+88 -> 86.5, never 86

def height_shift(zone, shooter_reach, defender_reach):
    gap = max(0.0, shooter_reach - defender_reach)   # ONE-SIDED (v1)
    return HEIGHT_ZONE_WEIGHT[zone] * HEIGHT_MAX_BONUS * math.tanh(gap / HEIGHT_REF_SCALE)

def eff_rating(zone, zone_skill, defense_rating, sh_ath, df_ath, sh_reach, df_reach, height_on):
    baseline   = zone_skill
    skillShift = gapfn(baseline - defense_rating, SKILL_STEEP, SKILL_EXP, REF_SCALE)
    physShift  = gapfn(sh_ath - df_ath, PHYS_STEEP, PHYS_EXP, REF_SCALE)
    hShift     = height_shift(zone, sh_reach, df_reach) if height_on else 0.0
    return baseline + skillShift + physShift + hShift

# ---- Archetypes: (name, shooter(H,W,ath), defender(H,W,ath)) ----
# zone skill & defense rating held at 50 to isolate length; athleticism realistic.
# TALLER_CENTER is the deliberate ODD-SUM reach case: (85,88) -> 86.5.
ARCH = [
    ("BIG_ON_GUARD",   (90,94,45), (38,42,70)),
    ("TALLER_CENTER",  (85,88,50), (74,76,50)),   # odd-sum reach 86.5
    ("TALL_WING",      (62,66,60), (46,48,60)),
    ("POST_VS_POST",   (88,90,50), (88,90,50)),   # equal reach -> exact zero
    ("SMALL_ON_BIG",   (40,42,70), (90,94,45)),   # negative gap -> exact zero (one-sided)
]

def golden_rows():
    rows = []
    for name,(sH,sW,sA),(dH,dW,dA) in ARCH:
        sr, dr = reach(sH,sW), reach(dH,dW)
        for zone in ZONES:
            e0 = eff_rating(zone,50,50,sA,dA,sr,dr,height_on=False)
            e1 = eff_rating(zone,50,50,sA,dA,sr,dr,height_on=True)
            rows.append({
                "archetype": name, "zone": zone,
                "shooter_height": sH, "shooter_wingspan": sW, "shooter_ath": sA,
                "defender_height": dH, "defender_wingspan": dW, "defender_ath": dA,
                "shooter_reach": sr, "defender_reach": dr,
                "effective_rating": e1,
                "make_probability": make_prob(zone, e1),
                "make_delta_vs_no_term": make_prob(zone,e1) - make_prob(zone,e0),
            })
    return rows

def print_table():
    print(f"{'='*84}\n  Height-over-defender oracle — LOCKED (MaxBonus={HEIGHT_MAX_BONUS}, RefScale={HEIGHT_REF_SCALE},")
    print(f"  reach=(H+W)/2, zoneW rim/short/mid/long/three = 1.0/0.8/0.3/0.05/0.0, one-sided)\n{'='*84}")
    for name,(sH,sW,sA),(dH,dW,dA) in ARCH:
        sr, dr = reach(sH,sW), reach(dH,dW)
        print(f"\n  {name}: shooter reach {sr}  vs defender reach {dr}  (gap {sr-dr:+})")
        print(f"    {'zone':6}{'make% now':>11}{'make% new':>11}{'delta':>8}")
        for zone in ZONES:
            e0 = eff_rating(zone,50,50,sA,dA,sr,dr,height_on=False)
            e1 = eff_rating(zone,50,50,sA,dA,sr,dr,height_on=True)
            m0, m1 = make_prob(zone,e0)*100, make_prob(zone,e1)*100
            print(f"    {zone:6}{m0:10.1f}%{m1:10.1f}%{m1-m0:+7.1f}")

def helper_asserts():
    # reach: even-sum and ODD-SUM
    assert reach(90,94) == 92.0
    assert reach(38,42) == 40.0
    assert reach(85,88) == 86.5, "odd-sum reach must be 86.5, not 86 (integer-divide bug)"
    # one-sided clamp
    assert height_shift('Rim', 60, 40) > 0          # positive gap -> positive
    assert height_shift('Rim', 50, 50) == 0.0       # zero gap -> exactly zero
    assert height_shift('Rim', 40, 60) == 0.0       # negative gap -> exactly zero
    # zone ordering for the same positive gap: Rim > Short > Mid > Long > Three(==0)
    sh = {z: height_shift(z, 90, 40) for z in ZONES}
    assert sh['Rim'] > sh['Short'] > sh['Mid'] > sh['Long'] > 0
    assert sh['Three'] == 0.0
    # saturation: monotone increasing, stays strictly below the zone cap, and approaches it
    cap = HEIGHT_MAX_BONUS * HEIGHT_ZONE_WEIGHT['Rim']
    s1, s2, s3 = height_shift('Rim',60,40), height_shift('Rim',90,40), height_shift('Rim',199,0)
    assert 0 < s1 < s2 < s3 < cap
    assert s3 > cap * 0.99
    print("\n  helper asserts: PASS (reach incl. odd-sum, one-sided clamp, zone order, saturation)")

if __name__ == "__main__":
    print_table()
    helper_asserts()
    rows = golden_rows()
    payload = {"constants": {"HeightMaxBonus": HEIGHT_MAX_BONUS,
                             "HeightReferenceScale": HEIGHT_REF_SCALE,
                             "HeightZoneWeight": HEIGHT_ZONE_WEIGHT},
               "tolerance": {"effective_rating": 1e-6, "make_probability": 1e-9},
               "cases": rows}
    output_path = Path(__file__).with_name("height_over_defender_golden.json")
    with output_path.open("w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
    print(f"\n  wrote {output_path} — {len(rows)} cases (5 archetypes x 5 zones), full precision")
