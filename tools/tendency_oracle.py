#!/usr/bin/env python3
"""
Skill-derived shot-tendency derivation — LOCKED SPEC ORACLE (2026-07-04).

Status: the constants and shapes below were reviewed and approved by Emmett
(archetype diet table + structural checks). They are the approved spec for the
generator build pass: the C# port mirrors this file constant-for-constant and
stage-for-stage. If the C# and this oracle ever disagree, the oracle wins.
Future tuning happens HERE first (new approval), never in the C# alone.

Mirrors the intended C# derivation (the table GenRoles[..].Tendencies is deleted;
each player's 5 zone tendencies fall out of his own drawn skills). Proves STRUCTURAL
claims across seeds; prints league three-share as a DIAGNOSTIC (not a gate).

DETERMINISM RULING (locked): the derivation is a pure function of the final rating
map. No player-style seed, no manufactured tendency noise. Identical final ratings
yield identical integer diets; population variety comes from varied drawn skills,
and shot VOLUME differences are usage/hierarchy's job, not this function's.

Zone order everywhere: Rim, Short, Mid, Long, Three  (matches GenTendencies[]).
All tie-breaks (largest-remainder rounding, the 99-cap redistribution) resolve in
that fixed zone order.
"""
import json, random, statistics

Z = ["Rim", "Short", "Mid", "Long", "Three"]

def clamp(x, lo, hi): return lo if x < lo else hi if x > hi else x
def gate(x, lo, hi):   return clamp((x - lo) / (hi - lo), 0.0, 1.0)

# ---------------------------------------------------------------------------
# THE DERIVATION
# ---------------------------------------------------------------------------
# ---- first-cut constants ----
CREATION_LO, CREATION_HI = 45, 78          # what "having a creation game" means
MID_CRED_LO, MID_CRED_HI   = 44, 62        # a mid jumper is a real shot above here (catch-&-shoot credible)
THREE_CRED_LO, THREE_CRED_HI = 34, 56      # a three is a real shot above here; below it he's left open, doesn't fire
RIM_FED_W, RIM_CREATE_W  = 0.72, 0.60      # rim = fed finish + self-created downhill (downhill is primary for creators)
FLOATER_SCALE            = 0.55            # the floater is a secondary/counter shot, below the rim it replaces
POST_TOUCH_GATE          = (55, 85)        # a post touch needs a REAL post game, not ordinary PostMoves
LONG_GUARD_CAP           = 46.0            # max raw long from the star pull-up path
LONG_STRETCH_CAP         = 46.0            # max raw long from the stretch-pop path (lower bar, modest size)
GUARD_CREATE_GATE        = (66, 86)        # dominant-only: both factors must be near-elite
GUARD_PULLUP_GATE        = (64, 86)
STRETCH_PLAUS_GATE       = (58, 80)        # frontcourt body plausibility (NO shooting in here)
STRETCH_CRED_GATE        = (48, 74)        # catch-&-shoot credibility (ALL shooting in here); lower bar
GAMMA_BASE, GAMMA_SHAPE, GAMMA_DEFICIT = 1.10, 2.30, 1.90
CREDIBLE_CEILING         = 85.0            # a top-2 average at/above this earns full flatness
MARGIN_BLEED             = 0.07            # porousness of the zone walls: each zone spills this
                                           # fraction of the gap into its distance-ladder neighbors.
                                           # (foot on the line, bumped off the rim, chased off the arc)
FLOOR_INSIDE             = 0.025           # the layup, floater, wide-open 12-footer basketball hands everyone
FLOOR_LONG_PERIM         = 0.030           # a perimeter player pulls up from midrange a few times a year
FLOOR_THREE_PERIM        = 0.045           # a perimeter player ALWAYS launches a few (kick-out, heave) —
                                           # gated so a paint big who never steps to the arc still takes zero

