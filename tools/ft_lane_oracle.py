#!/usr/bin/env python3
"""S120 — THE FREE-THROW LANE: the oracle (O-118).

Implements the ruled model from the S120 prompt (§4b–4e), independently of the C# engine, and
emits tools/ft_lane_golden.json, which Phase 111 C1 reproduces through the engine:

  * who stands where (4b): each side ordered by the lane score — half the rebound body (on the
    0–99 scale), half the side's rebounding rating — best first, men in foul trouble last, ties
    by slot. Offense: the shooter at the line, the first two others on the lane, the rest back.
    Defense: the first four on the lane, the fifth back.
  * the split (4c): the offense's share of the lane's body total and rebounding total, each
    against the NORMAL lane's share (the four frozen totals in config.json, the same numbers the
    engine reads — never this script's own), each scaled so two offensive lane men LaneAnchorPoints
    better read as that many points on the live-ball gap scale, through GapFn, weighted, and bent
    by tanh between the shared floor and ceiling.
  * who gets it (4d, 4e): the shooter and the men back at their fixed shares; the lane men split
    the rest by today's picker weights computed over the lane men only.

Usage:  python3 tools/ft_lane_oracle.py src/Charm.Harness/config.json tools/ft_lane_golden.json
Every number is written with repr() (17 significant digits); the C# side compares within a
ULP bound because math.tanh / math.pow are not bit-portable (CONVENTIONS §2).
"""
import json, math, sys

cfg = json.load(open(sys.argv[1], encoding="utf-8"))
M, RM = cfg["Matchup"], cfg["RollM"]

W_BODY = M["ReboundStrengthWeight"] + M["ReboundHeightWeight"] + M["ReboundWingspanWeight"]
MASS = RM["OffensiveRebound"] + RM["DefensiveRebound"]
BASE_OFF = RM["OffensiveRebound"] / MASS

# ── players ──────────────────────────────────────────────────────────────────────────────
def H(inches):  # the height rating for a height in inches (docs/attributes.md), rounded
    return int(round(40 + (inches - 68) / 0.36))

def P(name, inches, wing, stren, vert, pd, orb, drb, hus=55, disc=50):
    return dict(name=name, Height=H(inches), Wingspan=wing, Strength=stren, Vertical=vert,
                PostDefense=pd, OffensiveRebounding=orb, DefensiveRebounding=drb,
                Hustle=hus, Discipline=disc)

def phys(p):
    return (M["ReboundStrengthWeight"] * p["Strength"] + M["ReboundHeightWeight"] * p["Height"]
            + M["ReboundWingspanWeight"] * p["Wingspan"])

def postness(p):
    return (M["PostnessHeight"] * p["Height"] + M["PostnessPostDefense"] * p["PostDefense"]
            + M["PostnessStrength"] * p["Strength"])

def lane_score(p, offense):
    r = p["OffensiveRebounding"] if offense else p["DefensiveRebounding"]
    return 0.5 * phys(p) / W_BODY + 0.5 * r

# ── 4b: the lane ─────────────────────────────────────────────────────────────────────────
def in_trouble(fouls, clock):
    if clock is None:
        return False
    period, left = clock
    if period == 1:
        return fouls >= RM["LaneFoulTroubleFirstHalfFouls"]
    if period == 2:
        return fouls >= RM["LaneFoulTroubleSecondHalfFouls"] and left > RM["LaneFoulTroubleSecondHalfSeconds"]
    return False

def order(team, offense, exclude, size, fouls, clock):
    men = [(n, team[n - 1]) for n in range(1, 6) if n != exclude and team[n - 1] is not None]
    keyed = sorted(men, key=lambda m: (in_trouble(fouls.get(m[0], 0), clock), -lane_score(m[1], offense), m[0]))
    lane = sorted(n for n, _ in keyed[:size])
    back = sorted(n for n, _ in keyed[size:])
    return lane, back

