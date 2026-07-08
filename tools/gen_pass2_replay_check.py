#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_pass2_replay_check.py  --  S42.2 replay round-trip proof / the C# port's reference reader.

Reads the committed math-replay fixture (tools/gen_pass2_replay_fixture_s42_2.json) and, using
ONLY the recorded raw draws + the frozen constants, REIMPLEMENTS every deterministic transform
FROM SCRATCH -- no call into the oracle's functions, no RNG anywhere -- and checks every named
checkpoint for every player. If this passes, the fixture is proven SUFFICIENT for deterministic
math replay (a real replay fixture, not a stored-output fixture), which is exactly what the C#
port's parity gate will do.

Constants guardrail (ruled at the S42.2 check-in): constants are imported from the oracle module
and asserted against the fixture's constants echo BEFORE any replay runs -- any mismatch is a
loud, itemized failure (exit 2). Constants drift between fixture-authoring and check time is
impossible to miss; the oracle source stays the single canonical home of the values.

Comparison convention (the contract, echoed in the fixture header): integer checkpoints EXACT;
float checkpoints |diff| <= 1e-9. In this same-interpreter Python replay the observed float
deviation is expected to be exactly 0.0; the tolerance exists for the cross-language C# gate,
where exp/tanh may land ~1 ulp apart between libm and .NET.

Run:  python tools/gen_pass2_replay_check.py            (fixture read from beside this script)
      python tools/gen_pass2_replay_check.py <path>     (explicit fixture path)

Failure style: print/collect, don't stop at the first -- every mismatch is reported (first 20
printed in full), and the exit code carries the verdict (0 pass, 1 replay mismatch, 2 constants
drift, 3 fixture missing/unreadable).
"""

import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_pass2_skillfirst_oracle as O   # CONSTANTS ONLY -- no oracle function is called below

TOL = 1e-9

# ----------------------------------------------------------------------------
# local reimplementations (the whole point: formulas rebuilt from scratch)
# ----------------------------------------------------------------------------
def clamp(x, lo, hi):
    return lo if x < lo else (hi if x > hi else x)

def derive_ft_local(outside, ft_idio, height):
    val = (O.FT_CENTER + O.FT_OUT_SPAN * math.tanh((outside - 50.0) / O.FT_OUT_SCALE)
           - O.FT_HEIGHT_COEF * ((height - 55.0) / 40.0) + ft_idio)
    return int(clamp(round(val), O.FT_MIN, O.FT_MAX))

def first_argmax(keys, score):
    """The contractual tie rule: scan in list order, replace only on strictly-greater."""
    best_k, best_v = None, None
    for k in keys:
        v = score(k)
        if best_v is None or v > best_v:
            best_k, best_v = k, v
    return best_k

def rscore_parts_local(cur, card, height, o):
    ath = sum(card[k] for k in ("Strength", "Speed", "Quickness", "FirstStep", "Vertical")) / 5.0
    access        = max(cur["BallHandling"], cur["OffBallMovement"], cur["Outside"]) / 99.0
    mid_eff       = cur["Mid"] * min(1.0, access / 0.45)
    entry_p       = max(cur["Outside"], cur["BallHandling"], mid_eff)
    perim_support = (cur["Passing"] + cur["Playmaking"] + cur["SelfCreation"] + cur["OffBallMovement"]) / 4.0
    perim_def     = max(cur["PerimeterDefense"], cur["Steals"], cur["OffBallDefense"])
    perim_val = max(0.0, entry_p - 20) * (0.55 + 0.30 * perim_support / 99 + 0.15 * perim_def / 99) + 0.14 * ath
    post_skill    = max(cur["RimProtection"], cur["PostMoves"], cur["Close"], cur["Finishing"], cur["PostDefense"])
    post_support  = (cur["Screening"] + cur["PostDefense"] + cur["RimProtection"]) / 3.0
    height_factor = clamp(O.HF_LO + O.HF_RANGE / (1.0 + math.exp(-O.HF_STEEP * (height - O.HF_MID))), O.HF_LO, O.HF_HI)
    skill_val     = max(0.0, post_skill - 24) * (0.60 + 0.40 * post_support / 99) * height_factor
    glass         = (card["OffensiveRebounding"] + card["DefensiveRebounding"]) / 2.0
    reb_val       = glass * 0.16 * min(1.0, post_skill / 45.0)
    low_taper     = clamp((height - 40.0) / (O.LOW_TAPER_TOP - 40.0), O.LOW_TAPER_FLOOR, 1.0)
    post_val = (skill_val + reb_val + 0.10 * ath * min(1.0, height_factor)) * low_taper
    perim_w = 1.00 - O.PERIM_OW * o
    post_w  = (1.0 - O.POST_OW) + O.POST_OW * o
    wperim, wpost = perim_w * perim_val, post_w * post_val
    total = max(wperim, wpost)
    return {"rscore": total, "which": "perim" if wperim >= wpost else "post",
            "entry_p": entry_p, "perim_support": perim_support, "perim_def": perim_def, "perim_val": perim_val,
            "post_skill": post_skill, "glass": glass, "reb_val": reb_val, "skill_val": skill_val,
            "post_support": post_support, "post_val": post_val, "ath": ath,
            "wperim": wperim, "wpost": wpost, "o": o}

# ----------------------------------------------------------------------------
# failure collection ("print/diff, don't assert": report everything, exit code carries the verdict)
# ----------------------------------------------------------------------------
FAILS = []
MAXDEV = [0.0]
CHECKS = [0]

def chk_int(idx, field, expected, got):
    CHECKS[0] += 1
    if expected != got:
        FAILS.append((idx, field, expected, got))

def chk_float(idx, field, expected, got):
    CHECKS[0] += 1
    d = abs(expected - got)
    if d > MAXDEV[0]:
        MAXDEV[0] = d
    if d > TOL:
        FAILS.append((idx, field, expected, got))

def chk_eq(idx, field, expected, got):
    CHECKS[0] += 1
    if expected != got:
        FAILS.append((idx, field, expected, got))

# ----------------------------------------------------------------------------
def check_constants(echo):
    """The loud tripwire: every echoed constant must equal the oracle module's value exactly."""
    drift = []
    for name, val in echo.items():
        live = getattr(O, name)
        if isinstance(live, tuple):
            live = list(live)
        if live != val:
            drift.append((name, val, live))
    return drift