def raw_signals(a):
    """a: dict of 0-99 attributes -> five raw per-zone capability signals (0-99)."""
    creation = gate(0.50*a["SelfCreation"] + 0.30*a["BallHandling"] + 0.20*a["FirstStep"],
                    CREATION_LO, CREATION_HI)
    burst = (a["FirstStep"] + a["Speed"] + a["Vertical"]) / 3.0

    # RIM: fed finish (no creation needed) + self-created downhill (creation-gated, primary for creators)
    fed_rim    = a["Finishing"]
    create_rim = (0.55*burst + 0.45*a["BallHandling"]) * creation
    R_rim = clamp(RIM_FED_W*fed_rim + RIM_CREATE_W*create_rim, 0, 99)

    # MID: a real shot only if the jumper is credible (catch-&-shoot) OR he can create the pull-up
    mid_access = clamp(gate(a["Mid"], MID_CRED_LO, MID_CRED_HI) + 0.70*creation, 0, 1)
    R_mid = a["Mid"] * mid_access

    # THREE: catch-&-shoot spot-up — only if he can actually shoot it (else he's left open, doesn't fire)
    R_three = a["Outside"] * gate(a["Outside"], THREE_CRED_LO, THREE_CRED_HI)

    # SHORT: two routes that STACK (each earns its own volume), each near-zero without its real skill
    post_touch = gate(a["PostMoves"], *POST_TOUCH_GATE) * (0.70*a["PostMoves"] + 0.30*a["Close"])
    floater    = (0.45*max(a["Close"], a["Finishing"]) + 0.30*a["BallHandling"]
                  + 0.25*max(a["SelfCreation"], a["FirstStep"])) * creation * FLOATER_SCALE
    R_short = clamp(post_touch + floater, 0, 99)

    # LONG: two independent capped gated paths
    creation_style  = 0.7*a["SelfCreation"] + 0.3*a["BallHandling"]
    pullup_shooting = 0.7*a["Mid"] + 0.3*a["Outside"]
    g_guard = gate(creation_style, *GUARD_CREATE_GATE) * gate(pullup_shooting, *GUARD_PULLUP_GATE)
    guard_long = LONG_GUARD_CAP * g_guard

    plaus = 0.55*a["Height"] + 0.20*a["Weight"] + 0.15*a["Screening"] + 0.10*a["PostMoves"]  # NO shooting
    cred  = 0.7*a["Mid"] + 0.3*a["Outside"]                                                   # ALL shooting
    g_stretch = gate(plaus, *STRETCH_PLAUS_GATE) * gate(cred, *STRETCH_CRED_GATE)
    stretch_long = LONG_STRETCH_CAP * g_stretch

    R_long = clamp(guard_long + stretch_long, 0, 99)

    return [R_rim, R_short, R_mid, R_long, R_three]

def peakedness_gamma(R):
    """Two inputs: lopsided shape AND absolute capability. Both push spikier."""
    rmax  = max(R); rmean = sum(R)/len(R)
    lop   = 0.0 if rmax == 0 else (rmax - rmean)/rmax          # relative shape
    top2  = sum(sorted(R, reverse=True)[:2]) / 2.0
    defic = clamp(1 - top2/CREDIBLE_CEILING, 0, 1)             # absolute capability deficit
    return clamp(GAMMA_BASE + GAMMA_SHAPE*lop + GAMMA_DEFICIT*defic, 1.0, 6.0)

def to_int_diet(weights):
    """Normalize to ints summing to 100, each <=99 (Player.Validate ceiling).
    DETERMINISTIC TIE-BREAKS, locked for the C# port:
      - largest-remainder rounding: remainders sorted descending; equal remainders
        resolve in zone order (Rim, Short, Mid, Long, Three);
      - 99-cap redistribution: overflow moves to the smallest zone; equal smallest
        resolves to the earliest zone in the same fixed order."""
    s = sum(weights)
    if s <= 0: weights = [1.0]*len(weights); s = len(weights)
    raw = [100*w/s for w in weights]
    floor = [int(x) for x in raw]
    rem = 100 - sum(floor)
    order = sorted(range(len(raw)), key=lambda i: (-(raw[i]-floor[i]), i))
    for k in range(rem): floor[order[k]] += 1
    # ceiling guard: no single zone may be 100
    for i in range(len(floor)):
        if floor[i] >= 100:
            j = min(range(len(floor)), key=lambda k: (floor[k], k))
            floor[i] -= 1; floor[j] += 1
    return floor

