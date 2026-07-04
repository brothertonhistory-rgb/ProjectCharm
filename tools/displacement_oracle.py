#!/usr/bin/env python3
"""
Roll G matchup displacement — LOCKED SPEC ORACLE v1 (2026-07-04).

Status: LOCKED on commit. Every design ruling is Emmett's (recorded in
docs/rollg-displacement-brief.md §3/§3a/§4/§5, plus the Route B and
physical-term decisions in §6.1/§6.3). Reviewed per CONVENTIONS §6c across
three rounds: round 3 folded the level-matched 5a/5b fixture and the
heterogeneous aggregation pair, and rejected a false physical-constants claim
against a fresh pull; round 4 (green) added `base` and `ladder` to the golden
trace for genuinely stage-wise parity. The C# port mirrors this file
constant-for-constant and stage-for-stage; if the C# and this oracle ever
disagree, the oracle wins. Future tuning happens HERE first (new approval),
never in the C# alone.

MAGNITUDE CONSTANTS ARE CALIBRATION PLACEHOLDERS (DISP_PHYS_STEEP, DISP_REF,
DISP_MAX, DISP_USAGE_SCALE, the ladder weights, the inside-skill gate anchors):
tuned later from season pages per the standing calibration-deferred and
page-only principles — never suite-asserted. The STRUCTURE (Route B
decomposition, the asymmetric gated ladder, the aggregation reads, the
composition order) is what this lock fixes.

THE DESIGN (the brief is the record; this header is the executable summary):
  The engine simulates the AGGREGATE of a game's shots, not individual good/bad
  looks. A defending lineup superior to a featured shooter forces his diet
  OUTWARD (away from the rim) unconditionally; an inferior lineup INVITES him
  inward, and he accepts only per his own inside skills (the force/invite
  asymmetry, §3a R2). Everything is RELATIVE to the player himself (§3a R1).
  Usage is the volume knob: zero at/below an equal share, smooth above (§4).
  Efficiency needs nothing new — the make curve prices it (§3).

THE DECOMPOSITION (Route B, ruled 2026-07-04):
  zone gap[z]   = shooter zone skill − lineup zone resistance      (existing read)
  overall level = Σ desiredDiet[z]·gap[z]  +  physical term        (diet-weighted, §6.1)
  residual[z]   = gap[z] − skillLevel                              (level stripped out)
  Phase 9 bend  = existing multiplier form ON RESIDUALS (shape only — a uniform
                  defensive upgrade moves it by exactly zero, proven in checks)
  displacement  = bounded tanh(level) × usage gate × asymmetric gated ladder,
                  multiplicative on the player's own PRE-BEND baseline (§6.2)
  compose       = baseline + Δ(phase9) + Δ(displacement), clamp ≥0, renorm once;
                  usage widening (Phase 17, attention-amplified) stays LAST and
                  is NOT modeled here — it applies downstream unchanged (§6.5).

THE PHYSICAL TERM (§6.3, settled): shooter Athleticism composite vs the
  defending lineup's MEAN Athleticism, through the same GapFn at a fraction of
  the make-door steepness (3.0 vs 11.5) — gentle by design; the make curve owns
  the harsh physical punishment. NOTE: the physical term feeds the LEVEL only;
  residuals are computed against the SKILL level alone, so Phase 9's zone-shape
  read stays purely the skill read it is today.

FASTBREAK / ZERO-DEFENDER (§6.7): displacement never applies on FastBreak
  (Roll G returns the fast-break pie before any of this) and never on the
  zero-defender fallback (no manufactured neutral defense; pure tendency +
  existing widening only). Asserted at build time, not modeled here.

Zone order everywhere: Rim, Short, Mid, Long, Three.
"""
import json, math, random, statistics

Z = ["Rim", "Short", "Mid", "Long", "Three"]

