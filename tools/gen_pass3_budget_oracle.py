#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_pass3_budget_oracle.py  --  Player-Generation Pass 3, TWO-PLANE BUDGET model.

*** LOCKED 2026-07-24 (S68) -- Emmett's table ruling on real bodies. ***
*** This file is the port spec. The C# port (Phase-59 parity pattern) binds to it.  ***
*** S68 rulings folded: D1 (marginal-only preservation), D2 (arrival mean follows   ***
*** the body, dice on top), D3 (Rscore weights from the realized card, label-free), ***
*** the DEFENSE pathway (elite defense is a ticket in -- interior via the post      ***
*** pathway, perimeter gated by size+athleticism+viable handle), the small-body     ***
*** handle/shoot pull lean ("no 5'9 elite defender who can't dribble at all"),      ***
*** shooting ruled ABUNDANT as built, ceiling pressure 23% ruled solid for now,     ***
*** the line standing at 17 in the deeper 79.5% world.                              ***

DESIGN MEDIUM / SPEC ORACLE (S68).  Python that proves the generation math before any
C# is written.  Deterministic (seed 20260724).  Spec of record: journal S67 (2026-07-24),
the six rulings + the locked family map, plus the three S68 gate rulings (D1/D2/D3).

THE MODEL, stated plainly:

  BODY FIRST (D1 ruling).  Height is drawn from the PRESERVED TOTAL MARGINAL of the
  old model (fitted once against the S42-era oracle's canonical 46,000 cohort at seed
  20260706, embedded below as HEIGHT_MARGINAL, hard-checked per height bin every run).
  Nothing conditional is preserved -- the old body<->role coupling is retired.
  Wingspan / Weight / the athletic card are the old machinery unchanged (the ruled
  athletic floors ARE SIZE_COEF).

  TWO PLANES, drawn from the body:
    defensive plane  -- body-DOMINATED (who can you guard): a continuous interiority
        score, logistic in Height plus modest athletic/dice noise.
    offensive role   -- drawn on SIZE-SLIDING ASYMMETRIC odds (S67 ruling 4): every
        role is legal at every height; height only BIASES the odds, and the slide is
        asymmetric on purpose (undersized post-oriented players outnumber oversized
        perimeter-oriented ones).  The undersized post scorer and the 7'0" shooter are
        both legal draws whose RATES the table rules.

  THE BUDGET BUYS CEILINGS (S67 ruling 1).  One talent draw sets a nominal budget of
  ceiling points.  Per-family pulls = role preference x body factor x per-player dice
  (S67 ruling 3: NOTHING pulls zero -- the floor above zero is a computational epsilon
  only, never a meaningful minimum spend).  Concentration is a per-player dice roll
  INDEPENDENT of talent (ruling 5).  Family-first allocation, within-family second
  stage.  DIMINISHING-RETURNS PRICING, uniform across all skills and players (ruling
  6): bad-to-good is cheap, good-to-elite is expensive; ratings SATURATE toward the
  body cap and never seek it -- spend past saturation buys almost nothing.  NO forced
  body allocation, NO generator-side rating floors beyond the flat base.

  THE BODY'S GIFTS ARE PAID LIVE BY THE GAME (S67 ruling 2).  The generator grants no
  free embodied rating: the old size-card rebounding (post_bonus = 8*o + height terms)
  is RETIRED; Rebounding is a SPENDABLE family, its body lean carried ONLY by the
  Rebounding-family pull body-factor and the cap.  The zero-rebounding 7-footer exists;
  the ENGINE's body floor (S45/S46) pays him on the floor.

  BODY CAPS.  Interior-defense skills (PostDefense, RimProtection) are hard-capped low
  on small bodies -- the card that dies is "small guy whose rating claims he can wall
  off centers"; the card that lives is "small guy with real reach-in craft, capped,
  who still pays the size bill live" (ruling 3).  Rebounding craft carries a milder
  small-body cap.  Nothing else is body-capped: the 7'0" pure shooter is legal.

  ARRIVAL (D2 ruling, S68): the arrival MEAN follows the BODY, not the style of play
  -- small arrives ready, big arrives raw -- and the existing per-player dice stay on
  top, so the ready 6'11" freshman and the raw 6'0" project both remain legal draws
  against the odds.  The expression transform is the S42 machinery unchanged: the
  budget model produces the LATENT card (ceilings); current = baseline + e*(latent -
  baseline); runway = latent - current by construction.

  RSCORE (D3 ruling, S68): the ruled intent survives (one college-caliber pathway;
  the height-ACCESS curve; the sub-6'0 taper; rebounding gated by interior skill).
  The pathway WEIGHTS now derive from the REALIZED card's family allocation -- the
  card itself, never a stored label -- so flipping the stored role/plane labels moves
  Rscore by EXACTLY ZERO (asserted).  The rebounding read moves to the Rebounding
  family on the skill card.

Every magnitude below is a placeholder SHAPE.  The scratch prototype's constants were
sandbox-only and are NOT on record (S68 gate finding); constants here are re-derived
from the recorded S67 shapes and re-tuned until the ruled table reproduces on real
bodies.  Calibration against the season page comes later, per standing practice.
"""

import random
import math
import sys
from collections import OrderedDict, Counter, defaultdict

SEED        = 20260724
N_CANDIDATE = 46000
R_LINE      = 17.0          # the standing recruiting line (S66 ruling, reversible)
OLD_HELD_SELECTIVITY = 0.559  # S66 recorded: 55.9% of the old cohort cleared line 17

# ============================================================================
# D1 -- THE PRESERVED HEIGHT MARGINAL (fitted once, old oracle, seed 20260706, N=46000)
# The only preservation constraint.  Per-value probabilities; drawn by inverse CDF.
# ============================================================================
HEIGHT_MARGINAL = {
 40:0.0468696,41:0.0067391,42:0.0084565,43:0.0099130,44:0.0106304,45:0.0120435,
 46:0.0145435,47:0.0178478,48:0.0199783,49:0.0240000,50:0.0246739,51:0.0310652,
 52:0.0348478,53:0.0383478,54:0.0360870,55:0.0365435,56:0.0365217,57:0.0360870,
 58:0.0366087,59:0.0376304,60:0.0343478,61:0.0351522,62:0.0331087,63:0.0319565,
 64:0.0296957,65:0.0289348,66:0.0290435,67:0.0268696,68:0.0267826,69:0.0263913,
 70:0.0237391,71:0.0233043,72:0.0211304,73:0.0190435,74:0.0176522,75:0.0155435,
 76:0.0135217,77:0.0111304,78:0.0090000,79:0.0061739,80:0.0050870,81:0.0038478,
 82:0.0029130,83:0.0017826,84:0.0014130,85:0.0012391,86:0.0007174,87:0.0004565,
 88:0.0001739,89:0.0001087,90:0.0001522,91:0.0000870,92:0.0000217,93:0.0000217,
 94:0.0,95:0.0000217,96:0.0,97:0.0,98:0.0,99:0.0,
}
_HM_TOTAL = sum(HEIGHT_MARGINAL.values())
HEIGHT_CDF = []
_acc = 0.0
for _h in range(40, 100):
    _acc += HEIGHT_MARGINAL[_h] / _HM_TOTAL
    HEIGHT_CDF.append((_acc, _h))
HEIGHT_CDF[-1] = (1.0, HEIGHT_CDF[-1][1])

def height_from_u(u):
    """Deterministic inverse-CDF lookup (the S69 seam split: transform only, no RNG)."""
    for cum, h in HEIGHT_CDF:
        if u <= cum:
            return h
    return 99

def draw_height(r, rec=None):
    u = r.random()
    if rec is not None: rec["height_u"] = u          # S69 recorder: after the draw, no RNG
    return height_from_u(u)

# ============================================================================
# BODY MACHINERY (surviving unchanged from the locked oracle -- o-free, verified S68 §0)
# ============================================================================
ATH_HEIGHT_CENTER = 60.0
SIZE_COEF = {"Strength": +0.42, "Speed": -0.22, "Quickness": -0.22, "FirstStep": -0.22,
             "Vertical": -0.02, "Endurance": -0.06, "Hustle": 0.00}
ATH_SIGMA = {"Strength": 6.0, "Speed": 7.0, "Quickness": 7.0, "FirstStep": 7.0,
             "Vertical": 8.0, "Endurance": 6.0, "Hustle": 8.0}
ATH_BASE_LO, ATH_BASE_HI = 25.0, 85.0
ATHQ_A, ATHQ_B = 2.2, 2.2
ATH_KEYS = ["Strength", "Speed", "Quickness", "FirstStep", "Vertical", "Endurance", "Hustle"]

# Arrival / expression (S42 machinery; D2: the MEAN's source is now the body)
ARR_READY = 0.72        # mean arrival at/below ARRB_LO (small body: arrives ready)
ARR_RAW   = 0.42        # mean arrival at/above ARRB_HI (big body: raw project)
ARRB_LO   = 48.0        # ~5'10": fully "small" for arrival purposes
ARRB_HI   = 78.0        # ~6'9"+: fully "big"
ARR_SIGMA = 0.18        # the per-player dice (D2: ready 6'11" / raw 6'0" both legal)
E_MIN     = 0.15
EXPR_BASELINE = 14.0

# FreeThrow derivation (S71 ruling 2026-07-24: mirror real-life FT shooting — median
# ~70, low-90s tail real, hack-target floor 40). Re-anchored at the population's own
# Outside center: the old inline anchor (Outside 50) sat far above the league's actual
# Outside median (~32), so any span strong enough to separate shooters dragged the
# middle down. FT_OUT_ANCHOR is now a NAMED constant, and FT_CENTER is legible: it IS
# what the median player shoots. FT_OUT_SCALE reaffirmed at 25.0.
FT_CENTER = 71.5; FT_OUT_ANCHOR = 36.0; FT_OUT_SPAN = 9.0; FT_OUT_SCALE = 25.0
FT_HEIGHT_COEF = 9.0; FT_MIN = 40.0; FT_MAX = 96.0
FT_SIGMA = 6.0

# ============================================================================
# THE SEVEN FAMILIES (journal S67, every member assigned; Rebounding SPENDABLE)
# ============================================================================
FAMILIES = OrderedDict([
    ("Shooting",        ["Outside", "Mid", "OffBallMovement"]),
    ("InteriorOffense", ["Close", "Finishing", "PostMoves", "Screening"]),
    ("Creation",        ["BallHandling", "Passing", "Playmaking", "SelfCreation", "FoulDrawing"]),
    ("PerimDefense",    ["PerimeterDefense", "Steals", "OffBallDefense"]),
    ("InteriorDefense", ["PostDefense", "RimProtection"]),
    ("Rebounding",      ["OffensiveRebounding", "DefensiveRebounding"]),
    ("Glue",            ["BasketballIQ", "Discipline", "HelpDefense"]),
])
SPEND_SKILLS = [k for fam in FAMILIES.values() for k in fam]     # 22 spendable skills
assert len(SPEND_SKILLS) == 22 and len(set(SPEND_SKILLS)) == 22
SKILL_TO_FAM = {k: f for f, ks in FAMILIES.items() for k in ks}
PERIM_FAMS = ("Shooting", "Creation", "PerimDefense")
POST_FAMS  = ("InteriorOffense", "InteriorDefense", "Rebounding")

# ============================================================================
# PLANE 1 -- DEFENSIVE POSITION (body-dominated: who can you guard)
# ============================================================================
DEF_MID, DEF_STEEP = 62.0, 9.0     # logistic center/scale in Height
DEF_NOISE = 0.10                    # modest dice: body dominates, does not dictate

def draw_def_plane(r, height, rec=None):
    base = 1.0 / (1.0 + math.exp(-(height - DEF_MID) / DEF_STEEP))
    g = r.gauss(0.0, DEF_NOISE)
    if rec is not None: rec["def_noise"] = g         # S69 recorder: after the draw, no RNG
    d = clamp(base + g, 0.0, 1.0)
    cat = "PerimD" if d < 0.35 else ("WingD" if d < 0.65 else "PostD")
    return d, cat

# ============================================================================
# PLANE 2 -- OFFENSIVE ROLE (size-sliding ASYMMETRIC odds; every role legal everywhere)
# ============================================================================
ROLES = ["Creator", "Shooter", "Slasher", "PostScorer", "Connector"]
ROLE_PRIOR = {"Creator": 0.24, "Shooter": 0.24, "Slasher": 0.20, "PostScorer": 0.18, "Connector": 0.14}
# Size slide: hfrac in [0,1] over the height range; perimeter roles decay as hfrac
# rises, post roles decay as it falls -- ASYMMETRIC decay rates per S67 ruling 4
# (perimeter identity dies FASTER on a big body than post identity dies on a small one:
# "more smaller post oriented players than oversized perimeter oriented ones").
HFRAC_LO, HFRAC_HI = 44.0, 84.0
PERIM_DECAY = 4.6    # steep: the oversized perimeter identity is rare
POST_DECAY  = 2.1    # gentle: the undersized post identity is uncommon, not rare

def role_odds(height):
    hf = clamp((height - HFRAC_LO) / (HFRAC_HI - HFRAC_LO), 0.0, 1.0)
    mult = {
        "Creator":    math.exp(-PERIM_DECAY * hf),
        "Shooter":    math.exp(-0.55 * PERIM_DECAY * hf),   # the tall shooter is the least-punished perimeter identity
        "Slasher":    math.exp(-1.4 * abs(hf - 0.42)),
        "PostScorer": math.exp(-POST_DECAY * (1.0 - hf)),
        "Connector":  1.0,
    }
    w = {ro: ROLE_PRIOR[ro] * mult[ro] for ro in ROLES}
    tot = sum(w.values())
    return {ro: w[ro] / tot for ro in ROLES}

def draw_role(r, height, rec=None):
    odds = role_odds(height)
    u = r.random()
    if rec is not None: rec["role_u"] = u            # S69 recorder: after the draw, no RNG
    acc = 0.0
    for ro in ROLES:
        acc += odds[ro]
        if u <= acc:
            return ro
    return ROLES[-1]

# ============================================================================
# THE BUDGET (talent draw -> nominal ceiling points; concentration INDEPENDENT)
# ============================================================================
TALENT_A, TALENT_B = 2.3, 2.7      # same gentle top-heavy slope as the old skill-quality draw
BUDGET_LO   = 260.0                # everyone can afford a real weapon or two
BUDGET_SPAN = 620.0
BUDGET_POW  = 1.35                 # top-heavier than linear: elite budgets rarer
CONC_A, CONC_B = 2.0, 2.0          # per-player concentration dice (quality-INDEPENDENT)

def draw_budget(r, rec=None):
    q = r.betavariate(TALENT_A, TALENT_B)
    if rec is not None: rec["q"] = q                 # S69 recorder: after the draw, no RNG
    B = BUDGET_LO + (q ** BUDGET_POW) * BUDGET_SPAN
    return q, B

# ============================================================================
# PULLS  (role preference x body factor x per-player dice; epsilon floor only)
# ============================================================================
PULL_EPS   = 0.010                 # computational floor: nothing pulls zero, nothing is forced
PULL_DICE_SIGMA = 0.50             # lognormal dice on EVERY pull ("weird players are possible")

# Role -> family offensive preference (defense/rebounding/glue get their lean from the body)
ROLE_FAM_PREF = {
    #             Shoot  IntOff Creat
    "Creator":    {"Shooting": 0.85, "InteriorOffense": 0.30, "Creation": 1.60},
    "Shooter":    {"Shooting": 1.75, "InteriorOffense": 0.30, "Creation": 0.55},
    "Slasher":    {"Shooting": 0.72, "InteriorOffense": 1.30, "Creation": 0.85},
    "PostScorer": {"Shooting": 0.42, "InteriorOffense": 1.90, "Creation": 0.30},
    "Connector":  {"Shooting": 0.80, "InteriorOffense": 0.80, "Creation": 0.80},
}
GLUE_PREF = 0.32                   # glue pulls WEAK for everyone (S67 ruling 3)

def family_pulls(r, role, height, dplane, rec=None):
    """pull = role preference x body factor x dice, floored at epsilon."""
    hf = clamp((height - HFRAC_LO) / (HFRAC_HI - HFRAC_LO), 0.0, 1.0)
    pref = dict(ROLE_FAM_PREF[role])
    # body leans defense + rebounding (S67 ruling 3); the defensive PLANE carries the split
    pref["PerimDefense"]    = 0.30 + 0.85 * (1.0 - dplane)
    pref["InteriorDefense"] = 0.30 + 0.85 * dplane
    pref["Rebounding"]      = 0.22 + 0.80 * hf          # the ruled Rebounding body-factor lean
    # S68 Emmett ruling: "there are no 5'9\" elite defenders who can't dribble at all" --
    # small bodies lean handle/shoot (a pull lean, budget-paid with dice; NOT a free floor)
    pref["Creation"] = pref.get("Creation", 0.8) * (1.0 + 0.45 * (1.0 - hf))
    pref["Shooting"] = pref.get("Shooting", 0.8) * (1.0 + 0.25 * (1.0 - hf))
    pref["Glue"]            = GLUE_PREF
    pulls = {}
    for fam in FAMILIES:
        g = r.gauss(0.0, PULL_DICE_SIGMA)
        if rec is not None: rec["pull_gauss"][fam] = g   # S69 recorder: after the draw, no RNG
        dice = math.exp(g)
        pulls[fam] = max(PULL_EPS, pref[fam] * dice)
    return pulls

# Within-family member preferences by role (dice per member on top; uniform default)
WITHIN_PREF = {
    "Creator":    {"BallHandling": 1.5, "Playmaking": 1.5, "Passing": 1.3, "SelfCreation": 1.2,
                   "Outside": 1.3, "PostMoves": 0.5, "Screening": 0.5},
    "Shooter":    {"Outside": 1.55, "OffBallMovement": 1.4, "Mid": 0.9, "SelfCreation": 0.7,
                   "PostMoves": 0.4, "Screening": 0.5},
    "Slasher":    {"Finishing": 1.7, "Close": 1.3, "FoulDrawing": 1.4, "SelfCreation": 1.3,
                   "BallHandling": 1.1, "Outside": 0.7, "PostMoves": 0.5},
    "PostScorer": {"PostMoves": 1.8, "Close": 1.5, "Finishing": 1.4, "Screening": 1.1,
                   "BallHandling": 0.5, "SelfCreation": 0.6, "Outside": 0.6},
    "Connector":  {},
}
WITHIN_DICE_SIGMA = 0.50

# ============================================================================
# CONCENTRATION -> allocation sharpening (family stage AND within-family stage)
# ============================================================================
GAMMA_LO, GAMMA_HI = 0.75, 3.2     # c=0 broad ... c=1 one-family spike

def sharpen(weights, gamma):
    w = {k: v ** gamma for k, v in weights.items()}
    tot = sum(w.values())
    return {k: v / tot for k, v in w.items()}

# ============================================================================
# BODY CAPS + PRICING (uniform diminishing returns; saturate toward the cap)
# ============================================================================
FLAT_BASE = 8.0                    # the flat base every skill starts at (holes live here)
BASE_JITTER = 2.2                  # small dice so holes read 8-14, not a wall of 8s

# Interior-defense hard body caps: small bodies cannot own a wall-off-centers rating.
IDCAP_LO_H, IDCAP_HI_H = 46.0, 74.0   # below ~5'10" the cap bottoms; above ~6'6" fully open
IDCAP_MIN = 34.0                      # the small guy's real reach-in craft ceiling
REBCAP_MIN = 52.0                     # rebounding craft: milder small-body cap

def body_cap(skill, height):
    if skill in ("PostDefense", "RimProtection"):
        t = clamp((height - IDCAP_LO_H) / (IDCAP_HI_H - IDCAP_LO_H), 0.0, 1.0)
        return IDCAP_MIN + (99.0 - IDCAP_MIN) * t
    if skill in ("OffensiveRebounding", "DefensiveRebounding"):
        t = clamp((height - IDCAP_LO_H) / (IDCAP_HI_H - IDCAP_LO_H), 0.0, 1.0)
        return REBCAP_MIN + (99.0 - REBCAP_MIN) * t
    return 99.0

PRICE_TAU = 52.0                   # the one price curve, same for every skill and player
                                   # (rating = base + (cap-base)*(1 - exp(-spend/tau)))

def price(spend, base, cap):
    return base + (cap - base) * (1.0 - math.exp(-spend / PRICE_TAU))

def clamp(x, lo, hi):
    return lo if x < lo else (hi if x > hi else x)

# ============================================================================
# THE GENERATOR -- one honest player, column by column
# ============================================================================
def derive_ft(outside, ft_idio, height):
    val = (FT_CENTER + FT_OUT_SPAN * math.tanh((outside - FT_OUT_ANCHOR) / FT_OUT_SCALE)
           - FT_HEIGHT_COEF * ((height - 55.0) / 40.0) + ft_idio)
    return int(clamp(round(val), FT_MIN, FT_MAX))

def generate_player(r, rec=None):
    # S69 recorder seam (S42.2 pattern): rec is an optional dict, every write lands
    # strictly AFTER its draw is assigned, and the recorder itself draws no RNG --
    # the full oracle run (rec=None) is byte-identical pre/post seam, asserted at port.
    if rec is not None:
        rec["ath_noise"] = {}; rec["pull_gauss"] = {}
        rec["within_gauss"] = {}; rec["base_jitter_gauss"] = {}
    # ---- 1. BODY FIRST (D1): height from the preserved marginal ------------------
    Height = draw_height(r, rec)
    ws_noise = r.gauss(4.0, 3.0)
    if rec is not None: rec["ws_noise"] = ws_noise
    Wingspan = int(clamp(round(Height + ws_noise), 40, 99))
    a = r.betavariate(ATHQ_A, ATHQ_B)
    if rec is not None: rec["a"] = a
    ath = {}
    for k in ATH_KEYS:
        acenter = ATH_BASE_LO + a * (ATH_BASE_HI - ATH_BASE_LO)
        g = r.gauss(0.0, ATH_SIGMA[k])
        if rec is not None: rec["ath_noise"][k] = g
        val = acenter + SIZE_COEF[k] * (Height - ATH_HEIGHT_CENTER) + g
        ath[k] = int(clamp(round(val), 8, 99))
    wg = r.gauss(0, 6)
    if rec is not None: rec["weight_noise"] = wg
    Weight = int(clamp(round(30 + 0.40 * Height + 0.30 * ath["Strength"] + wg), 20, 99))

    # ---- 2. THE TWO PLANES (role drawn FROM the body; height biases, never dictates)
    dplane, dcat = draw_def_plane(r, Height, rec)
    role = draw_role(r, Height, rec)

    # ---- 3. THE BUDGET (talent) + concentration (independent dice) ---------------
    q, B = draw_budget(r, rec)
    c = r.betavariate(CONC_A, CONC_B)
    if rec is not None: rec["c"] = c
    gamma = GAMMA_LO + c * (GAMMA_HI - GAMMA_LO)

    # ---- 4. PULLS -> family-first allocation -> within-family second stage --------
    pulls = family_pulls(r, role, Height, dplane, rec)
    fam_share = sharpen(pulls, gamma)
    fam_budget = {f: B * fam_share[f] for f in FAMILIES}

    spend = {}
    for fam, members in FAMILIES.items():
        wp = WITHIN_PREF.get(role, {})
        mw = {}
        for k in members:
            g = r.gauss(0.0, WITHIN_DICE_SIGMA)
            if rec is not None: rec["within_gauss"][k] = g   # member keys unique across families
            dice = math.exp(g)
            mw[k] = max(PULL_EPS, wp.get(k, 1.0) * dice)
        mshare = sharpen(mw, max(0.75, 0.55 * gamma + 0.35))
        for k in members:
            spend[k] = fam_budget[fam] * mshare[k]

    # ---- 5. PRICING -> the LATENT card (ceilings; concave, cap-saturating) --------
    latent = {}
    caps = {}
    for k in SPEND_SKILLS:
        g = r.gauss(0.0, BASE_JITTER)
        if rec is not None: rec["base_jitter_gauss"][k] = g  # pre-abs raw draw
        base = clamp(FLAT_BASE + abs(g), 8.0, 16.0)
        cap = body_cap(k, Height)
        caps[k] = cap
        latent[k] = int(clamp(round(price(spend[k], base, cap)), 8, 99))

    # ---- 6. ARRIVAL (D2: mean follows the BODY; dice on top) + expression ---------
    hb = clamp((Height - ARRB_LO) / (ARRB_HI - ARRB_LO), 0.0, 1.0)
    arr_mean = ARR_READY - hb * (ARR_READY - ARR_RAW)
    ag = r.gauss(arr_mean, ARR_SIGMA)
    if rec is not None: rec["arrival_raw"] = ag              # pre-clamp raw draw
    arrival = clamp(ag, 0.0, 1.0)
    e = E_MIN + arrival * (1.0 - E_MIN)
    current = {}
    for k in SPEND_SKILLS:
        L = latent[k]
        current[k] = L if L <= EXPR_BASELINE else int(round(EXPR_BASELINE + e * (L - EXPR_BASELINE)))

    # ---- 7. FreeThrow (derived; ONE persistent idiosyncrasy draw) -----------------
    ft_idio = r.gauss(0.0, FT_SIGMA)
    if rec is not None: rec["ft_idio"] = ft_idio
    latent_ft  = derive_ft(latent["Outside"], ft_idio, Height)
    current_ft = derive_ft(current["Outside"], ft_idio, Height)

    runway = {k: latent[k] - current[k] for k in SPEND_SKILLS}
    runway["FreeThrow"] = latent_ft - current_ft

    return {
        "Height": Height, "Wingspan": Wingspan, "Weight": Weight, "ath": ath,
        "dplane": dplane, "dcat": dcat, "role": role,
        "q": q, "budget": B, "conc": c, "gamma": gamma,
        "pulls": pulls, "fam_share": fam_share, "spend": spend, "caps": caps,
        "latent": latent, "current": current,
        "latent_ft": latent_ft, "current_ft": current_ft, "ft_idio": ft_idio,
        "arrival": arrival, "e": e,
        "runway": runway, "runway_total": sum(runway.values()),
    }

# ============================================================================
# RSCORE -- re-derived (D3): weights from the REALIZED card, never a label
# ============================================================================
HF_LO, HF_HI, HF_RANGE, HF_STEEP, HF_MID = 0.20, 1.45, 1.25, 0.13, 59.0
LOW_TAPER_FLOOR, LOW_TAPER_TOP = 0.10, 51.0
PATHWAY_W_FLOOR = 0.55           # the old PERIM_OW/POST_OW envelope, kept: weights in [0.55, 1.0]

def family_mass(cur, fams):
    return sum(max(0.0, cur[k] - 20.0) for f in fams for k in FAMILIES[f])

def rscore_parts(p):
    c = p["current"]; ath = sum(p["ath"][k] for k in ("Strength", "Speed", "Quickness", "FirstStep", "Vertical")) / 5.0
    # pathway weights from the REALIZED allocation on the card itself (label-free by construction)
    pm = family_mass(c, PERIM_FAMS); qm = family_mass(c, POST_FAMS)
    tilt = pm / (pm + qm) if (pm + qm) > 0 else 0.5
    perim_w = PATHWAY_W_FLOOR + (1.0 - PATHWAY_W_FLOOR) * tilt
    post_w  = PATHWAY_W_FLOOR + (1.0 - PATHWAY_W_FLOOR) * (1.0 - tilt)
    # PERIMETER pathway (structure unchanged from the locked oracle)
    access   = max(c["BallHandling"], c["OffBallMovement"], c["Outside"]) / 99.0
    mid_eff  = c["Mid"] * min(1.0, access / 0.45)
    entry_p  = max(c["Outside"], c["BallHandling"], mid_eff)
    perim_support = (c["Passing"] + c["Playmaking"] + c["SelfCreation"] + c["OffBallMovement"]) / 4.0
    perim_def = max(c["PerimeterDefense"], c["Steals"], c["OffBallDefense"])
    perim_val = max(0.0, entry_p - 20) * (0.55 + 0.30 * perim_support / 99 + 0.15 * perim_def / 99) + 0.14 * ath
    # POST pathway; the rebounding read now comes from the FAMILY on the skill card (D3)
    post_skill   = max(c["RimProtection"], c["PostMoves"], c["Close"], c["Finishing"], c["PostDefense"])
    post_support = (c["Screening"] + c["PostDefense"] + c["RimProtection"]) / 3.0
    height_factor = clamp(HF_LO + HF_RANGE / (1.0 + math.exp(-HF_STEEP * (p["Height"] - HF_MID))), HF_LO, HF_HI)
    skill_val = max(0.0, post_skill - 24) * (0.60 + 0.40 * post_support / 99) * height_factor
    glass   = (c["OffensiveRebounding"] + c["DefensiveRebounding"]) / 2.0
    reb_val = glass * 0.16 * min(1.0, post_skill / 45.0)
    low_taper = clamp((p["Height"] - 40.0) / (LOW_TAPER_TOP - 40.0), LOW_TAPER_FLOOR, 1.0)
    post_val = (skill_val + reb_val + 0.10 * ath * min(1.0, height_factor)) * low_taper
    # DEFENSE pathway (S68 Emmett ruling): elite defense alone is a ticket in --
    # interior defense already rides the post pathway above; the PERIMETER stopper needs
    # the size and athleticism to go with it, and at least a barely-viable handle/shot.
    stop_skill = max(c["PerimeterDefense"], c["Steals"], c["OffBallDefense"])
    size_gate  = clamp((p["Height"] - 51.0) / 12.0, 0.0, 1.0)          # 6'0" no, ~6'4"+ yes
    ath_gate   = clamp((ath - 45.0) / 25.0, 0.0, 1.0)                   # real athlete required
    viab_gate  = clamp((max(c["BallHandling"], c["Outside"]) - 15.0) / 20.0, 0.0, 1.0)
    def_val    = max(0.0, stop_skill - 30.0) * 0.55 * size_gate * ath_gate * viab_gate + 0.10 * ath * size_gate
    wperim, wpost = perim_w * perim_val, post_w * post_val
    best = max(wperim, wpost, def_val)
    which = "perim" if best == wperim else ("post" if best == wpost else "defense")
    return {"rscore": best, "which": which,
            "tilt": tilt, "perim_val": perim_val, "post_val": post_val, "def_val": def_val}

def rscore(p):
    return rscore_parts(p)["rscore"]

# ============================================================================
# COHORT + CHECKS + DIAGNOSTICS + TABLES
# ============================================================================
def build_cohort(seed=SEED, n=N_CANDIDATE):
    r = random.Random(seed)
    return [generate_player(r) for _ in range(n)]

def ht_str(h):
    inches = 68 + 0.36 * (h - 40)
    ft = int(inches // 12); im = int(round(inches - 12 * ft))
    if im == 12: ft += 1; im = 0
    return f"{ft}'{im}\""

HEIGHT_BINS = OrderedDict([
    ("5'8-5'9", (40, 44)), ("5'10-5'11", (45, 50)), ("6'0-6'1", (51, 56)),
    ("6'2-6'5", (57, 65)), ("6'6-6'7", (66, 70)), ("6'8-6'9", (71, 79)),
    ("6'10-7'0", (80, 86)), ("7'1-7'2", (87, 92)), ("7'3+", (93, 99)),
])
def height_bin(h):
    for name, (lo, hi) in HEIGHT_BINS.items():
        if lo <= h <= hi:
            return name
    return "7'3+"

def top_skills(card, n=3):
    return sorted(card.items(), key=lambda kv: -kv[1])[:n]

def fmt_tops(card, n=3):
    return " ".join(f"{k}:{v}" for k, v in top_skills(card, n))

def run(argv=None):
    print(f"=== gen_pass3_budget_oracle  seed={SEED}  N={N_CANDIDATE} ===")
    coh = build_cohort()

    # ---------- HARD CHECKS ----------
    fails = []
    def check(name, ok, detail=""):
        print(f"[{'OK' if ok else 'FAIL'}] {name}" + (f"  {detail}" if detail else ""))
        if not ok:
            fails.append(name)

    # determinism
    r2 = random.Random(SEED)
    for i in (0, 137, 45999):
        p2 = None
        rr = random.Random(SEED)
        for j in range(i + 1):
            p2 = generate_player(rr)
        same = (p2["latent"] == coh[i]["latent"] and p2["Height"] == coh[i]["Height"]
                and p2["role"] == coh[i]["role"] and abs(p2["budget"] - coh[i]["budget"]) < 1e-12)
        check(f"determinism player {i}", same)

    # bounds + latent>=current + budget conservation (exact)
    ok_b = all(8 <= p["latent"][k] <= 99 and 8 <= p["current"][k] <= 99 and p["current"][k] <= p["latent"][k]
               for p in coh for k in SPEND_SKILLS)
    check("bounds + current<=latent (all 46k x 22)", ok_b)
    ok_c = all(abs(sum(p["spend"].values()) - p["budget"]) < 1e-6 for p in coh)
    check("budget conservation exact (spend sums to nominal)", ok_c)

    # D1 read 1: height marginal preserved per bin (tolerance 0.6pp absolute per bin)
    binc = Counter(height_bin(p["Height"]) for p in coh)
    worst = 0.0
    for b, (lo, hi) in HEIGHT_BINS.items():
        want = sum(HEIGHT_MARGINAL[h] for h in range(lo, hi + 1)) / _HM_TOTAL
        got = binc.get(b, 0) / len(coh)
        worst = max(worst, abs(got - want))
    check("D1 height marginal per bin (<=0.6pp)", worst <= 0.006, f"worst {100*worst:.2f}pp")

    # concentration independent of talent
    n = len(coh)
    mq = sum(p["q"] for p in coh) / n; mc = sum(p["conc"] for p in coh) / n
    cov = sum((p["q"] - mq) * (p["conc"] - mc) for p in coh) / n
    sq = math.sqrt(sum((p["q"] - mq) ** 2 for p in coh) / n); sc = math.sqrt(sum((p["conc"] - mc) ** 2 for p in coh) / n)
    corr = cov / (sq * sc)
    check("concentration independent of talent |corr|<0.02", abs(corr) < 0.02, f"corr {corr:+.4f}")

    # D3: label sensitivity EXACTLY zero (flip role+plane labels, rscore unmoved)
    probe = coh[123]
    r_before = rscore(probe)
    flipped = dict(probe); flipped["role"] = "PostScorer" if probe["role"] != "PostScorer" else "Shooter"
    flipped["dcat"] = "PostD" if probe["dcat"] != "PostD" else "PerimD"; flipped["dplane"] = 1.0 - probe["dplane"]
    check("D3 Rscore label-flip sensitivity == 0", rscore(flipped) == r_before, f"{r_before:.3f}")

    # interior-defense cap honored (no small body walls off centers)
    ok_cap = all(p["latent"]["PostDefense"] <= body_cap("PostDefense", p["Height"]) + 0.51
                 and p["latent"]["RimProtection"] <= body_cap("RimProtection", p["Height"]) + 0.51 for p in coh)
    check("interior-defense body cap honored (all 46k)", ok_cap)

    # ---------- DIAGNOSTICS ----------
    print("\n---------- DIAGNOSTICS ----------")
    scores = [(rscore(p), p) for p in coh]
    cleared = sum(1 for s, _ in scores if s >= R_LINE)
    srt = sorted(s for s, _ in scores)
    sel_thresh = srt[int((1.0 - OLD_HELD_SELECTIVITY) * len(srt))]
    print(f"held-line-17: {cleared} clear ({100*cleared/n:.1f}%)   held-selectivity-55.9%: threshold Rscore {sel_thresh:.2f}")

    # acceptance leans (sanity anchor vs S66: post 71.9% vs perim 47.8%; 5'8-5'9 40.6% vs 6'10+ 78.5%)
    def acc(pred):
        sub = [(s, p) for s, p in scores if pred(p)]
        return 100.0 * sum(1 for s, _ in sub if s >= R_LINE) / max(1, len(sub))
    print(f"acceptance lean @17: post-leaning(tilt<0.5) {acc(lambda p: rscore_parts(p)['tilt'] < 0.5):.1f}%  "
          f"perim-leaning {acc(lambda p: rscore_parts(p)['tilt'] >= 0.5):.1f}%")
    smallbin = "5'8-5'9"
    print(f"acceptance by height @17: 5'8-5'9 {acc(lambda p: height_bin(p['Height']) == smallbin):.1f}%  "
          f"6'10\"+ {acc(lambda p: p['Height'] >= 80):.1f}%")

    # Q1 supply read: guard-sized (<=6'1") accepted @17 -- current Outside median (S66: 40; redesign target ~50)
    gacc = sorted(p["current"]["Outside"] for sc, p in scores if sc >= R_LINE and p["Height"] <= 56)
    gall = sorted(p["current"]["Outside"] for p in coh if p["Height"] <= 56)
    if gacc:
        print(f"guard-sized Outside median (current): all {gall[len(gall)//2]}  accepted@17 {gacc[len(gacc)//2]}   (S66 anchor: 29 -> 40; arc target ~50)")

    # S68 ruling read: small perimeter players without a viable handle/shot
    tiny_all = [p for p in coh if p["Height"] <= 50]
    tiny_bad = sum(1 for p in tiny_all if max(p["latent"]["BallHandling"], p["latent"]["Outside"]) < 25)
    tiny_bad_acc = sum(1 for sc, p in scores if sc >= R_LINE and p["Height"] <= 50
                       and max(p["latent"]["BallHandling"], p["latent"]["Outside"]) < 25)
    print(f"sub-5'11\" with NO viable handle/shot (ceiling<25 both): {tiny_bad}/{len(tiny_all)} generated, {tiny_bad_acc} make college")

    # D1 reads 2+3: plane mix + role mix by height band
    print("\nD1: defensive-plane mix by height band (PerimD/WingD/PostD %):")
    for b in HEIGHT_BINS:
        sub = [p for p in coh if height_bin(p["Height"]) == b]
        if not sub: continue
        cts = Counter(p["dcat"] for p in sub)
        print(f"  {b:10s} n={len(sub):5d}  " + "  ".join(f"{c}:{100*cts.get(c,0)/len(sub):5.1f}%" for c in ("PerimD","WingD","PostD")))
    print("\nD1: offensive-role mix by height band (%):")
    for b in HEIGHT_BINS:
        sub = [p for p in coh if height_bin(p["Height"]) == b]
        if not sub: continue
        cts = Counter(p["role"] for p in sub)
        print(f"  {b:10s} n={len(sub):5d}  " + "  ".join(f"{ro[:6]}:{100*cts.get(ro,0)/len(sub):4.1f}%" for ro in ROLES))

    # pairing counts in the four buckets by height band (the ruled asymmetry visible)
    print("\npairing buckets by height band (defensive plane x offensive lean):")
    def olean(p):
        return "postO" if p["role"] in ("PostScorer", "Slasher") else "perimO"
    for b in HEIGHT_BINS:
        sub = [p for p in coh if height_bin(p["Height"]) == b]
        if not sub: continue
        cts = Counter((("perimD" if p["dcat"] != "PostD" else "postD"), olean(p)) for p in sub)
        print(f"  {b:10s} " + "  ".join(f"{d}/{o}:{cts.get((d,o),0):5d}" for d in ("perimD","postD") for o in ("perimO","postO")))
    small_postO = sum(1 for p in coh if p["Height"] <= 56 and p["role"] == "PostScorer")
    tall_perimO = sum(1 for p in coh if p["Height"] >= 80 and p["role"] in ("Creator", "Shooter"))
    print(f"  ASYMMETRY: sub-6'2\" PostScorers {small_postO}  vs  6'10\"+ Creators/Shooters {tall_perimO}")

    # anti-target: top-decile budget, good-at-everything-elite-at-nothing (~0 required)
    top10 = sorted(coh, key=lambda p: -p["budget"])[: n // 10]
    anti = sum(1 for p in top10
               if min(p["latent"][k] for k in SPEND_SKILLS) >= 35 and max(p["latent"][k] for k in SPEND_SKILLS) < 92)
    top347 = sorted(coh, key=lambda p: -p["budget"])[:347]
    anti347 = sum(1 for p in top347
                  if min(p["latent"][k] for k in SPEND_SKILLS) >= 35 and max(p["latent"][k] for k in SPEND_SKILLS) < 92)
    print(f"\nanti-target (min>=35 & max<92): top-decile {anti}   top-347 {anti347}   (must be ~0)")

    # elite weakest-skill depth (scratch anchor: median second-weakest 13)
    med2 = sorted(sorted(p["latent"][k] for k in SPEND_SKILLS)[1] for p in top347)[len(top347) // 2]
    print(f"elite hole depth: top-347 median SECOND-weakest latent skill = {med2}")

    # flat-card share (old failure: 26% of guards shapeless)
    flat = sum(1 for p in coh if max(p["latent"].values()) - sorted(p["latent"].values())[11] < 15)
    print(f"flat-card share (top - median latent < 15): {100*flat/n:.1f}%")

    # ceiling pressure (scratch 28% vs old 31%)
    press = sum(1 for p in coh if any(p["latent"][k] >= 95 for k in SPEND_SKILLS))
    print(f"ceiling pressure (any latent >= 95): {100*press/n:.1f}%")

    # elite-shooter density (the challenged number: scratch 105@95+/262@90+ per 3,470; median 44)
    o95 = sum(1 for p in coh if p["latent"]["Outside"] >= 95)
    o90 = sum(1 for p in coh if p["latent"]["Outside"] >= 90)
    omed = sorted(p["latent"]["Outside"] for p in coh)[n // 2]
    print(f"elite shooters: Outside>=95 {o95} ({100*o95/n:.2f}%; per-3470 {o95*3470/n:.0f})  "
          f">=90 {o90} (per-3470 {o90*3470/n:.0f})   median Outside ceiling {omed}")

    # top-1/2/3 family budget share + runway
    t1 = sum(sorted(p["fam_share"].values(), reverse=True)[0] for p in coh) / n
    t2 = sum(sum(sorted(p["fam_share"].values(), reverse=True)[:2]) for p in coh) / n
    t3 = sum(sum(sorted(p["fam_share"].values(), reverse=True)[:3]) for p in coh) / n
    rmed = sorted(p["runway_total"] for p in coh)[n // 2]
    print(f"family concentration: top-1 {100*t1:.1f}%  top-2 {100*t2:.1f}%  top-3 {100*t3:.1f}%   median total runway {rmed}")

    # card-collapse check: role centroids of family-share vectors must separate
    cent = {}
    for ro in ROLES:
        sub = [p for p in coh if p["role"] == ro and 57 <= p["Height"] <= 65]
        cent[ro] = {f: sum(p["fam_share"][f] for p in sub) / max(1, len(sub)) for f in FAMILIES}
    mind = min(sum(abs(cent[a][f] - cent[b][f]) for f in FAMILIES)
               for i, a in enumerate(ROLES) for b in ROLES[i + 1:])
    print(f"card-collapse: min pairwise role-centroid L1 distance (6'2-6'5 band) = {mind:.3f} (near 0 = collapsed)")

    # budget conservation stratified: landed rating points + saturation waste by decile x body band
    print("\nbudget realization (landed latent points over base, and %% of budget spent past 95%% of cap):")
    def waste_frac(p):
        w = 0.0
        for k in SPEND_SKILLS:
            cap = p["caps"][k]; s = p["spend"][k]
            s95 = PRICE_TAU * 3.0  # spend at which the curve reaches ~95% of (cap-base)
            if s > s95:
                w += (s - s95)
        return w / p["budget"]
    decs = sorted(coh, key=lambda p: p["budget"])
    for dl, dh, lbl in ((0, n // 10, "bottom decile"), (n // 2 - n // 20, n // 2 + n // 20, "middle"), (9 * n // 10, n, "top decile")):
        for blo, bhi, blbl in ((40, 56, "small"), (57, 70, "mid"), (71, 99, "big")):
            sub = [p for p in decs[dl:dh] if blo <= p["Height"] <= bhi]
            if not sub: continue
            landed = sum(sum(p["latent"][k] for k in SPEND_SKILLS) - 22 * FLAT_BASE for p in sub) / len(sub)
            wf = sum(waste_frac(p) for p in sub) / len(sub)
            print(f"  {lbl:13s} {blbl:5s} n={len(sub):4d}  landed {landed:6.0f} pts  saturation-waste {100*wf:4.1f}%")

    # ---------- THE TABLE (Emmett's sign-off medium) ----------
    print("\n========== THE TABLE (real bodies; latent=ceiling, cur=current) ==========")
    def line_cols(p):
        s = rscore(p)
        return f"R={s:5.1f} line17:{'Y' if s >= R_LINE else 'n'} sel55.9:{'Y' if s >= sel_thresh else 'n'}"
    def row(tag, p, extra=""):
        L, C = p["latent"], p["current"]
        print(f"  {tag:28s} {ht_str(p['Height']):5s} {p['role']:10s} {p['dcat']:6s} bud={p['budget']:5.0f} "
              f"arr={p['arrival']:.2f}  tops[{fmt_tops(L)}] cur[{fmt_tops(C)}]  "
              f"PostD:{L['PostDefense']} Reb:{L['OffensiveRebounding']}/{L['DefensiveRebounding']}  {line_cols(p)} {extra}")

    def find(pred, key=lambda p: -p["budget"]):
        m = [p for p in coh if pred(p)]
        return sorted(m, key=key), len(m)

    bud_thresh = sorted((p["budget"] for p in coh), reverse=True)[int(0.03 * n)]
    print("--- the seven freak cards ---")
    m, cnt = find(lambda p: p["Height"] <= 47 and p["budget"] >= bud_thresh
                  and sorted((p["latent"][k] for k in FAMILIES["Creation"]), reverse=True)[1] >= 90)
    if m: row(f"elite little PG (n={cnt})", m[0])
    else: print(f"  elite little PG: NONE FOUND (finding to surface)")
    m, cnt = find(lambda p: p["latent"]["Outside"] >= 95
                  and max(p["latent"][k] for f in ("PerimDefense","InteriorDefense") for k in FAMILIES[f]) <= 25,
                  key=lambda p: (-p["latent"]["Outside"], max(p["latent"][k] for k in FAMILIES["PerimDefense"])))
    if m: row(f"catch-and-shoot freak (n={cnt})", m[0])
    else: print("  catch-and-shoot freak: NONE FOUND (finding to surface)")
    m, cnt = find(lambda p: p["Height"] >= 87 and p["latent"]["RimProtection"] >= 95
                  and max(p["latent"][k] for k in FAMILIES["Shooting"] + FAMILIES["Creation"]) <= 30)
    if m: row(f"all-defense blocker (n={cnt})", m[0])
    else: print("  all-defense blocker: NONE FOUND (finding to surface)")
    m, cnt = find(lambda p: p["Height"] >= 84 and p["latent"]["Outside"] >= 88, key=lambda p: -rscore(p))
    if m: row(f"7-foot pure shooter (n={cnt})", m[0], "(scratch anchor: 6/3470)")
    else: print("  7-foot pure shooter: NONE FOUND (finding to surface)")
    m, cnt = find(lambda p: p["Height"] <= 50 and p["latent"]["PerimeterDefense"] >= 90,
                  key=lambda p: -p["latent"]["PerimeterDefense"])
    if m: row(f"small lockdown stopper (n={cnt})", m[0], "(ruled: needs the handle he now has)")
    else: print("  small guards-up stopper: NONE FOUND (finding to surface)")
    m, cnt = find(lambda p: p["Height"] >= 60 and p["latent"]["PerimeterDefense"] >= 90
                  and p["current"]["BallHandling"] <= 40 and p["current"]["Outside"] <= 40,
                  key=lambda p: -rscore(p))
    if m: row(f"sized stopper, barely dribbles (n={cnt})", m[0], "(the ruled 6'5\" defender at the 3/4)")
    else: print("  sized stopper: NONE FOUND (finding to surface)")
    m, cnt = find(lambda p: p["Height"] <= 62 and p["role"] == "PostScorer" and p["latent"]["PostMoves"] >= 80)
    if m: row(f"undersized post scorer (n={cnt})", m[0], "(scratch anchor: 48 vs 6)")
    else: print("  undersized post scorer: NONE FOUND (finding to surface)")
    m, cnt = find(lambda p: 63 <= p["Height"] <= 68 and rscore(p) < R_LINE
                  and max(p["latent"][k] for k in FAMILIES["InteriorOffense"]) >= 55,
                  key=lambda p: -rscore(p))
    if m: row(f"D3 tweener, below line (n={cnt})", m[0])
    else: print("  D3 tweener: NONE FOUND (finding to surface)")

    print("--- the boring middle ---")
    med_b = sorted(p["budget"] for p in coh)[n // 2]
    m, _ = find(lambda p: p["role"] == "Creator" and abs(p["budget"] - med_b) < 40 and 51 <= p["Height"] <= 56,
                key=lambda p: abs(p["budget"] - med_b))
    if m: row("median-budget PG", m[0])
    m, _ = find(lambda p: p["role"] == "PostScorer" and p["Height"] >= 78 and p["latent"]["Outside"] <= 20,
                key=lambda p: -sum(p["latent"][k] for k in FAMILIES["InteriorOffense"] + FAMILIES["Rebounding"] + FAMILIES["InteriorDefense"]))
    if m: row("traditional center", m[0])
    m, _ = find(lambda p: p["role"] == "Connector" and 60 <= p["Height"] <= 68 and p["conc"] < 0.35
                and p["budget"] > med_b, key=lambda p: abs(p["budget"] - 1.25 * med_b))
    if m: row("balanced starter (low conc)", m[0])
    m, _ = find(lambda p: p["role"] == "Shooter" and abs(p["budget"] - med_b) < 40,
                key=lambda p: abs(p["budget"] - med_b))
    if m: row("median role shooter", m[0])
    m, _ = find(lambda p: p["budget"] < med_b * 0.6, key=lambda p: -p["budget"])
    if m: row("low-budget walk-on", m[0])
    m, _ = find(lambda p: p["role"] == "Slasher" and abs(p["budget"] - med_b) < 40 and 57 <= p["Height"] <= 65,
                key=lambda p: abs(p["budget"] - med_b))
    if m: row("median wing slasher", m[0])
    print("--- the anti-target row ---")
    softest = min(top347, key=lambda p: max(p["latent"].values()) - sorted(p["latent"].values())[1])
    row("top-347 closest-to-anti-target", softest, "(his holes must still be real)")

    print("\n" + ("!!! CHECK FAILURES: " + ", ".join(fails) if fails else "ALL ORACLE CHECKS OK"))
    return 1 if fails else 0

# ============================================================================
# S69 FIXTURE DUMP (--fixture [path]) -- the C# port's parity medium.
# Additive only: never touches the math above. Selection is BRANCH-REPRESENTATIVE
# (S69 prompt): every role x plane pairing, conc extremes, cap-binding interior
# spend, a near-epsilon pull floor, the short post-role and tall-shooter cards --
# then a deterministic stride pad to N_FIXTURE. Plus the inverse-CDF edge table:
# hand-picked uniforms at every cumulative height-bin boundary (boundary-eps /
# boundary / boundary+eps) so a '<' vs '<=' mismatch in a port dies loudly.
# ============================================================================
N_FIXTURE = 300
EDGE_EPS  = 1e-12

def _flat_draws(rec):
    """THE DRAW-ORDER CONTRACT, one home (imported by the replay checker; the C# live
    generator mirrors this order). (kind, value) per semantic slot, in stream order:
    height uniform -> wingspan gauss -> athletic-quality beta -> 7 ath gauss (ATH_KEYS)
    -> weight gauss -> def-plane gauss -> role uniform -> talent beta -> concentration
    beta -> 7 family-pull gauss (FAMILIES order) -> within-member gauss (family order,
    member order inside each family) -> 22 base-jitter gauss (SPEND_SKILLS) -> arrival
    gauss -> ft-idio gauss. 68 semantic slots."""
    out = [("random", rec["height_u"]), ("gauss", rec["ws_noise"]), ("beta", rec["a"])]
    out += [("gauss", rec["ath_noise"][k]) for k in ATH_KEYS]
    out += [("gauss", rec["weight_noise"]), ("gauss", rec["def_noise"]),
            ("random", rec["role_u"]), ("beta", rec["q"]), ("beta", rec["c"])]
    out += [("gauss", rec["pull_gauss"][f]) for f in FAMILIES]
    out += [("gauss", rec["within_gauss"][k]) for f, ks in FAMILIES.items() for k in ks]
    out += [("gauss", rec["base_jitter_gauss"][k]) for k in SPEND_SKILLS]
    out += [("gauss", rec["arrival_raw"]), ("gauss", rec["ft_idio"])]
    return out

class _ReplayR:
    """Replays recorded semantic draws in contract order; asserts each call KIND.
    A reordered or missing draw in a port dies here loudly, not in a soft moment check."""
    def __init__(self, flat):
        self.flat = list(flat); self.i = 0
    def _pop(self, kind):
        assert self.i < len(self.flat), f"over-consumed at slot {self.i} ({kind})"
        k, v = self.flat[self.i]
        assert k == kind, f"slot {self.i}: recorded kind {k}, generator asked for {kind}"
        self.i += 1
        return v
    def random(self):                 return self._pop("random")
    def gauss(self, mu, sigma):       return self._pop("gauss")
    def betavariate(self, a, b):      return self._pop("beta")
    def fully_consumed(self):         return self.i == len(self.flat)

def _constants_echo():
    return {
        "SEED": SEED, "N_CANDIDATE": N_CANDIDATE, "R_LINE": R_LINE,
        "HEIGHT_MARGINAL": {str(k): v for k, v in HEIGHT_MARGINAL.items()},
        "ATH_HEIGHT_CENTER": ATH_HEIGHT_CENTER, "SIZE_COEF": SIZE_COEF, "ATH_SIGMA": ATH_SIGMA,
        "ATH_BASE_LO": ATH_BASE_LO, "ATH_BASE_HI": ATH_BASE_HI, "ATHQ_A": ATHQ_A, "ATHQ_B": ATHQ_B,
        "ARR_READY": ARR_READY, "ARR_RAW": ARR_RAW, "ARRB_LO": ARRB_LO, "ARRB_HI": ARRB_HI,
        "ARR_SIGMA": ARR_SIGMA, "E_MIN": E_MIN, "EXPR_BASELINE": EXPR_BASELINE,
        "FT_CENTER": FT_CENTER, "FT_OUT_ANCHOR": FT_OUT_ANCHOR, "FT_OUT_SPAN": FT_OUT_SPAN,
        "FT_OUT_SCALE": FT_OUT_SCALE,
        "FT_HEIGHT_COEF": FT_HEIGHT_COEF, "FT_MIN": FT_MIN, "FT_MAX": FT_MAX, "FT_SIGMA": FT_SIGMA,
        "FAMILIES": {f: list(ks) for f, ks in FAMILIES.items()},
        "FAMILY_ORDER": list(FAMILIES.keys()), "SPEND_SKILLS": SPEND_SKILLS,
        "ATH_KEYS": ATH_KEYS, "ROLES": ROLES,
        "DEF_MID": DEF_MID, "DEF_STEEP": DEF_STEEP, "DEF_NOISE": DEF_NOISE,
        "ROLE_PRIOR": ROLE_PRIOR, "HFRAC_LO": HFRAC_LO, "HFRAC_HI": HFRAC_HI,
        "PERIM_DECAY": PERIM_DECAY, "POST_DECAY": POST_DECAY,
        "TALENT_A": TALENT_A, "TALENT_B": TALENT_B,
        "BUDGET_LO": BUDGET_LO, "BUDGET_SPAN": BUDGET_SPAN, "BUDGET_POW": BUDGET_POW,
        "CONC_A": CONC_A, "CONC_B": CONC_B,
        "PULL_EPS": PULL_EPS, "PULL_DICE_SIGMA": PULL_DICE_SIGMA,
        "ROLE_FAM_PREF": ROLE_FAM_PREF, "GLUE_PREF": GLUE_PREF,
        "WITHIN_PREF": WITHIN_PREF, "WITHIN_DICE_SIGMA": WITHIN_DICE_SIGMA,
        "GAMMA_LO": GAMMA_LO, "GAMMA_HI": GAMMA_HI,
        "FLAT_BASE": FLAT_BASE, "BASE_JITTER": BASE_JITTER,
        "IDCAP_LO_H": IDCAP_LO_H, "IDCAP_HI_H": IDCAP_HI_H,
        "IDCAP_MIN": IDCAP_MIN, "REBCAP_MIN": REBCAP_MIN, "PRICE_TAU": PRICE_TAU,
        "HF_LO": HF_LO, "HF_HI": HF_HI, "HF_RANGE": HF_RANGE, "HF_STEEP": HF_STEEP, "HF_MID": HF_MID,
        "LOW_TAPER_FLOOR": LOW_TAPER_FLOOR, "LOW_TAPER_TOP": LOW_TAPER_TOP,
        "PATHWAY_W_FLOOR": PATHWAY_W_FLOOR,
        "WS_NOISE_MEAN": 4.0, "WS_NOISE_SIGMA": 3.0, "WEIGHT_NOISE_SIGMA": 6.0,
    }

def _edge_table():
    """Every cumulative boundary probed at boundary-eps / boundary / boundary+eps,
    expected height computed through height_from_u itself (the transform of record)."""
    rows = []
    for cum, h in HEIGHT_CDF:
        for u in (cum - EDGE_EPS, cum, cum + EDGE_EPS):
            if 0.0 <= u <= 1.0:
                rows.append({"u": u, "expected_height": height_from_u(u)})
    rows.append({"u": 0.0, "expected_height": height_from_u(0.0)})
    return rows

def _checkpoints(p):
    parts = rscore_parts(p)
    return {
        "Height": p["Height"], "Wingspan": p["Wingspan"], "Weight": p["Weight"],
        "ath": p["ath"], "dplane": p["dplane"], "dcat": p["dcat"], "role": p["role"],
        "budget": p["budget"], "gamma": p["gamma"],
        "pulls": p["pulls"], "fam_share": p["fam_share"], "spend": p["spend"], "caps": p["caps"],
        "latent": p["latent"], "current": p["current"],
        "latent_ft": p["latent_ft"], "current_ft": p["current_ft"],
        "arrival": p["arrival"], "e": p["e"], "runway_total": p["runway_total"],
        "rscore": parts["rscore"], "rscore_which": parts["which"],
    }

def dump_fixture(path):
    import json
    r = random.Random(SEED)
    cohort = []
    for i in range(N_CANDIDATE):
        rec = {}
        p = generate_player(r, rec)
        cohort.append((i, p, rec))

    chosen, tags = [], {}
    def take(i, tag):
        if i not in tags:
            tags[i] = []
            chosen.append(i)
        tags[i].append(tag)

    # every role x plane pairing
    for ro in ROLES:
        for dc in ("PerimD", "WingD", "PostD"):
            for i, p, _ in cohort:
                if p["role"] == ro and p["dcat"] == dc:
                    take(i, f"pair:{ro}x{dc}"); break
    # concentration extremes
    hi = [i for i, p, _ in cohort if p["conc"] >= 0.92][:3]
    lo = [i for i, p, _ in cohort if p["conc"] <= 0.08][:3]
    for i in hi: take(i, "conc-high")
    for i in lo: take(i, "conc-low")
    # cap-binding interior spend on a small body (saturated toward a sub-99 cap)
    nb = [i for i, p, _ in cohort if p["Height"] < IDCAP_HI_H
          and (p["latent"]["PostDefense"] >= p["caps"]["PostDefense"] - 0.5
               or p["latent"]["RimProtection"] >= p["caps"]["RimProtection"] - 0.5)][:3]
    for i in nb: take(i, "cap-binding")
    # near-epsilon pull (the max(PULL_EPS, .) floor fired)
    ne = [i for i, p, _ in cohort if min(p["pulls"].values()) == PULL_EPS][:3]
    for i in ne: take(i, "pull-floor")
    # the ruled edge cards
    sp = [i for i, p, _ in cohort if p["Height"] <= 56 and p["role"] == "PostScorer"][:3]
    ts = [i for i, p, _ in cohort if p["Height"] >= 80 and p["role"] == "Shooter"][:3]
    for i in sp: take(i, "short-post-role")
    for i in ts: take(i, "tall-shooter")
    # deterministic stride pad to N_FIXTURE
    stride = max(1, N_CANDIDATE // (N_FIXTURE - len(chosen) + 1))
    i = 0
    while len(chosen) < N_FIXTURE and i < N_CANDIDATE:
        if i not in tags:
            take(i, "stride")
        i += stride

    players = []
    for i in sorted(chosen):
        _, p, rec = cohort[i]
        players.append({"index": i, "tags": tags[i], "draws": rec, "checkpoints": _checkpoints(p)})

    # SYNTHETIC pull-floor row (S69 finding: at the locked constants the epsilon floor
    # needs a ~-7-sigma dice draw and NO real cohort player reaches it -- so the branch
    # gets a hand-built draws row, checkpointed through generate_player itself).
    syn = {k: (dict(v) if isinstance(v, dict) else v) for k, v in cohort[0][2].items()}
    syn["pull_gauss"]["Glue"] = -9.0          # fires max(PULL_EPS, ...) at the family stage
    syn["within_gauss"]["Screening"] = -9.0   # fires max(PULL_EPS, ...) at the member stage
    rr = _ReplayR(_flat_draws(syn))
    ps = generate_player(rr)
    assert rr.fully_consumed(), "synthetic row: draw-count contract violated"
    assert min(ps["pulls"].values()) == PULL_EPS, "synthetic row failed to fire the pull floor"
    players.append({"index": -1, "tags": ["synthetic:pull-floor"], "draws": syn,
                    "checkpoints": _checkpoints(ps)})

    draw_order = (["height_u", "ws_noise", "a"]
                  + [f"ath_noise.{k}" for k in ATH_KEYS]
                  + ["weight_noise", "def_noise", "role_u", "q", "c"]
                  + [f"pull_gauss.{f}" for f in FAMILIES]
                  + [f"within_gauss.{k}" for k in SPEND_SKILLS]     # family order, member order inside
                  + [f"base_jitter_gauss.{k}" for k in SPEND_SKILLS]
                  + ["arrival_raw", "ft_idio"])                     # 68 semantic slots
    fx = {"schema": {"schema_version": "s69-1", "seed": SEED, "n_cohort": N_CANDIDATE,
                     "n_players": len(players), "float_tolerance": 1e-9,
                     "draw_order": draw_order,
                     "key_orders": {"ATH_KEYS": ATH_KEYS, "SPEND_SKILLS": SPEND_SKILLS,
                                    "FAMILY_ORDER": list(FAMILIES.keys()), "ROLES": ROLES},
                     "constants": _constants_echo(),
                     "note": "S69 branch-representative replay fixture, regenerated at S71 under the "
                             "re-anchored FT derivation (FT_OUT_ANCHOR named; 72 constants echoed); "
                             "draws recorded by the output-neutral recorder seam; ints EXACT, floats 1e-9."},
          "edge_table": _edge_table(), "players": players}
    with open(path, "w") as f:
        json.dump(fx, f, indent=1)
    counts = Counter(t for ts_ in tags.values() for t in ts_ if not t.startswith("pair:"))
    npair = sum(1 for ts_ in tags.values() for t in ts_ if t.startswith("pair:"))
    print(f"fixture written: {path}  players={len(players)}  role-x-plane pairs={npair}  "
          f"special={dict(counts)}  edge rows={len(_edge_table())}")
    return 0

if __name__ == "__main__":
    if "--fixture" in sys.argv[1:]:
        args = sys.argv[1:]
        j = args.index("--fixture")
        out = args[j + 1] if j + 1 < len(args) else "tools/gen_pass3_replay_fixture_s69.json"
        sys.exit(dump_fixture(out))
    sys.exit(run(sys.argv[1:]))