def bleed_margins(w):
    """The zone walls are porous. Each zone spills MARGIN_BLEED of the gap to its
    neighbors on the distance ladder Rim-Short-Mid-Long-Three. Conserves the total;
    shaves impossible peaks; fills the in-between shots a clean diet drops to zero."""
    s = sum(w)
    if s <= 0: return w
    d = [x/s for x in w]
    nbr = [[1], [0, 2], [1, 3], [2, 4], [3]]   # rim, short, mid, long, three
    return [d[i] + MARGIN_BLEED*sum(d[j]-d[i] for j in nbr[i]) for i in range(5)]

def opportunity_floor(w, a):
    """No zone a player can plausibly reach is ever exactly zero. Inside shots are handed
    to everyone; perimeter shots only to players who actually operate out there (a paint
    big who never steps to the arc still takes zero threes).

    DEFERRED (Roll G, separate add-in): the emergency heave. A true non-shooter still puts
    up 2-3 threes over a CAREER (~0.2%) from buzzer/desperation. That is below integer-
    tendency resolution and belongs as a tiny ~0.2% floor on every zone when Roll G builds
    the pie (fractional weights), NOT in the authored tendency here. This function keeps a
    genuine non-shooter's three at 0; Roll G floors it nonzero at shot time."""
    s = sum(w)
    d = [x/s for x in w] if s > 0 else list(w)
    perim = clamp(max(1 - gate(a["Height"], 68, 79), gate(a["BallHandling"], 45, 70)), 0, 1)
    floors = [FLOOR_INSIDE, FLOOR_INSIDE, FLOOR_INSIDE,
              FLOOR_LONG_PERIM*perim, FLOOR_THREE_PERIM*perim]
    return [max(d[i], floors[i]) for i in range(5)]

def derive(a):
    R = raw_signals(a)
    g = peakedness_gamma(R)
    w = [r**g for r in R]
    w = bleed_margins(w)
    w = opportunity_floor(w, a)
    return to_int_diet(w), R, g

# ---------------------------------------------------------------------------
# ARCHETYPES  (hand-set profiles, the readable proof)
# ---------------------------------------------------------------------------
def P(**kw):
    base = dict(Close=48, Mid=48, Outside=45, Finishing=50, FreeThrow=60, FoulDrawing=50,
                BallHandling=50, Passing=50, Playmaking=50, SelfCreation=48, PostMoves=45,
                OffBallMovement=50, Screening=45, Height=60, Wingspan=60, Weight=55,
                Strength=55, Speed=55, Quickness=55, FirstStep=55, Vertical=55, Endurance=55)
    base.update(kw); return base

ARCH = {
 "Pure spot-up shooter":       P(Outside=88, Mid=62, SelfCreation=42, BallHandling=50, Finishing=48, FirstStep=48),
 "3&D wing (catch-shoot)":     P(Outside=82, Mid=55, SelfCreation=46, BallHandling=52, Height=70, Finishing=58),
 "Slasher (handle+hops, no J)":P(Outside=44, Mid=47, Finishing=74, SelfCreation=80, BallHandling=82, FirstStep=84, Speed=80, Vertical=78),
 "Floor general (balanced G)": P(Outside=66, Mid=68, Finishing=66, SelfCreation=72, BallHandling=80, Playmaking=82, FirstStep=70, Speed=68),
 "Wing scorer (multi-level)":  P(Outside=78, Mid=80, Finishing=74, SelfCreation=76, BallHandling=72, Height=70, FirstStep=70, Speed=68, Vertical=68),
 "Mid-range maestro (CP3)":    P(Outside=74, Mid=90, Finishing=68, SelfCreation=86, BallHandling=86, Playmaking=84, FirstStep=72),
 "Post scorer (bruiser)":      P(Outside=20, Mid=45, Finishing=80, PostMoves=84, Close=80, Strength=82, Height=83, Weight=85, SelfCreation=30, BallHandling=30),
 "Rim runner (lob/putback)":   P(Outside=8, Mid=42, Finishing=86, Screening=80, Vertical=82, Height=83, Weight=82, PostMoves=48, SelfCreation=20, BallHandling=25, Close=52),
 "Stretch big (3pt shooter)":  P(Outside=74, Mid=68, Finishing=68, PostMoves=50, Screening=74, Height=82, Weight=80, Close=58, SelfCreation=35, BallHandling=38),
 "Pick-&-pop big (mid, long2)":P(Outside=44, Mid=82, Finishing=68, PostMoves=52, Screening=74, Height=82, Weight=80, Close=58, SelfCreation=35, BallHandling=38),
 "Weak non-shooter (fringe)":  P(Outside=40, Mid=42, Finishing=46, Close=44, PostMoves=40, SelfCreation=40, BallHandling=42, FirstStep=44, Height=64),
 "Weak shooter (fringe)":      P(Outside=64, Mid=46, Finishing=44, Close=42, PostMoves=38, SelfCreation=40, BallHandling=44, FirstStep=42, Height=60),
}