# ---------------------------------------------------------------------------
# ENGINE CONSTANTS — mirrored exactly from MatchupConfig defaults (read from
# source 2026-07-04). The C# build READS these from config; the oracle pins the
# same values so its numbers are the engine's numbers.
# ---------------------------------------------------------------------------
SKILL_STEEP, SKILL_EXP, REF_SCALE = 6.0, 2.0, 25.0
PHYS_EXP                          = 1.75            # make-door exponent, reused
LOC_MAX_MULT, LOC_REF_SHIFT       = 2.5, 20.0
LOC_BLEND                         = (0.55, 0.30, 0.15)   # top-three defender weights
DEF_BLEND = {   # per-zone (perimeter, post, rim) defensive-skill blend
    "Rim":   (0.00, 0.35, 0.65),
    "Short": (0.15, 0.85, 0.00),
    "Mid":   (0.50, 0.50, 0.00),
    "Long":  (0.85, 0.15, 0.00),
    "Three": (1.00, 0.00, 0.00),
}
OFF_KEY = {"Rim": "Finishing", "Short": "Close", "Mid": "Mid",
           "Long": "Outside", "Three": "Outside"}

# ---------------------------------------------------------------------------
# DISPLACEMENT CONSTANTS — all [CALIBRATION PLACEHOLDER], tuned from season
# pages after the build; none ever suite-asserted.
# ---------------------------------------------------------------------------
DISP_PHYS_STEEP = 3.0     # §6.3: fraction of the make door's 11.5 — gentle by design
DISP_REF        = 20.0    # rating-points of level reaching ~76% of max magnitude (tanh)
DISP_MAX        = 0.35    # cap on |mag|: max per-zone ladder scaling at full mismatch+usage
DISP_USAGE_SCALE = 3.0    # converts UsagePressure (observed ~0..0.17) to a 0..1 gate
LADDER = {"Rim": +2.0, "Short": +1.0, "Mid": 0.0, "Long": -1.0, "Three": -2.0}
GATE_RIM   = (38.0, 72.0)  # §3a R2: inside-skill gates on the INWARD pull only
GATE_SHORT = (36.0, 68.0)

# ---------------------------------------------------------------------------
# PRIMITIVES — mirror Matchup.cs exactly
# ---------------------------------------------------------------------------
def clamp(x, lo, hi): return lo if x < lo else hi if x > hi else x
def gate(x, lo, hi):  return clamp((x - lo) / (hi - lo), 0.0, 1.0)

def gapfn(g, steep=SKILL_STEEP, exp=SKILL_EXP):
    s = 1.0 if g > 0 else -1.0 if g < 0 else 0.0
    return s * steep * (abs(g) / REF_SCALE) ** exp

def def_rating(zone, d):
    pw, po, ri = DEF_BLEND[zone]
    return pw * d["PerimeterDefense"] + po * d["PostDefense"] + ri * d["RimProtection"]

def def_resistance(zone, defenders):
    scores = sorted((def_rating(zone, d) for d in defenders), reverse=True)[:3]
    w = LOC_BLEND[:len(scores)]; ws = sum(w)
    return sum((w[i] / ws) * scores[i] for i in range(len(scores)))

def athleticism(p):
    return (p["Strength"] + p["Speed"] + p["Quickness"] + p["FirstStep"] + p["Vertical"]) / 5.0

def loc_mult_from_gap(g):
    """The existing bounded multiplier form — unchanged; only its INPUT changes
    under Route B (residualized gap instead of raw)."""
    return math.exp(math.log(LOC_MAX_MULT) * math.tanh(gapfn(g) / LOC_REF_SHIFT))

# ---------------------------------------------------------------------------
# THE DERIVATION
# ---------------------------------------------------------------------------
def zone_gaps(shooter, defenders):
    return {z: shooter[OFF_KEY[z]] - def_resistance(z, defenders) for z in Z}

def skill_level(base_diet, gaps):
    """§6.1: the diet-weighted skill level — weights are the PRE-BEND baseline (§6.2)."""
    return sum(base_diet[z] * gaps[z] for z in Z)

def physical_level(shooter, defenders):
    """§6.3: gentle athleticism term vs the LINEUP MEAN, in rating-points."""
    lineup_mean = sum(athleticism(d) for d in defenders) / len(defenders)
    return gapfn(athleticism(shooter) - lineup_mean, DISP_PHYS_STEEP, PHYS_EXP)

def ladder_for(shooter, mag):
    """§3a R2: outward push unconditional; inward pull gated by inside skill."""
    if mag <= 0.0:
        return dict(LADDER)
    L = dict(LADDER)
    L["Rim"]   = +2.0 * gate(shooter["Finishing"], *GATE_RIM)
    L["Short"] = +1.0 * gate(shooter["Close"],     *GATE_SHORT)
    return L

