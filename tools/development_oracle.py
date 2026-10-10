"""S121 — THE OFFSEASON CAMP'S ORACLE. Implements PROMPT-S121 §4a–4c from the text.

Every configurable value is read from config.json's "Development" section (never from defaults in
this file): the odds, rates, splits, tilts, the IQ / Discipline keys, AttributeOrder, GuardDownSkills,
BigDownSkills. What is code here, as in the engine: K1's group membership, the generator's 19 spend
skills, and the generator's free-throw constants (PlayerGenPass3.cs FT_*).

Random inputs are INJECTED (lists of u values in the documented order) for every parity case; the
SplitMix64 mirror and the K5 seeding are used only for the stream cases and the archetype careers,
which are themselves parity cases (Phase 112 C1 replays both).

Started from the draft's s121_model.py (the reference model); differs from it in three places, each
on purpose: (1) values come from config.json, (2) randomness is injected or SplitMix64 (K5), never
Python's random, (3) the archetype table is this oracle's own, defined below with its seeds — the
draft's s121_tables.py did not ship with the prompt, so §0's table is not reproduced; these rows
cover the same player types.

Usage:
  python3 tools/development_oracle.py                 # writes tools/development_golden.json
  python3 tools/development_oracle.py --check         # regenerates in memory; refuses if the file differs
Run from the repository root (reads src/Charm.Harness/config.json).
"""
import copy
import hashlib
import json
import math
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONFIG = os.path.join(ROOT, "src", "Charm.Harness", "config.json")
GOLDEN = os.path.join(ROOT, "tools", "development_golden.json")

# ── K1 — group membership (code, as in the engine) ─────────────────────────────────────
SKILLS = ["Outside", "Mid", "OffBallMovement", "Close", "Finishing", "PostMoves", "Screening",
          "BallHandling", "Passing", "Playmaking", "SelfCreation", "FoulDrawing",
          "PerimeterDefense", "Steals", "OffBallDefense", "PostDefense", "RimProtection",
          "OffensiveRebounding", "DefensiveRebounding", "HelpDefense"]
BODY = ["Strength", "Weight", "Endurance"]
ATH = ["Speed", "Quickness", "FirstStep", "Vertical"]
FUNDED = SKILLS + BODY + ATH                        # canonical order (the scouting file's)
GROUP = {**{k: "Skill" for k in SKILLS}, **{k: "Body" for k in BODY}, **{k: "Athleticism" for k in ATH}}
# PlayerGenPass3.SPEND_SKILLS, family order (Shooting, InteriorOffense, Creation, PerimDefense,
# InteriorDefense, Rebounding)
SPEND19 = ["Outside", "Mid", "OffBallMovement", "Close", "Finishing", "PostMoves", "Screening",
           "BallHandling", "Passing", "Playmaking", "SelfCreation", "FoulDrawing",
           "PerimeterDefense", "Steals", "OffBallDefense", "PostDefense", "RimProtection",
           "OffensiveRebounding", "DefensiveRebounding"]
BODY_INDEX = ["Height", "Strength", "Speed", "Quickness", "FirstStep", "Vertical"]
FT_CENTER, FT_OUT_ANCHOR, FT_OUT_SPAN, FT_OUT_SCALE, FT_HEIGHT_COEF, FT_MIN, FT_MAX = 71.5, 36.0, 9.0, 25.0, 9.0, 40.0, 96.0

# ── K5 — the streams' salts and mix primes (Program.Development.cs) ────────────────────
MASK = (1 << 64) - 1
POT_SALT, POT_MIX = 0x907E5C0D9071E7A1, 0xD1B54A32D192ED03
CAMP_SALT, CAMP_MIX = 0xCA4B5EA5C0FFEE21, 0xAEF17502108EF2D9

ATTR_INT_KEYS = {"WorkEthicBetaShape", "Budget", "MaxPerAttribute", "RepeatAfter", "BreakoutFloorTier",
                 "IqCapAboveArrival", "DisciplineCapAboveArrival", "HeightSpurtRating", "HeightSpurtWingspan"}


class SplitMix:
    def __init__(self, seed):
        self.s = seed & MASK

    def u64(self):
        self.s = (self.s + 0x9E3779B97F4A7C15) & MASK
        z = self.s
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK
        return z ^ (z >> 31)

    def u(self):
        return (self.u64() >> 11) * (1.0 / (1 << 53))


def potential_stream(arrival_season_seed, arrival_index):
    seed = (arrival_season_seed & MASK) ^ POT_SALT
    seed ^= ((arrival_index + 1) * POT_MIX) & MASK
    return SplitMix(seed)


def camp_stream(dev_seed, next_season_seed):
    seed = dev_seed ^ CAMP_SALT
    seed ^= (((next_season_seed + 1) & MASK) * CAMP_MIX) & MASK
    return SplitMix(seed)


def clamp(x, lo, hi):
    return max(lo, min(hi, x))


def pick(odds, u):
    c = 0.0
    for i, p in enumerate(odds):
        c += p
        if u < c:
            return i
    return len(odds) - 1


def injected(us):
    it = iter(us)
    return lambda: next(it)