def print_arch():
    print(f"{'Archetype':<30}{'Rim':>5}{'Short':>6}{'Mid':>5}{'Long':>5}{'Three':>6}   gamma")
    print("-"*72)
    for name, a in ARCH.items():
        diet, R, g = derive(a)
        print(f"{name:<30}" + "".join(f"{d:>5}" if i==0 else f"{d:>6}" if i in(1,4) else f"{d:>5}"
              for i,d in enumerate(diet)) + f"   {g:.2f}")

# ---------------------------------------------------------------------------
# POPULATION  (approximate generator-mirroring draw; three-share DIAGNOSTIC)
# ---------------------------------------------------------------------------
SIZE = {"G":(52,64,40,52), "W":(63,76,52,64), "B":(76,90,66,78)}
EMPH = {
 "FloorGeneral":("G",{"Playmaking","Passing","BallHandling","BasketballIQ","Discipline"}),
 "PassFirstGuard":("G",{"Passing","Playmaking","BallHandling","OffBallMovement"}),
 "PerimeterShooter":("G",{"Outside","OffBallMovement","Mid"}),
 "Slasher":("G",{"FirstStep","Finishing","BallHandling","SelfCreation"}),
 "ThreeAndDWing":("W",{"Outside","PerimeterDefense","OffBallDefense","HelpDefense"}),
 "WingScorer":("W",{"Mid","Outside","SelfCreation","Finishing"}),
 "PostScorer":("B",{"PostMoves","Close","Finishing","Strength"}),
 "RimRunner":("B",{"Finishing","Screening","OffensiveRebounding","Vertical"}),
 "AthleticBig":("B",{"Finishing","RimProtection","DefensiveRebounding","Vertical","Strength"}),
}
SIZE_R = {"Height","Wingspan","Weight","OffensiveRebounding","DefensiveRebounding"}
ATH_R  = {"Strength","Speed","Quickness","FirstStep","Vertical","Endurance","Hustle"}
HOLES  = {"G":{"RimProtection","PostDefense","PostMoves","Screening","OffensiveRebounding"},
          "W":{"RimProtection","PostMoves","Screening"},
          "B":{"Outside","Mid","BallHandling","SelfCreation","Playmaking","Passing",
               "PerimeterDefense","Steals","OffBallMovement"}}
NEEDED = ["Close","Mid","Outside","Finishing","BallHandling","SelfCreation","PostMoves",
          "Screening","Height","Weight","Strength","Speed","Quickness","FirstStep","Vertical"]

def leg_of(rt): return "SIZE" if rt in SIZE_R else "ATH" if rt in ATH_R else "SKILL"

def draw_player(role, legs, rng):
    pos, emph = EMPH[role]
    pLo,pHi,oLo,oHi = SIZE[pos]
    prio = (["SKILL","ATH","SIZE"] if pos=="G" else ["SIZE","SKILL","ATH"] if pos=="B"
            else ["SKILL"] + (["ATH","SIZE"] if rng.random()<0.5 else ["SIZE","ATH"]))
    plus = set(prio[:legs])
    a = {}
    for rt in NEEDED:
        lg = leg_of(rt)
        if lg == "SIZE":
            v = rng.randint(pLo,pHi) if "SIZE" in plus else rng.randint(oLo,oHi)
        elif lg in plus:
            v = rng.randint(78,88) if rt in emph else rng.randint(70,78)
            if lg=="ATH" and pos=="B": v = max(0, v-8)
        else:
            v = rng.randint(0,58) if (rt in HOLES[pos] and rt not in emph) else rng.randint(44,58)
            if lg=="ATH" and pos=="B": v = max(0, v-8)
        a[rt] = v
    return a

