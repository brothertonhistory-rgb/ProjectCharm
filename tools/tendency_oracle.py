#!/usr/bin/env python3
"""
Skill-derived shot-tendency derivation — LOCKED SPEC ORACLE v3 (2026-07-22, S65).

Status: v3 is the THREE-VOLUME REVERSAL. Emmett's S65 ruling OVERTURNS the v2
"compressed frequency" model for the three (v2 rulings 1 and 4, struck in
effect): the S64 season read league 3PA rate 0.69 vs real ~0.39, with 71% of the
league's three-tendency mass coming from players rated under 35 Outside
(corr(Outside, ThreeTendency) = 0.130 on the S63 pool). That volume was by
design under v2; Emmett is overriding that design as the basketball authority.
The C# port mirrors this file constant-for-constant and stage-for-stage. If the
C# and this oracle ever disagree, the oracle wins. Future tuning happens HERE
first (new approval), never in the C# alone.

v3 RULINGS (Emmett, 2026-07-22) — the three-volume reversal:
  1. VOLUME FOLLOWS THE RATING (reverses v2 ruling 1 for the three). Two rules,
     blended by perimeter-ness:
     - PERIMETER: a floor, not a gate. Everyone out there shoots some by virtue
       of being out there (~20 a season in the flow) — no perimeter player reads
       zero three-tendency — but the bottom is a genuine floor, and tendency
       climbs with Outside so a real shooter fires far more. Below the shooter
       threshold the volume is STRUCTURAL (kick-outs, rhythm) and lives in the
       opportunity floor, share-pinned; the raw path is the SHOOTER'S climb.
     - INTERIOR: a hard volume threshold. A big who can't shoot basically
       doesn't (emergency heaves, a couple all season). A small rare allowance
       opens as Outside crosses ~30 (the wide-open ones), scaling so a genuine
       stretch big (~55-60+) reaches real arc volume. The threshold gates
       VOLUME, not percentage.
  2. THE PARADOX, REVERSED (reverses v2 ruling 4): a poor shooter with no other
     tools no longer skews three-heavy. His three sits near the structural
     floor REGARDLESS of how weak his other signals are (the floor is
     share-pinned, companion-independent); the rest of his diet falls to
     whatever he can least-badly do (usually the rim).
  3. THE EFFICIENCY HALF IS ALREADY PRICED — no special rule here. A 30-Outside
     big's rare threes are the wide-open ones and shoot decently via the
     existing context channels (C1 openness, S60 usage relief) on top of the
     make curve's honest pricing of his neutral ability; when volume sneaks up,
     his % falls toward his rating. This file owns TENDENCY only.

v2 RULINGS RETAINED: the era profile (culture stage, untouched); determinism;
nonzero-rating-=-capable narrowed to the interior EMERGENCY floor (a rating-8
big still fires a couple of desperation ones; only a literal Outside == 0
INTERIOR player reads zero — a perimeter player never reads zero, per ruling 1).

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
from pathlib import Path

Z = ["Rim", "Short", "Mid", "Long", "Three"]

def clamp(x, lo, hi): return lo if x < lo else hi if x > hi else x
def gate(x, lo, hi):   return clamp((x - lo) / (hi - lo), 0.0, 1.0)

# ---------------------------------------------------------------------------
# THE DERIVATION
# ---------------------------------------------------------------------------
# ---- v2 constants ----
CREATION_LO, CREATION_HI = 45, 78          # what "having a creation game" means
MID_CRED_LO, MID_CRED_HI   = 44, 62        # a mid jumper is a real shot above here (catch-&-shoot credible)
# THREE — v3 (S65 reversal): volume follows the rating. Two rules blended by
# perimeter-ness; each rule's FLOOR half lives in opportunity_floor (share-pinned,
# companion-independent), its RAW half here (the skill-responsive climb).
THREE_PERIM_MAIN = 38.0                       # the shooter's climb: main ramp height
THREE_PERIM_MAIN_LO, THREE_PERIM_MAIN_HI = 34, 54   # where the three becomes HIS shot, not just in-the-flow
THREE_PERIM_TOP  = 26.0                       # the elite extension on top of the main ramp
THREE_PERIM_TOP_LO, THREE_PERIM_TOP_HI = 50, 88
THREE_STRETCH = 46.0                          # interior stretch ramp height (real arc volume)
THREE_STRETCH_LO, THREE_STRETCH_HI = 38, 63   # a big earns real arc volume only through here
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
# THE ERA PROFILE (v2 ruling 3): weight-space multipliers applied AFTER peakedness,
# Rim/Short/Mid/Long/Three. Encodes the modern shot-selection culture, cleanly
# separated from individual capability. An earlier-era league is these 5 numbers.
ERA_PROFILE              = [1.00, 0.66, 0.44, 0.63, 2.44]
FLOOR_INSIDE             = 0.025           # the layup, floater, wide-open 12-footer basketball hands everyone
FLOOR_LONG_PERIM         = 0.030           # a perimeter player pulls up from midrange a few times a year
# The three floor — v3: a p-blend of the two rules' own floors (mirrors the raw
# path's blend so each rule lives on its own side of BOTH stages):
FLOOR_THREE_EMERGENCY    = 0.012           # interior, any Outside>0: the season's couple of desperation heaves
FLOOR_THREE_ALLOW        = 0.055           # the big's rare wide-open allowance (the threshold's small opening)...
FLOOR_THREE_ALLOW_LO, FLOOR_THREE_ALLOW_HI = 26, 40  # ...opening as Outside crosses ~30
FLOOR_THREE_PERIM_BASE   = 0.055           # everyone out there shoots his ~20 a season — no perimeter zero
FLOOR_THREE_PERIM_RAMP   = 0.115           # in-the-flow volume grows with credibility below the shooter threshold
FLOOR_THREE_PERIM_RAMP_LO, FLOOR_THREE_PERIM_RAMP_HI = 14, 46

def perimeter_ness(a):
    """How perimeter-shaped a player is, 0..1 — small OR a real handle qualifies.
    Shared by the three signal (path blend) and the opportunity floor."""
    return clamp(max(1 - gate(a["Height"], 68, 79), gate(a["BallHandling"], 45, 70)), 0, 1)

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

    # THREE (v3, the S65 reversal): volume follows the rating. Two ramps blended by
    # perimeter-ness — both are ZERO below their thresholds ON PURPOSE: a raw signal
    # here competes through gamma/era against the player's other signals, so any
    # nonzero raw handed to a below-threshold player becomes three-dominance for a
    # player with nothing else (the retired v2 paradox). Below-threshold volume is
    # the opportunity floor's job (share-pinned, companion-independent).
    #   Perimeter: the shooter's climb — main ramp plus an elite extension.
    #   Interior: the stretch ramp — a big earns arc volume only through a real rating.
    p = perimeter_ness(a)
    perim_three = (THREE_PERIM_MAIN * gate(a["Outside"], THREE_PERIM_MAIN_LO, THREE_PERIM_MAIN_HI)
                   + THREE_PERIM_TOP * gate(a["Outside"], THREE_PERIM_TOP_LO, THREE_PERIM_TOP_HI))
    inter_three = THREE_STRETCH * gate(a["Outside"], THREE_STRETCH_LO, THREE_STRETCH_HI)
    R_three = clamp(p*perim_three + (1-p)*inter_three, 0, 99)

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
    to everyone. The three (v3): a p-blend of the two rules' own floors, mirroring the
    raw path's blend so neither rule contaminates the other —
      INTERIOR floor = emergency heaves (any Outside>0, a couple all season) + the rare
        wide-open ALLOWANCE that opens as Outside crosses ~30 (the threshold's small
        opening; the raw stretch ramp takes over above it);
      PERIMETER floor = the structural in-the-flow volume (kick-outs, rhythm — everyone
        out there shoots some), growing modestly with credibility below the shooter
        threshold. Share-pinned HERE, not in the raw path, so it is companion-
        independent: a skill-less perimeter player reads the floor, never dominance.
    Only a literal Outside==0 INTERIOR player reads a zero three tendency (that
    residual ~0.2% buzzer heave remains Roll G's, at pie time, if ever needed); a
    perimeter player never reads zero (v3 ruling 1)."""
    s = sum(w)
    d = [x/s for x in w] if s > 0 else list(w)
    perim = perimeter_ness(a)
    capable = 1.0 if a["Outside"] > 0 else 0.0
    interior_floor = (FLOOR_THREE_EMERGENCY * capable
                      + FLOOR_THREE_ALLOW * gate(a["Outside"], FLOOR_THREE_ALLOW_LO, FLOOR_THREE_ALLOW_HI))
    perim_floor = (FLOOR_THREE_PERIM_BASE
                   + FLOOR_THREE_PERIM_RAMP * gate(a["Outside"], FLOOR_THREE_PERIM_RAMP_LO, FLOOR_THREE_PERIM_RAMP_HI))
    floors = [FLOOR_INSIDE, FLOOR_INSIDE, FLOOR_INSIDE,
              FLOOR_LONG_PERIM*perim,
              perim*perim_floor + (1-perim)*interior_floor]
    return [max(d[i], floors[i]) for i in range(5)]

