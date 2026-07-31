#!/usr/bin/env python3
"""
S88 — WHO GOT BACK.  The LOCKED oracle for transition defence.

Signed off by Emmett in the S88 design conversation (2026-07-31) by reading the
archetype tables this file prints.  The tables ARE the spec: the C# port is held
to `emit_golden()`, not to a re-derivation of the formulas.

────────────────────────────────────────────────────────────────────────────────
THE BASKETBALL
────────────────────────────────────────────────────────────────────────────────
Before S88 a fast break was ONE TEAM AVERAGE against another: the defence's mean
Hustle vs the offence's mean Hustle shaved a couple of points off the make and
that was the entire transition defence.  Nobody guarded anybody, no individual
rating was read, and the engine could not tell a break against a rim protector
from a break against a shooting guard.  It is the one place in the engine where a
team is a scalar instead of five men.

S88 replaces it.  Each of the five carries a GOT-BACK number, and that one number
does three jobs:

  1. WHO is the defender on this break        (relative, within the lineup)
  2. HOW SET he is when he arrives            (absolute — a man who sprinted back
                                               is closer to a set defender than one
                                               who arrived late and gasping)
  3. HOW MANY of them got back                (the team aggregate — Emmett's ruling:
                                               "high team speed means more guys get
                                               back, thus harder to convert")

Job 3 is the DOMINANT channel and the one the old Hustle wire was reaching for.

────────────────────────────────────────────────────────────────────────────────
RULINGS THIS FILE ENCODES  (Emmett, S88 design conversation)
────────────────────────────────────────────────────────────────────────────────
R1  No guarantee how many get back.  Faster players are MORE LIKELY to be the one
    there; nobody is ever impossible.  Hence LUCK_FLOOR > 0 and a weighted draw
    rather than a threshold.

R2  Depth is set by THE MAN YOU ARE GUARDING, not by your own body.  Even five
    identical rangy athletes have one man playing the five, and he is under the
    basket.  His odds are MUTED, never zero, and his legs fight it.
    ★ This is why depth is read off the OPPOSING lineup: a defence does not get
    stranded under the rim against a team that goes small, because there is no
    post to be stuck guarding.  Reading it off the defender's own size would be a
    scalar wearing a costume (the S81.1 mistake in a new place).

R3  The break block is a CHASE-DOWN: speed and length, not rim protection.  A
    rangy fast wing whose block rating gives him nothing in the halfcourt runs
    people down in transition.  Rim protection is a junior partner, not the driver.

R4  A break stays a better look than a halfcourt set even against a good defender —
    "fast break attempts happen because it's considered better than a halfcourt
    set" — so the defensive read is discounted, never full strength.  But it is
    not FREE, which is what it is today.

R5  Being there is worth something ON ITS OWN, independent of any rating.  A guard
    standing in the lane makes you change the shot even though he will never block
    it.  ★ Without this the whole design is inert: the first draft had arrival
    scale only a defender's rim-protection DEVIATION from average, which cancels
    across a lineup, and whole-team speed moved break FG% by 0.0 points.

R6  Hustle rides INSIDE the individual legs term, it does not get its own channel.
    The old team-mean Hustle wire (RollHGenerator C8) RETIRES into this — keeping
    both would pay a fast, high-effort team twice for turning and running, which is
    the same double-count that retired two dials at S86.

R7  Where he shot from moves him on THIS trip.  A man who just shot at the rim is
    standing under it; a man who just shot a three is already at the arc, halfway
    back.  A stretch big is genuinely better transition defence than a
    back-to-the-basket big of identical speed.

R8  No discrete "nobody got back" branch.  Emmett: we lump every transition
    iteration into one sum — the 2-on-0 and the 1-on-3 acrobatic finish are already
    inside the make distribution, and the play-by-play layer names them later from
    the outcome.  A very weak contest IS the runaway dunk.

────────────────────────────────────────────────────────────────────────────────
ANCHOR
────────────────────────────────────────────────────────────────────────────────
Five average men against an average offence reproduce today's page EXACTLY —
break FG% 46.6%, break block rate 9.62% — so a neutral lineup moves nothing and no
config rebase is needed.  Same anchoring discipline as S62's per-man aggregate.

All magnitudes below are CALIBRATION PLACEHOLDERS.  None is ever suite-asserted;
they live on the season page (page-only calibration principle).
"""
import math

# ── dials ─────────────────────────────────────────────────────────────────────
LUCK_FLOOR      = 0.15    # R1: nobody is ever impossible
LEGS_SPAN       = 0.45    # how much legs move his odds
DEPTH_SPAN      = 0.34    # R2: how much being on their big hurts
EFFORT_MIX      = 0.75    # R6: speed vs hustle inside the legs term
POSTNESS_SCALE  = 25.0    # tanh denominator, same idiom as the reach-in lean
ARRIVAL_SPAN    = 0.60    # how much arriving well vs late scales his own defence
TRANSITION_DISCOUNT = 0.55  # R4: a backpedalling man is not a set defender

BASE_BREAK_FG   = 0.466   # today's page — the anchor
BASE_BREAK_BLK  = 0.0962  # today's page — the anchor

RIMPROT_SWING   = 0.14    # the picked man's rim protection, before the discount
TEAM_PRESENCE   = 0.22    # R5 + job 3: HOW MANY got back — the dominant channel
CHASE_SWING     = 0.11    # R3: the chase-down, length-led
CHASE_LENGTH_W  = 0.70
CHASE_RIMPROT_W = 0.30
CHASE_SPEED     = 0.055   # R3: running him down, in the block term directly