ROSTER_POS = ["G"]*4 + ["W"]*3 + ["B"]*3          # approximate 10-man shape
ROLES_BY_POS = {"G":["FloorGeneral","PassFirstGuard","PerimeterShooter","Slasher"],
                "W":["ThreeAndDWing","WingScorer"], "B":["PostScorer","RimRunner","AthleticBig"]}

def population(nteams=800, seed=20260703):
    rng = random.Random(seed)
    three_shares, all_diets = [], []
    for _ in range(nteams):
        for pos in ROSTER_POS:
            role = rng.choice(ROLES_BY_POS[pos])
            u = rng.random(); legs = 3 if u<0.12 else 2 if u<0.50 else 1
            a = draw_player(role, legs, rng)
            diet,_,_ = derive(a)
            three_shares.append(diet[4]); all_diets.append(diet)
    means = [statistics.mean(d[i] for d in all_diets) for i in range(5)]
    return three_shares, means, all_diets

# ---------------------------------------------------------------------------
# GOLDEN PARITY VECTORS  (exact port proof for the C# build)
# ---------------------------------------------------------------------------
# Named input rating maps -> exact expected integer diets. The C# DeriveTendencies
# must reproduce every diet ELEMENT-FOR-ELEMENT in zone order Rim,Short,Mid,Long,
# Three. Includes rounding/tie traps beyond the basketball archetypes.
GOLDEN_EXTRA = {
 "All-low (20s across)":      P(**{k:22 for k in ("Close","Mid","Outside","Finishing","PostMoves","BallHandling",
                                                  "SelfCreation","FirstStep","Speed","Vertical","Screening")},
                                Height=60, Weight=55),
 "All-high (90s across)":     P(**{k:92 for k in ("Close","Mid","Outside","Finishing","PostMoves","BallHandling",
                                                  "SelfCreation","FirstStep","Speed","Vertical","Screening")},
                                Height=75, Weight=70),
 "All-flat 50s (tie trap)":   P(**{k:50 for k in ("Close","Mid","Outside","Finishing","PostMoves","BallHandling",
                                                  "SelfCreation","FirstStep","Speed","Vertical","Screening")},
                                Height=60, Weight=55),
 "Equal-remainder trap":      P(Close=50, Mid=57, Outside=57, Finishing=57, PostMoves=50, BallHandling=57,
                                SelfCreation=57, FirstStep=57, Speed=57, Vertical=57, Screening=50,
                                Height=60, Weight=55),
 "Near-cap single zone":      P(Close=20, Mid=10, Outside=10, Finishing=97, PostMoves=10, BallHandling=15,
                                SelfCreation=10, FirstStep=90, Speed=88, Vertical=92, Screening=30,
                                Height=84, Weight=84),
}

def golden_vectors():
    out = []
    for name, a in list(ARCH.items()) + list(GOLDEN_EXTRA.items()):
        diet, _, _ = derive(a)
        out.append({"name": name, "ratings": a, "expected": diet})
    return out

def emit_golden(path="tendency_golden.json"):
    with open(path, "w") as f:
        json.dump({"zoneOrder": Z, "vectors": golden_vectors()}, f, indent=1, sort_keys=True)
    print(f"golden parity fixture written: {path} ({len(golden_vectors())} vectors)")