# ── 4a — config ────────────────────────────────────────────────────────────────────────
def load_config():
    with open(CONFIG, encoding="utf-8") as f:
        root = json.load(f)
    return root["Development"]


def validate(c):
    """Every failure's KEY (the C# refusal names the same key first in each message)."""
    errs = []

    def need(ok, key):
        if not ok:
            errs.append(key)

    def finite(v):
        if isinstance(v, bool):
            return True
        if isinstance(v, (int, float)):
            return math.isfinite(v)
        if isinstance(v, list):
            return all(finite(x) for x in v)
        if isinstance(v, dict):
            return all(finite(x) for x in v.values())
        return True
    for k, v in c.items():
        if not finite(v):
            errs.append(k)
    if errs:
        return errs
    for k in ATTR_INT_KEYS:
        if not (isinstance(c[k], int) and not isinstance(c[k], bool)):
            errs.append(k)
    if errs:
        return errs
    need(c["Budget"] >= 1, "Budget")
    need(1 <= c["MaxPerAttribute"] <= c["Budget"], "MaxPerAttribute")
    need(isinstance(c["GroupFactor"], dict) and sorted(c["GroupFactor"]) == ["Athleticism", "Body", "Skill"], "GroupFactor")
    o = c["AttributeOrder"]
    need(len(o) == len(FUNDED) and len(set(o)) == len(FUNDED) and set(o) <= set(FUNDED), "AttributeOrder")
    for k in ("GuardDownSkills", "BigDownSkills"):
        need(len(set(c[k])) == len(c[k]) and set(c[k]) <= set(SKILLS), k)
    for k, n in (("PlayerTierOdds", 5), ("SkillOffsetOdds", 5), ("BodyTierOdds", 5), ("CampOdds", 4)):
        s = 0.0
        for p in c[k]:
            s += p
        need(len(c[k]) == n and all(p >= 0 for p in c[k]) and abs(s - 1.0) <= 1e-9, k)
    need(len(c["CampMultiplier"]) == 4 and all(m >= 0 for m in c["CampMultiplier"]), "CampMultiplier")
    need(len(c["CampOdds"]) != 4 or c["CampOdds"][2] + c["CampOdds"][3] > 0, "CampOdds")
    for k in ("BodyFirstNotchDown", "ReadyNotchDown", "TalentNotchUp", "AtrophyChance", "HeightSpurtChance"):
        need(0.0 <= c[k] <= 1.0, k)
    need(0 < c["TalentTopFraction"] <= 1, "TalentTopFraction")
    for k in ("BodyFirstFullGap", "ReadyFullZ", "MinutesSpan"):
        need(c[k] > 0, k)
    need(c["WorkEthicBetaShape"] >= 1, "WorkEthicBetaShape")
    need(len(c["TierRate"]) == 5 and all(r >= 0 for r in c["TierRate"]), "TierRate")
    need(len(c["NaturalClimb"]) == 5 and all(r >= 0 for r in c["NaturalClimb"]), "NaturalClimb")
    need(not isinstance(c["GroupFactor"], dict) or all(v >= 0 for v in c["GroupFactor"].values()), "GroupFactor")
    need(0 <= c["NaturalJitterLo"] <= c["NaturalJitterHi"], "NaturalJitterLo")
    need(0 <= c["GrowthJitterLo"] <= c["GrowthJitterHi"], "GrowthJitterLo")
    need(0 <= c["BreakoutFloorTier"] <= 4, "BreakoutFloorTier")
    need(len(c["PrioritySplits"]) >= 1 and all(len(sp) >= 1 and all(isinstance(p, int) and 1 <= p <= c["MaxPerAttribute"] for p in sp)
                                                and sum(sp) <= c["Budget"] for sp in c["PrioritySplits"]), "PrioritySplits")
    need(c["RepeatAfter"] >= 1, "RepeatAfter")
    need(0 <= c["RepeatFactor"] <= 1, "RepeatFactor")
    for k in ("WorkEthicTilt", "MinutesTilt", "IqAgeGrowth", "IqMinutesGrowth", "IqCapAboveArrival",
              "DisciplineAgeGrowth", "DisciplineMinutesGrowth", "DisciplineCapAboveArrival",
              "HeightSpurtRating", "HeightSpurtWingspan"):
        need(c[k] >= 0, k)
    return errs


# ── 4b-1 — cohort descriptors ──────────────────────────────────────────────────────────
def mean_sd(xs):
    s = 0.0
    for x in xs:
        s += x
    mu = s / len(xs)
    v = 0.0
    for x in xs:
        v += (x - mu) * (x - mu)
    sd = math.sqrt(v / len(xs))
    return mu, (sd if sd > 0 else 1.0)