def replay_player(row):
    idx = row["index"]
    d, cp = row["draws"], row["checkpoints"]
    card_fx, rec_fx = row["card"], row["recruiting"]

    o, q, a, s = d["o"], d["q"], d["a"], d["s"]

    # orientation axis
    oaxis = 2.0 * o - 1.0
    chk_float(idx, "oaxis", cp["oaxis"], oaxis)

    # height: logistic ceiling + the recorded branch
    oh       = 1.0 / (1.0 + math.exp(-O.HT_ORI_STEEP * (o - O.HT_ORI_MID)))
    mu       = O.HT_MU_PERIM + oh * (O.HT_MU_POST - O.HT_MU_PERIM)
    sigma_up = O.HT_SIGMA_UP_PERIM + oh * (O.HT_SIGMA_UP_POST - O.HT_SIGMA_UP_PERIM)
    chk_float(idx, "oh", cp["oh"], oh)
    chk_float(idx, "mu", cp["mu"], mu)
    chk_float(idx, "sigma_up", cp["sigma_up"], sigma_up)
    sel, branch, hn = d["height_branch_selector_raw"], d["height_branch"], d["height_noise_raw"]
    chk_eq(idx, "height_branch(consistency)", branch, "upper_gauss" if sel < 0.5 else "lower_exp")
    h_raw = mu + abs(hn) if branch == "upper_gauss" else mu - hn
    chk_float(idx, "h_raw", cp["h_raw"], h_raw)
    Height = int(round(clamp(h_raw, O.HT_MIN, O.HT_MAX)))
    chk_int(idx, "Height", cp["Height"], Height)

    # base skills from q + orientation suppression + recorded noise
    base = {}
    for k in O.DRAWN_SKILLS:
        supp = O.MISMATCH_STRENGTH * max(0.0, -oaxis * O.PAXIS[k])
        base[k] = q - supp + d["skill_noise"][k]
        chk_float(idx, "base.%s" % k, cp["base"][k], base[k])

    # eligibility + the two argmaxes (first-max tie rule, DRAWN_SKILLS scan order)
    eligible = [k for k in O.DRAWN_SKILLS
                if k not in O.WEAPON_EXCLUDE and max(0.0, -oaxis * O.PAXIS[k]) < O.WEAPON_MISMATCH_MAX]
    chk_eq(idx, "eligible", cp["eligible"], eligible)
    chk_eq(idx, "eligible(non-empty invariant)", True, len(eligible) > 0)
    pool = eligible if eligible else list(O.DRAWN_SKILLS)
    weapon_raw = first_argmax(pool, lambda k: base[k])
    weapon     = first_argmax(pool, lambda k: base[k] + O.WEAPON_CENSUS_OFFSET.get(k, 0.0))
    chk_eq(idx, "weapon_raw", cp["weapon_raw"], weapon_raw)
    chk_eq(idx, "weapon", cp["weapon"], weapon)

    # size + athletic card (bypasses expression)
    Wingspan = int(clamp(round(Height + d["wingspan_noise"]), O.HT_MIN, 99))
    chk_int(idx, "Wingspan", cp["Wingspan"], Wingspan)
    ath = {}
    ath_center = O.ATH_BASE_LO + a * (O.ATH_BASE_HI - O.ATH_BASE_LO)
    chk_float(idx, "ath_center", cp["ath_center"], ath_center)
    for k in O.ATH_KEYS:
        raw = ath_center + O.SIZE_COEF[k] * (Height - O.ATH_HEIGHT_CENTER) + d["ath_noise"][k]
        chk_float(idx, "ath_raw.%s" % k, cp["ath_raw"][k], raw)
        ath[k] = int(clamp(round(raw), 8, 99))
        chk_int(idx, "ath.%s" % k, card_fx[k], ath[k])
    Weight = int(clamp(round(30 + 0.40 * Height + 0.30 * ath["Strength"] + d["weight_noise"]), 20, 99))
    chk_int(idx, "Weight", cp["Weight"], Weight)
    post_bonus = 8.0 * o
    OREB = int(clamp(round(20 + 0.34 * Height + 0.14 * ath["Strength"] + post_bonus + d["oreb_noise"]), 8, 99))
    DREB = int(clamp(round(22 + 0.36 * Height + 0.18 * ath["Strength"] + post_bonus + d["dreb_noise"]), 8, 99))
    chk_int(idx, "OffensiveRebounding", cp["OffensiveRebounding"], OREB)
    chk_int(idx, "DefensiveRebounding", cp["DefensiveRebounding"], DREB)

    # arrival + expression
    arr_mean = O.ARR_PERIM - o * (O.ARR_PERIM - O.ARR_POST)
    chk_float(idx, "arr_mean", cp["arr_mean"], arr_mean)
    arrival = clamp(d["arrival_draw_raw"], 0.0, 1.0)
    chk_float(idx, "arrival", cp["arrival"], arrival)
    e = O.E_MIN + arrival * (1.0 - O.E_MIN)
    chk_float(idx, "e", cp["e"], e)

    # latent / current / FT / runway
    latent, current = {}, {}
    for k in O.DRAWN_SKILLS:
        t = base[k] + (s * O.WEAPON_BUMP if k == weapon else -s * O.SUPPORT_DRAIN)
        latent[k] = int(clamp(round(O.RATING_LO + t * O.RATING_SPAN), O.HOLE_FLOOR, 99))
        L = latent[k]
        current[k] = L if L <= O.EXPR_BASELINE else int(round(O.EXPR_BASELINE + e * (L - O.EXPR_BASELINE)))
    latent["FreeThrow"]  = derive_ft_local(latent["Outside"],  d["ft_idio"], Height)
    current["FreeThrow"] = derive_ft_local(current["Outside"], d["ft_idio"], Height)
    runway = {k: latent[k] - current[k] for k in O.SKILL_KEYS}
    for k in O.SKILL_KEYS:
        chk_int(idx, "latent.%s" % k, cp["latent"][k], latent[k])
        chk_int(idx, "current.%s" % k, cp["current"][k], current[k])
        chk_int(idx, "runway.%s" % k, cp["runway"][k], runway[k])
    chk_int(idx, "runway_total", cp["runway_total"], sum(runway.values()))

    # the full 33-key card
    card = {"Height": Height, "Wingspan": Wingspan, "Weight": Weight,
            "OffensiveRebounding": OREB, "DefensiveRebounding": DREB}
    card.update(ath)
    for k in O.SKILL_KEYS:
        card[k] = current[k]
    for k in O.ALL_KEYS:
        chk_int(idx, "card.%s" % k, card_fx[k], card[k])

    # age/class -- PLACEHOLDER-OUTPUT asserts (S42.1 ruling: values checked; formula not ported as spec)
    age = int(clamp(round(18 + O.AGE_ARR_SPAN * arrival + d["age_noise_raw"]), 17, 23))
    cls = "Fr" if age <= 18 else ("So" if age == 19 else ("Jr" if age <= 21 else "Sr"))
    chk_int(idx, "age(placeholder)", cp["age"], age)
    chk_eq(idx, "cls(placeholder)", cp["cls"], cls)

    # recruiting line, recomputed from the REPLAYED card (end-to-end)
    parts = rscore_parts_local(current, card, Height, o)
    fx_parts = rec_fx["rscore_parts"]
    chk_float(idx, "rscore", rec_fx["rscore"], parts["rscore"])
    for k, v in fx_parts.items():
        if isinstance(v, str):
            chk_eq(idx, "rscore_parts.%s" % k, v, parts[k])
        else:
            chk_float(idx, "rscore_parts.%s" % k, v, parts[k])