# ---------------------------------------------------------------------------
# STRUCTURAL CHECKS  (the real gate; three-share is only printed)
# ---------------------------------------------------------------------------
def checks():
    res = []
    def chk(name, ok): res.append((name, ok))

    d_shoot = derive(ARCH["Pure spot-up shooter"])[0]
    chk("pure shooter is three-dominant (Three>=55, top zone)", d_shoot[4]>=55 and d_shoot[4]==max(d_shoot))
    chk("pure shooter still has a foot-on-line long-two tail (Long>=2)", d_shoot[3]>=2)

    # no archetype is walled at a single zone — the margins always leak
    walled = max((max(derive(a)[0]) for a in ARCH.values()))
    chk("no archetype is walled at one zone (every diet's max <= 95)", walled<=95)

    d_slash = derive(ARCH["Slasher (handle+hops, no J)"])[0]
    chk("slasher is rim-heavy (Rim top zone)", d_slash[0]==max(d_slash))
    chk("slasher takes almost no long two (Long<=4)", d_slash[3]<=4)
    chk("perimeter player never takes ZERO threes (slasher Three>=2)", d_slash[4]>=2)

    d_rr = derive(ARCH["Rim runner (lob/putback)"])[0]
    chk("rim runner collapses to rim (Rim>=50, top zone)", d_rr[0]>=50 and d_rr[0]==max(d_rr))
    chk("rim runner catches the occasional 12-footer (Mid>=1)", d_rr[2]>=1)
    chk("paint big who never steps out takes zero threes (rim runner Three==0)", d_rr[4]==0)

    d_ml = derive(ARCH["Wing scorer (multi-level)"])[0]
    chk("multi-level scorer is flatter than shooter (max<=45)", max(d_ml)<=45)
    chk("multi-level flatter than shooter (its max < shooter's max)", max(d_ml)<max(d_shoot))

    d_wns = derive(ARCH["Weak non-shooter (fringe)"])[0]
    chk("weak non-shooter tilts to rim, not spread out (Rim top)", d_wns[0]==max(d_wns))
    d_ws  = derive(ARCH["Weak shooter (fringe)"])[0]
    chk("weak shooter tilts to three (Three top)", d_ws[4]==max(d_ws))

    d_pop = derive(ARCH["Pick-&-pop big (mid, long2)"])[0]
    d_st  = derive(ARCH["Stretch big (3pt shooter)"])[0]
    d_ps  = derive(ARCH["Post scorer (bruiser)"])[0]
    chk("pick-&-pop big earns a real long two (Long>=6)", d_pop[3]>=6)
    chk("pick-&-pop big > traditional post at long two", d_pop[3]>d_ps[3])
    chk("3pt stretch big spaces with the three, not the long two (Three>Long)", d_st[4]>d_st[3])

    d_cp3 = derive(ARCH["Mid-range maestro (CP3)"])[0]
    chk("maestro's long two is a prominent, signature part of his game (Long>=8)", d_cp3[3]>=8)
    chk("maestro takes far more long twos than a rim-first slasher", d_cp3[3]>d_slash[3])

    # conservation + validity across a random population
    rng = random.Random(999); bad = 0
    for _ in range(4000):
        role = rng.choice(list(EMPH)); u=rng.random(); legs=3 if u<0.12 else 2 if u<0.5 else 1
        d,_,_ = derive(draw_player(role, legs, rng))
        if sum(d)!=100 or any(x<0 or x>99 for x in d) or sum(d)<=0: bad += 1
    chk("all diets sum to 100, each in [0,99], sum>0 (4000 players)", bad==0)

    return res

# ---------------------------------------------------------------------------
if __name__ == "__main__":
    print("="*72); print("ARCHETYPE SHOT DIETS  (Rim / Short / Mid / Long / Three, sum=100)"); print("="*72)
    print_arch()

    print(); print("="*72)
    print("POPULATION DIAGNOSTIC — mean generated NEUTRAL TENDENCY diet")
    print("(directional only: the sampler approximates, not reproduces, the real")
    print(" roster pipeline; and tendency is not realized FGA share — Roll G bends it)")
    print("="*72)
    for seed in (20260703, 40, 12345):
        shares, means, _ = population(seed=seed)
        print(f"seed {seed:<9}  mean league diet  "
              f"Rim {means[0]:4.1f}  Short {means[1]:4.1f}  Mid {means[2]:4.1f}  "
              f"Long {means[3]:4.1f}  Three {means[4]:4.1f}")

    print(); print("="*72); print("STRUCTURAL CHECKS  (the actual gate)"); print("="*72)
    allok = True
    for name, ok in checks():
        allok &= ok; print(f"  [{'PASS' if ok else 'FAIL'}]  {name}")
    print("-"*72); print("  ALL STRUCTURAL CHECKS PASS" if allok else "  *** SOME CHECKS FAILED ***")

    print()
    emit_golden()