def descriptors(c, men):
    """men: list of dict(idx, pos, card). Returns dict idx -> descriptor."""
    out = {}
    for pos in sorted({m["pos"] for m in men}):
        P = sorted((m for m in men if m["pos"] == pos), key=lambda m: m["idx"])
        b, s = [], []
        for m in P:
            acc = 0
            for k in BODY_INDEX:
                acc += m["card"][k]
            b.append(acc / len(BODY_INDEX))
            top = sum(sorted((m["card"][k] for k in SPEND19), reverse=True)[:5])
            s.append(top / 5.0)
        bm, bs = mean_sd(b)
        sm, ss = mean_sd(s)
        rows = []
        for m, bi, si in zip(P, b, s):
            bz, sz = (bi - bm) / bs, (si - sm) / ss
            rows.append(dict(idx=m["idx"], body=bi, skill=si, bz=bz, sz=sz, gap=bz - sz, talent=(bz + sz) / 2))
        ranked = sorted(rows, key=lambda r: (-r["talent"], r["idx"]))
        cut = round(len(P) * c["TalentTopFraction"])
        for i, r in enumerate(ranked):
            r["talentTop"] = i < cut
            out[r["idx"]] = r
    return out


# ── 4b-2..5 — the potential roll ───────────────────────────────────────────────────────
def roll_potential(c, d, pos, card, u):
    shift = 0
    u1, u2, u3, u4 = u(), u(), u(), u()
    if u1 < c["BodyFirstNotchDown"] * clamp(d["gap"] / c["BodyFirstFullGap"], 0, 1):
        shift -= 1
    if u2 < c["ReadyNotchDown"] * clamp(d["sz"] / c["ReadyFullZ"], 0, 1):
        shift -= 1
    if d["talentTop"] and u3 < c["TalentNotchUp"]:
        shift += 1
    P = clamp(pick(c["PlayerTierOdds"], u4) + clamp(shift, -1, 1), 0, 4)
    O = c["AttributeOrder"]
    best = max(SPEND19, key=lambda k: (card[k], -O.index(k)))
    tiers = {}
    for k in (a for a in O if GROUP[a] == "Skill"):
        off = pick(c["SkillOffsetOdds"], u()) - 2
        sh = 0
        if pos == "G" and k in c["GuardDownSkills"]:
            sh -= 1
        if pos == "B" and k in c["BigDownSkills"]:
            sh -= 1
        if k == best:
            sh -= 1
        tiers[k] = clamp(P + off + clamp(sh, -1, 1), 0, 4)
    for k in (a for a in O if GROUP[a] != "Skill"):
        tiers[k] = pick(c["BodyTierOdds"], u())
    a = c["WorkEthicBetaShape"]
    b = sorted(u() for _ in range(2 * a - 1))[a - 1]
    we = 1 + math.floor(98 * b + 0.5)
    return dict(rawShift=shift, playerTier=P, best=FUNDED.index(best), tiers=[tiers[k] for k in FUNDED], workEthic=we)


# ── 4c ─────────────────────────────────────────────────────────────────────────────────
def promise(c, tiers, k):
    return c["TierRate"][tiers[FUNDED.index(k)]] * c["GroupFactor"][GROUP[k]]


def allocate(c, tiers, card, camp_no, streaks, lowest_first=False):
    split = c["PrioritySplits"][min(camp_no, len(c["PrioritySplits"])) - 1]
    O = c["AttributeOrder"]

    def rank(k):
        r = promise(c, tiers, k)
        return r * (c["RepeatFactor"] if streaks[FUNDED.index(k)] >= c["RepeatAfter"] else 1.0)
    elig = [k for k in O if promise(c, tiers, k) > 0]
    sign = 1 if lowest_first else -1
    elig.sort(key=lambda k: (sign * rank(k), -card[k], O.index(k)))
    points = [0] * len(FUNDED)
    for k, p in zip(elig, split):
        points[FUNDED.index(k)] = p
    return points


def points_refused(c, points_by_name):
    """True if the hand-set points are refused (unknown name, non-integer, <0, >Max, over budget)."""
    for k, p in points_by_name.items():
        if k not in FUNDED or isinstance(p, bool) or not isinstance(p, int) or p < 0 or p > c["MaxPerAttribute"]:
            return True
    return sum(points_by_name.values()) > c["Budget"]


def camp_odds(c, we, share):
    T = c["WorkEthicTilt"] * (we - 50) / 49 + c["MinutesTilt"] * clamp((share - c["MinutesPivot"]) / c["MinutesSpan"], -1, 1)
    bad, nor, good, bo = c["CampOdds"]
    g0, b0 = good, bo
    if T >= 0:
        d = min(T, bad)
        bad -= d
        good += d * g0 / (g0 + b0)
        bo += d * b0 / (g0 + b0)
    else:
        d = min(-T, good + bo)
        good -= d * g0 / (g0 + b0)
        bo -= d * b0 / (g0 + b0)
        bad += d
    return [bad, nor, good, bo]


def grow_one(c, k, tier, points, outcome, ua, ub):
    g = GROUP[k]
    gain, slipped = 0.0, False
    if g == "Athleticism":
        gain += c["NaturalClimb"][tier] * (c["NaturalJitterLo"] + (c["NaturalJitterHi"] - c["NaturalJitterLo"]) * ua)
    if points > 0:
        rate = c["TierRate"][tier]
        if outcome == 3:
            rate = max(rate, c["TierRate"][c["BreakoutFloorTier"]])
        gain += points * rate * c["GroupFactor"][g] * c["CampMultiplier"][outcome] * (
            c["GrowthJitterLo"] + (c["GrowthJitterHi"] - c["GrowthJitterLo"]) * ub)
    elif g != "Athleticism":
        slipped = ub < c["AtrophyChance"]
    return gain, slipped