def derive(a):
    R = raw_signals(a)
    g = peakedness_gamma(R)
    w = [R[i]**g * ERA_PROFILE[i] for i in range(5)]   # v2 ruling 3: the era stage
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
 "Weak-shooting spot-up (fringe)": P(Outside=40, Mid=42, Finishing=46, Close=44, PostMoves=40, SelfCreation=40, BallHandling=42, FirstStep=44, Height=64),
 "Weak shooter (fringe)":      P(Outside=64, Mid=46, Finishing=44, Close=42, PostMoves=38, SelfCreation=40, BallHandling=44, FirstStep=42, Height=60),
 # v2 probes (kept as vectors; their v3 diets are the reversal made visible):
 "Open-only guard (O=25)":     P(Outside=25, Mid=45, SelfCreation=50, BallHandling=55, FirstStep=58, Finishing=55, PerimeterDefense=75),
 "True non-shooter big (O=3)": P(Outside=3, Mid=30, Finishing=78, PostMoves=70, Close=70, Height=84, Weight=84, SelfCreation=25, BallHandling=25, Screening=70, Vertical=70),
 # v3 probes (S65): the interior threshold's two sides.
 "30-Outside big (threshold)": P(Outside=30, Mid=38, Finishing=72, PostMoves=68, Close=66, Height=84, Weight=82, SelfCreation=25, BallHandling=25, Screening=66, Vertical=62),
 "Pure stretch five (O=60)":   P(Outside=60, Mid=55, Finishing=62, PostMoves=52, Close=56, Height=85, Weight=80, SelfCreation=25, BallHandling=25, Screening=65, Vertical=50),
}