ZONE_SHOOTER = {"rim":0.70, "short":0.80, "mid":1.00, "long":1.10, "three":1.20}  # R7
REFERENCE_GOTBACK = LUCK_FLOOR + 1.0   # a league-average man on a neutral assignment


def clamp(x, lo=-1.0, hi=1.0):
    return max(lo, min(hi, x))


def legs_factor(speed, hustle):
    """R6 — speed-primary, hustle riding inside it.  Symmetric about 50."""
    effort = EFFORT_MIX * speed + (1.0 - EFFORT_MIX) * hustle
    return 1.0 + LEGS_SPAN * clamp((effort - 50.0) / 49.0)


def depth_factor(opp_postness, opp_lineup_mean_postness):
    """R2 — set by the man you are guarding, relative to HIS OWN lineup.
    Returns > 1 when you are on a perimeter man, < 1 when you are on their post."""
    o = 0.5 - 0.5 * math.tanh((opp_postness - opp_lineup_mean_postness) / POSTNESS_SCALE)
    return 1.0 + DEPTH_SPAN * (2.0 * o - 1.0)


def got_back(defender, opponent, opp_mean_postness, shooter_zone=None):
    f = legs_factor(defender["speed"], defender["hustle"]) \
        * depth_factor(opponent["post"], opp_mean_postness)
    if shooter_zone is not None:
        f *= ZONE_SHOOTER[shooter_zone]          # R7
    return LUCK_FLOOR + f


def lineup_got_back(defence, offence, shooter_index=None, shooter_zone=None):
    opp_mean = sum(m["post"] for m in offence) / len(offence)
    return [got_back(defence[i], offence[i], opp_mean,
                     shooter_zone if i == shooter_index else None)
            for i in range(len(defence))]


def team_aggregate(weights):
    """Job 3 — S62's per-man aggregate idiom.  Five average men give exactly 1.0.
    Emergent from five individuals; NOT a team rating."""
    return sum(weights) / (len(weights) * REFERENCE_GOTBACK)


def arrival_quality(g):
    """Job 2 — how set he is when he arrives.  1.0 = league-average man, neutral assignment."""
    return 1.0 + ARRIVAL_SPAN * clamp((g - REFERENCE_GOTBACK) / REFERENCE_GOTBACK)


def break_make_pct(defender, g, aggregate):
    a  = arrival_quality(g)
    rp = clamp((defender["rimprot"] - 50.0) / 49.0)
    return max(0.0, min(1.0,
        BASE_BREAK_FG
        - RIMPROT_SWING * rp * TRANSITION_DISCOUNT * a     # R4: who contested it, discounted
        - TEAM_PRESENCE * (aggregate - 1.0)))              # R5/job 3: how many got back


def break_block_pct(defender, g):
    """R3 — the chase-down.  Length-led, speed direct, rim protection junior."""
    a  = arrival_quality(g)
    rp = clamp((defender["rimprot"] - 50.0) / 49.0)
    lg = clamp((defender["length"]  - 50.0) / 49.0)
    return max(0.0,
        BASE_BREAK_BLK
        + CHASE_SWING * a * (CHASE_LENGTH_W * lg + CHASE_RIMPROT_W * rp)
        + CHASE_SPEED * (a - 1.0))


# ── golden fixture ────────────────────────────────────────────────────────────
def _p(speed, post, rimprot, length, hustle=60):
    return dict(speed=speed, post=post, rimprot=rimprot, length=length, hustle=hustle)

AVG_OFFENCE = [_p(60,30,40,44), _p(58,34,42,48), _p(56,50,50,58),
               _p(50,70,66,74), _p(44,88,86,90)]

def emit_golden():
    """Every case the C# port must reproduce.  Bound at 1e-9 ABSOLUTE, deliberately
    NOT bitwise — S81.3 shipped a bit-exact cross-platform fixture and produced a red
    suite on Emmett's machine with nothing wrong in the engine."""
    cases = []
    speeds  = [20, 35, 50, 65, 80, 95]
    posts   = [25, 45, 65, 85]
    lengths = [30, 50, 70, 90]
    rims    = [25, 50, 75, 95]
    opp_mean = sum(m["post"] for m in AVG_OFFENCE) / 5
    for s in speeds:
        for op in posts:
            d = _p(s, 50, 50, 50)
            g = got_back(d, _p(50, op, 50, 50), opp_mean)
            cases.append(("gotback", s, op, -1, -1, None, g))
    for s in speeds:
        for z in [None, "rim", "short", "mid", "long", "three"]:
            d = _p(s, 50, 50, 50)
            g = got_back(d, AVG_OFFENCE[2], opp_mean, z)
            cases.append(("shooter", s, -1, -1, -1, z, g))
    for s in speeds:
        for rp in rims:
            d = _p(s, 50, rp, 50)
            g = got_back(d, AVG_OFFENCE[2], opp_mean)
            for agg in [0.85, 1.00, 1.25]:
                cases.append(("make", s, -1, rp, -1, f"{agg:.2f}",
                              break_make_pct(d, g, agg)))
    for s in speeds:
        for lg in lengths:
            for rp in rims:
                d = _p(s, 50, rp, lg)
                g = got_back(d, AVG_OFFENCE[2], opp_mean)
                cases.append(("block", s, -1, rp, lg, None, break_block_pct(d, g)))
    return cases


if __name__ == "__main__":
    g = emit_golden()
    print(f"# transition_defense golden — {len(g)} cases, bound 1e-9 absolute")
    for kind, s, op, rp, lg, z, v in g:
        print(f"{kind}\t{s}\t{op}\t{rp}\t{lg}\t{z}\t{v:.12f}")