def ft_core(o, h):
    return FT_CENTER + FT_OUT_SPAN * math.tanh((o - FT_OUT_ANCHOR) / FT_OUT_SCALE) - FT_HEIGHT_COEF * ((h - 55.0) / 40.0)


def ft_delta(old_out, old_h, new_out, new_h):
    raw = ft_core(new_out, new_h) - ft_core(old_out, old_h)
    return int(round(raw)), raw                      # round() is half-to-even


def camp(c, st, card, camp_no, share, u, hand=None, keep_past_99=False):
    """One camp, in place on st (tiers/streaks/progress/workEthic/arrivalIq/arrivalDiscipline) and card."""
    points = hand if hand is not None else allocate(c, st["tiers"], card, camp_no, st["streaks"])
    st["streaks"] = [s + 1 if p > 0 else 0 for s, p in zip(st["streaks"], points)]
    odds = camp_odds(c, st["workEthic"], share)
    outcome = pick(odds, u())
    old_out, old_h = card["Outside"], card["Height"]
    gains, slipped = [0.0] * len(FUNDED), [False] * len(FUNDED)
    for k in c["AttributeOrder"]:
        i = FUNDED.index(k)
        ua = u() if GROUP[k] == "Athleticism" else None
        ub = u()
        gain, slip = grow_one(c, k, st["tiers"][i], points[i], outcome, ua, ub)
        gains[i], slipped[i] = gain, slip
        if slip:
            card[k] = max(0, card[k] - 1)
        st["progress"][i] += gain
        w = math.floor(st["progress"][i])
        card[k] += w
        st["progress"][i] -= w
        if card[k] >= 99 and not keep_past_99:
            card[k], st["progress"][i] = 99, 0.0
    for key, slot, arr, age, mg, capk in (
            ("BasketballIQ", 27, st["arrivalIq"], c["IqAgeGrowth"], c["IqMinutesGrowth"], c["IqCapAboveArrival"]),
            ("Discipline", 28, st["arrivalDiscipline"], c["DisciplineAgeGrowth"], c["DisciplineMinutesGrowth"], c["DisciplineCapAboveArrival"])):
        cap = min(99, arr + capk)
        st["progress"][slot] += age + mg * share
        w = math.floor(st["progress"][slot])
        card[key] += w
        st["progress"][slot] -= w
        if card[key] >= cap:
            card[key], st["progress"][slot] = cap, 0.0
    spurt = u() < c["HeightSpurtChance"]
    if spurt:
        card["Height"] = min(99, card["Height"] + c["HeightSpurtRating"])
        card["Wingspan"] = min(99, card["Wingspan"] + c["HeightSpurtWingspan"])
    fd, fraw = ft_delta(old_out, old_h, card["Outside"], card["Height"])
    card["FreeThrow"] = int(clamp(card["FreeThrow"] + fd, FT_MIN, FT_MAX))
    return dict(outcome=outcome, points=list(points), odds=odds, gains=gains, slipped=slipped, spurt=spurt,
                ftDelta=fd, ftRaw=fraw)


# ── Case builders ──────────────────────────────────────────────────────────────────────
CAMP_CARD_KEYS = FUNDED + ["BasketballIQ", "Discipline", "Height", "Wingspan", "FreeThrow", "Hustle"]


def base_card(v=50):
    card = {k: v for k in CAMP_CARD_KEYS}
    for k in BODY_INDEX:
        card[k] = v
    return card


def lcg_draws(seed, n):
    """Documented, deterministic draws for the injected cases (not the engine's streams)."""
    r = SplitMix(seed)
    return [r.u() for _ in range(n)]


def potential_draw_count(c):
    return 4 + 20 + 7 + 2 * c["WorkEthicBetaShape"] - 1


CAMP_DRAWS = 1 + 2 * len(ATH) + (len(SKILLS) + len(BODY)) + 1