# ---------------------------------------------------------------------------
# S65 EMBEDDED POOL ROWS — real players from the live S63 pool, replayed via
# BuildDivvyPool(347, 20260720) and embedded here (the S64 scratch-dump pattern)
# so the sign-off table's rows are real players and the oracle replays without
# the dump. oldThree = the committed v2 final ThreeTendency, for the before/after.
# ---------------------------------------------------------------------------
def PR(**kw):
    return {k: kw[k] for k in ("Outside","Mid","Close","Finishing","PostMoves","BallHandling",
                               "SelfCreation","FirstStep","Speed","Vertical","Screening","Height","Weight")}

POOL_ROWS = {
 # name: (ratings, stamped position, v2 final ThreeTendency)
 "Pool_806 median guard (O=27)":     (PR(Outside=27, Mid=21, Close=35, Finishing=20, PostMoves=8, BallHandling=67,
                                         SelfCreation=13, FirstStep=63, Speed=60, Vertical=44, Screening=18, Height=60, Weight=90), "G", 87),
 "Pool_1399 cant-shoot wing (O=21)": (PR(Outside=21, Mid=20, Close=9, Finishing=16, PostMoves=12, BallHandling=15,
                                         SelfCreation=22, FirstStep=41, Speed=32, Vertical=33, Screening=17, Height=51, Weight=63), "W", 87),
 "Pool_46 perimeter control (O=49)": (PR(Outside=49, Mid=44, Close=28, Finishing=38, PostMoves=18, BallHandling=36,
                                         SelfCreation=38, FirstStep=55, Speed=43, Vertical=37, Screening=24, Height=43, Weight=62), "G", 86),
 "Pool_1389 3&D wing (O=70)":        (PR(Outside=70, Mid=71, Close=57, Finishing=55, PostMoves=45, BallHandling=55,
                                         SelfCreation=63, FirstStep=44, Speed=60, Vertical=52, Screening=56, Height=59, Weight=58), "W", 65),
 "Pool_97 elite sniper (O=99)":      (PR(Outside=99, Mid=59, Close=28, Finishing=44, PostMoves=8, BallHandling=42,
                                         SelfCreation=61, FirstStep=58, Speed=59, Vertical=48, Screening=29, Height=40, Weight=63), "G", 81),
 "Pool_2630 non-shooting big (O=8)": (PR(Outside=8, Mid=37, Close=8, Finishing=8, PostMoves=12, BallHandling=8,
                                         SelfCreation=8, FirstStep=46, Speed=44, Vertical=37, Screening=9, Height=81, Weight=84), "B", 4),
 "Pool_2717 30-Outside big":         (PR(Outside=30, Mid=37, Close=35, Finishing=36, PostMoves=35, BallHandling=27,
                                         SelfCreation=31, FirstStep=48, Speed=41, Vertical=60, Screening=34, Height=81, Weight=75), "B", 4),
 "Pool_2443 tall shooter (O=74)":    (PR(Outside=74, Mid=19, Close=26, Finishing=9, PostMoves=24, BallHandling=17,
                                         SelfCreation=19, FirstStep=42, Speed=51, Vertical=38, Screening=13, Height=79, Weight=73), "B", 87),
}
# NOTE (honest gap): no true "pure stretch five" (interior p≈0, Outside 55-68, real
# post game) EXISTS in the S63 pool — the closest interior shooters are Pool_2443's
# shape (tall, shooting-only, no post game). The stretch-five row is therefore the
# hand-set archetype above; the pool's gap is the generation constraint's business.

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
    """Each vector carries the final diet AND a per-stage trace (raw signals, gamma,
    post-era weights, post-bleed weights, post-floor weights) so C# parity proves the
    PIPELINE, not just the final integers — two different implementations can round to
    the same 5 ints. C# compares intermediate doubles at tight tolerance (relative
    1e-9), final integers exactly."""
    out = []
    pool_vecs = [(name, dict(P(), **r)) for name, (r, _, _) in POOL_ROWS.items()]
    for name, a in list(ARCH.items()) + list(GOLDEN_EXTRA.items()) + pool_vecs:
        R = raw_signals(a)
        g = peakedness_gamma(R)
        w_era   = [R[i]**g * ERA_PROFILE[i] for i in range(5)]
        w_bleed = bleed_margins(w_era)
        w_floor = opportunity_floor(w_bleed, a)
        diet    = to_int_diet(w_floor)
        out.append({"name": name, "ratings": a, "expected": diet,
                    "trace": {"rawSignals": R, "gamma": g,
                              "postEraWeights": w_era,
                              "postBleedWeights": w_bleed,
                              "postFloorWeights": w_floor}})
    return out