def derive(diet_raw, shooter, defenders, usage_pressure):
    """Full pipeline. diet_raw: the coached pre-bend baseline (any positive scale).
    Returns (trace dict) with every stage, for checks and the golden fixture."""
    tot = sum(diet_raw.values())
    base = {z: diet_raw[z] / tot for z in Z}

    gaps  = zone_gaps(shooter, defenders)
    sLvl  = skill_level(base, gaps)
    pLvl  = physical_level(shooter, defenders)
    level = sLvl + pLvl

    # Phase 9 on RESIDUALS (Route B): residual vs the SKILL level only.
    resid = {z: gaps[z] - sLvl for z in Z}
    bent  = {z: base[z] * loc_mult_from_gap(resid[z]) for z in Z}
    sb = sum(bent.values()); bent = {z: bent[z] / sb for z in Z}
    d9 = {z: bent[z] - base[z] for z in Z}

    # Displacement: bounded, usage-gated, asymmetric gated ladder, on the baseline.
    mag = DISP_MAX * math.tanh(level / DISP_REF) * min(1.0, DISP_USAGE_SCALE * usage_pressure)
    L = ladder_for(shooter, mag)
    disp = {z: max(0.0, base[z] * (1.0 + mag * L[z])) for z in Z}
    sd = sum(disp.values()); disp = {z: disp[z] / sd for z in Z}
    dd = {z: disp[z] - base[z] for z in Z}

    # Compose deltas from the SAME baseline, clamp, renormalize ONCE (§6.5).
    out = {z: max(0.0, base[z] + d9[z] + dd[z]) for z in Z}
    so = sum(out.values()); out = {z: out[z] / so for z in Z}

    return {"base": base, "gaps": gaps, "skillLevel": sLvl, "physLevel": pLvl,
            "level": level, "residuals": resid, "bentShapeOnly": bent,
            "mag": mag, "ladder": L, "final": out}

# ---------------------------------------------------------------------------
# ARCHETYPES — the §6.6 proof set, RELATIVELY LABELED (§3a R1): every defense
# is specified relative to the archetype's own level, stated in the name.
# ---------------------------------------------------------------------------
def P(fin, clo, mid, out, ath):
    return {"Finishing": fin, "Close": clo, "Mid": mid, "Outside": out,
            "Strength": ath, "Speed": ath, "Quickness": ath, "FirstStep": ath, "Vertical": ath}
def defender(pe, po, ri, ath):
    return {"PerimeterDefense": pe, "PostDefense": po, "RimProtection": ri,
            "Strength": ath, "Speed": ath, "Quickness": ath, "FirstStep": ath, "Vertical": ath}
def D(pe, po, ri, ath=None):
    a = ath if ath is not None else (pe + po + ri) / 3.0
    return [defender(pe, po, ri, a)] * 5

def solve_level_matched_uneven(shooter, base_diet, pe, ri, target_lineup, ath):
    """Point-2 fixture builder: given fixed perimeter/rim skills, solve PostDefense so
    the shooter's diet-weighted SKILL level against the uneven lineup EXACTLY equals
    his level against the uniform `target_lineup` rating. Exact by construction —
    the level-matched shape comparison depends on it."""
    tot = sum(base_diet.values())
    w = {z: base_diet[z] / tot for z in Z}
    # diet-weighted resistance = Σ w_z · (blend_pe·pe + blend_po·po + blend_ri·ri)
    # uniform lineup resistance in every zone == the uniform rating.
    kp = sum(w[z] * DEF_BLEND[z][1] for z in Z)                       # coefficient of po
    fixed = sum(w[z] * (DEF_BLEND[z][0] * pe + DEF_BLEND[z][2] * ri) for z in Z)
    po = (target_lineup - fixed) / kp
    return [defender(pe, po, ri, ath)] * 5

SHOOTER = P(48, 48, 55, 72, 50)   # avg shooter, middling drive
SLASHER = P(80, 60, 42, 40, 72)   # rim slasher, no jumper
STAR    = P(78, 66, 80, 78, 70)   # multi-level
SPOTUP  = P(46, 44, 52, 74, 46)   # spot-up specialist, cannot finish