def build_lane(off, dfn, shooter, off_fouls=None, def_fouls=None, clock=None):
    ol, ob = order(off, True, shooter or 0, 2, off_fouls or {}, clock)
    dl, db = order(dfn, False, 0, 4, def_fouls or {}, clock)
    return dict(shooter=shooter, off_lane=ol, off_back=ob, def_lane=dl, def_back=db)

# ── 4c: the split ────────────────────────────────────────────────────────────────────────
def gapfn(g, steep, expo, scale):
    return math.copysign(steep * (abs(g) / scale) ** expo, g) if g != 0 else 0.0

def scales():
    ob, db = RM["LaneNormalOffenseBody"], RM["LaneNormalDefenseBody"]
    orr, dr = RM["LaneNormalOffenseRebounding"], RM["LaneNormalDefenseRebounding"]
    a = RM["LaneAnchorPoints"]
    s0, r0 = ob / (ob + db), orr / (orr + dr)
    ob_up, or_up = ob + 2 * a * W_BODY, orr + 2 * a
    return s0, a / (ob_up / (ob_up + db) - s0), r0, a / (or_up / (or_up + dr) - r0)

def totals(off, dfn, lane):
    ob = sum(phys(off[n - 1]) for n in lane["off_lane"])
    db = sum(phys(dfn[n - 1]) for n in lane["def_lane"])
    orr = sum(off[n - 1]["OffensiveRebounding"] for n in lane["off_lane"])
    dr = sum(dfn[n - 1]["DefensiveRebounding"] for n in lane["def_lane"])
    return ob, db, orr, dr

def off_share(ob, db, orr, dr):
    s0, ks, r0, kr = scales()
    s, r = ob / (ob + db), orr / (orr + dr)
    total = (M["ReboundSizeWeight"] * gapfn((s - s0) * ks, M["PhysicalSteepness"], M["PhysicalExponent"], M["ReferenceScale"])
             + M["ReboundSkillWeight"] * gapfn((r - r0) * kr, M["SkillSteepness"], M["SkillExponent"], M["ReferenceScale"]))
    span = (M["ReboundOffShareCeiling"] - BASE_OFF) if total >= 0 else (BASE_OFF - M["ReboundOffShareFloor"])
    return BASE_OFF + span * math.tanh(total / M["ReboundReferenceShift"])

# ── 4d / 4e: who gets it — today's picker weights over the lane men only ──────────────────
def mean(xs):
    return sum(xs) / len(xs)

def rebounder_weights(men, key):   # OffensiveRebounderPicker / DefensiveRebounderPicker, no nerf
    pn = [postness(p) for p in men]; mp = mean(pn)
    mw = mean([p["Wingspan"] for p in men]); mph = mean([phys(p) for p in men]); mv = mean([p["Vertical"] for p in men])
    out = []
    for p, x in zip(men, pn):
        pw = 1 + M["ReboundPositionalSwing"] * math.tanh((x - mp) / M["ReboundPositionalScale"])
        wm = 1 + M["ReboundWingspanSwing"] * math.tanh((p["Wingspan"] - mw) / M["ReboundWingspanScale"])
        hm = 1 + M["HustleRebounderSteepness"] * math.tanh((p["Hustle"] - 50.0) / M["HustleRebounderScale"])
        vm = 1 + M["ReboundVerticalSwing"] * math.tanh((p["Vertical"] - mv) / M["ReboundVerticalScale"])
        body = M["ReboundBodyPullWeight"] * max(0.0, phys(p) - mph)
        floor = M["ReboundBodyFloorCeiling"] * math.tanh(max(0.0, phys(p) - M["ReboundBodyFloorReference"]) / M["ReboundBodyFloorScale"])
        out.append(M["ReboundLuckWeight"] + p[key] * pw * wm * hm * vm + body + floor)
    return out