def emit_golden():
    # Anchor the fixture BESIDE this oracle, never the caller's working directory — the
    # byte-identical parity gate must touch tools/tendency_golden.json regardless of where
    # the command is run from (mirrors the fastbreak oracle's emit fix, its L97).
    path = Path(__file__).with_name("tendency_golden.json")
    with path.open("w") as f:
        json.dump({"zoneOrder": Z, "vectors": golden_vectors()}, f, indent=1, sort_keys=True)
    print(f"golden parity fixture written: {path} ({len(golden_vectors())} vectors)")

# ---------------------------------------------------------------------------
# STRUCTURAL CHECKS  (the real gate; three-share is only printed)
# ---------------------------------------------------------------------------
# S65 SIGN-OFF INSTRUMENTS — the axis walks and the stage-trace table Emmett
# rules on (final tendencies + shares only; NEVER converted to attempts here —
# attempts come only from the season harness after the port).
# ---------------------------------------------------------------------------
WALK_FIXED = {
 # everything but Outside held fixed; the two bodies the two rules own.
 "perimeter": dict(Height=62, BallHandling=60),                                # pure guard body (p=1)
 "interior":  dict(Height=85, BallHandling=25, PostMoves=75, Close=68,
                   Finishing=72, Screening=65, Weight=80),                     # post big (p=0)
}