DIET_SHOOTER = {"Rim": 18, "Short": 10, "Mid": 18, "Long": 9, "Three": 45}
DIET_SLASHER = {"Rim": 55, "Short": 18, "Mid": 12, "Long": 5, "Three": 10}
DIET_STAR    = {"Rim": 30, "Short": 15, "Mid": 22, "Long": 11, "Three": 22}
DIET_SPOTUP  = {"Rim": 8,  "Short": 6,  "Mid": 12, "Long": 8,  "Three": 66}

UNIFORM_STRONG   = D(70, 70, 70, ath=70)
UNEVEN_LVLMATCH  = solve_level_matched_uneven(SHOOTER, DIET_SHOOTER, pe=65, ri=82,
                                              target_lineup=70, ath=70)
# Point-3 heterogeneous lineup: three strong-skill defenders (always the top three
# in every zone) + two weak-skill defenders whose ATHLETICISM alone varies between
# the paired fixtures. Zone resistance must be bit-identical across the pair;
# only the five-man mean athleticism (the physical term) may differ.
HETERO_A = [defender(74, 70, 78, 68), defender(66, 72, 70, 62), defender(70, 64, 72, 71),
            defender(30, 28, 26, 40), defender(28, 32, 30, 45)]
HETERO_B = [defender(74, 70, 78, 68), defender(66, 72, 70, 62), defender(70, 64, 72, 71),
            defender(30, 28, 26, 85), defender(28, 32, 30, 88)]

CASES = {
 # name: (diet, shooter, defenders, usagePressure) — matches derive()
 "1 shooter vs uniform-strong (above HIS level)":      (DIET_SHOOTER, SHOOTER, D(70,70,70), 0.15),
 "2 slasher vs ELITE wall (rim res above HIS 80)":     (DIET_SLASHER, SLASHER, D(78,82,92), 0.15),
 "3 star vs weak (well below HIS level)":              (DIET_STAR,    STAR,    D(30,30,30), 0.15),
 "4 spot-up in blowout, LOW usage":                    (DIET_SPOTUP,  SPOTUP,  D(30,30,30), 0.00),
 "5a shooter vs UNIFORM strong, ath pinned 70":        (DIET_SHOOTER, SHOOTER, UNIFORM_STRONG, 0.15),
 "5b shooter vs UNEVEN rim-first, LEVEL-MATCHED to 5a":(DIET_SHOOTER, SHOOTER, UNEVEN_LVLMATCH, 0.15),
 "6 spot-up specialist HIGH usage, avg defense":       (DIET_SPOTUP,  SPOTUP,  D(52,52,52), 0.17),
 "7 star vs defense AT HIS level (equal-to-him)":      (DIET_STAR,    STAR,    D(76,76,76), 0.17),
 "8 star vs HETEROGENEOUS lineup (bench ath low)":     (DIET_STAR,    STAR,    HETERO_A, 0.15),
 "8b same top-three, bench ath HIGH (physical only)":  (DIET_STAR,    STAR,    HETERO_B, 0.15),
}