def cohort_cases(c):
    cases = []
    # A: a mixed cohort, three positions, uneven sizes, a talent tie, SD-zero position.
    men = []
    spec = [("G", 0, dict(Height=40, Strength=40, Speed=70, Quickness=72, FirstStep=70, Vertical=60, BallHandling=75, Passing=70, Outside=68)),
            ("G", 1, dict(Height=42, Strength=38, Speed=60, Quickness=62, FirstStep=60, Vertical=55, BallHandling=60, Passing=55, Outside=50)),
            ("G", 2, dict(Height=38, Strength=45, Speed=80, Quickness=80, FirstStep=78, Vertical=75, BallHandling=50, Passing=45, Outside=40)),
            ("G", 3, dict(Height=42, Strength=38, Speed=60, Quickness=62, FirstStep=60, Vertical=55, BallHandling=60, Passing=55, Outside=50)),
            ("W", 4, dict(Height=60, Strength=55, Speed=65, Quickness=60, FirstStep=60, Vertical=65, Outside=72, Mid=65, PerimeterDefense=60)),
            ("W", 5, dict(Height=58, Strength=50, Speed=55, Quickness=55, FirstStep=50, Vertical=55, Outside=55, Mid=50)),
            ("B", 6, dict(Height=88, Strength=80, Speed=55, Quickness=50, FirstStep=50, Vertical=80, Finishing=40, RimProtection=45, PostMoves=30)),
            ("B", 7, dict(Height=80, Strength=70, Speed=40, Quickness=40, FirstStep=40, Vertical=50, Finishing=70, RimProtection=60, PostMoves=72, Close=68)),
            ("B", 8, dict(Height=84, Strength=75, Speed=45, Quickness=45, FirstStep=45, Vertical=60, Finishing=55, RimProtection=55, PostMoves=50)),
            ("B", 9, dict(Height=76, Strength=60, Speed=50, Quickness=50, FirstStep=50, Vertical=55, Finishing=45, RimProtection=35, PostMoves=40))]
    for pos, idx, over in spec:
        card = base_card(30)
        card.update(over)
        men.append(dict(idx=idx * 3 + 5, pos=pos, card=card))      # arrival indices sparse and not 0-based
    cases.append(("mixed cohort", men, 101))
    # B: a one-man position (SD 0 -> 1) and identical twins (talent tie to the lower index)
    twin = base_card(45)
    twin.update(dict(Outside=60, Height=55))
    men2 = [dict(idx=11, pos="W", card=dict(twin)), dict(idx=4, pos="W", card=dict(twin)),
            dict(idx=7, pos="G", card=base_card(40))]
    cases.append(("twins and a lone guard", men2, 202))
    out = []
    for name, men, seed in cases:
        d = descriptors(c, men)
        rows = []
        for m in men:
            draws = lcg_draws(seed * 1000 + m["idx"], potential_draw_count(c))
            r = roll_potential(c, d[m["idx"]], m["pos"], m["card"], injected(draws))
            rows.append(dict(idx=m["idx"], pos=m["pos"], card=m["card"], draws=draws,
                             expect=dict(**{k: d[m["idx"]][k] for k in ("body", "skill", "bz", "sz", "gap", "talent", "talentTop")}, **r)))
        out.append(dict(name=name, men=rows))
    return out


def work_ethic_cases(c):
    a = c["WorkEthicBetaShape"]
    sets = [[0.5] * (2 * a - 1), [0.0] * (2 * a - 1), [0.999999999] * (2 * a - 1),
            lcg_draws(9001, 2 * a - 1), lcg_draws(9002, 2 * a - 1)]
    out = []
    for us in sets:
        b = sorted(us)[a - 1]
        out.append(dict(us=us, expect=1 + math.floor(98 * b + 0.5)))
    return out


def alloc_cases(c, order_name):
    out = []
    card = base_card(50)
    tiers = [2] * len(FUNDED)
    # ties on promise, broken by current rating then order
    t1 = list(tiers)
    for k in ("Outside", "Finishing", "RimProtection", "Mid"):
        t1[FUNDED.index(k)] = 4
    card1 = dict(card, Outside=52, Finishing=40, RimProtection=52, Mid=60)
    out.append(dict(name="ties on promise", tiers=t1, card=card1, campNumber=1, streaks=[0] * len(FUNDED)))
    # fewer eligible than priorities (only two attributes with any promise)
    t2 = [0] * len(FUNDED)
    t2[FUNDED.index("Close")] = 1
    t2[FUNDED.index("Speed")] = 4
    out.append(dict(name="fewer eligible than priorities", tiers=t2, card=card, campNumber=3, streaks=[0] * len(FUNDED)))
    # each camp number, and one beyond the list
    t3 = [1 + (i % 4) for i in range(len(FUNDED))]
    for n in (1, 2, 3, len(c["PrioritySplits"]) + 2):
        out.append(dict(name=f"camp number {n}", tiers=t3, card=card, campNumber=n, streaks=[0] * len(FUNDED)))
    # rotation: Outside (very high) funded two summers running drops below a high skill it outranked
    t4 = [1] * len(FUNDED)
    t4[FUNDED.index("Outside")] = 4
    t4[FUNDED.index("Passing")] = 3
    st4 = [0] * len(FUNDED)
    st4[FUNDED.index("Outside")] = 2
    out.append(dict(name="rotation", tiers=t4, card=card, campNumber=3, streaks=st4))
    # body and athleticism compete with skills on promise
    t5 = [1] * len(FUNDED)
    t5[FUNDED.index("Strength")] = 4
    t5[FUNDED.index("Quickness")] = 4
    out.append(dict(name="body beats a low skill, athleticism discounted", tiers=t5, card=card, campNumber=1, streaks=[0] * len(FUNDED)))
    for case in out:
        case["order"] = order_name
        case["expect"] = allocate(c, case["tiers"], case["card"], case["campNumber"], case["streaks"])
        case["expectLowestFirst"] = allocate(c, case["tiers"], case["card"], case["campNumber"], case["streaks"], lowest_first=True)
    return out


def tilt_cases(c):
    out = []
    for we, share in ((99, 1.0), (99, 0.0), (1, 1.0), (1, 0.0), (50, c["MinutesPivot"]), (73, 0.62), (12, 0.15)):
        out.append(dict(workEthic=we, share=share, expect=camp_odds(c, we, share)))
    return out


