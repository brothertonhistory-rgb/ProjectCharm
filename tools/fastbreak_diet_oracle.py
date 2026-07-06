# ============================================================================
# LOCKED SPEC ORACLE v1 — fast-break shot diet (Session 38)
# The executable truth for the C# port. If the C# and this oracle ever
# disagree, THE ORACLE WINS. Ported constant-for-constant, zone order
# Rim, Short, Mid, Long, Three.
#
# DESIGN (Emmett's rulings, this conversation):
#  - The break dictates a modern BASE diet (rim-heavy, real three share, long
#    twos nearly gone). The shooter's own STORED neutral tendencies bend it,
#    zone by zone, via an identity-relative ratio (the same multiplier idiom
#    Roll G's matchup bend uses) — a shooter fills the corner, a rim-runner
#    keeps running. No additive floor, so a non-shooter does NOT inherit the
#    base's threes (his ratio is tiny); his break diet stays ~rim-run.
#  - The coach's PaceBias tilts the three share up for run-and-gun teams
#    ("higher pace") — a tempo dial that already exists. ShotSelectionBias
#    ("prioritize outside") is DEFERRED to the future coach philosophy layer;
#    the fast-break diet does NOT read it this session.
#  - Bigs who can shoot trail for three (stretch/pick-pop carry a real three
#    tendency → the blend hands them the trailer three); true non-shooters do
#    not. This falls out of the tendency read — NO new gate (a gate would
#    double-count what the derivation already encoded).
#
# All constants below are CALIBRATION PLACEHOLDERS, tuned off the season
# page's new fast-break shot-diet readout, never asserted against a target.
# ============================================================================
# Fast-break shot-diet oracle — DESIGN DRAFT (Session 38 candidate)
# The break dictates the base; the shooter's own neutral tendencies pull it;
# the coach's PaceBias tilts the three share. No team scalar anywhere.
# Run: python3 tools/fastbreak_diet_oracle.py
import random, statistics
import tendency_oracle as T

# ---- design constants (PLACEHOLDERS for Emmett's ruling) --------------------
BASE = {"Rim":0.57, "Short":0.08, "Mid":0.03, "Long":0.02, "Three":0.30}
BETA        = 0.70      # strength of the shooter-identity pull (0=pure base, 1=full ratio)
RATIO_CAP   = (0.15, 2.2)  # clamp on the identity ratio (guards the extremes)
PACE_TILT   = 0.035     # per PaceBias point above/below 5, multiplied into Three
ZONES = ["Rim","Short","Mid","Long","Three"]
# league mean neutral diet (the tendency oracle's population diagnostic)
MEAN = {"Rim":0.335, "Short":0.155, "Mid":0.098, "Long":0.058, "Three":0.355}

def fb_pie(neutral_diet, pace=5.0):
    """Identity-relative pull: the base break diet bent by how this shooter's own
    neutral diet compares to the league's, zone by zone — the same multiplier idiom
    Roll G's bend uses. A non-shooter's transition threes stay rare (his ratio is
    tiny); a pure shooter's kick-ahead three swells. No additive floor leaks the
    base's threes to players who cannot shoot."""
    nd = {z: neutral_diet[i]/100.0 for i,z in enumerate(ZONES)}
    def ratio(z):
        r = (nd[z]/MEAN[z]) ** BETA
        return min(max(r, RATIO_CAP[0]), RATIO_CAP[1])
    blend = {z: BASE[z] * ratio(z) for z in ZONES}
    blend["Three"] *= (1.0 + PACE_TILT*(pace-5.0))
    s = sum(blend.values())
    return {z: v/s for z,v in blend.items()}