# ---------------------------------------------------------------------------
# STRUCTURAL CHECKS — the gate. No magnitude asserted anywhere; only shape,
# direction, invariance, and validity.
# ---------------------------------------------------------------------------
def checks():
    res = []
    def chk(name, ok): res.append((name, ok))
    t = {k: derive(*v) for k, v in CASES.items()}

    # Route B invariance: uniform defensive shift -> shape-only Phase 9 unmoved.
    a = derive(DIET_SHOOTER, SHOOTER, D(40, 40, 40), 0.15)["bentShapeOnly"]
    b = derive(DIET_SHOOTER, SHOOTER, D(65, 65, 65), 0.15)["bentShapeOnly"]
    chk("Route B: uniform +25 defense shift moves shape-bend by exactly ~0",
        max(abs(a[z] - b[z]) for z in Z) < 1e-12)

    c1 = t["1 shooter vs uniform-strong (above HIS level)"]
    chk("1 overmatched shooter: level negative", c1["level"] < 0)
    chk("1 overmatched shooter: threes rise, rim falls (compress to core)",
        c1["final"]["Three"] > c1["base"]["Three"] and c1["final"]["Rim"] < c1["base"]["Rim"])

    c2 = t["2 slasher vs ELITE wall (rim res above HIS 80)"]
    chk("2 walled slasher: level negative", c2["level"] < 0)
    chk("2 walled slasher: STAYS rim-first (anchor holds)",
        c2["final"]["Rim"] == max(c2["final"].values()))
    chk("2 walled slasher: displacement leaks him outward vs shape-bend alone",
        c2["final"]["Three"] + c2["final"]["Long"] + c2["final"]["Mid"]
        > c2["bentShapeOnly"]["Three"] + c2["bentShapeOnly"]["Long"] + c2["bentShapeOnly"]["Mid"])

    c3 = t["3 star vs weak (well below HIS level)"]
    chk("3 advantaged star: level strongly positive", c3["level"] > 20)
    chk("3 advantaged star: pulled rim-ward (rim share rises)",
        c3["final"]["Rim"] > c3["base"]["Rim"])
    chk("3 advantaged star: full rim invitation (gate ~1)", c3["ladder"]["Rim"] > 1.9)

    c4 = t["4 spot-up in blowout, LOW usage"]
    chk("4 low-usage blowout spot-up: displacement contributes ZERO (mag==0)",
        abs(c4["mag"]) < 1e-12)
    chk("4 low-usage blowout spot-up: existing shape-bend feeds his arc niche",
        c4["final"]["Three"] > c4["base"]["Three"])

    c5a = t["5a shooter vs UNIFORM strong, ath pinned 70"]
    c5b = t["5b shooter vs UNEVEN rim-first, LEVEL-MATCHED to 5a"]
    chk("5 LEVEL-MATCHED: skill levels equal by construction (|diff| < 1e-9)",
        abs(c5a["skillLevel"] - c5b["skillLevel"]) < 1e-9)
    chk("5 LEVEL-MATCHED: physical terms equal (ath pinned)",
        abs(c5a["physLevel"] - c5b["physLevel"]) < 1e-9)
    chk("5 LEVEL-MATCHED: displacement magnitudes equal (level is the sole driver)",
        abs(c5a["mag"] - c5b["mag"]) < 1e-9)
    r5a, r5b = c5a["residuals"], c5b["residuals"]
    chk("5 LEVEL-MATCHED: residual spread materially larger under the UNEVEN defense",
        (max(r5b.values()) - min(r5b.values())) > (max(r5a.values()) - min(r5a.values())) + 5.0)
    chk("5 LEVEL-MATCHED: shape bend materially different (same level, different shape)",
        max(abs(c5a["bentShapeOnly"][z] - c5b["bentShapeOnly"][z]) for z in Z) > 0.02)

    c6 = t["6 spot-up specialist HIGH usage, avg defense"]
    chk("6 advantaged non-finisher: rim invitation mostly declined (gate < 0.5)",
        c6["ladder"]["Rim"] < 1.0)
    chk("6 advantaged non-finisher: three stays overwhelmingly dominant (>= 60%)",
        c6["final"]["Three"] >= 0.60)
    chk("6 advantaged non-finisher: small spread — no zone gains more than 2pp",
        max(c6["final"][z] - c6["base"][z] for z in Z if z != "Three") < 0.02)

    c7 = t["7 star vs defense AT HIS level (equal-to-him)"]
    chk("7 equal-to-him matchup: |level| small (< 5 rating-pts)", abs(c7["level"]) < 5)
    chk("7 equal-to-him matchup: displacement ~zero BEFORE widening (max move < 1.5pp)"
        " — widening-intact itself is the C# regression harness's proof, not this oracle's",
        max(abs(c7["final"][z] - c7["base"][z]) for z in Z) < 0.015)

    # Point-3 aggregation asymmetry: top-three skill resistance vs five-man physical mean.
    c8, c8b = t["8 star vs HETEROGENEOUS lineup (bench ath low)"], t["8b same top-three, bench ath HIGH (physical only)"]
    chk("8 aggregation: zone gaps bit-identical when only bench athleticism changes",
        all(abs(c8["gaps"][z] - c8b["gaps"][z]) < 1e-12 for z in Z))
    chk("8 aggregation: shape bend bit-identical (skill read untouched)",
        all(abs(c8["bentShapeOnly"][z] - c8b["bentShapeOnly"][z]) < 1e-12 for z in Z))
    chk("8 aggregation: physical level CHANGES (five-man mean read)",
        abs(c8["physLevel"] - c8b["physLevel"]) > 0.5)
    chk("8 aggregation: displacement magnitude changes with it",
        abs(c8["mag"] - c8b["mag"]) > 1e-6)

    # Symmetry + smoothness of the usage gate
    lo = derive(DIET_SHOOTER, SHOOTER, D(70, 70, 70), 0.02)
    hi = derive(DIET_SHOOTER, SHOOTER, D(70, 70, 70), 0.15)
    chk("usage smoothness: more load => strictly more displacement (same matchup)",
        abs(hi["mag"]) > abs(lo["mag"]) > 0)

    # Validity sweep: random players, lineups, usage — every output a valid pie.
    rng = random.Random(20260704); bad = 0
    for _ in range(4000):
        p = P(rng.randint(20, 95), rng.randint(20, 95), rng.randint(20, 95),
              rng.randint(20, 95), rng.randint(25, 90))
        ds = D(rng.randint(20, 92), rng.randint(20, 92), rng.randint(20, 92),
               rng.randint(25, 90))
        diet = {z: rng.randint(1, 60) for z in Z}
        up = rng.random() * 0.25
        f = derive(diet, p, ds, up)["final"]
        if abs(sum(f.values()) - 1.0) > 1e-9 or any(v < 0 for v in f.values()):
            bad += 1
    chk("validity: 4000 random derivations all sum to 1, none negative", bad == 0)

    # Bound: |mag| never exceeds DISP_MAX
    rng = random.Random(7); worst = 0.0
    for _ in range(2000):
        p = P(rng.randint(5, 99), rng.randint(5, 99), rng.randint(5, 99),
              rng.randint(5, 99), rng.randint(5, 99))
        ds = D(rng.randint(5, 99), rng.randint(5, 99), rng.randint(5, 99), rng.randint(5, 99))
        worst = max(worst, abs(derive(DIET_STAR, p, ds, 0.5)["mag"]))
    chk(f"bound: |mag| <= DISP_MAX across extreme sweeps (worst {worst:.3f})", worst <= DISP_MAX + 1e-12)

    return res