def interior_weights(men):         # TurnoverInteriorPicker
    pn = [postness(p) for p in men]; mp = mean(pn)
    gf, sc = M["TurnoverInteriorGuardFloor"], M["TurnoverInteriorPostnessScale"]
    return [max(1.0, p["Strength"] * (gf + (1 - gf) * ((math.tanh((x - mp) / sc) + 1) / 2))) for p, x in zip(men, pn)]

def situational_weights(men):      # FoulCommitter.NonShootingWeights, isReachIn = false
    return [1.0 - M["ReachInDiscSpan"] * max(-1.0, min(1.0, (p["Discipline"] - 50.0) / 49.0)) for p in men]

def compose(shooter, shooter_share, back, back_share, lane, weights):
    out, rare = [], 0.0
    if shooter:
        out.append((shooter, "Shooter", shooter_share)); rare += shooter_share
    for b in back:
        out.append((b, "Back", back_share)); rare += back_share
    wsum = sum(weights)
    for n, w in zip(lane, weights):
        out.append((n, "Lane", (1.0 - rare) * w / wsum))
    return [dict(slot=n, spot=s, share=x) for n, s, x in out]

def draws(off, dfn, lane):
    olm = [off[n - 1] for n in lane["off_lane"]]
    dlm = [dfn[n - 1] for n in lane["def_lane"]]
    sh = lane["shooter"]
    return dict(
        offensive_board=compose(sh, RM["LaneShooterBoardShare"], lane["off_back"], RM["LaneOffenseBackBoardShare"],
                                lane["off_lane"], rebounder_weights(olm, "OffensiveRebounding")),
        defensive_board=compose(None, 0.0, lane["def_back"], RM["LaneDefenseBackBoardShare"],
                                lane["def_lane"], rebounder_weights(dlm, "DefensiveRebounding")),
        scramble_fouled=compose(sh, RM["LaneShooterScrambleShare"], lane["off_back"], 0.0,
                                lane["off_lane"], rebounder_weights(olm, "OffensiveRebounding")),
        scramble_offensive_fouler=compose(sh, RM["LaneShooterScrambleShare"], lane["off_back"], 0.0,
                                          lane["off_lane"], interior_weights(olm)),
        scramble_defensive_fouler=compose(None, 0.0, lane["def_back"], 0.0,
                                          lane["def_lane"], situational_weights(dlm)),
    )

# ── the eight archetype cases (the design conversation's table, 2026-10-09) ────────────────
STD = [P("PG 6'0\" (no glass)", 72, 50, 40, 60, 30, 22, 32), P("SG 6'4\"", 76, 58, 48, 58, 38, 30, 40),
       P("Wing 6'7\"", 79, 68, 58, 62, 50, 45, 55), P("PF 6'9\"", 81, 74, 70, 55, 65, 65, 70),
       P("C 7'0\"", 84, 82, 78, 45, 75, 70, 78)]
def small(tag): return [P(f"{tag} guard {i}", 72 + i, 52 + i, 42, 60, 32, 25, 32) for i in range(5)]
BIG = P("Dominant C 7'1\"", 85, 90, 90, 60, 85, 95, 95, 65)
BIG2 = P("Elite PF 6'11\"", 83, 88, 88, 62, 80, 92, 90, 65)
def norb(p): return dict(p, OffensiveRebounding=15, DefensiveRebounding=15, name=p["name"] + " (can't rebound)")