def camp_cases(c, order_name):
    out = []

    def state(tiers=None, we=50, iq=50, disc=50, streaks=None, progress=None):
        return dict(tiers=tiers or [2] * len(FUNDED), streaks=streaks or [0] * len(FUNDED),
                    progress=progress or [0.0] * 29, workEthic=we, arrivalIq=iq, arrivalDiscipline=disc)

    def add(name, st, card, camp_no, share, draws, hand=None):
        st0, card0 = copy.deepcopy(st), dict(card)
        st1, card1 = copy.deepcopy(st), dict(card)
        rec = camp(c, st1, card1, camp_no, share, injected(draws), hand)
        st2, card2 = copy.deepcopy(st), dict(card)
        rec99 = camp(c, st2, card2, camp_no, share, injected(draws), hand, keep_past_99=True)
        out.append(dict(name=name, order=order_name, state=st0, card=card0, campNumber=camp_no, share=share, draws=draws,
                        hand=hand, expect=dict(rec, card=card1, progress=st1["progress"], streaks=st1["streaks"]),
                        expectKeepPast99=dict(card=card2, progress=st2["progress"])))

    funded_tiers = [3] * len(FUNDED)
    # funded / unfunded / slip, a normal camp (outcome draw 0.5)
    draws = [0.5] + lcg_draws(31, CAMP_DRAWS - 1)
    add("normal camp, computer's points", state(funded_tiers), base_card(50), 1, 0.4, draws)
    # every unfunded attribute slips (ub = 0.1 < atrophy) — hand points on three attributes
    draws2 = [0.5] + [0.1] * (CAMP_DRAWS - 2) + [0.9]
    add("slips on the unfunded", state(funded_tiers), base_card(50), 1, 0.2, draws2,
        hand=[20 if k == "Outside" else 15 if k in ("Strength", "Quickness") else 0 for k in FUNDED])
    # breakout floor: a low-tier funded skill grows at the floor's rate
    draws3 = [0.999] + [0.6] * (CAMP_DRAWS - 2) + [0.9]
    low = [1] * len(FUNDED)
    add("breakout floor", state(low, we=99), base_card(50), 2, 1.0, draws3)
    # the 99 discard: a 98 shooter funded on a breakout
    card99 = dict(base_card(50), Outside=98, Speed=98)
    vh = [4] * len(FUNDED)
    add("the 99 discard", state(vh, we=99, progress=[0.9] * 29), card99, 1, 1.0, [0.999] + [0.99] * (CAMP_DRAWS - 2) + [0.9])
    # the IQ cap, the height spurt and the free-throw delta
    cardiq = dict(base_card(50), BasketballIQ=57, Discipline=90, Height=60, Wingspan=61, Outside=40, FreeThrow=70)
    add("IQ cap, spurt, free throws", state([4] * len(FUNDED), iq=50, disc=88), cardiq, 1, 1.0,
        [0.3] + lcg_draws(77, CAMP_DRAWS - 2) + [0.0])
    # a spurt at the ceiling
    cardh = dict(base_card(50), Height=98, Wingspan=99)
    add("spurt at the ceiling", state(), cardh, 2, 0.5, [0.1] + lcg_draws(78, CAMP_DRAWS - 2) + [0.01])
    return out


def ft_cases():
    out = []
    for o0, h0, o1, h1 in ((36, 55, 36, 55), (36, 55, 46, 55), (52, 60, 61, 60), (70, 80, 70, 83), (20, 40, 25, 43), (90, 55, 99, 55)):
        d, raw = ft_delta(o0, h0, o1, h1)
        out.append(dict(old=[o0, h0], new=[o1, h1], delta=d, raw=raw))
    return out


def config_refusal_cases(c):
    edits = [
        ("odds not summing to one", {"PlayerTierOdds": [0.1, 0.2, 0.3, 0.2, 0.1]}),
        ("odds wrong length", {"CampOdds": [0.5, 0.5]}),
        ("a negative odds entry", {"BodyTierOdds": [-0.1, 0.4, 0.3, 0.3, 0.1]}),
        ("good + breakout zero", {"CampOdds": [0.5, 0.5, 0.0, 0.0]}),
        ("probability above one", {"AtrophyChance": 1.5}),
        ("a zero divisor", {"MinutesSpan": 0.0}),
        ("talent fraction zero", {"TalentTopFraction": 0.0}),
        ("negative rate", {"TierRate": [0.0, -0.1, 0.22, 0.35, 0.5]}),
        ("jitter inverted", {"GrowthJitterLo": 1.3}),
        ("breakout floor tier out of range", {"BreakoutFloorTier": 5}),
        ("split over the cap", {"PrioritySplits": [[25, 25]]}),
        ("split over the budget", {"PrioritySplits": [[20, 20, 20]]}),
        ("empty split", {"PrioritySplits": [[]]}),
        ("repeat after zero", {"RepeatAfter": 0}),
        ("repeat factor above one", {"RepeatFactor": 1.2}),
        ("IQ growth negative", {"IqMinutesGrowth": -1.0}),
        ("discipline cap negative", {"DisciplineCapAboveArrival": -2}),
        ("spurt negative", {"HeightSpurtWingspan": -1}),
        ("beta shape zero", {"WorkEthicBetaShape": 0}),
        ("budget zero", {"Budget": 0, "PrioritySplits": [[0]]}),
        ("max per attribute above budget", {"MaxPerAttribute": 60}),
        ("group factor missing a group", {"GroupFactor": {"Skill": 1.0, "Body": 1.0}}),
        ("attribute order missing a name", {"AttributeOrder": FUNDED[:-1]}),
        ("attribute order with a duplicate", {"AttributeOrder": FUNDED[:-1] + ["Outside"]}),
        ("down skills naming a body attribute", {"BigDownSkills": ["Outside", "Strength"]}),
        ("several bad keys at once", {"Budget": 0, "MinutesSpan": -1.0, "AtrophyChance": 2.0, "RepeatFactor": -0.5,
                                      "AttributeOrder": ["Outside"]}),
    ]
    out = []
    for name, ed in edits:
        cc = copy.deepcopy(c)
        cc.update(ed)
        keys = sorted(set(validate(cc)))
        assert keys, f"refusal case '{name}' was not refused by the oracle"
        out.append(dict(name=name, edits=ed, expectKeys=keys))
    return out