# ---------------------------------------------------------------------------
# GOLDEN VECTORS — emitted for the eventual C# stage-wise parity, tendency style.
# ---------------------------------------------------------------------------
def emit_golden(path="displacement_golden.json"):
    out = []
    for name, (diet, p, ds, up) in CASES.items():
        t = derive(diet, p, ds, up)
        out.append({"name": name, "shooter": p, "diet": diet,
                    "defenders": list(ds),   # the FULL five-man lineup — a single-
                    # defender emit could not catch a wrong aggregation (top-three
                    # skill vs five-man physical mean) in the C# port
                    "usagePressure": up,
                    "trace": {k: t[k] for k in
                              ("base", "gaps", "skillLevel", "physLevel", "level",
                               "residuals", "bentShapeOnly", "mag", "ladder",
                               "final")}})
    with open(path, "w") as f:
        json.dump({"zoneOrder": Z, "vectors": out}, f, indent=1, sort_keys=True)
    print(f"golden fixture written: {path} ({len(out)} vectors)")

# ---------------------------------------------------------------------------
if __name__ == "__main__":
    print("=" * 76)
    print("ARCHETYPE TABLE  (baseline -> final, shares %)")
    print("=" * 76)
    for name, args in CASES.items():
        t = derive(*args)
        b, f = t["base"], t["final"]
        print(f"{name}")
        print(f"   level {t['level']:+6.1f} (skill {t['skillLevel']:+5.1f} phys {t['physLevel']:+4.1f})  mag {t['mag']:+.3f}")
        print("   base : " + "  ".join(f"{z} {100*b[z]:4.1f}" for z in Z))
        print("   final: " + "  ".join(f"{z} {100*f[z]:4.1f}" for z in Z))
    print()
    print("=" * 76); print("STRUCTURAL CHECKS  (the gate)"); print("=" * 76)
    ok_all = True
    for name, ok in checks():
        ok_all &= ok
        print(f"  [{'PASS' if ok else 'FAIL'}]  {name}")
    print("-" * 76)
    print("  ALL STRUCTURAL CHECKS PASS" if ok_all else "  *** SOME CHECKS FAILED ***")
    print()
    emit_golden()