CASES = [
    dict(id=1, label="Two identical normal teams, point guard at the line", approved=0.200,
         off=STD, dfn=STD, shooter=1),
    dict(id=2, label="All small and weak (five guards a side)", approved=0.178,
         off=small("O"), dfn=small("D"), shooter=1),
    dict(id=3, label="Offense has one dominant 7'1\" big on the lane", approved=0.215,
         off=STD[:4] + [BIG], dfn=STD, shooter=1),
    dict(id=4, label="Defense has the same dominant big", approved=0.199,
         off=STD, dfn=STD[:4] + [BIG], shooter=1),
    dict(id=5, label="Offense's center is the shooter", approved=0.188,
         off=STD, dfn=STD, shooter=5),
    dict(id=6, label="Defense's center in foul trouble, stays back", approved=0.237,
         off=STD, dfn=STD, shooter=1, def_fouls={5: 2}, clock=(1, 600.0)),
    dict(id=7, label="Defense's 3rd and 4th lane men can't rebound", approved=0.222,
         off=STD, dfn=[STD[0], norb(STD[1]), norb(STD[2]), STD[3], STD[4]], shooter=1),
    dict(id=8, label="Two elite offensive bigs against four men who cannot rebound", approved=0.503,
         off=STD[:3] + [BIG2, BIG], dfn=[norb(p) for p in STD], shooter=1),
]

def run_case(c):
    lane = build_lane(c["off"], c["dfn"], c["shooter"], {}, c.get("def_fouls"), c.get("clock"))
    ob, db, orr, dr = totals(c["off"], c["dfn"], lane)
    share = off_share(ob, db, orr, dr)
    def roster(team):
        keys = ["Height", "Wingspan", "Strength", "Vertical", "PostDefense", "OffensiveRebounding",
                "DefensiveRebounding", "Hustle", "Discipline"]
        return [dict(name=p["name"], **{k: p[k] for k in keys}) for p in team]
    return dict(id=c["id"], label=c["label"], approved_board=c["approved"], shooter=c["shooter"],
                def_fouls={str(k): v for k, v in (c.get("def_fouls") or {}).items()},
                clock=list(c["clock"]) if c.get("clock") else None,
                offense=roster(c["off"]), defense=roster(c["dfn"]),
                lane=lane, totals=dict(off_body=ob, def_body=db, off_reb=orr, def_reb=dr),
                off_share=share, board=MASS * share, draws=draws(c["off"], c["dfn"], lane))

# the foul-trouble orderings (4b), on case 1's defense: who is on the lane at each clock
def trouble_orderings():
    out = []
    for label, fouls, clock in [
        ("first half, C at 2 fouls", {5: 2}, (1, 900.0)),
        ("first half, C at 1 foul", {5: 1}, (1, 900.0)),
        ("second half, C at 4 fouls, 301 s left", {5: 4}, (2, 301.0)),
        ("second half, C at 4 fouls, 300 s left", {5: 4}, (2, 300.0)),
        ("overtime, C at 4 fouls", {5: 4}, (3, 200.0)),
        ("no clock, C at 4 fouls", {5: 4}, None),
    ]:
        lane = build_lane(STD, STD, 1, {}, fouls, clock)
        out.append(dict(label=label, def_fouls={str(k): v for k, v in fouls.items()},
                        clock=list(clock) if clock else None, def_lane=lane["def_lane"], def_back=lane["def_back"]))
    return out

if __name__ == "__main__":
    s0, ks, r0, kr = scales()
    golden = dict(
        about="S120 free-throw lane oracle (tools/ft_lane_oracle.py) — Phase 111 C1 reproduces it",
        normal_lane=dict(off_body=RM["LaneNormalOffenseBody"], def_body=RM["LaneNormalDefenseBody"],
                         off_reb=RM["LaneNormalOffenseRebounding"], def_reb=RM["LaneNormalDefenseRebounding"],
                         s0=s0, ks=ks, r0=r0, kr=kr),
        base_off_share=BASE_OFF, mass=MASS,
        cases=[run_case(c) for c in CASES],
        trouble=trouble_orderings(),
    )
    with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f:
        json.dump(golden, f, indent=1)
        f.write("\n")
    for c in golden["cases"]:
        print(f"{c['id']}. {c['label']:62} {100*c['board']:5.1f}%  (approved {100*c['approved_board']:.1f}%)  "
              f"lane O{c['lane']['off_lane']} D{c['lane']['def_lane']}")