def axis_walk(kind, step=5):
    """[(Outside, final ThreeTendency)] for the fixed archetype, O = 0..95 by step."""
    out = []
    for O in range(0, 96, step):
        a = P(Outside=O, **WALK_FIXED[kind])
        out.append((O, derive(a)[0][4]))
    return out

def three_stage_trace(a):
    """The three's share of total mass at each pipeline stage (percent), plus final."""
    R = raw_signals(a)
    g = peakedness_gamma(R)
    w_gam  = [R[i]**g for i in range(5)]
    w_era  = [w_gam[i] * ERA_PROFILE[i] for i in range(5)]
    w_bld  = bleed_margins(w_era)
    w_flr  = opportunity_floor(w_bld, a)
    def sh(w):
        s = sum(w); return (w[4]/s*100.0) if s > 0 else 0.0
    return dict(p=perimeter_ness(a), rawThree=R[4], shRaw=sh(R), shGamma=sh(w_gam),
                shEra=sh(w_era), shBleed=sh(w_bld), shFloor=sh(w_flr),
                final=to_int_diet(w_flr)[4], diet=to_int_diet(w_flr))

def print_signoff():
    print("="*104)
    print("S65 SIGN-OFF TABLE 1 — THE RULED ROWS, full stage trace")
    print("(share of mass at each stage, %; final = the integer ThreeTendency; old = committed v2 value)")
    print("="*104)
    hdr = f"{'Row':<36}{'pos':>4}{'p':>6}{'O':>4}{'raw':>6}{'shRaw':>7}{'gamma':>7}{'era':>7}{'bleed':>7}{'floor':>7}{'FINAL':>7}{'old':>5}"
    print(hdr); print("-"*104)
    named = [
        ("Open-only guard (O=25)",      ARCH["Open-only guard (O=25)"],      "G", 62),
        ("Weak-shooting spot-up (O=40)",ARCH["Weak-shooting spot-up (fringe)"],"W", None),
        ("True non-shooter big (O=3)",  ARCH["True non-shooter big (O=3)"],  "B", None),
        ("30-Outside big (threshold)",  ARCH["30-Outside big (threshold)"],  "B", None),
        ("Pure stretch five (O=60)*",   ARCH["Pure stretch five (O=60)"],    "B", None),
    ]
    for name, (r, pos, old) in POOL_ROWS.items():
        named.append((name, dict(P(), **r), pos, old))
    for name, a, pos, old in named:
        t = three_stage_trace(a)
        print(f"{name:<36}{pos:>4}{t['p']:>6.2f}{a['Outside']:>4}{t['rawThree']:>6.1f}"
              f"{t['shRaw']:>7.1f}{t['shGamma']:>7.1f}{t['shEra']:>7.1f}{t['shBleed']:>7.1f}"
              f"{t['shFloor']:>7.1f}{t['final']:>7}{('' if old is None else old):>5}")
    print("  * no true stretch five exists in the S63 pool (see POOL_ROWS note) — hand-set archetype.")

    print(); print("="*104)
    print("S65 SIGN-OFF TABLE 2 — AXIS WALKS: final ThreeTendency vs Outside, all else fixed")
    print("(monotonicity is hard-asserted in checks; CLIFF = interval gain > 2x the median positive gain — surfaced, not gated)")
    print("="*104)
    for kind in ("perimeter", "interior"):
        walk = axis_walk(kind)
        gains = [walk[i+1][1]-walk[i][1] for i in range(len(walk)-1)]
        pos_gains = sorted(g for g in gains if g > 0)
        med = pos_gains[len(pos_gains)//2] if pos_gains else 0
        print(f"{kind.upper()} (fixed: {WALK_FIXED[kind]})")
        print("  O:     " + " ".join(f"{o:>3}" for o, _ in walk))
        print("  Three: " + " ".join(f"{f:>3}" for _, f in walk))
        flags = ["   "] + [("CLF" if med > 0 and g > 2*med else "   ") for g in gains]
        print("  step:  " + " ".join(f"{s:>3}" for s in ([" "] + [f"+{g}" if g>=0 else str(g) for g in gains])))
        cliffs = [(walk[i][0], walk[i+1][0], gains[i]) for i in range(len(gains)) if med > 0 and gains[i] > 2*med]
        if cliffs:
            print("  CLIFF candidates (Emmett's table ruling): " +
                  ", ".join(f"{a}->{b} (+{g}, median +{med})" for a, b, g in cliffs))
        else:
            print(f"  no cliff candidates (median positive gain +{med})")
        print()

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
    # v3 REWRITE (was v2: 2<=Three<=6): the interior threshold — an 8-Outside paint
    # big is EMERGENCY-only now (a couple of desperation ones all season).
    chk("rim runner's threes are emergency-only (1 <= Three <= 2)", 1<=d_rr[4]<=2)

    d_ml = derive(ARCH["Wing scorer (multi-level)"])[0]
    # v2 ruling: the modern-era three bump lifts every archetype's three share, so the
    # multi-level wing's max climbs past the old stale 45. Retire the bare number; guard
    # the multi-level IDENTITY instead — still a real multi-level scorer, flatter than a
    # pure shooter, with a protected rim and a live mid-range.
    chk("multi-level scorer keeps a bounded three (Three<=50)", d_ml[4]<=50)
    chk("multi-level scorer protects the rim (Rim>=25)", d_ml[0]>=25)
    chk("multi-level scorer keeps a live mid-range (Mid>=8)", d_ml[2]>=8)
    chk("multi-level scorer is not three-lopsided (Three-Rim<=20)", d_ml[4]-d_ml[0]<=20)
    chk("multi-level flatter than shooter (its max < shooter's max)", max(d_ml)<max(d_shoot))

    # v3 REWRITE (was v2: "the corner guy", Three top): Outside 40 is BELOW the
    # shooter threshold — his three now sits on the structural floor, in the flow,
    # never dominant. The legitimate three-dominant spot-up begins where the raw
    # ramp expresses (the THREE_PERIM_MAIN gate), not at 40.
    d_wss = derive(ARCH["Weak-shooting spot-up (fringe)"])[0]
    chk("weak-shooting spot-up no longer tilts three (Three NOT top)", d_wss[4]!=max(d_wss))
    chk("weak-shooting spot-up keeps structural volume (8 <= Three <= 25)", 8<=d_wss[4]<=25)
    # Outside 64 is a REAL shooter on this scale — his tilt survives the reversal.
    d_ws  = derive(ARCH["Weak shooter (fringe)"])[0]
    chk("64-Outside 'weak' shooter still tilts three (Three top)", d_ws[4]==max(d_ws))

    # v3 probes: both sides of the reversal.
    d_tns = derive(ARCH["True non-shooter big (O=3)"])[0]
    chk("TRUE non-shooter big tilts to rim (Rim top)", d_tns[0]==max(d_tns))
    # v3 REWRITE (was v2: 2<=Three<=6): emergency only.
    chk("TRUE non-shooter big's threes are emergency-only (1 <= Three <= 2)", 1<=d_tns[4]<=2)
    # v3 REWRITE (was v2: "the paradox, embraced", Three top): REVERSED — the
    # open-only guard's three sits near the structural floor now; his diet falls
    # to what he can least-badly do.
    d_oog = derive(ARCH["Open-only guard (O=25)"])[0]
    chk("open-only guard's three near the structural floor (Three <= 12)", d_oog[4]<=12)
    chk("open-only guard no longer three-top — the paradox, reversed", d_oog[4]!=max(d_oog))
    chk("open-only guard keeps a real rim share (Rim>=30)", d_oog[0]>=30)

    # v3: the interior threshold's two sides and the stretch payoff.
    d_30  = derive(ARCH["30-Outside big (threshold)"])[0]
    chk("30-Outside big's allowance opens (Three > non-shooter's, still <= 6)",
        d_30[4]>d_tns[4] and d_30[4]<=6)
    d_s5  = derive(ARCH["Pure stretch five (O=60)"])[0]
    # five-a-game arithmetic: ~5 of a five's ~8-11 FGA/game is a 45-65% share.
    chk("pure stretch five reaches real five-a-game arc volume (45 <= Three <= 65)", 45<=d_s5[4]<=65)
    chk("pure stretch five spaces with the three, not the long two (Three>Long)", d_s5[4]>d_s5[3])

    # v3: the real S63-pool rows (the before/after the reversal exists to fix).
    dp = {name: derive(dict(P(), **r))[0] for name, (r, _, _) in POOL_ROWS.items()}
    chk("median guard (Pool_806, O=27) drops hard (87 -> Three <= 15)",
        dp["Pool_806 median guard (O=27)"][4]<=15)
    chk("can't-shoot wing (Pool_1399, O=21) near floor, not zero (5 <= Three <= 12)",
        5<=dp["Pool_1399 cant-shoot wing (O=21)"][4]<=12)
    chk("can't-shoot wing distinctly below the O=49 control (gap >= 15)",
        dp["Pool_46 perimeter control (O=49)"][4] - dp["Pool_1399 cant-shoot wing (O=21)"][4] >= 15)
    chk("3&D wing (Pool_1389, O=70) stays a high-volume shooter (Three >= 45)",
        dp["Pool_1389 3&D wing (O=70)"][4]>=45)
    chk("elite sniper (Pool_97, O=99) barely moved (Three >= 65)",
        dp["Pool_97 elite sniper (O=99)"][4]>=65)
    chk("non-shooting big (Pool_2630, O=8) emergency-only (1 <= Three <= 2)",
        1<=dp["Pool_2630 non-shooting big (O=8)"][4]<=2)
    chk("30-Outside big (Pool_2717) small allowance (2 <= Three <= 6)",
        2<=dp["Pool_2717 30-Outside big"][4]<=6)
    chk("tall interior shooter (Pool_2443, O=74) keeps real arc volume (Three >= 35)",
        dp["Pool_2443 tall shooter (O=74)"][4]>=35)

    # v3 axis walks: final ThreeTendency monotone nondecreasing in Outside for a
    # fixed perimeter archetype and a fixed interior archetype (hard assert; the
    # cliff shape is a SURFACED diagnostic in the sign-off emitter, not a gate).
    # Past the raw ramp's top the LONG-TWO — which also reads Outside — keeps
    # growing and honestly competes a point of share away, so beyond the ramp top
    # the assert tolerates a 1-point integer wobble (strict below it).
    ramp_top = {"perimeter": THREE_PERIM_TOP_HI, "interior": THREE_STRETCH_HI}
    for kind in ("perimeter", "interior"):
        walk = axis_walk(kind)
        chk(f"{kind} axis walk monotone nondecreasing in Outside (±1 past ramp top)",
            all(walk[i+1][1] >= walk[i][1] - (1 if walk[i][0] >= ramp_top[kind] else 0)
                for i in range(len(walk)-1)))
    chk("perimeter axis walk starts nonzero at Outside 0 (the floor, not a gate)",
        axis_walk("perimeter")[0][1] >= 3)
    chk("interior axis walk starts at zero at Outside 0 (literal zero preserved)",
        axis_walk("interior")[0][1] == 0)

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

    print()
    print_signoff()

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