def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "gen_pass2_replay_fixture_s42_2.json")
    try:
        with open(path) as f:
            fx = json.load(f)
    except Exception as ex:
        print("REPLAY CHECK: cannot read fixture at %s (%s)" % (path, ex))
        sys.exit(3)

    schema, players = fx["schema"], fx["players"]
    print("=" * 92)
    print("S42.2 REPLAY ROUND-TRIP CHECK -- fixture %s (schema %s, seed %d, %d players)"
          % (os.path.basename(path), schema["schema_version"], schema["seed"], len(players)))
    print("  raw recorded draws + frozen constants ONLY; every formula reimplemented locally; no RNG.")
    print("=" * 92)

    drift = check_constants(schema["constants"])
    if drift:
        print("CONSTANTS TRIPWIRE: %d mismatch(es) between the fixture echo and the oracle module --" % len(drift))
        for name, echoed, live in drift:
            print("    %-24s fixture echo=%r   oracle=%r" % (name, echoed, live))
        print("VERDICT: FAIL (constants drift; replay not run)")
        sys.exit(2)
    print("constants echo vs oracle module: %d/%d match -- tripwire clear" %
          (len(schema["constants"]), len(schema["constants"])))

    for row in players:
        replay_player(row)

    print("players replayed: %d   field checks: %d   failures: %d" % (len(players), CHECKS[0], len(FAILS)))
    print("max float deviation observed: %.3e   (contract tolerance %.0e; same-interpreter replay "
          "is expected to be exactly 0)" % (MAXDEV[0], TOL))
    if FAILS:
        print("FIRST %d FAILURES:" % min(20, len(FAILS)))
        for idx, field, expected, got in FAILS[:20]:
            print("    player %-6d %-28s expected=%r  got=%r" % (idx, field, expected, got))
        print("VERDICT: FAIL")
        sys.exit(1)
    print("VERDICT: PASS -- the fixture is SUFFICIENT for deterministic math replay: every checkpoint,")
    print("card value, and recruiting field reproduced for every player from recorded inputs alone.")
    sys.exit(0)

if __name__ == "__main__":
    main()