def points_refusal_cases(c):
    out = []
    for name, pts in (("over the budget", {"Outside": 20, "Mid": 20, "Close": 15}),
                      ("over the per-attribute cap", {"Outside": 25}),
                      ("negative", {"Outside": -1}),
                      ("not a funded attribute", {"Height": 10}),
                      ("legal", {"Outside": 20, "Mid": 15, "Close": 15})):
        out.append(dict(name=name, points=pts, refused=points_refused(c, pts)))
    return out


def stream_cases():
    out = []
    for arrival_seed, idx in ((20260720, 0), (20260720, 4510), (-5, 17), (9223372036854775807, 3)):
        r = potential_stream(arrival_seed, idx)
        dev = r.u64()
        out.append(dict(kind="potential", seed=arrival_seed, index=idx, devSeed=str(dev), next=[r.u() for _ in range(3)]))
        cs = camp_stream(dev, arrival_seed + 1 if arrival_seed < 9223372036854775807 else -9223372036854775808)
        out.append(dict(kind="camp", devSeed=str(dev), seasonSeed=arrival_seed + 1 if arrival_seed < 9223372036854775807 else -9223372036854775808,
                        next=[cs.u() for _ in range(3)]))
    return out


# ── The archetype careers (page-only, and a K5 parity case) ────────────────────────────
ARCH_SEED = 121_000
ARCH_N = 2000


def archetypes():
    """Each row: a player type with fixed tiers on his named attributes; his other tiers rolled as
    4b with a fixed descriptor; three camps at a fixed minutes share. Seeds: potential stream
    (ARCH_SEED + row, career index); camp n at season seed ARCH_SEED + 100 * (row + 1) + n."""
    def card(over):
        k = base_card(40)
        k.update(over)
        return k

    def hand(spec):
        return [spec.get(k, 0) for k in FUNDED]
    return [
        dict(name="Raw 7-footer, average minutes", pos="B", desc=dict(gap=1.0, sz=-0.5, talentTop=False),
             card=card(dict(Height=88, Wingspan=88, Strength=70, Finishing=38, RimProtection=42, BasketballIQ=40)),
             tiers=dict(Finishing=4, RimProtection=4), share=0.40, targets=["Finishing", "RimProtection", "BasketballIQ"]),
        dict(name="Ready point guard, starter", pos="G", desc=dict(gap=-0.5, sz=1.2, talentTop=True),
             card=card(dict(BallHandling=68, Passing=66, Playmaking=64, Quickness=75, BasketballIQ=60)),
             tiers=dict(BallHandling=2, Quickness=1), share=0.80, targets=["BallHandling", "Quickness", "BasketballIQ"]),
        dict(name="Shooter, poor work ethic", pos="W", desc=dict(gap=0.0, sz=0.3, talentTop=False),
             card=card(dict(Outside=52, Mid=48)), tiers=dict(Outside=4, Mid=3), workEthic=20, share=0.50,
             targets=["Outside", "Mid"]),
        dict(name="Wing, weight room every summer (camp set by hand)", pos="W", desc=dict(gap=0.2, sz=0.0, talentTop=False),
             card=card(dict(Strength=45, Weight=45, Outside=50)), tiers=dict(Strength=3, Outside=3), share=0.50,
             hand=hand(dict(Strength=20, Weight=20, Endurance=10)), targets=["Strength", "Outside"]),
        dict(name="Monster athlete, low skill potential, funded finishing 20 + perimeter D 20 + close 10", pos="W",
             desc=dict(gap=1.2, sz=-1.0, talentTop=False),
             card=card(dict(Speed=85, Quickness=85, FirstStep=85, Vertical=88, Finishing=40, PerimeterDefense=40, Close=40)),
             tiers=dict(Finishing=1, PerimeterDefense=1, Close=1), share=0.50,
             hand=hand(dict(Finishing=20, PerimeterDefense=20, Close=10)), targets=["Finishing"]),
        dict(name="Same athlete, the computer's camp", pos="W", desc=dict(gap=1.2, sz=-1.0, talentTop=False),
             card=card(dict(Speed=85, Quickness=85, FirstStep=85, Vertical=88, Finishing=40, PerimeterDefense=40, Close=40)),
             tiers=dict(Finishing=1, PerimeterDefense=1, Close=1), share=0.50, targets=["Finishing"]),
        dict(name="Walk-on (every skill low), bench", pos="G", desc=dict(gap=0.0, sz=-1.5, talentTop=False),
             card=card(dict(Outside=38)), tiers={k: 1 for k in SKILLS}, share=0.05, targets=["Outside"]),
        dict(name="Quickness (high), not funded", pos="G", desc=dict(gap=0.0, sz=0.0, talentTop=False),
             card=card(dict(Quickness=60)), tiers=dict(Quickness=3), share=0.50,
             hand=hand(dict(Outside=20, Mid=15, Close=15)), targets=["Quickness"]),
        dict(name="Quickness (high), funded 20 every summer", pos="G", desc=dict(gap=0.0, sz=0.0, talentTop=False),
             card=card(dict(Quickness=60)), tiers=dict(Quickness=3), share=0.50,
             hand=hand(dict(Quickness=20, Outside=15, Mid=15)), targets=["Quickness"]),
    ]