def league(seed, pace=5.0):
    """Slot-weighted transition three share over the oracle population.
    Roll E transition selection is slot-weighted (0.30/0.30/0.25/0.10/0.05,
    slots ~ G/G/W/B/B in the opening-five shape); approximate by drawing the
    shooter's ROLE with those weights."""
    rng = random.Random(seed)
    slotw = [(("G"),0.30),(("G"),0.30),(("W"),0.25),(("B"),0.10),(("B"),0.05)]
    shares=[]
    for _ in range(4000):
        r=rng.random(); acc=0
        for pos,w in slotw:
            acc+=w
            if r<acc: break
        role=rng.choice(T.ROLES_BY_POS[pos])
        u=rng.random(); legs=3 if u<0.12 else 2 if u<0.50 else 1
        diet,_,_=T.derive(T.draw_player(role,legs,rng))
        shares.append(fb_pie(diet,pace)["Three"])
    return statistics.mean(shares)


def emit_golden():
    """Named archetype -> expected fast-break pie (pace 5 and pace 8), zone order
    Rim,Short,Mid,Long,Three. The C# DeriveFastBreakPie must reproduce every weight
    element-for-element. Includes both bigs (trailer vs rim-run) to pin the ruling."""
    import json
    vectors={}
    for name,p in T.ARCH.items():
        diet,_,_=T.derive(p)
        vectors[name]={
            "tendency":diet,
            "fb_pace5":[round(fb_pie(diet,5.0)[z],10) for z in ZONES],
            "fb_pace8":[round(fb_pie(diet,8.0)[z],10) for z in ZONES],
        }
    out={"base":BASE,"beta":BETA,"ratio_cap":list(RATIO_CAP),"pace_tilt":PACE_TILT,
         "mean":MEAN,"zones":ZONES,"vectors":vectors}
    json.dump(out,open("fastbreak_golden.json","w"),indent=1)
    print(f"golden parity fixture written: fastbreak_golden.json ({len(vectors)} vectors)")

if __name__=="__main__":
    print(f"base break diet: {BASE}   identity pull beta={BETA} cap={RATIO_CAP}   pace tilt={PACE_TILT}/pt")
    print()
    print("ARCHETYPE TABLE — transition three share, today (5%) vs proposed")
    print(f"{'archetype':38s} {'neutral 3-tend':>14s} {'FB3 pace5':>10s} {'FB3 pace8':>10s}")
    rows = [
        ("Pure spot-up shooter",      "Pure spot-up"),
        ("Weak-shooting spot-up",     "Weak-shooting spot-up"),
        ("Open-only guard (O=25)",    "Open-only guard"),
        ("Slasher (no jumper)",       "Slasher"),
        ("Wing scorer (multi-level)", "Wing scorer"),
        ("Pick-&-pop big",            "Pick-&-pop big"),
        ("Stretch big (3pt)",         "Stretch big"),
        ("Rim runner (lob/putback)",  "Rim runner"),
        ("True non-shooter big (O=3)","True non-shooter big"),
    ]
    A = T.ARCH
    def find(key):
        for name,p in A.items():
            if key.lower() in name.lower(): return name,p
        return None,None
    for label,key in rows:
        name,p = find(key)
        if p is None:
            print(f"{label:38s} {'(not found: '+key+')'}"); continue
        diet,_,_ = T.derive(p)
        f5 = fb_pie(diet,5.0)["Three"]; f8 = fb_pie(diet,8.0)["Three"]
        print(f"{label:38s} {diet[4]:>13d}% {f5*100:>9.1f}% {f8*100:>9.1f}%")
    print()
    for seed in (20260703, 40, 12345):
        for pace in (5.0,):
            print(f"LEAGUE transition three share (slot-weighted, seed {seed}, pace {pace:.0f}): {league(seed,pace)*100:.1f}%   [today: 5.0%]")
    print()
    # league 3PA-rate consequence: new = old + fbFgaShare * (newFB3 - 0.05)
    print("league realized 3PA-rate consequence (fb FGA share unknown until the page prints it):")
    for f in (0.10,0.15,0.20,0.25):
        newfb = league(20260703,5.0)
        print(f"  if fast-break FGA share = {f:.0%}: 3PA rate {0.32:.3f} -> {0.32 + f*(newfb-0.05):.3f}")
    emit_golden()
