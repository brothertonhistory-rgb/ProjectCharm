#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_pass2_skillfirst_oracle.py  --  Player-Generation Pass 2, SKILL-FIRST model.

DESIGN MEDIUM / SPEC ORACLE.  This is Python that proves the generation math before
any C# is written.  It is deterministic (seed 20260706).  It emits an honest candidate
cohort and the recruitable tier; the WORLD/ENGINE decides viability, never the generator.
No gate, no repair, no floor, no redraw, no rejection.  Everyone generated ships as drawn.

Authoritative model (each is stated, not implied):

  THREE INDEPENDENT QUALITY DRAWS (quality never touches Height):
    orientation o in [0,1]  (0 = pure perimeter, 1 = pure post) ~ Beta(mean,conc)
    skill quality q in [0,1] (latent skill CEILING level)       ~ Beta
    athletic quality a in [0,1] ("how athletic")                ~ Beta
    specialization s in [0,1] (broad -> pure spike)             ~ Beta

  THREE CAUSAL CONDITIONINGS (the only dependencies, all real basketball):
    orientation -> Height ceiling : perimeter Height sits low & front-loaded, a hard
        cliff after 6'7"; post Height uncapped (reaches 7'3"+); hybrid interpolates.
        Modeled as an asymmetric-exponential-tail draw whose location MU(o) and
        upper-tail scale SCALE_UP(o) interpolate continuously in o.  Quality does NOT enter.
    size -> athleticism : Strength rises with Height (+coef); burst/quickness eases with
        Height (-coef) but with a WIDE tail so 6'9"+ freaks exist; Vertical ~flat.
    orientation -> arrival/runway : perimeter arrives DEVELOPED (short runway, thin
        raw-guard tail); post arrives RAW (long runway, fundable project); hybrid between.

  POTENTIAL AXIS (baked):
    latent skill map drawn from (o, q, s).  Arrival stage yields expression e in [E_MIN,1].
    current = baseline + e*(latent - baseline)  (if latent<baseline, current=latent;
    expression never raises).  Holes barely move; strengths suppressed proportionally.
    Applies to the SKILL card ONLY; size + athletic ratings bypass (drawn at current value).
    FreeThrow is DERIVED (f of Outside+Height+idiosyncrasy): ONE persistent per-player
    idiosyncrasy draw (gauss(0,FT_SIGMA)) feeds BOTH the latent-FT (from latent Outside) and
    current-FT (from current Outside) derivations -- a shooter-specific trait, so identical
    inputs no longer give identical FT and the oddball tails (the skilled low-FT hitch, the
    weak-shooting big who's automatic at the line) exist.  Runway = full per-skill
    (latent-current) vector (21 skills incl. FT) + one summary.

  WEAPON SELECTION (S42.1): the identity weapon is the argmax of base[k] + census offset
    over the eligible set -- the strongest eligibility-CORRECTED candidate.  The offsets
    (WEAPON_CENSUS_OFFSET, the ruled table) correct the S42 census artifact where universally-
    eligible skills won disproportionately (multiple-comparisons + PAXIS lean); they shift the
    argmax comparison only, never the card math.  Proven by the [F3] paired-counterfactual
    census: both rules applied to identical pre-weapon bases and eligibility sets.

  AGE/CLASS IS A PLACEHOLDER projection of arrival.  The season layer owns the real
    population structure, the one-class-vs-standing-pool question, and the ready-freshman
    existence requirement.  Do not port the age/class labels as spec; arrival is the ruled
    mechanism, the labels are decoration on it.

The generated Height value is an abstract 0-99 attribute, NOT inches.  All audits use the
authoritative bin table (see HEIGHT_BINS).  inches ~= 68 + 0.36*(Height-40) is display only.
"""

import random
import math
import json      # S42.2: fixture-dump mode only (pure stdlib; the dump routine does not port)
import os        # S42.2: fixture path resolution (written beside this script, CWD-independent)
import sys       # S42.2: the --fixture argv switch
from collections import defaultdict, OrderedDict

# ============================================================================
# THE FIVE DIALS  (these are what change how players FEEL; tune with Emmett)
# ============================================================================

# --- Dial 1: 60/40 orientation lean (how hard the world tilts guard-heavy) ---
ORI_MEAN = 0.446        # Beta mean; tuned so P(o<0.5) ~= 0.60 (perimeter share)
ORI_CONC = 4.5          # Beta concentration = "hybrid density" (mass near 0.5 center)

# --- Dial 2: perimeter Height cliff (where perimeter mass sits, how hard it drops) --
# The orientation->Height ceiling is a LOGISTIC in o: near-flat-and-low across the whole
# perimeter range, rising steeply through the hybrid zone, near-flat-and-high for post.
# This is what keeps perimeter players short (2-3 reach 6'10"-7'0" in the pool) while a
# true hybrid (o~=0.5) can still, rarely, be a 7-footer (Wembanyama).
HT_ORI_MID    = 0.54    # o at which the ceiling is halfway up (pushed toward post: tall => post-lean)
HT_ORI_STEEP  = 15.0    # steepness of the knee (higher = harder perimeter/post separation)
HT_MU_PERIM   = 53.0    # perimeter Height location  (~5'11"/6'0")
HT_MU_POST    = 72.0    # post Height location       (~6'8")
HT_SIGMA_UP_PERIM = 7.6 # perimeter upper-tail Gaussian sigma -> the CLIFF (small = hard drop)
HT_SIGMA_UP_POST  = 6.0 # post upper-tail Gaussian sigma (bell tail: 7'0" present, 7'3"+ near-miracle)
HT_SCALE_DOWN     = 7.0  # lower-tail scale, both orientations (allows short players)
HT_MIN            = 40.0
HT_MAX            = 99.0

# --- Dial 3: size -> athleticism coefficients (start values +0.42/-0.22/sigma7) ------
ATH_HEIGHT_CENTER = 60.0
SIZE_COEF = {           # rating-points per Height-point away from center
    "Strength": +0.42, "Speed": -0.22, "Quickness": -0.22, "FirstStep": -0.22,
    "Vertical": -0.02, "Endurance": -0.06, "Hustle": 0.00,
}
ATH_SIGMA = {           # per-attribute spread; burst=7 IS the freak-tail width
    "Strength": 6.0, "Speed": 7.0, "Quickness": 7.0, "FirstStep": 7.0,
    "Vertical": 8.0, "Endurance": 6.0, "Hustle": 8.0,
}
ATH_BASE_LO, ATH_BASE_HI = 25.0, 85.0   # maps athletic-quality a in [0,1] -> center rating

# --- Dial 4: runway skew along the bend (perimeter developed vs post raw) ------------
ARR_PERIM = 0.72        # mean arrival for pure perimeter (near-ready)
ARR_POST  = 0.42        # mean arrival for pure post (raw project)
ARR_SIGMA = 0.18        # arrival spread; controls raw-guard tail thickness
E_MIN     = 0.15        # minimum expression (a totally raw player expresses this fraction)
EXPR_BASELINE = 14.0    # rating a raw skill decays toward (holes already near it)
AGE_ARR_SPAN  = 4.0     # age = 18 + AGE_ARR_SPAN*arrival + noise
AGE_NOISE     = 0.95    # so a raw senior / ready frosh can exist

# --- Dial 5: confluence rarity (upper-tail cuts for the two rarest confluences) ------
LEBRON_Q_PCT, LEBRON_SZ_PCT, LEBRON_A_PCT = 0.970, 0.850, 0.970   # elite skill + elite ath + big wing
WEMBY_SZ_PCT, WEMBY_Q_PCT, WEMBY_RES_PCT  = 0.985, 0.800, 0.965   # size hi, skill ok, high-for-size

# ============================================================================
# FROZEN (NOT dials): population sizing.  Calibrated once, then frozen.
# ============================================================================
N_CANDIDATE = 46000     # near-college candidate cohort (NOT the ~86% HS substrate)
R_LINE      = 17.0      # recruiting-line threshold on the declared RScore below
# Post-pathway height-ACCESS curve (logistic): how efficiently an interior SKILL converts to
# roster value by height. Inflection HF_MID~6'2 -> 5'8 strands (~0.34), 6'1 borderline (~0.59),
# 6'6 neutral (~1.09), 6'8+ amplified. Rebounding stays OUT of this (already height-encoded).
HF_LO    = 0.20        # floor: a very short interior rating cashes almost nothing
HF_HI    = 1.45        # ceiling: extreme height amplifies a REAL interior tool
HF_RANGE = 1.25        # HF_HI - HF_LO
HF_STEEP = 0.13        # logistic steepness
HF_MID   = 59.0        # inflection height (~6'2) -- the access transition band
# Orientation-weighted pathway SELECTION: each pathway is weighted by the player's orientation
# BEFORE the max, so a perimeter-leaning card cannot clear on a post-only spike (or vice versa)
# unless it is genuinely hybrid. Continuous, not a role table.
PERIM_OW = 0.45        # perim_weight = 1.00 - PERIM_OW * o   (full at o=0, reduced for post players)
POST_OW  = 0.45        # post_weight  = (1-POST_OW) + POST_OW * o  (full at o=1, reduced for perimeter)
# Sub-6'0 interior taper: an extraordinary post skill on a very short body does not convert into a
# college pathway. Continuous ramp from ~5'8 up to 6'0 (H51); at 6'0+ it is 1.0 (no effect), so the
# 6'1 borderline and 6'8 raw-big cases are untouched. Not a hard "no short post" rule.
LOW_TAPER_FLOOR = 0.10  # floor at the shortest heights (interior game barely converts)
LOW_TAPER_TOP   = 51.0  # height (~6'0) at/above which the taper is fully released

# ============================================================================
# FROZEN: skill-quality / specialization / athletic-quality draw shapes
# ============================================================================
SKILL_Q_A, SKILL_Q_B = 2.3, 2.7   # gentle top-heavy slope (elite skill rarer)
SPEC_A,    SPEC_B     = 2.0, 2.0   # broad <-> spike spread
ATHQ_A,    ATHQ_B     = 2.2, 2.2   # athletic-quality spread

# ============================================================================
# FROZEN: latent-skill construction constants
# ============================================================================
MISMATCH_STRENGTH = 0.85   # how hard an opposite-axis skill is suppressed (0..1 units)
SKILL_NOISE       = 0.12   # per-skill idiosyncrasy within a player (0..1 units)
# Specialization is a TRUE CHOSEN-WEAPON mechanism (not a cluster contrast): after
# orientation sets the legal skill family, ONE skill is the weapon. As s rises the
# weapon is bumped and every OTHER skill drains -> broad at low s, one-weapon at high s.
WEAPON_BUMP       = 0.62   # weapon lift at s=1 (in 0..1 units); lets a low-q specialist carry one elite skill
SUPPORT_DRAIN     = 0.42   # how far the non-weapon skills drain at s=1 (in 0..1 units)
WEAPON_MISMATCH_MAX = 0.30 # a skill is weapon-eligible only if its orientation mismatch is below this
WEAPON_EXCLUDE = ("BasketballIQ", "Discipline", "HelpDefense")  # glue skills are never the "weapon"
# S42.1: weapon-census offsets -- the argmax correction (the ruled table).
# The S42 rule (natural-best argmax over the eligible set) had a census artifact: universally-
# eligible skills (Mid, FoulDrawing, OffBall*) get more chances to be the maximum (a multiple-
# comparisons effect), and PAXIS lean shifts each skill's expected base by orientation -- so the
# most common identity in the universe was "mid-range specialist" (8.9%) while post-play identity
# (4.2%) ran rarer than off-ball-defense identity (7.0%). These offsets are added to base[k]
# INSIDE THE ARGMAX ONLY (weapon_score[k] = base[k] + offset[k]); base[k] and every piece of
# downstream card math are untouched -- the weapon's bump and the support drain still apply to
# the true base. The selected identity is the strongest eligibility-CORRECTED candidate; after
# offsets it may not be the highest raw base[k] -- that is the accepted tradeoff, stated honestly.
# Initialized from measured bias and tuned against the [F3] paired-counterfactual census table;
# the AFTER table, not any formula claim, is the only proof they worked. Default target ruled at
# the table: near-flat identity census across the 17 weapon-eligible skills (~5.9% each).
WEAPON_CENSUS_OFFSET = {
    "Close": +0.013, "Mid": -0.030, "Outside": -0.018, "Finishing": +0.018, "FoulDrawing": -0.011,
    "BallHandling": -0.006, "Passing": -0.012, "Playmaking": -0.005, "SelfCreation": -0.008,
    "PostMoves": +0.032, "OffBallMovement": -0.017, "Screening": +0.023, "PerimeterDefense": -0.005,
    "PostDefense": +0.027, "RimProtection": +0.025, "Steals": -0.010, "OffBallDefense": -0.014,
}
RATING_LO         = 18.0   # t=0 -> ~18
RATING_SPAN       = 70.0   # t=1 -> ~88
HOLE_FLOOR        = 8       # rating-DOMAIN lower bound (the abstract 0-99 scale's floor, applied at
                           # draw time); NOT a post-draw realism floor -- no card is inspected and lifted.

# FreeThrow derivation (mirrors the live "good shooters shoot better FTs" shape)
FT_CENTER = 66.0; FT_OUT_SPAN = 10.0; FT_OUT_SCALE = 25.0
FT_HEIGHT_COEF = 6.0; FT_MIN = 25.0; FT_MAX = 95.0
# S42.1: per-player FT idiosyncrasy -- ONE shared draw per player (r.gauss(0, FT_SIGMA)),
# added inside derive_ft for BOTH the latent-FT and current-FT derivations (same value in both:
# a persistent shooter-specific trait, not a second development axis). Restores the Session 29
# ruling ("skilled players usually shoot better FTs, with real oddballs in the tails") -- before
# this, identical Outside+Height gave identical FT every time and the tails did not exist.
# FT_SIGMA is the dial ruled at the [F4] tables.
FT_SIGMA = 9.0

# ============================================================================
# ATTRIBUTE TAXONOMY  (the 33-key contract the generator must emit)
# ============================================================================
SIZE_KEYS  = ["Height", "Wingspan", "Weight", "OffensiveRebounding", "DefensiveRebounding"]
ATH_KEYS   = ["Strength", "Speed", "Quickness", "FirstStep", "Vertical", "Endurance", "Hustle"]
SKILL_KEYS = ["Close", "Mid", "Outside", "Finishing", "FreeThrow", "FoulDrawing",
              "BallHandling", "Passing", "Playmaking", "SelfCreation", "PostMoves",
              "OffBallMovement", "Screening", "PerimeterDefense", "PostDefense",
              "RimProtection", "Steals", "HelpDefense", "OffBallDefense",
              "BasketballIQ", "Discipline"]
ALL_KEYS = SIZE_KEYS + ATH_KEYS + SKILL_KEYS   # 5 + 7 + 21 = 33
assert len(ALL_KEYS) == 33 and len(set(ALL_KEYS)) == 33

# The 20 skills that go through the expression transform (FreeThrow is derived separately)
DRAWN_SKILLS = [k for k in SKILL_KEYS if k != "FreeThrow"]
assert len(DRAWN_SKILLS) == 20

# Perimeter<->post axis of each drawn skill: -1 pure perimeter, +1 pure post, 0 universal
PAXIS = {
    "Close": +0.30, "Mid": 0.00, "Outside": -0.15, "Finishing": +0.40,
    "FoulDrawing": +0.10, "BallHandling": -0.70, "Passing": -0.30, "Playmaking": -0.60,
    "SelfCreation": -0.60, "PostMoves": +0.90, "OffBallMovement": -0.20, "Screening": +0.60,
    "PerimeterDefense": -0.70, "PostDefense": +0.80, "RimProtection": +0.80, "Steals": -0.40,
    "HelpDefense": 0.00, "OffBallDefense": -0.20, "BasketballIQ": 0.00, "Discipline": 0.00,
}
# NOTE: shooting (Outside -0.15, Mid 0.00) is near-orientation-neutral ON PURPOSE -- bigs shoot,
# so "tall + jumper" (stretch big) is common. Ball-handling/creation/playmaking stay strongly
# perimeter (-0.6..-0.7), so "tall + guard handle" (point-center) stays rare. Both hold together.
assert set(PAXIS) == set(DRAWN_SKILLS)

# ============================================================================
# HEIGHT BIN TABLE (authoritative audit currency)
# ============================================================================
HEIGHT_BINS = OrderedDict([
    ("5'8-5'9",   (40, 44)),
    ("5'10-5'11", (45, 50)),
    ("6'0-6'1",   (51, 56)),
    ("6'2-6'5",   (57, 65)),
    ("6'6-6'7",   (66, 70)),
    ("6'8-6'9",   (71, 79)),
    ("6'10-7'0",  (80, 86)),   # <-- the perimeter-cliff audit bin
    ("7'1-7'2",   (87, 92)),
    ("7'3+",      (93, 99)),
])
def height_bin(h):
    for name, (lo, hi) in HEIGHT_BINS.items():
        if lo <= h <= hi:
            return name
    return "7'3+" if h > 99 else "<5'8(OOB)"  # HT_MIN=40 guarantees this never triggers; explicit if it ever does

def clamp(x, lo, hi):
    return lo if x < lo else (hi if x > hi else x)

# ============================================================================
# SKILL-STATE CONSTRUCTION  (S42.1: factored out of generate_player so the [F3]
# paired-counterfactual census can rebuild the SAME player's card under the
# alternative weapon choice -- identical inputs, two selection rules)
# ============================================================================
def derive_ft(outside, ft_idio, height):
    # ft_idio is the player's ONE persistent idiosyncrasy draw; the SAME value feeds both the
    # latent-FT and current-FT calls, so the runway carries no INDEPENDENT measurement noise
    # between the two states -- it is a trait, not development. (At the [FT_MIN, FT_MAX] clamp
    # edges the shared term can still shift the FT runway entry by a point or two; the claim is
    # no independent noise, not perfect cancellation.) Clamped as always.
    val = (FT_CENTER + FT_OUT_SPAN * math.tanh((outside - 50.0) / FT_OUT_SCALE)
           - FT_HEIGHT_COEF * ((height - 55.0) / 40.0) + ft_idio)
    return int(clamp(round(val), FT_MIN, FT_MAX))

def build_skill_state(base, weapon, s, e, height, ft_idio):
    """latent / current / runway for a GIVEN weapon choice. Everything here is deterministic
    in its inputs (no RNG), which is what makes the [F3] counterfactual an honest pairing."""
    latent = {}
    for k in DRAWN_SKILLS:
        t = base[k] + (s * WEAPON_BUMP if k == weapon else -s * SUPPORT_DRAIN)
        latent[k] = int(clamp(round(RATING_LO + t * RATING_SPAN), HOLE_FLOOR, 99))
    current = {}
    for k in DRAWN_SKILLS:
        L = latent[k]
        if L <= EXPR_BASELINE:
            current[k] = L
        else:
            current[k] = int(round(EXPR_BASELINE + e * (L - EXPR_BASELINE)))
    latent["FreeThrow"]  = derive_ft(latent["Outside"], ft_idio, height)
    current["FreeThrow"] = derive_ft(current["Outside"], ft_idio, height)
    runway = {k: latent[k] - current[k] for k in SKILL_KEYS}
    return latent, current, runway, sum(runway.values())

# ============================================================================
# THE GENERATOR  --  one honest player, column by column
# ============================================================================
def generate_player(r, rec=None):
    # S42.2 recording seam: `rec` is an optional plain dict this function writes RAW DRAWS and
    # non-returned intermediates INTO, strictly AFTER each draw is made and assigned -- the
    # recorder draws no RNG and returns nothing the math reads, so rec=None (the default, and
    # every audit path) is byte-for-byte today's behavior. Proof is the §0.1 output diff.
    # ---- 1. three independent quality draws + specialization (quality never touches Height)
    # orientation ~ Beta(mean,conc); a=mean*conc, b=(1-mean)*conc
    o = r.betavariate(ORI_MEAN * ORI_CONC, (1.0 - ORI_MEAN) * ORI_CONC)
    q = r.betavariate(SKILL_Q_A, SKILL_Q_B)     # latent skill CEILING level
    a = r.betavariate(ATHQ_A, ATHQ_B)           # athletic quality
    s = r.betavariate(SPEC_A, SPEC_B)           # specialization (broad->spike)
    oaxis = 2.0 * o - 1.0                        # -1 perimeter .. +1 post
    if rec is not None:
        rec["o"], rec["q"], rec["a"], rec["s"] = o, q, a, s
        rec["skill_noise"], rec["ath_noise"], rec["ath_raw"] = {}, {}, {}

    # ---- 2. orientation -> Height (logistic ceiling in o; quality absent) ------------
    oh       = 1.0 / (1.0 + math.exp(-HT_ORI_STEEP * (o - HT_ORI_MID)))
    mu       = HT_MU_PERIM + oh * (HT_MU_POST - HT_MU_PERIM)
    sigma_up = HT_SIGMA_UP_PERIM + oh * (HT_SIGMA_UP_POST - HT_SIGMA_UP_PERIM)
    # S42.2: the two inline draws are hoisted to named locals so the recorder can capture them
    # after assignment (x = f(); use(x) is identical to use(f()); the RNG call order is unchanged).
    h_sel = r.random()
    if h_sel < 0.5:
        h_noise  = r.gauss(0.0, sigma_up)
        h_branch = "upper_gauss"
        h_raw    = mu + abs(h_noise)              # Gaussian upper tail: kills 7'3"+ excess
    else:
        h_noise  = r.expovariate(1.0 / HT_SCALE_DOWN)
        h_branch = "lower_exp"
        h_raw    = mu - h_noise
    Height = int(round(clamp(h_raw, HT_MIN, HT_MAX)))
    if rec is not None:
        rec["height_branch_selector_raw"] = h_sel
        rec["height_branch"]              = h_branch
        rec["height_noise_raw"]           = h_noise   # upper: pre-abs gauss; lower: the expovariate value
        rec["oh"], rec["mu"], rec["sigma_up"], rec["h_raw"] = oh, mu, sigma_up, h_raw

    # ---- 3. LATENT skill card from (o, q, s) -- body-blind ---------------------------
    # Orientation suppresses opposite-axis skills (-> holes). Specialization is a chosen
    # weapon: among the legal (aligned/neutral) family, the natural-best skill becomes the
    # weapon, bumped by s; every other skill drains by s. Low s -> broad; high s -> spike.
    base = {}
    for k in DRAWN_SKILLS:
        mismatch = max(0.0, -oaxis * PAXIS[k])          # opposite-axis suppression 0..1
        supp = MISMATCH_STRENGTH * mismatch
        sk_noise = r.gauss(0.0, SKILL_NOISE)            # S42.2: hoisted for the recorder; same call, same order
        if rec is not None:
            rec["skill_noise"][k] = sk_noise
        base[k] = q - supp + sk_noise
    # weapon (S42.1): the identity is the strongest ELIGIBILITY-CORRECTED candidate --
    # argmax of base[k] + WEAPON_CENSUS_OFFSET[k] over the eligible set. Offsets shift the
    # argmax comparison ONLY; base[k] and all downstream card math are untouched.
    # weapon_raw (the uncorrected S42 natural-best rule) is kept SOLELY for the [F3]
    # paired-counterfactual census -- no card math reads it.
    eligible = [k for k in DRAWN_SKILLS
                if k not in WEAPON_EXCLUDE and max(0.0, -oaxis * PAXIS[k]) < WEAPON_MISMATCH_MAX]
    pool = eligible if eligible else DRAWN_SKILLS
    if rec is not None:
        rec["eligible"] = list(eligible)   # never empty: Mid (PAXIS 0.0, not excluded) is always eligible
    weapon_raw = max(pool, key=lambda k: base[k])
    weapon     = max(pool, key=lambda k: base[k] + WEAPON_CENSUS_OFFSET.get(k, 0.0))
    # (latent/current/FT/runway are built AFTER arrival + the FT idiosyncrasy draw, via
    #  build_skill_state -- construction is RNG-free, so moving it does not shift the stream)

    # ---- 4. size card (bypasses expression; drawn at current value) ------------------
    ws_noise = r.gauss(4.0, 3.0)                        # S42.2: hoisted for the recorder (mean 4.0 INCLUDED in the drawn value)
    if rec is not None:
        rec["wingspan_noise"] = ws_noise
    Wingspan = int(clamp(round(Height + ws_noise), HT_MIN, 99))
    # athletic card (bypasses expression)
    ath = {}
    for k in ATH_KEYS:
        acenter = ATH_BASE_LO + a * (ATH_BASE_HI - ATH_BASE_LO)   # (renamed from `base` in S42.1:
        a_noise = r.gauss(0.0, ATH_SIGMA[k])                      #  the skill base dict now lives past this loop)
        val = acenter + SIZE_COEF[k] * (Height - ATH_HEIGHT_CENTER) + a_noise
        if rec is not None:
            rec["ath_center"]      = acenter        # same value every iteration; recorded once semantically
            rec["ath_noise"][k]    = a_noise
            rec["ath_raw"][k]      = val            # pre-round/pre-clamp checkpoint
        ath[k] = int(clamp(round(val), 8, 99))
    wt_noise = r.gauss(0, 6)                            # S42.2: three hoists; draw order weight -> OREB -> DREB unchanged
    Weight = int(clamp(round(30 + 0.40 * Height + 0.30 * ath["Strength"] + wt_noise), 20, 99))
    # rebounding lives on the size card (physical; cashes now even for a raw project)
    post_bonus = 8.0 * o
    oreb_noise = r.gauss(0, 7)
    OREB = int(clamp(round(20 + 0.34 * Height + 0.14 * ath["Strength"] + post_bonus + oreb_noise), 8, 99))
    dreb_noise = r.gauss(0, 7)
    DREB = int(clamp(round(22 + 0.36 * Height + 0.18 * ath["Strength"] + post_bonus + dreb_noise), 8, 99))
    if rec is not None:
        rec["weight_noise"], rec["oreb_noise"], rec["dreb_noise"] = wt_noise, oreb_noise, dreb_noise

    # ---- 5. arrival stage + expression transform (SKILL card only) -------------------
    arr_mean = ARR_PERIM - o * (ARR_PERIM - ARR_POST)
    arr_draw = r.gauss(arr_mean, ARR_SIGMA)             # S42.2: hoisted -- the clamp DESTROYS this raw value when it binds
    arrival = clamp(arr_draw, 0.0, 1.0)
    e = E_MIN + arrival * (1.0 - E_MIN)
    if rec is not None:
        rec["arrival_draw_raw"], rec["arr_mean"] = arr_draw, arr_mean

    # ---- FT idiosyncrasy (S42.1): ONE persistent shooter-specific draw per player ----
    # The SAME value feeds both derive_ft calls inside build_skill_state (latent-FT and
    # current-FT). Two independent draws would give one player two FT identities and pollute
    # the runway vector with measurement noise instead of development.
    ft_idio = r.gauss(0.0, FT_SIGMA)
    if rec is not None:
        rec["ft_idio"] = ft_idio

    # ---- latent / current / FT / runway (RNG-free construction; see build_skill_state)
    latent, current, runway, runway_total = build_skill_state(base, weapon, s, e, Height, ft_idio)

    # ---- 7. class / age -- PLACEHOLDER (S42.1 label; correlated with arrival) ---------
    # Age/class is a placeholder projection of arrival. The season layer owns the real
    # population structure, the one-class-vs-standing-pool question, and the ready-freshman
    # existence requirement. Do NOT port the age/class labels as spec; arrival is the ruled
    # mechanism, the labels are decoration on it.
    age_noise = r.gauss(0, AGE_NOISE)                   # S42.2: hoisted; recorded as PLACEHOLDER-OUTPUT
    if rec is not None:                                 # (S42.1 ruling: age/class do not port as spec)
        rec["age_noise_raw"] = age_noise
    age = int(clamp(round(18 + AGE_ARR_SPAN * arrival + age_noise), 17, 23))
    cls = "Fr" if age <= 18 else ("So" if age == 19 else ("Jr" if age <= 21 else "Sr"))

    # ---- assemble the 33-key card (current) ------------------------------------------
    card = {"Height": Height, "Wingspan": Wingspan, "Weight": Weight,
            "OffensiveRebounding": OREB, "DefensiveRebounding": DREB}
    card.update(ath)
    for k in SKILL_KEYS:
        card[k] = current[k]

    return {
        "card": card, "latent": dict(latent), "current": dict(current), "runway": runway,
        "runway_total": runway_total, "o": o, "oaxis": oaxis, "q": q, "a": a, "s": s,
        "arrival": arrival, "e": e, "age": age, "cls": cls, "Height": Height, "weapon": weapon,
        # S42.1: pre-weapon state + the counterfactual inputs for the [F3] paired census
        "base": base, "pool": pool, "weapon_raw": weapon_raw, "ft_idio": ft_idio,
    }

# ============================================================================
# RECRUITING LINE  --  declared continuous recruitability score (printed below)
# ============================================================================
def rscore_parts(p):
    # Recruiting proxy = value of the player's BEST viable PATHWAY to minutes. A body or a lone
    # weapon may AMPLIFY a plausible current pathway; neither may SUBSTITUTE for every other tool.
    c = p["current"]; cd = p["card"]
    ath = sum(cd[k] for k in ("Strength", "Speed", "Quickness", "FirstStep", "Vertical")) / 5.0
    # PERIMETER pathway: needs a handle/shot ENTRY tool above replacement; survival adds creation
    # + perimeter defense. A lone off-entry spike (e.g. SelfCreation without a handle) cannot carry.
    # Mid is a COMPLEMENTARY tool: it only cashes as an entry when paired with real access
    # (handle / off-ball movement / three-point shot) -- a lone midrange rating is not a guard.
    access        = max(c["BallHandling"], c["OffBallMovement"], c["Outside"]) / 99.0
    mid_eff       = c["Mid"] * min(1.0, access / 0.45)
    entry_p       = max(c["Outside"], c["BallHandling"], mid_eff)
    perim_support = (c["Passing"] + c["Playmaking"] + c["SelfCreation"] + c["OffBallMovement"]) / 4.0
    perim_def     = max(c["PerimeterDefense"], c["Steals"], c["OffBallDefense"])
    perim_val = max(0.0, entry_p - 20) * (0.55 + 0.30 * perim_support / 99 + 0.15 * perim_def / 99) + 0.14 * ath
    # POST pathway: interior SKILL cashes through a continuous height-ACCESS curve (logistic,
    # inflection ~6'2). Below the lower-big range an interior rating converts to little roster
    # value; a 5'8 post skill is stranded, a 6'1 post is borderline, a 6'8+ interior tool is real.
    post_skill    = max(c["RimProtection"], c["PostMoves"], c["Close"], c["Finishing"], c["PostDefense"])
    post_support  = (c["Screening"] + c["PostDefense"] + c["RimProtection"]) / 3.0
    height_factor = clamp(HF_LO + HF_RANGE / (1.0 + math.exp(-HF_STEEP * (p["Height"] - HF_MID))), HF_LO, HF_HI)
    skill_val     = max(0.0, post_skill - 24) * (0.60 + 0.40 * post_support / 99) * height_factor
    glass         = (cd["OffensiveRebounding"] + cd["DefensiveRebounding"]) / 2.0
    reb_val       = glass * 0.16 * min(1.0, post_skill / 45.0)      # rebounding needs REAL interior skill; height-encoded, NOT re-amplified
    low_taper     = clamp((p["Height"] - 40.0) / (LOW_TAPER_TOP - 40.0), LOW_TAPER_FLOOR, 1.0)  # sub-6'0 interior doesn't convert
    post_val = (skill_val + reb_val + 0.10 * ath * min(1.0, height_factor)) * low_taper
    # ORIENTATION-WEIGHTED SELECTION: weight each pathway by o before the max, so a perimeter-leaning
    # card cannot clear on a post-only pathway (nor a post card on a perimeter spike) unless genuinely hybrid.
    perim_w = 1.00 - PERIM_OW * p["o"]
    post_w  = (1.0 - POST_OW) + POST_OW * p["o"]
    wperim, wpost = perim_w * perim_val, post_w * post_val
    total = max(wperim, wpost)
    return {"rscore": total, "which": "perim" if wperim >= wpost else "post",
            "entry_p": entry_p, "perim_support": perim_support, "perim_def": perim_def, "perim_val": perim_val,
            "post_skill": post_skill, "glass": glass, "reb_val": reb_val, "skill_val": skill_val,
            "post_support": post_support, "post_val": post_val, "ath": ath,
            "wperim": wperim, "wpost": wpost, "o": p["o"]}

def rscore(p):
    return rscore_parts(p)["rscore"]

RSCORE_FORMULA = ("RScore = max(perim_weight*perimeter_pathway, post_weight*post_pathway), where "
                  "perim_weight=1-0.45*o and post_weight=0.55+0.45*o (orientation-weighted, continuous -- "
                  "not a role table). Perimeter entry = handle/shot, Mid access-gated. Post entry = an interior "
                  "SKILL cashed through a height-ACCESS curve (~6'2 inflection); rebounding is gated by interior "
                  "skill and NOT re-amplified by height. | recruitable iff RScore >= R_LINE")

def high_for_size_residual(p):
    """burst/quickness/vertical measured against the population expectation AT this Height
       (NOT Strength, which rises with Height by design)."""
    exp_mid = ATH_BASE_LO + 0.5 * (ATH_BASE_HI - ATH_BASE_LO)
    res = 0.0
    for k in ("Quickness", "FirstStep", "Vertical"):
        expected = exp_mid + SIZE_COEF[k] * (p["Height"] - ATH_HEIGHT_CENTER)
        res += p["card"][k] - expected
    return res / 3.0

# ============================================================================
# COHORT + REPORTS
# ============================================================================
_COHORT_CACHE = {}
def build_cohort(seed, n=N_CANDIDATE):
    key = (seed, n)
    if key not in _COHORT_CACHE:
        r = random.Random(seed)
        _COHORT_CACHE[key] = [generate_player(r) for _ in range(n)]
    return _COHORT_CACHE[key]

def ori_class(o):
    return "perimeter" if o < 0.4 else ("hybrid" if o <= 0.6 else "post")

def pct_threshold(values, p):
    v = sorted(values)
    idx = int(clamp(round(p * (len(v) - 1)), 0, len(v) - 1))
    return v[idx]

def fmt_card_summary(p):
    c = p["card"]
    top = sorted(((c[k], k) for k in DRAWN_SKILLS), reverse=True)[:4]
    tops = ", ".join(f"{k}{v}" for v, k in top)
    return (f"o={p['o']:.2f}({ori_class(p['o'])}) H={p['Height']}({height_bin(p['Height'])}) "
            f"q={p['q']:.2f} a={p['a']:.2f} s={p['s']:.2f} arr={p['arrival']:.2f} {p['cls']}/{p['age']}y | "
            f"Str{c['Strength']} Qck{c['Quickness']} Vert{c['Vertical']} | top: {tops} | "
            f"FT{c['FreeThrow']} | RScore={rscore(p):.1f}")

def find_nearest(pool, target, weights, extra=None):
    best, bestd = None, 1e18
    for p in pool:
        if extra and not extra(p):
            continue
        d = 0.0
        for key, tv in target.items():
            scale = 99.0 if key in ("Height",) else 1.0
            d += weights.get(key, 1.0) * ((p[key] - tv) / scale) ** 2
        if d < bestd:
            best, bestd = p, d
    return best

# ============================================================================
# S42.2: MATH-REPLAY FIXTURE DUMP  (opt-in via --fixture; a pure recording
# side-channel -- NO generation math changes; the default run is byte-identical)
# ============================================================================
FIXTURE_SCHEMA_VERSION = "s42.2-v1"
FIXTURE_FILENAME       = "gen_pass2_replay_fixture_s42_2.json"
FIXTURE_PREFIX_COUNT   = 300   # ruled at the S42.2 check-in: first 300 players + targeted edge indices

# The 40-slot RNG stream order per player -- CONTRACTUAL, declared here and echoed into the
# fixture header so the port never infers it from JSON key order. height_branch (a derived
# string, not a draw) is recorded beside the selector for readability but is NOT a slot.
DRAW_ORDER = (["o", "q", "a", "s", "height_branch_selector_raw", "height_noise_raw"]
              + ["skill_noise.%s" % k for k in DRAWN_SKILLS]
              + ["wingspan_noise"]
              + ["ath_noise.%s" % k for k in ATH_KEYS]
              + ["weight_noise", "oreb_noise", "dreb_noise",
                 "arrival_draw_raw", "ft_idio", "age_noise_raw"])
assert len(DRAW_ORDER) == 40

def _fixture_constants_echo():
    """Every constant the deterministic replay (and the C# port) consumes. The port asserts its
    transcribed constants equal this echo BEFORE running parity; the Python replay checker fails
    loudly on any mismatch. A tripwire against silent constant drift, not a second source of truth
    (the oracle source stays canonical)."""
    return OrderedDict([
        ("ORI_MEAN", ORI_MEAN), ("ORI_CONC", ORI_CONC),
        ("SKILL_Q_A", SKILL_Q_A), ("SKILL_Q_B", SKILL_Q_B),
        ("SPEC_A", SPEC_A), ("SPEC_B", SPEC_B), ("ATHQ_A", ATHQ_A), ("ATHQ_B", ATHQ_B),
        ("HT_ORI_MID", HT_ORI_MID), ("HT_ORI_STEEP", HT_ORI_STEEP),
        ("HT_MU_PERIM", HT_MU_PERIM), ("HT_MU_POST", HT_MU_POST),
        ("HT_SIGMA_UP_PERIM", HT_SIGMA_UP_PERIM), ("HT_SIGMA_UP_POST", HT_SIGMA_UP_POST),
        ("HT_SCALE_DOWN", HT_SCALE_DOWN), ("HT_MIN", HT_MIN), ("HT_MAX", HT_MAX),
        ("ATH_HEIGHT_CENTER", ATH_HEIGHT_CENTER), ("SIZE_COEF", SIZE_COEF), ("ATH_SIGMA", ATH_SIGMA),
        ("ATH_BASE_LO", ATH_BASE_LO), ("ATH_BASE_HI", ATH_BASE_HI),
        ("ARR_PERIM", ARR_PERIM), ("ARR_POST", ARR_POST), ("ARR_SIGMA", ARR_SIGMA),
        ("E_MIN", E_MIN), ("EXPR_BASELINE", EXPR_BASELINE),
        ("AGE_ARR_SPAN", AGE_ARR_SPAN), ("AGE_NOISE", AGE_NOISE),
        ("MISMATCH_STRENGTH", MISMATCH_STRENGTH), ("SKILL_NOISE", SKILL_NOISE),
        ("WEAPON_BUMP", WEAPON_BUMP), ("SUPPORT_DRAIN", SUPPORT_DRAIN),
        ("WEAPON_MISMATCH_MAX", WEAPON_MISMATCH_MAX), ("WEAPON_EXCLUDE", list(WEAPON_EXCLUDE)),
        ("WEAPON_CENSUS_OFFSET", WEAPON_CENSUS_OFFSET),
        ("RATING_LO", RATING_LO), ("RATING_SPAN", RATING_SPAN), ("HOLE_FLOOR", HOLE_FLOOR),
        ("FT_CENTER", FT_CENTER), ("FT_OUT_SPAN", FT_OUT_SPAN), ("FT_OUT_SCALE", FT_OUT_SCALE),
        ("FT_HEIGHT_COEF", FT_HEIGHT_COEF), ("FT_MIN", FT_MIN), ("FT_MAX", FT_MAX),
        ("FT_SIGMA", FT_SIGMA), ("PAXIS", PAXIS),
        ("R_LINE", R_LINE), ("HF_LO", HF_LO), ("HF_HI", HF_HI), ("HF_RANGE", HF_RANGE),
        ("HF_STEEP", HF_STEEP), ("HF_MID", HF_MID),
        ("PERIM_OW", PERIM_OW), ("POST_OW", POST_OW),
        ("LOW_TAPER_FLOOR", LOW_TAPER_FLOOR), ("LOW_TAPER_TOP", LOW_TAPER_TOP),
    ])

def _fixture_target_filters():
    """Edge-creature filters (ruled coverage list + clamp-edge bonuses). Definitions reuse the
    oracle's own audit language ([F4] hitch/auto-big, the [I]-table tiny-post family, HEIGHT_BINS).
    Each contributes at most one index: the FIRST match in cohort order (deterministic)."""
    cur = lambda p, k: p["current"][k]
    F = []
    def add(name, desc, pred, fb_desc=None, fb=None):
        F.append({"name": name, "description": desc, "pred": pred, "fb_desc": fb_desc, "fb": fb})
    add("giant_7_3_plus", "Height >= 93 (the 7'3\"+ Gaussian-tail giant)",
        lambda p: p["Height"] >= 93,
        "fallback: Height >= 87 (7'1\"+)", lambda p: p["Height"] >= 87)
    add("tiny_post_family", "Height 40-44 (5'8-5'9) AND o > 0.6 AND weapon in (PostMoves, Close)",
        lambda p: 40 <= p["Height"] <= 44 and p["o"] > 0.6 and p["weapon"] in ("PostMoves", "Close"),
        "fallback: Height 40-44 AND o > 0.6 (any weapon)",
        lambda p: 40 <= p["Height"] <= 44 and p["o"] > 0.6)
    add("ft_hitch", "current Outside >= 70 AND current FreeThrow < 50 (the [F4] skilled low-FT hitch)",
        lambda p: cur(p, "Outside") >= 70 and cur(p, "FreeThrow") < 50,
        "fallback near-hitch: current Outside >= 65 AND current FreeThrow < 55",
        lambda p: cur(p, "Outside") >= 65 and cur(p, "FreeThrow") < 55)
    add("auto_line_weak_big", "Height >= 71 AND current Outside <= 40 AND current FreeThrow > 80 (the [F4] other tail)",
        lambda p: p["Height"] >= 71 and cur(p, "Outside") <= 40 and cur(p, "FreeThrow") > 80)
    add("high_s_spike", "s >= 0.95 (pure one-weapon specialist)",
        lambda p: p["s"] >= 0.95, "fallback: s >= 0.90", lambda p: p["s"] >= 0.90)
    add("low_s_broad", "s <= 0.05 (maximally broad; no weapon lean)",
        lambda p: p["s"] <= 0.05, "fallback: s <= 0.10", lambda p: p["s"] <= 0.10)
    add("raw_arrival_post", "o > 0.6 AND arrival <= 0.15 (deep raw-project post)",
        lambda p: p["o"] > 0.6 and p["arrival"] <= 0.15,
        "fallback: o > 0.6 AND arrival <= 0.20",
        lambda p: p["o"] > 0.6 and p["arrival"] <= 0.20)
    add("ready_arrival_guard", "o < 0.4 AND arrival >= 0.95 (arrives essentially finished)",
        lambda p: p["o"] < 0.4 and p["arrival"] >= 0.95,
        "fallback: o < 0.4 AND arrival >= 0.90",
        lambda p: p["o"] < 0.4 and p["arrival"] >= 0.90)
    add("arrival_clamp_bound", "arrival == 0.0 or 1.0 exactly (the [0,1] clamp bound; the raw draw is otherwise destroyed)",
        lambda p: p["arrival"] in (0.0, 1.0))
    add("height_low_clamp", "Height == 40 (HT_MIN clamp region)", lambda p: p["Height"] == 40)
    add("height_high_clamp", "Height == 99 (HT_MAX clamp; may legitimately be absent -- noted if so)",
        lambda p: p["Height"] == 99)
    add("ft_floor_clamp", "current FreeThrow == 25 (FT_MIN clamp)", lambda p: cur(p, "FreeThrow") == 25)
    add("ft_ceiling_clamp", "current FreeThrow == 95 (FT_MAX clamp; may legitimately be absent -- noted if so)",
        lambda p: cur(p, "FreeThrow") == 95)
    return F

def _first_index(coh, pred):
    for i, p in enumerate(coh):
        if pred(p):
            return i
    return None

def _assemble_row(i, rec, p):
    parts = rscore_parts(p)
    draws = OrderedDict()
    for key in ("o", "q", "a", "s", "height_branch_selector_raw", "height_branch", "height_noise_raw"):
        draws[key] = rec[key]
    draws["skill_noise"] = OrderedDict((k, rec["skill_noise"][k]) for k in DRAWN_SKILLS)
    draws["wingspan_noise"] = rec["wingspan_noise"]
    draws["ath_noise"] = OrderedDict((k, rec["ath_noise"][k]) for k in ATH_KEYS)
    for key in ("weight_noise", "oreb_noise", "dreb_noise", "arrival_draw_raw", "ft_idio", "age_noise_raw"):
        draws[key] = rec[key]
    cp = OrderedDict()
    cp["oaxis"] = p["oaxis"]
    for key in ("oh", "mu", "sigma_up", "h_raw"):
        cp[key] = rec[key]
    cp["Height"] = p["Height"]
    cp["base"] = OrderedDict((k, p["base"][k]) for k in DRAWN_SKILLS)
    cp["eligible"] = rec["eligible"]
    cp["weapon_raw"] = p["weapon_raw"]
    cp["weapon"] = p["weapon"]
    cp["ath_center"] = rec["ath_center"]
    cp["ath_raw"] = OrderedDict((k, rec["ath_raw"][k]) for k in ATH_KEYS)
    for key in ("Wingspan", "Weight", "OffensiveRebounding", "DefensiveRebounding"):
        cp[key] = p["card"][key]
    cp["arr_mean"] = rec["arr_mean"]
    cp["arrival"] = p["arrival"]
    cp["e"] = p["e"]
    cp["latent"]  = OrderedDict((k, p["latent"][k]) for k in SKILL_KEYS)
    cp["current"] = OrderedDict((k, p["current"][k]) for k in SKILL_KEYS)
    cp["runway"]  = OrderedDict((k, p["runway"][k]) for k in SKILL_KEYS)
    cp["runway_total"] = p["runway_total"]
    cp["age"] = p["age"]      # placeholder-output (S42.1 ruling): assert values, do not port formula
    cp["cls"] = p["cls"]
    return OrderedDict([
        ("index", i),
        ("draws", draws),
        ("checkpoints", cp),
        ("card", OrderedDict((k, p["card"][k]) for k in ALL_KEYS)),
        ("recruiting", OrderedDict([("rscore", parts["rscore"]), ("rscore_parts", parts)])),
    ])

def dump_replay_fixture(seed, coh):
    """Selection pass over the already-built canonical cohort (no RNG), then ONE fresh recorder
    pass with an identical stream (recording draws nothing). Writes the fixture beside this
    script; returns the trailing announce lines for the audit output."""
    prefix_n = min(FIXTURE_PREFIX_COUNT, len(coh))
    sel = set(range(prefix_n))
    filters_out = []
    for f in _fixture_target_filters():
        idx = _first_index(coh, f["pred"])
        via_fb = False
        entry = OrderedDict([("name", f["name"]), ("description", f["description"])])
        if idx is None and f["fb"] is not None:
            idx = _first_index(coh, f["fb"])
            via_fb = idx is not None
            if via_fb:
                entry["fallback_description"] = f["fb_desc"]
        entry["matched_index"] = idx
        entry["via_fallback"] = via_fb
        filters_out.append(entry)
        if idx is not None:
            sel.add(idx)
    targeted_beyond = sorted(i for i in sel if i >= prefix_n)

    # recorder pass: same seed, same code path (rec never draws) -> the same people. Every
    # regenerated player is cross-checked against the canonical cohort as the in-run seam proof.
    limit = max(sel) + 1
    r2 = random.Random(seed)
    rows_by_index = {}
    mismatches = 0
    for i in range(limit):
        rec = {} if i in sel else None
        p2 = generate_player(r2, rec)
        p1 = coh[i]
        if (p2["card"] != p1["card"] or p2["weapon"] != p1["weapon"] or p2["latent"] != p1["latent"]
                or p2["o"] != p1["o"] or p2["ft_idio"] != p1["ft_idio"] or p2["arrival"] != p1["arrival"]):
            mismatches += 1
        if rec is not None:
            rows_by_index[i] = _assemble_row(i, rec, p2)

    schema = OrderedDict([
        ("schema_version", FIXTURE_SCHEMA_VERSION),
        ("oracle", "tools/gen_pass2_skillfirst_oracle.py (S42.2 fixture-exporting build of the S42.1 re-locked model)"),
        ("purpose", "Deterministic MATH-REPLAY fixture for the C# port: every final value is replayable "
                    "from recorded raw draws + the frozen constants, with named intermediate checkpoints, "
                    "so the port proves math parity -- never mere deserialization. RNG is factored out."),
        ("seed", seed),
        ("n_cohort", len(coh)),
        ("selection_policy", OrderedDict([
            ("prefix_count", prefix_n),
            ("targeted_indices", targeted_beyond),
            ("selected_total", len(sel)),
            ("note", "Rows are the first prefix_count players of the canonical cohort PLUS the first "
                     "cohort index matching each target filter (guaranteed branch coverage; sparse "
                     "indices are fine because replay never touches the RNG). targeted_indices lists "
                     "only the matches beyond the prefix; each filter's matched_index shows where its "
                     "creature lives regardless."),
            ("target_filters", filters_out),
        ])),
        ("draw_order", DRAW_ORDER),
        ("draw_notes", OrderedDict([
            ("height_branch", "derived flag, not a draw slot: selector_raw < 0.5 => 'upper_gauss' "
                              "(h_raw = mu + abs(noise)), else 'lower_exp' (h_raw = mu - noise); "
                              "height_noise_raw is the PRE-abs gauss on the upper branch, the raw "
                              "expovariate value on the lower branch"),
            ("wingspan_noise", "gauss(4.0, 3.0) as drawn -- the 4.0 mean is INCLUDED in the value"),
            ("arrival_draw_raw", "gauss(arr_mean, ARR_SIGMA) as drawn, PRE-clamp; arrival = clamp(raw, 0, 1)"),
            ("age_noise_raw", "PLACEHOLDER-OUTPUT (S42.1 ruling): recorded for stream completeness; the "
                              "age/class labels are decoration on arrival and do NOT port as spec -- the "
                              "port asserts the recorded age/cls values and never recomputes them as spec"),
        ])),
        ("key_orders", OrderedDict([
            ("DRAWN_SKILLS", DRAWN_SKILLS), ("ATH_KEYS", ATH_KEYS),
            ("SKILL_KEYS", SKILL_KEYS), ("SIZE_KEYS", SIZE_KEYS), ("ALL_KEYS", ALL_KEYS),
        ])),
        ("comparison_convention", "Integer checkpoints (Height, Wingspan, Weight, rebounding, athletic "
                                  "ratings, latent, current, runway, card, age): EXACT equality. Float "
                                  "checkpoints: |diff| <= 1e-9 -- the cross-language allowance for exp/tanh, "
                                  "which Python's libm and .NET may compute ~1 ulp apart; every other "
                                  "operation is plain IEEE-754 double arithmetic and lands bit-identical "
                                  "given the same operation order."),
        ("weapon_selection_contract", "eligible = DRAWN_SKILLS (in list order) where the skill is not in "
                                      "WEAPON_EXCLUDE and max(0, -oaxis*PAXIS[k]) < WEAPON_MISMATCH_MAX. "
                                      "eligible is never empty (Mid: PAXIS 0.0, not excluded, always passes); "
                                      "the code's fall-back to all DRAWN_SKILLS is therefore unreachable. "
                                      "weapon_raw = argmax of base[k]; weapon = argmax of base[k] + "
                                      "WEAPON_CENSUS_OFFSET[k]. TIE SEMANTICS: scan in DRAWN_SKILLS order and "
                                      "take the FIRST maximum (replace only on strictly-greater), matching "
                                      "Python's max()."),
        ("recompute_obligations", [
            "base[k] from q, orientation mismatch (oaxis, PAXIS, MISMATCH_STRENGTH), and skill_noise[k] -- assert vs recorded base",
            "Height from height_branch + mu + height_noise_raw, then clamp(HT_MIN, HT_MAX) and round -- assert h_raw and final Height",
            "each athletic rating from ath_center + SIZE_COEF[k]*(Height-ATH_HEIGHT_CENTER) + ath_noise[k] -- assert ath_raw and the rounded/clamped rating",
            "Wingspan / Weight / rebounding from their formulas + recorded noise -- assert final ints",
            "arrival = clamp(arrival_draw_raw, 0, 1); e = E_MIN + arrival*(1-E_MIN) -- assert both (and arr_mean from o)",
            "latent/current per skill via weapon bump / support drain / expression transform -- assert all 20+20",
            "FreeThrow (latent and current) via the tanh derivation + the ONE shared ft_idio -- assert both",
            "runway = latent - current (21 keys incl. FreeThrow) + runway_total -- assert all",
            "the full 33-key card -- assert all",
            "rscore + every rscore_parts field from the replayed card -- assert all (floats at tolerance)",
        ]),
        ("constants_note", "The port asserts its transcribed constants equal this echo BEFORE running "
                           "parity (the replay checker fails loudly on any mismatch). A tripwire against "
                           "silent constant drift; the oracle source stays canonical."),
        ("constants", _fixture_constants_echo()),
    ])
    fixture = OrderedDict([("schema", schema),
                           ("players", [rows_by_index[i] for i in sorted(rows_by_index)])])
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), FIXTURE_FILENAME)
    with open(path, "w") as f:
        json.dump(fixture, f, indent=1)
        f.write("\n")

    lines = []
    lines.append("[S42.2] replay fixture written: tools/%s   (players=%d: prefix %d + %d targeted beyond it; seed=%d)"
                 % (FIXTURE_FILENAME, len(sel), prefix_n, len(targeted_beyond), seed))
    lines.append("[S42.2] recorder-pass cross-check vs canonical cohort: %d players regenerated, %d mismatches"
                 % (limit, mismatches))
    lines.append("[S42.2] coverage: " + ", ".join(
        "%s@%s%s" % (e["name"], e["matched_index"] if e["matched_index"] is not None else "NONE",
                     "(fb)" if e["via_fallback"] else "") for e in filters_out))
    return lines

def main(write_fixture=False):
    SEED = 20260706
    MULTI_SEEDS = [20260706, 11, 22, 33, 44, 55]
    L = []
    P = L.append
    P("=" * 92)
    P("PLAYER-GENERATION PASS 2 -- SKILL-FIRST ORACLE (first cut)   seed=%d  N_candidate=%d"
      % (SEED, N_CANDIDATE))
    P("=" * 92)
    P("NOTE: no v1 before/after baseline is shown -- the prior 'v1 skill-first oracle' was never")
    P("      built (the retired body/package gate oracle was a different design); this oracle was")
    P("      built fresh from the model spec per Emmett's ruling, so there is no v1 to diff against.")
    P("Authoritative draws:")
    P("  orientation o ~ Beta(mean=%.3f, conc=%.2f)  [0=perimeter,1=post; perimeter := o<0.5]"
      % (ORI_MEAN, ORI_CONC))
    P("  skill quality q ~ Beta(%.2f,%.2f) ; athletic quality a ~ Beta(%.2f,%.2f) ; "
      "specialization s ~ Beta(%.2f,%.2f)" % (SKILL_Q_A, SKILL_Q_B, ATHQ_A, ATHQ_B, SPEC_A, SPEC_B))
    P("  Height (abstract 0-99): logistic-in-o ceiling, MU(o)=%.0f..%.0f, upper Gaussian sigma(o)=%.1f..%.1f "
      "(knee at o=%.2f; bell tail kills 7'3\"+ excess), lower-scale=%.1f" %
      (HT_MU_PERIM, HT_MU_POST, HT_SIGMA_UP_PERIM, HT_SIGMA_UP_POST, HT_ORI_MID, HT_SCALE_DOWN))
    P("  size->ath: Strength %+.2f/Height-pt, burst %+.2f/Height-pt, sigma(burst)=%.0f (freak tail)"
      % (SIZE_COEF["Strength"], SIZE_COEF["Quickness"], ATH_SIGMA["Quickness"]))
    P("  arrival mean(o)=%.2f..%.2f, E_MIN=%.2f, baseline=%.0f  (perimeter developed, post raw)"
      % (ARR_PERIM, ARR_POST, E_MIN, EXPR_BASELINE))
    P("  S42.1 weapon = argmax of base+offset over eligible (WEAPON_CENSUS_OFFSET, the ruled table;")
    P("        offsets shift the argmax only, never card math -- proof is the [F3] paired census)")
    P("  S42.1 FT idiosyncrasy: ONE gauss(0,%.1f) per player, shared by latent-FT and current-FT" % FT_SIGMA)
    P("        (a persistent trait; the [F4] tables prove the tails exist)" )
    P("  S42.1 AGE/CLASS IS A PLACEHOLDER projection of arrival: the season layer owns the real")
    P("        population structure, one-class-vs-standing-pool, and the ready-freshman existence")
    P("        requirement -- do not port the age/class labels as spec.")
    P("  " + RSCORE_FORMULA.replace("R_LINE", "%.1f" % R_LINE))
    P("")

    coh = build_cohort(SEED)
    for p in coh:
        p["_r"] = rscore(p)
    rec = [p for p in coh if p["_r"] >= R_LINE]
    nonrec = [p for p in coh if p["_r"] < R_LINE]

    def recruitable_of(cohort):
        return [p for p in cohort if rscore(p) >= R_LINE]

    # ---- (A) rating-schema assertion -------------------------------------------------
    missing = 0
    for p in coh:
        for k in ALL_KEYS:
            if k not in p["card"]:
                missing += 1
    P("[A] RATING-SCHEMA ASSERTION -- all 33 keys present on every player")
    P("    players=%d  missing-key hits=%d  ->  %s" % (len(coh), missing, "PASS" if missing == 0 else "FAIL"))
    P("")

    # ---- (B) orientation histogram + shares + 60/40 split ----------------------------
    below = sum(1 for p in coh if p["o"] < 0.5)
    cl = defaultdict(int)
    for p in coh:
        cl[ori_class(p["o"])] += 1
    P("[B] ORIENTATION -- realized lean & hybrid density")
    P("    P(o<0.5) perimeter share = %.3f   (target ~0.60)" % (below / len(coh)))
    P("    class shares  perimeter(o<0.4)=%.3f  hybrid(0.4-0.6)=%.3f  post(o>0.6)=%.3f" %
      (cl["perimeter"] / len(coh), cl["hybrid"] / len(coh), cl["post"] / len(coh)))
    # coarse histogram
    hist = [0] * 10
    for p in coh:
        hist[min(9, int(p["o"] * 10))] += 1
    P("    o histogram (deciles 0.0..1.0): " + " ".join("%d" % (h * 1000 // len(coh)) for h in hist) + "   (per-mille)")
    P("")

    # ---- (C) orientation x Height-bin counts -----------------------------------------
    P("[C] ORIENTATION x HEIGHT-BIN  (counts in the %d-player candidate cohort)" % len(coh))
    P("    %-11s %8s %8s %8s" % ("bin", "perim", "hybrid", "post"))
    grid = defaultdict(lambda: defaultdict(int))
    for p in coh:
        grid[height_bin(p["Height"])][ori_class(p["o"])] += 1
    for b in HEIGHT_BINS:
        P("    %-11s %8d %8d %8d" % (b, grid[b]["perimeter"], grid[b]["hybrid"], grid[b]["post"]))
    P("")

    # ---- (C2) stretch big vs point-center -- tall+jumper is common, tall+handle is rare ----
    stretch = sum(1 for p in rec if p["Height"] >= 71 and ori_class(p["o"]) != "perimeter"
                  and p["current"]["Outside"] >= 55 and p["current"]["BallHandling"] < 55)
    stretch_bigtall = sum(1 for p in rec if p["Height"] >= 80
                          and p["current"]["Outside"] >= 55 and p["current"]["BallHandling"] < 55)
    ptcenter = sum(1 for p in rec if p["Height"] >= 80
                   and p["current"]["BallHandling"] >= 60 and p["current"]["Outside"] >= 55)
    P("[C2] STRETCH BIG vs POINT-CENTER (recruitable pool of %d) -- shooting is orientation-neutral," % len(rec))
    P("     ball-handling is perimeter-locked, so these two diverge as they should:")
    P("    stretch bigs (6'8+, shoots>=55, NO handle) = %d   (of which 6'10+ = %d)" % (stretch, stretch_bigtall))
    P("    point-centers (6'10+, handle>=60 AND shoots) = %d   <- must stay rare (tall + guard handle)" % ptcenter)
    P("")

    # ---- (D) rare perimeter-height audit (fixed + multi-seed, in RECRUITABLE pool) ---
    def perim_in_cliff(cohort):
        return sum(1 for p in cohort if ori_class(p["o"]) == "perimeter"
                   and 80 <= p["Height"] <= 86)
    fixed_cand = perim_in_cliff(coh)
    fixed_rec = perim_in_cliff(rec)
    counts = []
    for sd in MULTI_SEEDS:
        counts.append(perim_in_cliff(rec if sd == SEED else recruitable_of(build_cohort(sd))))
    zero_rate = sum(1 for c in counts if c == 0) / len(counts)
    P("[D] RARE PERIMETER-HEIGHT AUDIT -- perimeter-oriented players in the 6'10\"-7'0\" bin (80-86)")
    P("    target ~2-3 per RECRUITABLE pool (dial 2)")
    P("    fixed-seed:  candidate-cohort=%d   RECRUITABLE=%d" % (fixed_cand, fixed_rec))
    P("    multi-seed recruitable counts %s  mean=%.1f  zero-count-rate=%.2f" %
      (counts, sum(counts) / len(counts), zero_rate))
    P("")

    # ---- (E) quality x Height correlation WITHIN each orientation --------------------
    def corr(xs, ys):
        n = len(xs)
        if n < 3:
            return float("nan")
        mx, my = sum(xs) / n, sum(ys) / n
        sxy = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
        sxx = sum((x - mx) ** 2 for x in xs)
        syy = sum((y - my) ** 2 for y in ys)
        return sxy / math.sqrt(sxx * syy) if sxx > 0 and syy > 0 else float("nan")
    P("[E] QUALITY x HEIGHT CORRELATION  (must be ~0: quality is independent of size)")
    gq = [p["q"] for p in coh]; gh = [p["Height"] for p in coh]
    P("    GLOBAL corr(q,Height) = %+.3f" % corr(gq, gh))
    for oc in ("perimeter", "hybrid", "post"):
        sub = [p for p in coh if ori_class(p["o"]) == oc]
        P("    within %-9s corr(q,Height) = %+.3f   (n=%d)" %
          (oc, corr([p["q"] for p in sub], [p["Height"] for p in sub]), len(sub)))
    P("")

    # ---- (F) elite-skill players by Height bin within each orientation ---------------
    q_elite = pct_threshold([p["q"] for p in coh], 0.90)
    P("[F] ELITE-SKILL (latent q >= 90th pct = %.2f) players by Height bin, within orientation" % q_elite)
    P("    %-11s %8s %8s %8s" % ("bin", "perim", "hybrid", "post"))
    eg = defaultdict(lambda: defaultdict(int))
    for p in coh:
        if p["q"] >= q_elite:
            eg[height_bin(p["Height"])][ori_class(p["o"])] += 1
    for b in HEIGHT_BINS:
        P("    %-11s %8d %8d %8d" % (b, eg[b]["perimeter"], eg[b]["hybrid"], eg[b]["post"]))
    P("")

    # ---- (F2) specialization audit -- proves the pure-spike dial is real -------------
    def sortedskills(p):
        return sorted((p["current"][k] for k in DRAWN_SKILLS), reverse=True)
    lo = [p for p in coh if p["s"] < 0.33]
    hi = [p for p in coh if p["s"] > 0.66]
    def band(group):
        t1 = [sortedskills(p)[0] for p in group]
        t2 = [sortedskills(p)[1] for p in group]
        gap = [a - b for a, b in zip(t1, t2)]
        return (sum(t1) / len(t1), sum(t2) / len(t2), sum(gap) / len(gap),
                pct_threshold(gap, 0.10), pct_threshold(gap, 0.90))
    P("[F2] SPECIALIZATION AUDIT -- does high s make one distinct weapon? (top1 minus top2)")
    P("    %-14s %6s %6s %8s   %s" % ("s band", "top1", "top2", "gap", "gap p10..p90"))
    for name, g in (("broad  s<0.33", lo), ("spike  s>0.66", hi)):
        m1, m2, mg, p10, p90 = band(g)
        P("    %-14s %6.1f %6.1f %8.1f   [%d .. %d]  (n=%d)" % (name, m1, m2, mg, p10, p90, len(g)))
    wc = defaultdict(int)
    for p in coh:
        wc[p["weapon"]] += 1
    topw = sorted(wc.items(), key=lambda kv: -kv[1])[:6]
    P("    most-drawn weapons: " + ", ".join("%s %d" % (k, v) for k, v in topw))
    P("")

    # ---- (F3) WEAPON CENSUS -- paired counterfactual of the selection rule (S42.1) ----
    # BEFORE = the S42 raw argmax; AFTER = the S42.1 offset argmax. Both rules are applied
    # to the SAME generated pre-weapon bases and eligibility sets (weapon_raw vs weapon on
    # the same player) -- a paired counterfactual of the rule, not two drifting cohorts.
    # The recruitable-export columns run the unchanged downstream card math separately from
    # each counterfactual weapon choice on the same pre-weapon state, then evaluate the
    # unchanged recruiting line on each resulting card: same person, two candidate weapons,
    # two cards, one recruiting screen.
    def cf_player(p, w):
        """The same player under weapon choice w (counterfactual card + rscore inputs)."""
        if w == p["weapon"]:
            return p
        lat, cur, run, _rt = build_skill_state(p["base"], w, p["s"], p["e"], p["Height"], p["ft_idio"])
        card = dict(p["card"])
        for k in SKILL_KEYS:
            card[k] = cur[k]
        return {"card": card, "current": cur, "Height": p["Height"], "o": p["o"]}
    P("[F3] WEAPON CENSUS -- paired counterfactual: S42 raw argmax (BEFORE) vs S42.1 offset")
    P("     argmax (AFTER) on identical pre-weapon bases + eligibility sets.  Offsets:")
    off_items = ["%s %+0.3f" % (k, WEAPON_CENSUS_OFFSET[k]) for k in sorted(WEAPON_CENSUS_OFFSET)]
    for i in range(0, len(off_items), 4):
        P("       " + "  ".join(off_items[i:i + 4]))
    elig_n = defaultdict(int); bef = defaultdict(int); aft = defaultdict(int)
    bef_per = defaultdict(int); aft_per = defaultdict(int)
    bef_post = defaultdict(int); aft_post = defaultdict(int)
    bef_rec = defaultdict(int); aft_rec = defaultdict(int)
    n = len(coh)
    changed = 0
    for p in coh:
        for k in p["pool"]:
            elig_n[k] += 1
        wb, wa = p["weapon_raw"], p["weapon"]
        bef[wb] += 1; aft[wa] += 1
        oc = ori_class(p["o"])
        if oc == "perimeter":
            bef_per[wb] += 1; aft_per[wa] += 1
        elif oc == "post":
            bef_post[wb] += 1; aft_post[wa] += 1
        if wb != wa:
            changed += 1
        # recruitable-export under each counterfactual card (AFTER card == shipping card)
        if rscore(cf_player(p, wb)) >= R_LINE:
            bef_rec[wb] += 1
        if p["_r"] >= R_LINE:
            aft_rec[wa] += 1
    tot_bref = sum(bef_rec.values()); tot_aref = sum(aft_rec.values())
    P("     weapon changed by the offsets on %d/%d players (%.1f%%); recruitable pool:" % (changed, n, 100.0 * changed / n))
    P("     BEFORE-rule cards %d vs AFTER-rule (shipping) cards %d" % (tot_bref, tot_aref))
    P("     %-17s %14s %16s %16s %13s %13s %15s" %
      ("skill", "eligible n/shr", "weapon BEFORE", "weapon AFTER", "perim B/A", "post B/A", "recr-exp B/A"))
    order = sorted((k for k in DRAWN_SKILLS if k not in WEAPON_EXCLUDE), key=lambda k: -aft[k])
    for k in order:
        P("     %-17s %6d %6.1f%% %8d %6.2f%% %8d %6.2f%% %6d/%-6d %5d/%-6d %6d/%-6d" %
          (k, elig_n[k], 100.0 * elig_n[k] / n,
           bef[k], 100.0 * bef[k] / n, aft[k], 100.0 * aft[k] / n,
           bef_per[k], aft_per[k], bef_post[k], aft_post[k], bef_rec[k], aft_rec[k]))
    P("     PostMoves vs OffBallDefense (Emmett's question -- post-play identity must not be")
    P("       rarer than off-ball-defense identity unless ruled so):")
    P("       BEFORE  PostMoves %d (%.2f%%)  vs  OffBallDefense %d (%.2f%%)" %
      (bef["PostMoves"], 100.0 * bef["PostMoves"] / n, bef["OffBallDefense"], 100.0 * bef["OffBallDefense"] / n))
    P("       AFTER   PostMoves %d (%.2f%%)  vs  OffBallDefense %d (%.2f%%)" %
      (aft["PostMoves"], 100.0 * aft["PostMoves"] / n, aft["OffBallDefense"], 100.0 * aft["OffBallDefense"] / n))
    P("")

    # ---- (F4) FT IDIOSYNCRASY AUDITS (S42.1) -- operational definitions fixed in the ----
    # ---- build prompt, printed not asserted; FT_SIGMA is Emmett's dial at these tables ----
    P("[F4] FT IDIOSYNCRASY -- ONE shared gauss(0,%.1f) per player; the tails must exist" % FT_SIGMA)
    P("  conditional-spread audit (predeclared current-Outside x Height bins; pass = SD")
    P("  materially nonzero in every bin):")
    FT_BINS = [
        ("guard/wing mid-skill  Out[55,70) x H[51,66)", lambda p: 55 <= p["current"]["Outside"] < 70 and 51 <= p["Height"] < 66),
        ("skilled perimeter     Out[70,99] x H[40,66)", lambda p: p["current"]["Outside"] >= 70 and p["Height"] < 66),
        ("weak-shooting big     Out[25,45) x H[71,99]", lambda p: 25 <= p["current"]["Outside"] < 45 and p["Height"] >= 71),
    ]
    for name, f in FT_BINS:
        sub = [p["current"]["FreeThrow"] for p in coh if f(p)]
        if len(sub) < 2:
            P("    %-46s n=%d (BIN TOO THIN -- predeclare wider)" % (name, len(sub)))
            continue
        m = sum(sub) / len(sub)
        sd = math.sqrt(sum((x - m) ** 2 for x in sub) / (len(sub) - 1))
        P("    %-46s n=%5d  mean=%.1f  SD=%.1f  min=%d  max=%d%s" %
          (name, len(sub), m, sd, min(sub), max(sub), "" if len(sub) >= 100 else "  (n<100!)"))
    def hitch(p):     # skilled low-FT tail
        return p["current"]["Outside"] >= 70 and p["current"]["FreeThrow"] < 50
    def autobig(p):   # weak-shooting-big high-FT tail
        return p["Height"] >= 71 and p["current"]["Outside"] <= 40 and p["current"]["FreeThrow"] > 80
    P("  tails per cohort (count / rate per 46k) -- reachability is proven by >=1 occurrence;")
    P("  whether the RATE is sensible is Emmett's FT_SIGMA ruling, not a pass/fail here:")
    for sd_ in MULTI_SEEDS:
        c2 = coh if sd_ == SEED else build_cohort(sd_)
        h = sum(1 for p in c2 if hitch(p)); ab = sum(1 for p in c2 if autobig(p))
        P("    seed %-9d skilled low-FT hitch (Out>=70 & FT<50): %3d (%.3f%%)   "
          "auto-line weak big (H>=71 & Out<=40 & FT>80): %3d (%.3f%%)" %
          (sd_, h, 100.0 * h / len(c2), ab, 100.0 * ab / len(c2)))
    hitches = [p for p in coh if hitch(p)]
    P("  FT-hitch archetype, nearest card (rare-but-reachable is the pass condition; judgment")
    P("  on the archetype itself stays deferred to the season layer per the standing ruling):")
    if hitches:
        best = max(hitches, key=lambda p: p["current"]["Outside"])
        P("    " + fmt_card_summary(best))
    else:
        found = None
        for sd_ in MULTI_SEEDS[1:]:
            c2 = build_cohort(sd_)
            cand = [p for p in c2 if hitch(p)]
            if cand:
                found = (sd_, max(cand, key=lambda p: p["current"]["Outside"]))
                break
        if found:
            P("    (none in canonical cohort; cross-seed fallback, seed %d)" % found[0])
            P("    " + fmt_card_summary(found[1]))
        else:
            P("    NONE in any sampled seed -- tail not reachable at this FT_SIGMA (raise the dial)")
    P("")

    # ---- (G) recruiting-line audit ---------------------------------------------------
    multi_rec = []
    for sd in MULTI_SEEDS:
        c2 = coh if sd == SEED else build_cohort(sd)
        multi_rec.append(sum(1 for p in c2 if rscore(p) >= R_LINE))
    P("[G] RECRUITING-LINE AUDIT   (line is an oracle-only selection proxy; formula printed above)")
    P("    N_candidate=%d   R_LINE=%.1f" % (N_CANDIDATE, R_LINE))
    P("    recruitable=%d (%.1f%%)   non-recruitable=%d (%.1f%%)   [target recruitable 20-30k]" %
      (len(rec), 100 * len(rec) / len(coh), len(nonrec), 100 * len(nonrec) / len(coh)))
    P("    multi-seed recruitable counts %s  mean=%.0f" % (multi_rec, sum(multi_rec) / len(multi_rec)))
    worst = min(rec, key=lambda p: p["_r"])
    P("    WORST recruitable, any type (right at the line):")
    P("      " + fmt_card_summary(worst))
    P("")

    # ---- (G2) recruiting-threshold sensitivity (is the count stable or cliff-like?) --
    P("[G2] THRESHOLD SENSITIVITY -- recruitable count vs R_LINE (canonical cohort)")
    P("    " + "  ".join("L=%d:%d" % (t, sum(1 for p in coh if p["_r"] >= t)) for t in (13, 15, 17, 19, 21, 23)))
    P("    (smooth, monotone -> the 20-30k export is not sitting on a cliff edge)")
    P("")

    # ---- (G3) arrival / class decoupling audit (population scale) ---------------------
    RAW, READY = 0.40, 0.75
    raw_sr = sum(1 for p in coh if p["arrival"] < RAW and p["age"] >= 22)
    rdy_fr = sum(1 for p in coh if p["arrival"] > READY and p["age"] <= 18)
    raw_jr = sum(1 for p in coh if p["arrival"] < RAW and 20 <= p["age"] <= 21)
    rdy_so = sum(1 for p in coh if p["arrival"] > READY and p["age"] == 19)
    P("[G3] ARRIVAL / CLASS DECOUPLING -- class correlates with arrival but is NOT identical")
    P("    raw seniors (arr<%.2f, age>=22) = %d (%.2f%%)   ready frosh (arr>%.2f, age<=18) = %d (%.2f%%)" %
      (RAW, raw_sr, 100 * raw_sr / len(coh), READY, rdy_fr, 100 * rdy_fr / len(coh)))
    P("    raw juniors = %d (%.2f%%)   ready sophomores = %d (%.2f%%)   (the off-diagonal tails exist)" %
      (raw_jr, 100 * raw_jr / len(coh), rdy_so, 100 * rdy_so / len(coh)))
    P("    current-vs-latent skill gap by arrival band (mean latent minus mean current, 21 skills):")
    for blo, bhi in ((0.0, 0.30), (0.30, 0.50), (0.50, 0.70), (0.70, 1.01)):
        sub = [p for p in coh if blo <= p["arrival"] < bhi]
        if sub:
            gap = sum(p["runway_total"] / 21.0 for p in sub) / len(sub)
            P("      arrival [%.2f,%.2f): mean per-skill runway = %.1f   (n=%d)" % (blo, bhi, gap, len(sub)))
    P("")

    # ---- (G4) PATHWAY EXPORT AUDIT -- why the recruiting line admits/rejects edge bodies ----
    P("[G4] PATHWAY EXPORT AUDIT -- the recruiting line is pathway-gated, not size-additive")
    P("     principle: a body or one weapon may AMPLIFY a current pathway; neither SUBSTITUTES for every tool")
    P("     selection is ORIENTATION-WEIGHTED: perim_weight=1-0.45*o, post_weight=0.55+0.45*o (continuous)")
    tall93 = sorted([p for p in coh if p["Height"] >= 93], key=lambda p: -p["Height"])
    P("  all Height 93+ (7'3\"+) candidates -- do they cash their size into a current tool?")
    if not tall93:
        P("    (none in this cohort)")
    for p in tall93:
        b = rscore_parts(p)
        verdict = "RECRUITABLE" if b["rscore"] >= R_LINE else "below line"
        why = ("via %-5s | interior SKILL=%d (needs >24, height-amp), rebounding=%.0f gated to +%.1f, "
               "o=%.2f -> weighted post=%.1f" %
               (b["which"], b["post_skill"], b["glass"], b["reb_val"], b["o"], b["wpost"]))
        P("    H=%d  RScore=%.1f  %-12s | %s" % (p["Height"], b["rscore"], verdict, why))
    # S42.1: the scrub half of the invariant is DEMONSTRATED with printed rows, never asserted
    # from prose alone (a PRNG-shifted cohort may have no 7'3"+ scrub, as S42.1's canonical didn't).
    # tall real tool  := H>=93 and interior skill >= 40  (must clear)
    # rebound-only scrub := H>=87 and interior skill <= 30 and glass >= 65  (must stay below line)
    def _g4_row(p):
        b = rscore_parts(p)
        v = "RECRUITABLE" if b["rscore"] >= R_LINE else "below line"
        return ("H=%d  RScore=%.1f  %-12s | interior SKILL=%d, rebounding=%.0f gated to +%.1f, "
                "o=%.2f -> weighted post=%.1f" %
                (p["Height"], b["rscore"], v, b["post_skill"], b["glass"], b["reb_val"], b["o"], b["wpost"]))
    def _ps(p):
        return rscore_parts(p)["post_skill"]
    def _gl(p):
        return (p["card"]["OffensiveRebounding"] + p["card"]["DefensiveRebounding"]) / 2.0
    P("  the invariant, shown as rows (tool clears / rebound-only scrub does not):")
    tools = [p for p in coh if p["Height"] >= 93 and _ps(p) >= 40]
    if tools:
        P("    tallest real-tool giant:      " + _g4_row(max(tools, key=lambda p: p["Height"])))
    else:
        P("    tallest real-tool giant:      (none H>=93 with skill>=40 in canonical cohort)")
    scrubs = [p for p in coh if p["Height"] >= 87 and _ps(p) <= 30 and _gl(p) >= 65]
    if scrubs:
        P("    tallest rebound-only scrub:   " + _g4_row(max(scrubs, key=lambda p: p["Height"])))
    else:
        P("    tallest rebound-only scrub:   (none H>=87/skill<=30/glass>=65 in canonical cohort)")
    strict = [p for p in coh if p["Height"] >= 93 and _ps(p) <= 30 and _gl(p) >= 65]
    if strict:
        P("    strict 7'3\"+ scrub:           " + _g4_row(max(strict, key=lambda p: p["Height"])))
    else:
        found = None
        for sd_ in MULTI_SEEDS[1:]:
            c2 = build_cohort(sd_)
            cand = [p for p in c2 if p["Height"] >= 93 and _ps(p) <= 30 and _gl(p) >= 65]
            if cand:
                found = (sd_, max(cand, key=lambda p: p["Height"]))
                break
        if found:
            P("    strict 7'3\"+ scrub (cross-seed fallback, seed %d):" % found[0])
            P("      " + _g4_row(found[1]))
        else:
            P("    strict 7'3\"+ scrub:           none in any sampled seed (rare by construction; the")
            P("      H>=87 scrub row above carries the proof at 7'1\"+)")
    P("    read: extreme height with no interior SKILL cannot convert rebounding alone into a")
    P("          roster spot -- as the scrub row(s) above show; only the real interior tool clears")
    # lowest recruitable perimeter guard -- must clear via the PERIMETER pathway
    guards = [p for p in rec if ori_class(p["o"]) == "perimeter" and p["Height"] <= 65]
    if guards:
        g = min(guards, key=lambda p: p["_r"]); b = rscore_parts(g)
        P("  lowest recruitable perimeter guard: H=%d o=%.2f RScore=%.1f  clears via [%s]  (want perim)" %
          (g["Height"], b["o"], b["rscore"], b["which"]))
        P("    entry(handle/shot)=%d (needs >20)  creation-support=%.0f  perimeter-def=%d  ath=%.0f  |  weighted perim=%.1f vs post=%.1f" %
          (b["entry_p"], b["perim_support"], b["perim_def"], b["ath"], b["wperim"], b["wpost"]))
    # lowest recruitable post/big -- must clear via the POST pathway
    bigs = [p for p in rec if ori_class(p["o"]) != "perimeter" and p["Height"] >= 71]
    if bigs:
        bg = min(bigs, key=lambda p: p["_r"]); b = rscore_parts(bg)
        P("  lowest recruitable post/big: H=%d o=%.2f RScore=%.1f  clears via [%s]  (want post)" %
          (bg["Height"], b["o"], b["rscore"], b["which"]))
        P("    interior-skill=%d (needs >24, height-amp)  rebounding=%.0f(+%.1f gated)  post-support=%.0f  ath=%.0f  |  weighted post=%.1f vs perim=%.1f" %
          (b["post_skill"], b["glass"], b["reb_val"], b["post_support"], b["ath"], b["wpost"], b["wperim"]))
    # aggregate: the tiny-post-scorer FAMILY (not just the named case) split by winning pathway
    fam = [p for p in coh if 40 <= p["Height"] <= 44 and ori_class(p["o"]) == "post"
           and p["weapon"] in ("PostMoves", "Close") and p["current"][p["weapon"]] >= 55]
    fam_post = sum(1 for p in fam if rscore_parts(p)["rscore"] >= R_LINE and rscore_parts(p)["which"] == "post")
    fam_per  = sum(1 for p in fam if rscore_parts(p)["rscore"] >= R_LINE and rscore_parts(p)["which"] == "perim")
    P("  tiny post-scorer FAMILY (H40-44, post, PostMoves/Close weapon>=55): %d candidates / %d recruitable"
      % (len(fam), fam_post + fam_per))
    P("    -> %d via POST path (target 0: interior skill does not convert at 5'8-5'9), %d via PERIMETER path "
      "(legit: real guard tools, recruited as a small perimeter player -- not as a stranded post savant)"
      % (fam_post, fam_per))
    # aggregate: cross-path leakage -- how often orientation and winning pathway disagree (hybrid blur is OK)
    parts_rec = [rscore_parts(p) for p in rec]
    perim_via_post = sum(1 for p, b in zip(rec, parts_rec) if ori_class(p["o"]) == "perimeter" and b["which"] == "post")
    post_via_perim = sum(1 for p, b in zip(rec, parts_rec) if ori_class(p["o"]) == "post" and b["which"] == "perim")
    P("  cross-path (continuity check, not a role table): %d perimeter-class recruits clear via POST, "
      "%d post-class recruits clear via PERIMETER  (%.1f%% / %.1f%% of pool -- continuous cross-path "
      "exceptions; concentrated near the hybrid band but not restricted to it)"
      % (perim_via_post, post_via_perim, 100 * perim_via_post / len(rec), 100 * post_via_perim / len(rec)))
    P("")

    # ---- (H) confluence classifiers (count from LATENT vars, over RECRUITABLE export) -
    qs = [p["q"] for p in coh]; hs = [p["Height"] for p in coh]; as_ = [p["a"] for p in coh]
    ress = [high_for_size_residual(p) for p in coh]
    lq, lsz, la = pct_threshold(qs, LEBRON_Q_PCT), pct_threshold(hs, LEBRON_SZ_PCT), pct_threshold(as_, LEBRON_A_PCT)
    wsz, wq, wres = pct_threshold(hs, WEMBY_SZ_PCT), pct_threshold(qs, WEMBY_Q_PCT), pct_threshold(ress, WEMBY_RES_PCT)
    def leb(pool):
        return sum(1 for p in pool if p["q"] >= lq and p["Height"] >= lsz and p["a"] >= la)
    def wem(pool):
        return sum(1 for p in pool if p["Height"] >= wsz and p["q"] >= wq and high_for_size_residual(p) >= wres)
    leb_multi = [leb(rec if sd == SEED else recruitable_of(build_cohort(sd))) for sd in MULTI_SEEDS]
    wem_multi = [wem(rec if sd == SEED else recruitable_of(build_cohort(sd))) for sd in MULTI_SEEDS]
    P("[H] LATENT CONFLUENCE CLASSIFIERS -- counted from LATENT variables, over the RECRUITABLE export")
    P("    (broad three-axis tails; these are NOT the strict basketball-identity unicorns -- see [I2])")
    P("    elite three-axis confluence  := q>=%.2f (%.0fpct) AND Height>=%d (%.0fpct) AND a>=%.2f (%.0fpct)" %
      (lq, LEBRON_Q_PCT * 100, lsz, LEBRON_SZ_PCT * 100, la, LEBRON_A_PCT * 100))
    P("      recruitable count = %d   multi-seed %s mean=%.1f   (canonical run may legitimately be 0)" %
      (leb(rec), leb_multi, sum(leb_multi) / len(leb_multi)))
    P("    extreme size-skill-athletic  := Height>=%d (%.1fpct) AND q>=%.2f (%.0fpct) AND burst-residual>=%.1f (%.0fpct)" %
      (wsz, WEMBY_SZ_PCT * 100, wq, WEMBY_Q_PCT * 100, wres, WEMBY_RES_PCT * 100))
    P("      (residual = mean of Quickness/FirstStep/Vertical minus expectation-at-Height; NOT Strength)")
    P("      recruitable count = %d   multi-seed %s mean=%.1f" %
      (wem(rec), wem_multi, sum(wem_multi) / len(wem_multi)))
    P("")

    # ---- (I) ARCHETYPE / CASE TABLE  (the sign-off medium) ---------------------------
    P("=" * 92)
    P("[I] ARCHETYPE / CASE TABLE -- nearest-match in-population (the sign-off medium)")
    P("=" * 92)
    W = {"o": 3.0, "Height": 3.0, "q": 2.0, "a": 1.5, "s": 0.6, "arrival": 1.0}

    def show(label, pool, target, weights, extra=None, note="", force=None, fallback_seeds=None):
        p = find_nearest(pool, target, weights, extra)
        src = "canonical world"
        if p is None and fallback_seeds:
            for sd in fallback_seeds:
                if sd == SEED:
                    continue
                cand = find_nearest(recruitable_of(build_cohort(sd)), target, weights, extra)
                if cand is not None:
                    p, src = cand, "seed=%d; NONE in the canonical world" % sd
                    break
        if p is None:
            P("  %-26s : (no match at current dials)" % label); return None
        tag = "" if src == "canonical world" else "   [%s]" % src
        P("  %-26s : %s%s" % (label, note, tag))
        P("      " + fmt_card_summary(p))
        if force:
            P("      show: " + "  ".join("%s%d" % (k, p["current"][k]) for k in force))
        P("      latent-q=%.2f  runway(total=%d, top-room: %s)" %
          (p["q"], p["runway_total"],
           ", ".join(f"{k}+{p['runway'][k]}" for k in
                     sorted(p["runway"], key=lambda k: -p["runway"][k])[:4] if p["runway"][k] > 0)))
        return p

    P("-- RECRUITABLE TIER (the pool this oracle sizes) " + "-" * 42)
    def maxskill(p):  # current top skill name
        return max(DRAWN_SKILLS, key=lambda k: p["current"][k])
    def othermean(p, exclude):  # mean current skill excluding one
        vs = [p["current"][k] for k in DRAWN_SKILLS if k != exclude]
        return sum(vs) / len(vs)
    def hbin(p, lo, hi):
        return lo <= p["Height"] <= hi
    MCGRADY_WEAPONS = ("Outside", "Mid", "BallHandling", "Playmaking", "SelfCreation", "Passing")

    # Mandatory facts are HARD filters (extra=...). A failed filter prints "no match" --
    # which is itself evidence about which dial needs work. Soft distance only ranks the
    # non-mandatory dims (q, a, s) inside the hard-filtered set.
    show("Korver (5'9 spacer)", rec, {"q": 0.92, "a": 0.18, "s": 0.85}, W,
         extra=lambda p: hbin(p, 40, 44) and ori_class(p["o"]) == "perimeter"
                         and p["weapon"] == "Outside" and p["current"]["Outside"] >= 82 and p["arrival"] >= 0.72,
         note="HARD: 5'8-5'9, perimeter, Outside weapon >=82 -> low-level floor spacer (NOT a 4)")
    show("Skilled 5'11 guard", rec, {"q": 0.85, "a": 0.50, "s": 0.45}, W,
         extra=lambda p: hbin(p, 45, 50) and ori_class(p["o"]) == "perimeter" and p["arrival"] >= 0.74,
         note="HARD: 5'10-5'11, perimeter, arrived -> very good in college (must survive)")
    show("Barkley (6'5 interior athlete)", rec, {"q": 0.90, "s": 0.45}, W,
         extra=lambda p: hbin(p, 57, 65) and ori_class(p["o"]) == "hybrid" and p["a"] >= 0.85
                         and p["weapon"] in ("Finishing", "Close", "PostMoves", "Screening")
                         and p["arrival"] >= 0.66,
         note="HARD: 6'2-6'5, hybrid, elite ath, INTERIOR weapon -> undersized bruiser (not a shooter)")
    show("Wembanyama (7'+ unicorn)", rec, {"q": 0.85, "a": 0.88, "s": 0.30}, W,
         extra=lambda p: p["Height"] >= 86 and high_for_size_residual(p) >= 8
                         and p["current"]["BallHandling"] >= 52 and p["current"]["Outside"] >= 50
                         and p["current"]["PostMoves"] >= 50,
         note="HARD: 6'10+, HANDLES (BH>=52) AND shoots AND posts AND freak-mobile -> rare/unicorn in sampled worlds (may be no-match; no world->year conversion yet)",
         force=["Outside", "BallHandling", "PostMoves", "RimProtection"], fallback_seeds=MULTI_SEEDS)
    show("McGrady (6'9 wing)", rec, {"q": 0.88, "a": 0.75, "s": 0.55}, W,
         extra=lambda p: hbin(p, 71, 79) and ori_class(p["o"]) != "post" and p["arrival"] >= 0.68
                         and p["weapon"] in MCGRADY_WEAPONS and p["current"]["PostMoves"] <= 30
                         and max(p["current"]["Outside"], p["current"]["SelfCreation"], p["current"]["Mid"]) >= 55,
         note="HARD: 6'8-6'9, OFFENSIVE weapon + real scoring/creation (>=55), no interior -> elite scoring wing")
    show("Junction 7-footer (Bol)", rec, {"q": 0.42, "a": 0.55, "s": 0.90}, W,
         extra=lambda p: p["Height"] >= 80 and ori_class(p["o"]) == "post"
                         and p["weapon"] == "RimProtection" and p["current"]["RimProtection"] >= 66
                         and othermean(p, "RimProtection") <= 34,
         note="HARD: 6'10+, RimProtection weapon >=66, horrific else -> useful-but-limited")
    raw = show("Raw big project (6'9)", rec, {"q": 0.55, "s": 0.5}, W,
               extra=lambda p: hbin(p, 71, 79) and ori_class(p["o"]) == "post"
                               and p["arrival"] < 0.40 and p["a"] >= 0.72,
               note="HARD: 6'8-6'9, post, raw (arr<0.40), athletic -> fundable project")
    rq = raw["q"] if raw else 0.55
    ra = raw["a"] if raw else 0.85
    rH = raw["Height"] if raw else 74
    show("  paired ready senior", rec, {"q": rq, "a": ra, "s": 0.5}, dict(W, q=3.0, a=2.5),
         extra=lambda p: hbin(p, 71, 79) and ori_class(p["o"]) == "post"
                         and p["arrival"] > 0.75 and abs(p["a"] - ra) <= 0.12,
         note="HARD: same 6'8-6'9 post band, ready (arr>0.75), ~same ath -> better TODAY (arrival has teeth)")
    show("LeBron (elite two-way wing)", rec, {"s": 0.40}, dict(W, q=2.5, a=2.5),
         extra=lambda p: hbin(p, 66, 77) and ori_class(p["o"]) != "post"
                         and p["q"] >= lq and p["a"] >= la and p["arrival"] >= 0.70,
         note="HARD: 6'6-6'9, non-post, latent skill AND ath both above the LeBron-class cut, arrived",
         fallback_seeds=MULTI_SEEDS)
    # worst recruitable GUARD = lowest RScore within the perimeter classification
    guards = [p for p in rec if ori_class(p["o"]) == "perimeter" and p["Height"] <= 65]
    if guards:
        wg = min(guards, key=lambda p: p["_r"])
        P("  %-26s : lowest RScore among recruitable perimeter guards (the guard floor):" % "Worst recruitable guard")
        P("      " + fmt_card_summary(wg))
    P("")
    P("-- BELOW THE LINE (non-recruitable; confirms the line falls right) " + "-" * 25)
    # 5'8 post-scorer: size caps an interior skill -- show the nearest one and which side he lands
    ps = find_nearest(coh, {"o": 0.75, "Height": 42, "q": 0.70, "s": 0.80},
                      dict(W, s=1.5), extra=lambda p: hbin(p, 40, 44) and ori_class(p["o"]) == "post"
                      and p["weapon"] in ("PostMoves", "Close") and p["current"][p["weapon"]] >= 55)
    if ps:
        side = "RECRUITABLE (marginal)" if ps["_r"] >= R_LINE else "below line"
        P("  %-26s : 5'8 elite post skill, height-penalized -> lands %s (RScore=%.1f vs line %.1f)" %
          ("5'8 post-scorer (size-capped)", side, ps["_r"], R_LINE))
        P("      " + fmt_card_summary(ps))
    show("5'10 weak guard", nonrec, {"q": 0.30, "s": 0.40}, W,
         extra=lambda p: hbin(p, 45, 50) and ori_class(p["o"]) == "perimeter",
         note="HARD: 5'10-5'11, perimeter, skills beneath the line -> correctly non-recruitable")
    # 6'1 post: show where he lands relative to the line
    p61 = find_nearest(coh, {"o": 0.75, "Height": 53, "q": 0.55, "s": 0.60}, dict(W))
    side = "RECRUITABLE" if p61["_r"] >= R_LINE else "below line"
    P("  %-26s : borderline edge case -> lands %s (RScore=%.1f vs line %.1f)" %
      ("6'1 post", side, p61["_r"], R_LINE))
    P("      " + fmt_card_summary(p61))
    P("")
    # ---- (I2) marquee unicorn frequency across seeds ---------------------------------
    wemby_filt = lambda p: (p["Height"] >= 86 and high_for_size_residual(p) >= 8
                            and p["current"]["BallHandling"] >= 52 and p["current"]["Outside"] >= 50
                            and p["current"]["PostMoves"] >= 50)
    lebron_filt = lambda p: (66 <= p["Height"] <= 77 and ori_class(p["o"]) != "post"
                             and p["q"] >= lq and p["a"] >= la and p["arrival"] >= 0.70)
    P("[I2] MARQUEE UNICORN FREQUENCY across %d seeds (recruitable pool) -- meant to be rare" % len(MULTI_SEEDS))
    for name, filt in (("Wembanyama inside-out", wemby_filt), ("LeBron two-way wing", lebron_filt)):
        counts = [sum(1 for p in (rec if sd == SEED else recruitable_of(build_cohort(sd))) if filt(p))
                  for sd in MULTI_SEEDS]
        worlds = sum(1 for c in counts if c > 0)
        P("    %-24s present in %d/%d worlds   per-world counts %s" % (name, worlds, len(MULTI_SEEDS), counts))
    P("")
    P("=" * 92)
    P("END -- tune the five dials against this table; oracle locks as the port spec on sign-off.")
    P("=" * 92)

    # S42.2: opt-in fixture dump -- trailing announce lines ONLY, appended after the full normal
    # audit. The default run (write_fixture=False) adds nothing and stays byte-identical.
    if write_fixture:
        for ln in dump_replay_fixture(SEED, coh):
            P(ln)

    out = "\n".join(L)
    print(out)
    with open("gen_pass2_skillfirst_oracle_output.txt", "w") as f:
        f.write(out + "\n")

if __name__ == "__main__":
    main(write_fixture=("--fixture" in sys.argv[1:]))