def run_archetypes(c):
    rows = []
    for a_i, a in enumerate(archetypes()):
        finals = {t: [] for t in a["targets"]}
        for n in range(ARCH_N):
            rng = potential_stream(ARCH_SEED + a_i, n)
            dev = rng.u64()
            r = roll_potential(c, a["desc"], a["pos"], a["card"], rng.u)
            tiers = list(r["tiers"])
            for k, t in a["tiers"].items():
                tiers[FUNDED.index(k)] = t
            st = dict(tiers=tiers, streaks=[0] * len(FUNDED), progress=[0.0] * 29,
                      workEthic=a.get("workEthic", r["workEthic"]),
                      arrivalIq=a["card"]["BasketballIQ"], arrivalDiscipline=a["card"]["Discipline"])
            card = dict(a["card"])
            for camp_no in (1, 2, 3):
                cs = camp_stream(dev, ARCH_SEED + 100 * (a_i + 1) + camp_no)
                camp(c, st, card, camp_no, a["share"], cs.u, a.get("hand"))
            for t in a["targets"]:
                finals[t].append(card[t])
        for t in a["targets"]:
            xs = sorted(finals[t])
            pct = [xs[((ARCH_N - 1) * k) // 10] for k in (1, 5, 9)]
            rows.append(dict(player=a["name"], attribute=t, fr=a["card"][t], p10=pct[0], p50=pct[1], p90=pct[2]))
    return rows


def permuted_order():
    """A fixed, documented permutation of the 27: reversed, with Outside and Strength swapped in."""
    o = list(reversed(FUNDED))
    i, j = o.index("Outside"), o.index("Strength")
    o[i], o[j] = o[j], o[i]
    return o


def build():
    base = load_config()
    errs = validate(base)
    if errs:
        sys.exit(f"config.json's Development section is refused by the oracle: {errs}")
    perm = copy.deepcopy(base)
    perm["AttributeOrder"] = permuted_order()
    golden = dict(
        provenance=dict(
            oracle="tools/development_oracle.py (S121)",
            configSha256=hashlib.sha256(json.dumps(base, sort_keys=True).encode()).hexdigest(),
            note="Every configurable value read from src/Charm.Harness/config.json's Development section."),
        permutedOrder=permuted_order(),
        cohorts=cohort_cases(base) + [dict(cc, name=cc["name"] + " (permuted order)", order="permuted")
                                       for cc in cohort_cases(perm)],
        workEthic=work_ethic_cases(base),
        allocations=alloc_cases(base, "default") + alloc_cases(perm, "permuted"),
        tilts=tilt_cases(base),
        camps=camp_cases(base, "default") + camp_cases(perm, "permuted"),
        freeThrows=ft_cases(),
        configRefusals=config_refusal_cases(base),
        pointsRefusals=points_refusal_cases(base),
        streams=stream_cases(),
        archetypeSeed=ARCH_SEED, archetypeCareers=ARCH_N,
        archetypeDefs=[dict(name=a["name"], pos=a["pos"], desc=a["desc"], card=a["card"], tiers=a["tiers"],
                            workEthic=a.get("workEthic"), share=a["share"], hand=a.get("hand"), targets=a["targets"])
                       for a in archetypes()],
        archetypes=run_archetypes(base),
    )
    for cc in golden["cohorts"]:
        cc.setdefault("order", "default")
    return json.dumps(golden, indent=1, sort_keys=False) + "\n"


def main():
    text = build()
    if "--check" in sys.argv:
        with open(GOLDEN, encoding="utf-8") as f:
            on_disk = f.read()
        if on_disk != text:
            sys.exit("development_golden.json does NOT reproduce from a clean run of the oracle.")
        print("development_golden.json reproduces from a clean run (byte for byte).")
        return
    with open(GOLDEN, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    again = build()
    if again != text:
        sys.exit("the oracle is not deterministic: two clean runs differ.")
    print(f"wrote {GOLDEN} ({len(text)} bytes); a second clean run reproduces it byte for byte.")


if __name__ == "__main__":
    main()
