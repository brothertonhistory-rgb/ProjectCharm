#!/usr/bin/env python3
"""
Project Charm — Session 102 matching oracle (THE MATCHING).

★ THIS DOCSTRING IS THE SPECIFICATION. The C# port in
`src/Charm.Harness/Program.Season.Matching.cs` is written against THIS file and is
proven by pair-for-pair, ledger-field-for-ledger-field parity against the golden this
script emits (`tools/matching_golden.json`, Phase 93 C14). Where the two disagree,
this file is right and the C# is wrong.

═══════════════════════════════════════════════════════════════════════════════════
WHAT THIS SESSION DOES, AND WHAT IT REFUSES TO DO
═══════════════════════════════════════════════════════════════════════════════════

Every school's November gets PAIRED: who plays whom, who hosts, which pairs are
neutral. NO DATE, NO CITY, NO SeasonGame, NO POSSESSION. Sites and nights are arc
session 3. The vocabulary here is GAMES, REQUESTS and TOKENS — the word "date"
appears nowhere, exactly as S101 established.

The input is S101's report (`BuildNonConferenceRequests`), consumed and never
recomputed by the matcher. This oracle DOES compute the report — it has to produce
one from somewhere — and the golden carries the report it used, so the C# side can
assert the live report is field-for-field identical to the golden's before it
believes a single pair (C14a). If S101's math ever moves, that assertion goes red
immediately rather than the parity check failing for a mysterious reason.

★ THE INDEPENDENTS ARE ABSENT FROM EVERY PHASE, including the terminal partner pool.
A school with no S101 request has no target, and a school with no target cannot be
handed an over-target game. On the stock world that is fourteen schools.

═══════════════════════════════════════════════════════════════════════════════════
★ THE DISTANCE KEY IS QUANTIZED, BECAUSE FLOAT ORDERING IS NOT PORTABLE
═══════════════════════════════════════════════════════════════════════════════════

Every ordering in this file breaks ties on distance. Python's and C#'s trig differ by
ULPs (the S81.3 lesson, now cross-language), and two NEARLY equal distances could sort
differently in the two languages, which would break pair-for-pair parity for a reason
that has nothing to do with the policy. So all ordering uses

    DistanceKey(a, b) = floor(DistanceMiles(a, b) + 0.5)      an INTEGER

and that exact formula in BOTH languages. Neither language's default rounding is used:
Python's round() and C#'s Math.Round() are both ties-to-even, which is not what this
wants and is a silent trap for a future session.

DistanceMiles is S92's function, reproduced here and NOT reinvented: haversine on a
spherical earth, mean radius 3958.7613 miles, the intermediate clamped to [0,1] before
the arcsine. This file's implementation is verified against `tools/geo_distance_golden.json`
at run time (see main), so "the same model" is asserted rather than assumed.

★ WHOLE-MILE QUANTIZATION MAKES PARITY ROBUST, NOT CERTAIN, AND THE MARGIN IS MEASURED.
A pair sitting exactly on a half-mile boundary could still quantize differently. Across
all 40,186 pairs of the 284 towns the 333 targeted schools live in, the closest any pair
comes to a half-mile boundary is 0.0000306 miles (Colorado in Boulder to San Francisco,
935.500031 mi) — about seven orders of magnitude above the ~1e-12 mi agreement this
file's haversine shows against the S92 golden. So no pair in the stock world is
vulnerable, and C14 parity is the standing tripwire for any future world. This margin
is RE-MEASURED on every run (see main) rather than quoted.

═══════════════════════════════════════════════════════════════════════════════════
THE BUCKETS AND THE MIXES (Emmett's rulings, 2026-08-05 — R8 constants, one seam)
═══════════════════════════════════════════════════════════════════════════════════

A home request names the KIND of opponent wanted, by that opponent's PRESTIGE — not by
its class. (Class carries S101's conference-tier floor; a bucket is about who the
opponent actually is. Northwestern at prestige 53 schedules as a Marquee school but
FILLS somebody else's Working bucket.)

    E  Easy      prestige < 25
    W  Working   prestige 25-54
    D  Decent    prestige 55-79
    N  Name      prestige 80+
    ANY          every band, and it never spills

    Marquee   5 E / 2 W / 1 D
    Solid     3 E / 2 W / 1 D
    Working   2 E / 2 W
    Selling   every home game is ANY

★ THE SPILL LADDER RUNS UP ONLY: E -> W -> D -> N, one tier at a time, never down.
N spills nowhere. Emmett: "25-54 is fine, not everyone gets what they want." A request
whose whole ladder holds no legal candidate becomes a SHORT TOKEN for phase 4. The
spill COUNT is one per request ultimately filled above its ORIGINAL bucket, regardless
of how many tiers it crossed.

★ THE SPILL IS NOT AN ACCIDENT, IT IS ARITHMETIC. The country's ruled mixes ask for 923
easy home games and only 759 easy road games exist to fill them, so at least 164
requests MUST move up a rung. On the stock world exactly 164 do, which means legality
never forced a spill beyond the structural one.

═══════════════════════════════════════════════════════════════════════════════════
CANDIDATE LEGALITY — the same five tests in every phase
═══════════════════════════════════════════════════════════════════════════════════

    1. different school
    2. different conference
    3. the unordered pair has not already been used
    4. the candidate holds the capacity the phase needs (road, or neutral tokens)
    5. the candidate has an S101 request (the Independents fail this)

★ RECORDED FOR ARC SESSION 4, NOT ACTED ON NOW: when the Independents join, test 2 must
EXEMPT their shared container. They are fourteen strangers in one bucket, not
league-mates, and R13 says they play each other constantly.

═══════════════════════════════════════════════════════════════════════════════════
THE ALGORITHM — four phases, total, deterministic, no randomness anywhere
═══════════════════════════════════════════════════════════════════════════════════

Every choice is an ordered pick whose key ENDS IN SCHOOL ID, so no tie can be broken by
dictionary order, insertion order, or anything else the two languages might disagree on.

0. ALLOCATE. Split each school's HOME count into bucket requests by largest remainder:
   exact_b = home * share_b / sum(shares); floor them all; hand out the remaining units
   by descending fractional remainder, TIES TO THE LOWER BUCKET. Selling sends every
   home game to ANY.
       Marquee home 9 -> 6 E / 2 W / 1 D
       Marquee home 6 -> 4 E / 1 W / 1 D    (E and D tie at .75; E is lower and takes it)

1. TOP-DOWN HOME FILL. R4 as a pick order: the top of the country states its schedule
   and everyone else adapts around it.
       traversal: class Marquee -> Solid -> Working -> Selling
                  within a class, prestige DESC then id ASC
                  within a school, buckets E -> W -> D -> N, ANY last
                  a bucket's request copies run one after another
       for each request: walk the spill ladder from its original bucket; at each tier
       the candidates are the legal schools with ROAD remaining whose prestige sits in
       that tier (every band, for ANY); take the minimum by
                  (DistanceKey ASC, prestige ASC, id ASC)
       the host spends one home request (by iteration), the candidate spends one ROAD.
       Ladder exhausted with nothing legal anywhere -> SHORT TOKEN.

2. NEUTRAL PAIRING. One token per NEUTRAL request; a neutral pair is two big names
   agreeing to meet somewhere that is nobody's gym (R9). Schools in prestige DESC, id
   ASC; each school spends its tokens one at a time; partner = legal school with neutral
   tokens remaining, minimum by (|prestige gap| ASC, DistanceKey ASC, id ASC). Both
   sides spend one.
       ★ A TOKEN THAT CANNOT FIND A PARTNER — the country's odd national count, or a
       legality dead end — CONVERTS to one ANY home request for its owner and runs
       step 1's pick immediately (counted: ConvertedNeutral). If that pick also fails,
       it becomes a short token. Nothing is discarded.

3. BOTTOM HOSTS BOTTOM (C-37). Whatever road games are left over belong to schools that
   all wanted to travel, so they pair off and one of them eats the home game.
       loop: among schools with ROAD remaining take the (prestige ASC, id ASC)-first
       school a; partner = legal school with ROAD remaining, minimum by
       (DistanceKey ASC, prestige ASC, id ASC); A HOSTS.
       ★ THE HOST RULE, NAMED: the LOWER PRESTIGE school hosts; EQUAL prestige, the
       LOWER ID hosts. (a satisfies both by how the pool is ordered, but the rule is the
       rule and Phase 93 C11 asserts it in these words rather than asserting the
       construction.)
       BOTH sides spend one ROAD. ★ A FILLER GAME CHANGES A SCHOOL'S SITE MIX, NEVER ITS
       GAME COUNT — a filler host is exactly on target, never over it.
       a has no legal partner anywhere -> every one of a's remaining ROAD games becomes a
       short token and a leaves the pool.

4. TERMINAL REPAIR. Short tokens are the country's odd parity plus whatever legality and
   greedy dead ends produced. Each is closed with a bounded +1 game.
       short-token owners in prestige DESC, id ASC; for each token, the partner search
       prefers class Selling, then Working, then Solid, then Marquee; inside a class the
       candidates are the legal schools NOT ALREADY USED as a terminal partner, minimum
       by (DistanceKey ASC, prestige ASC, id ASC).
       ★ THE PARTNER HOSTS and exceeds its own target by EXACTLY ONE GAME. A partner is
       used AT MOST ONCE, so the repair can never pile onto one school.
       No legal partner anywhere -> the token is REPORTED UNREPAIRED, never thrown. The
       short school's token is consumed either way.
       No minimum-terminal claim is made: this closes ALL residual short tokens, not the
       fewest possible.

★ COMPLETES-OR-REPORTS. Combinatorial infeasibility NEVER throws. The result carries the
completed pairing list, every unrepaired token with its owner, and the full ledger — a
structured shortfall, which is what Phase 93 C13 reads. Malformed input (a school naming
a place that does not exist) may still throw; that is bad data, not a hard schedule.

═══════════════════════════════════════════════════════════════════════════════════
THE LEDGER, AND THE TWO CONSERVATION IDENTITIES
═══════════════════════════════════════════════════════════════════════════════════

Per targeted school:
    RequestedHome / RequestedNeutral / RequestedRoad   from S101, IMMUTABLE — the matcher
        keeps its own remaining-counters and never writes back to the input report
    MatchedHome              hosted games this school hosted
    MatchedNeutral           neutral pairs it is in, either side
    MatchedRoadAsVisitor     games it travelled to and did not host — hosted, filler and
                             terminal visits alike
    FillerHosted             site-mix conversions; on target, never over
    TerminalExtra            0 or 1
    ShortUnrepaired          tokens that found nobody
    ConvertedNeutralToHome   ANNOTATION
    SpilledRequests          ANNOTATION

★ AN ANNOTATION RECORDS PROVENANCE AND NEVER REPLACES A ROLE COUNT. A neutral token that
converts and then fills as a home game increments BOTH MatchedHome and
ConvertedNeutralToHome. A spilled request still counts in MatchedHome. Adding an
annotation into the total would double-count it, which is why the formula names its
terms explicitly:

    PairedTotal = MatchedHome + MatchedNeutral + MatchedRoadAsVisitor
                + FillerHosted + TerminalExtra
                (annotations NOT added again; ShortUnrepaired never counted)

Nationally, BOTH of these hold and Phase 93 C10 asserts both:

    (i)  request disposition
         TotalTokens = 2*Hosted + 2*Neutral + 2*Filler + TerminalRepaired + Unrepaired
         where TotalTokens = home + neutral + road requests from S101

    (ii) actual participation
         2 * Pairs = TotalTokens - Unrepaired + TerminalExtra
         the terminal partners' extra games are real participations that no S101 request
         ever demanded, which is exactly why the two identities are not the same identity

On the stock world: 3,913 tokens = 2*1667 + 2*111 + 2*177 + 3 + 0, and
2*1958 = 3916 = 3913 - 0 + 3.

═══════════════════════════════════════════════════════════════════════════════════
★ A MEASURED FINDING THIS ORACLE PRINTS AND DOES NOT TUNE AWAY
═══════════════════════════════════════════════════════════════════════════════════

C-40 says the geographic tilt strengthens down the classes: a power school flies, a
small school buses. Measured on the trips schools actually take, the stock world does
the opposite at the bottom — Marquee 175 median, Solid 145, Working 121, and SELLING
249 with a p90 of 873. Two mechanisms: in phase 1 the HOST picks its nearest opponent
and the visitor has no say, and phase 3 then pairs the hardest-to-place leftovers off
at a median of 356 miles. Surfaced to Emmett at the S102 check-in and RULED TO STAND as
measured; the season page prints trips by class so it stays visible rather than hiding
inside a healthy-looking national median of 148.

═══════════════════════════════════════════════════════════════════════════════════
S101, REPRODUCED HERE ONLY TO PRODUCE THE GOLDEN'S INPUT
═══════════════════════════════════════════════════════════════════════════════════

Class = max(conference tier floor, prestige band), read from currentPrestige. Home comes
from the class band positioned by prestige rank; neutral is the class allowance capped
by what is left; road is the remainder. The seated set is BINARY SET MEMBERSHIP and is
authored below as a constant, read from the committed harness's own run
(`dotnet run -- season worlds/stock-d1.world.json 20260720`) — this file does not
reimplement the tournament seating draw, and the golden's embedded report is asserted
against the live one by C14a, so a drift cannot pass silently.
"""

import hashlib
import itertools
import json
import math
import os
import sys

# ─── S92's ruler, reproduced exactly (verified against the golden in main) ────────

EARTH_MEAN_RADIUS_MILES = 3958.7613
_DEG_TO_RAD = math.pi / 180.0


def distance_miles(lat1_deg, long1_deg, lat2_deg, long2_deg):
    """Great-circle miles, spherical earth. The S92 form, term for term."""
    lat1 = lat1_deg * _DEG_TO_RAD
    lat2 = lat2_deg * _DEG_TO_RAD
    half_dlat = (lat2 - lat1) * 0.5
    half_dlong = (long2_deg - long1_deg) * _DEG_TO_RAD * 0.5
    sin_lat = math.sin(half_dlat)
    sin_long = math.sin(half_dlong)
    h = sin_lat * sin_lat + math.cos(lat1) * math.cos(lat2) * sin_long * sin_long
    if h < 0.0:
        h = 0.0
    if h > 1.0:
        h = 1.0
    return 2.0 * EARTH_MEAN_RADIUS_MILES * math.asin(math.sqrt(h))


def distance_key(miles):
    """★ floor(miles + 0.5). NOT round() — Python's round is ties-to-even, and so is
    C#'s Math.Round; both are the wrong function and using either is the trap."""
    return int(math.floor(miles + 0.5))


# ─── S101's constants, and the seated set for the stock golden ───────────────────

SEASON_GAMES_SEATED = 31
SEASON_GAMES_UNSEATED = 29
EVENT_GAMES = 3
HOME_BANDS = [(0, 2), (3, 5), (5, 7), (7, 10)]      # Selling, Working, Solid, Marquee
SHOWCASE_ALLOWANCE = [0, 0, 1, 2]
CLASS_NAMES = ["Selling", "Working", "Solid", "Marquee"]
TIER_FLOOR = {"power": 3, "highMid": 2, "lowMid": 1, "low": 0}

# ★ S104 — TOURNAMENT seats only, read from the committed harness's own season run at
# seed 20260720. A SHOWCASE seat is deliberately NOT here: it does not buy the 31-game
# season, it spends one of the games the school already had (R26). 108 schools.
STOCK_SEATED_20260720 = [
    2, 9, 12, 17, 18, 20, 21, 25, 27, 29, 38, 39, 41, 43, 47, 50, 51, 54, 55, 56, 60,
    63, 64, 68, 70, 71, 82, 87, 89, 90, 94, 95, 97, 99, 103, 105, 107, 109, 114, 115,
    116, 117, 118, 119, 129, 130, 135, 139, 145, 150, 153, 157, 158, 161, 164, 168, 170,
    176, 177, 180, 182, 189, 192, 193, 195, 196, 197, 201, 202, 203, 205, 206, 209, 215,
    218, 221, 223, 227, 234, 235, 238, 239, 243, 246, 249, 254, 258, 261, 268, 269, 271,
    272, 279, 280, 287, 294, 297, 299, 300, 302, 303, 304, 305, 309, 313, 315, 319, 320,
]

# ★ S104 — schools owing ONE showcase game this season. Charged neutral -> road -> home
# AFTER the ordinary split and after any contract charge, per the ruled priority.
# 48 schools, 24 of them also in a tournament.
STOCK_SHOWCASE_20260720 = [
    10, 18, 21, 29, 33, 34, 38, 46, 50, 58, 68, 84, 93, 96, 99, 114, 115, 119, 122, 129,
    130, 142, 149, 158, 164, 174, 180, 187, 196, 197, 206, 207, 215, 222, 223, 226, 236,
    257, 261, 268, 286, 296, 301, 302, 310, 313, 314, 318,
]

# ─── S102's constants — the R8 seam. Every tunable number of this session is here ──

BUCKET_NAMES = ["Easy", "Working", "Decent", "Name"]
BUCKET_ANY = "Any"
BUCKET_NONE = ""            # the kinds that have no bucket: Neutral, Filler, Terminal

#   class -> (Easy, Working, Decent) shares. Selling is ANY and has no mix.
BUCKET_MIX = {
    "Marquee": (5, 2, 1),
    "Solid":   (3, 2, 1),
    "Working": (2, 2, 0),
}
TERMINAL_CLASS_PREFERENCE = ["Selling", "Working", "Solid", "Marquee"]
CLASS_TRAVERSAL = ["Marquee", "Solid", "Working", "Selling"]


def prestige_band(prestige):
    """0 Easy (<25), 1 Working (25-54), 2 Decent (55-79), 3 Name (80+)."""
    return 3 if prestige >= 80 else 2 if prestige >= 55 else 1 if prestige >= 25 else 0


def prestige_class(prestige):
    """S101's class band — the SAME thresholds, deliberately, because a bucket and a
    class are the same read of prestige; only the tier floor tells them apart."""
    return prestige_band(prestige)


# ═══ S101 ═════════════════════════════════════════════════════════════════════════

def home_spread(lo, hi, n):
    """The rank spread, in exact integer arithmetic: (2a + b) // (2b) is round-half-up
    of a/b for non-negative integers, so no float midpoint can tip a school's home
    count differently on another machine."""
    if n <= 0:
        return []
    if n == 1:
        return [lo]
    out = []
    for i in range(n):
        a = i * (hi - lo)
        b = n - 1
        out.append(lo + (2 * a + b) // (2 * b))
    return out


def build_requests(world, seated_ids, showcase_ids=()):
    """S101's report, with S104's showcase charge applied. School-id order."""
    seated = set(seated_ids)
    showcase = set(showcase_ids)
    conf_by_id = {c["id"]: c for c in world["conferences"]}
    by_class = {0: [], 1: [], 2: [], 3: []}
    out = {}

    for s in world["schools"]:
        conf = conf_by_id[s["conferenceId"]]
        if conf["games"] == 0:
            out[s["id"]] = {
                "schoolId": s["id"], "schoolName": s["name"], "className": "Independent",
                "isIndependent": True, "home": 0, "neutral": 0, "road": 0,
            }
            continue
        cls = max(TIER_FLOOR[conf["tierId"]], prestige_class(s["currentPrestige"]))
        by_class[cls].append(s)

    for cls in sorted(by_class):
        members = sorted(by_class[cls], key=lambda s: (s["currentPrestige"], s["id"]))
        lo, hi = HOME_BANDS[cls]
        spread = home_spread(lo, hi, len(members))
        for i, s in enumerate(members):
            conf = conf_by_id[s["conferenceId"]]
            is_seated = s["id"] in seated
            season_games = SEASON_GAMES_SEATED if is_seated else SEASON_GAMES_UNSEATED
            event_games = EVENT_GAMES if is_seated else 0
            open_games = season_games - conf["games"] - event_games
            if open_games < 0:
                home = neutral = road = 0
            else:
                home = min(spread[i], open_games)
                neutral = min(SHOWCASE_ALLOWANCE[cls], open_games - home)
                road = open_games - home - neutral
                # ★ S104 / R26 — a showcase costs one of your games, charged
                #   neutral -> road -> home. Season totals never change.
                if s["id"] in showcase:
                    if neutral > 0:
                        neutral -= 1
                    elif road > 0:
                        road -= 1
                    elif home > 0:
                        home -= 1
                    else:
                        raise AssertionError(
                            "showcase charge with nothing to spend: school %d" % s["id"])
            out[s["id"]] = {
                "schoolId": s["id"], "schoolName": s["name"], "className": CLASS_NAMES[cls],
                "isIndependent": False, "home": home, "neutral": neutral, "road": road,
            }

    return [out[s["id"]] for s in sorted(world["schools"], key=lambda x: x["id"])]


# ═══ S102 ═════════════════════════════════════════════════════════════════════════

def allocate_buckets(home, shares):
    """Largest remainder, TIES TO THE LOWER BUCKET (lower index wins a tie)."""
    total = sum(shares)
    exact = [home * s / total for s in shares]
    floors = [int(e) for e in exact]
    remaining = home - sum(floors)
    order = sorted(range(len(shares)), key=lambda i: (-(exact[i] - floors[i]), i))
    for i in order[:remaining]:
        floors[i] += 1
    return floors


class Matching:
    """The result. A plain carrier — no behaviour, so the C# record can mirror it."""

    def __init__(self):
        self.pairs = []            # ordered, and the order is part of the golden
        self.ledger = {}
        self.unrepaired = []       # (schoolId,) per token
        self.converted_neutrals = 0
        self.spills = 0


def match(world, report):
    """★ PURE AND TOTAL. (world, report) in, matching out. No randomness, no clock, no
    config, and the report is never written to."""
    school_by_id = {s["id"]: s for s in world["schools"]}
    place_by_id = {p["placeId"]: p for p in world["places"]}
    req_by_id = {r["schoolId"]: r for r in report}

    # The pool: every school with an S101 request. The Independents are not here, and
    # therefore cannot be picked, cannot host, and cannot be a terminal partner (A5).
    ids = sorted(r["schoolId"] for r in report if not r["isIndependent"])
    if not ids:
        m = Matching()
        return m

    prestige = {i: school_by_id[i]["currentPrestige"] for i in ids}
    conference = {i: school_by_id[i]["conferenceId"] for i in ids}
    class_of = {i: req_by_id[i]["className"] for i in ids}

    # DistanceKey for every ordered pair, computed once.
    dk = {}
    for a, b in itertools.combinations(ids, 2):
        pa, pb = place_by_id[school_by_id[a]["placeId"]], place_by_id[school_by_id[b]["placeId"]]
        k = distance_key(distance_miles(pa["lat"], pa["long"], pb["lat"], pb["long"]))
        dk[(a, b)] = k
        dk[(b, a)] = k

    road = {i: req_by_id[i]["road"] for i in ids}
    neutral = {i: req_by_id[i]["neutral"] for i in ids}
    used_pairs = set()

    m = Matching()
    for i in ids:
        r = req_by_id[i]
        m.ledger[i] = {
            "schoolId": i, "schoolName": r["schoolName"], "className": r["className"],
            "requestedHome": r["home"], "requestedNeutral": r["neutral"],
            "requestedRoad": r["road"],
            "matchedHome": 0, "matchedNeutral": 0, "matchedRoadAsVisitor": 0,
            "fillerHosted": 0, "terminalExtra": 0, "shortUnrepaired": 0,
            "convertedNeutralToHome": 0, "spilledRequests": 0,
        }

    def legal(a, b, need_road=False, need_neutral=False):
        if a == b:
            return False
        if conference[a] == conference[b]:
            return False
        if (min(a, b), max(a, b)) in used_pairs:
            return False
        if need_road and road[b] <= 0:
            return False
        if need_neutral and neutral[b] <= 0:
            return False
        return True

    def pick_road_candidate(host, band):
        """band None means ANY. Minimum by (DistanceKey, prestige, id)."""
        best = None
        best_key = None
        for c in ids:
            if road[c] <= 0 or not legal(host, c, need_road=True):
                continue
            if band is not None and prestige_band(prestige[c]) != band:
                continue
            key = (dk[(host, c)], prestige[c], c)
            if best_key is None or key < best_key:
                best_key, best = key, c
        return best

    def take(host, visitor, kind, origin_bucket, filled_bucket,
             was_spill, was_converted_neutral):
        used_pairs.add((min(host, visitor), max(host, visitor)))
        m.pairs.append({
            "kind": kind,
            "hostSchoolId": host if kind != "Neutral" else min(host, visitor),
            "visitorSchoolId": visitor if kind != "Neutral" else max(host, visitor),
            "distanceKey": dk[(host, visitor)],
            "originBucket": origin_bucket,
            "filledBucket": filled_bucket,
            "wasSpill": was_spill,
            "wasConvertedNeutral": was_converted_neutral,
        })

    def fill_home_request(host, origin_band):
        """One home request. origin_band None = ANY (never spills). Returns True when
        filled; False makes it a short token."""
        ladder = [None] if origin_band is None else list(range(origin_band, 4))
        for band in ladder:
            c = pick_road_candidate(host, band)
            if c is None:
                continue
            road[c] -= 1
            spilled = origin_band is not None and band != origin_band
            take(host, c, "Hosted",
                 BUCKET_ANY if origin_band is None else BUCKET_NAMES[origin_band],
                 BUCKET_ANY if band is None else BUCKET_NAMES[band],
                 spilled, False)
            m.ledger[host]["matchedHome"] += 1
            m.ledger[c]["matchedRoadAsVisitor"] += 1
            if spilled:
                m.spills += 1
                m.ledger[host]["spilledRequests"] += 1
            return True
        return False

    short_tokens = []

    # ── 1. TOP-DOWN HOME FILL ────────────────────────────────────────────────────
    class_rank = {name: i for i, name in enumerate(CLASS_TRAVERSAL)}
    traversal = sorted(ids, key=lambda i: (class_rank[class_of[i]], -prestige[i], i))
    for host in traversal:
        r = req_by_id[host]
        if class_of[host] == "Selling":
            requests = [None] * r["home"]
        else:
            counts = allocate_buckets(r["home"], list(BUCKET_MIX[class_of[host]]))
            requests = []
            for band in range(3):
                requests.extend([band] * counts[band])
        for origin_band in requests:
            if not fill_home_request(host, origin_band):
                short_tokens.append(host)

    # ── 2. NEUTRAL PAIRING ───────────────────────────────────────────────────────
    neutral_order = sorted([i for i in ids if neutral[i] > 0],
                           key=lambda i: (-prestige[i], i))
    for a in neutral_order:
        while neutral[a] > 0:
            best = None
            best_key = None
            for c in ids:
                if neutral[c] <= 0 or not legal(a, c, need_neutral=True):
                    continue
                key = (abs(prestige[c] - prestige[a]), dk[(a, c)], c)
                if best_key is None or key < best_key:
                    best_key, best = key, c
            if best is not None:
                neutral[a] -= 1
                neutral[best] -= 1
                take(a, best, "Neutral", BUCKET_NONE, BUCKET_NONE, False, False)
                m.ledger[a]["matchedNeutral"] += 1
                m.ledger[best]["matchedNeutral"] += 1
                continue
            # ★ No partner: the token converts to one ANY home request and runs
            #   step 1's pick immediately. Nothing is discarded.
            neutral[a] -= 1
            m.converted_neutrals += 1
            m.ledger[a]["convertedNeutralToHome"] += 1
            before = len(m.pairs)
            if fill_home_request(a, None):
                m.pairs[-1]["wasConvertedNeutral"] = True
            else:
                assert len(m.pairs) == before
                short_tokens.append(a)

    # ── 3. BOTTOM HOSTS BOTTOM ───────────────────────────────────────────────────
    while True:
        pool = sorted((i for i in ids if road[i] > 0), key=lambda i: (prestige[i], i))
        if not pool:
            break
        a = pool[0]
        best = None
        best_key = None
        for c in ids:
            if road[c] <= 0 or not legal(a, c, need_road=True):
                continue
            key = (dk[(a, c)], prestige[c], c)
            if best_key is None or key < best_key:
                best_key, best = key, c
        if best is None:
            # Every remaining road game of a's becomes a short token; a leaves.
            for _ in range(road[a]):
                short_tokens.append(a)
            road[a] = 0
            continue
        # ★ THE HOST RULE: lower prestige hosts; equal prestige, lower id hosts.
        #   a satisfies it by the pool's order; the rule is stated, not inferred.
        host, visitor = (a, best) if (prestige[a], a) < (prestige[best], best) else (best, a)
        road[a] -= 1
        road[best] -= 1
        take(host, visitor, "Filler", BUCKET_NONE, BUCKET_NONE, False, False)
        m.ledger[host]["fillerHosted"] += 1
        m.ledger[visitor]["matchedRoadAsVisitor"] += 1

    # ── 4. TERMINAL REPAIR ───────────────────────────────────────────────────────
    terminal_used = set()
    for owner in sorted(short_tokens, key=lambda i: (-prestige[i], i)):
        partner = None
        for cls in TERMINAL_CLASS_PREFERENCE:
            best = None
            best_key = None
            for c in ids:
                if c in terminal_used or class_of[c] != cls:
                    continue
                if not legal(owner, c):
                    continue
                key = (dk[(owner, c)], prestige[c], c)
                if best_key is None or key < best_key:
                    best_key, best = key, c
            if best is not None:
                partner = best
                break
        if partner is None:
            m.unrepaired.append(owner)
            m.ledger[owner]["shortUnrepaired"] += 1
            continue
        terminal_used.add(partner)
        take(partner, owner, "Terminal", BUCKET_NONE, BUCKET_NONE, False, False)
        m.ledger[partner]["terminalExtra"] += 1
        m.ledger[owner]["matchedRoadAsVisitor"] += 1

    return m


# ═══ The proofs this file runs on itself before it emits anything ═════════════════

def prove(world, report, m, label):
    ids = sorted(r["schoolId"] for r in report if not r["isIndependent"])
    targeted = [r for r in report if not r["isIndependent"]]
    conf_of = {s["id"]: s["conferenceId"] for s in world["schools"]}

    tokens = sum(r["home"] + r["neutral"] + r["road"] for r in targeted)
    hosted = sum(1 for p in m.pairs if p["kind"] == "Hosted")
    neutral = sum(1 for p in m.pairs if p["kind"] == "Neutral")
    filler = sum(1 for p in m.pairs if p["kind"] == "Filler")
    terminal = sum(1 for p in m.pairs if p["kind"] == "Terminal")
    unrepaired = len(m.unrepaired)

    # structure and legality
    seen = set()
    for p in m.pairs:
        a, b = p["hostSchoolId"], p["visitorSchoolId"]
        assert a != b, f"{label}: self pair"
        assert a in ids and b in ids, f"{label}: a pair names a school with no request"
        assert conf_of[a] != conf_of[b], f"{label}: same-conference pair"
        key = (min(a, b), max(a, b))
        assert key not in seen, f"{label}: duplicate unordered pair {key}"
        seen.add(key)
        assert p["kind"] in ("Hosted", "Neutral", "Filler", "Terminal")

    # the host rule, in the words C11 uses
    for p in m.pairs:
        if p["kind"] != "Filler":
            continue
        h, v = p["hostSchoolId"], p["visitorSchoolId"]
        ph = world_prestige(world, h)
        pv = world_prestige(world, v)
        assert (ph, h) < (pv, v), f"{label}: a filler game is hosted by the higher school"

    # identity (i) — request disposition
    lhs = 2 * hosted + 2 * neutral + 2 * filler + terminal + unrepaired
    assert lhs == tokens, f"{label}: disposition {lhs} != {tokens} tokens"

    # identity (ii) — actual participation
    extra = sum(l["terminalExtra"] for l in m.ledger.values())
    assert 2 * len(m.pairs) == tokens - unrepaired + extra, f"{label}: participation"

    # per-school totals, and every pair on exactly two ledgers
    roles = {i: 0 for i in ids}
    for p in m.pairs:
        roles[p["hostSchoolId"]] += 1
        roles[p["visitorSchoolId"]] += 1
    for i in ids:
        l = m.ledger[i]
        total = (l["matchedHome"] + l["matchedNeutral"] + l["matchedRoadAsVisitor"]
                 + l["fillerHosted"] + l["terminalExtra"])
        assert total == roles[i], f"{label}: {l['schoolName']} ledger {total} != {roles[i]}"
        assert total == (l["requestedHome"] + l["requestedNeutral"] + l["requestedRoad"]
                         + l["terminalExtra"] - l["shortUnrepaired"]), \
            f"{label}: {l['schoolName']} does not reconcile to its request"
        assert l["terminalExtra"] in (0, 1), f"{label}: a terminal partner used twice"
        assert l["fillerHosted"] <= l["requestedRoad"], f"{label}: filler exceeded a target"

    # spills never go down, and the count is per request filled above its origin
    counted = 0
    for p in m.pairs:
        if p["kind"] != "Hosted" or p["originBucket"] == BUCKET_ANY:
            continue
        o = BUCKET_NAMES.index(p["originBucket"])
        f = BUCKET_NAMES.index(p["filledBucket"])
        assert f >= o, f"{label}: a request filled BELOW its bucket"
        assert p["wasSpill"] == (f > o)
        if p["wasSpill"]:
            counted += 1
    assert counted == m.spills, f"{label}: spill count {m.spills} != {counted}"

    print(f"  {label}: proved — {len(m.pairs)} pairs, both identities hold, "
          f"{unrepaired} unrepaired")


_PRESTIGE_CACHE = {}


def world_prestige(world, sid):
    key = id(world)
    if key not in _PRESTIGE_CACHE:
        _PRESTIGE_CACHE[key] = {s["id"]: s["currentPrestige"] for s in world["schools"]}
    return _PRESTIGE_CACHE[key][sid]


def ledger_checksum(m):
    """A stable digest of the whole ledger, so a regenerated golden cannot quietly
    change its numbers while keeping its pair list."""
    h = hashlib.sha256()
    for i in sorted(m.ledger):
        l = m.ledger[i]
        h.update(("|".join(str(l[k]) for k in (
            "schoolId", "requestedHome", "requestedNeutral", "requestedRoad",
            "matchedHome", "matchedNeutral", "matchedRoadAsVisitor", "fillerHosted",
            "terminalExtra", "shortUnrepaired", "convertedNeutralToHome",
            "spilledRequests")) + "\n").encode("utf-8"))
    return h.hexdigest()


def report_fingerprint(report):
    h = hashlib.sha256()
    for r in report:
        h.update(f"{r['schoolId']}|{r['className']}|{int(r['isIndependent'])}|"
                 f"{r['home']}|{r['neutral']}|{r['road']}\n".encode("utf-8"))
    return h.hexdigest()


def median_lower(sorted_values):
    """The lower middle of an even sample; the middle of an odd one."""
    if not sorted_values:
        return None
    return sorted_values[(len(sorted_values) - 1) // 2]


def nearest_rank_p90(sorted_values):
    if not sorted_values:
        return None
    return sorted_values[math.ceil(0.9 * len(sorted_values)) - 1]


if __name__ == "__main__":
    root = sys.argv[1] if len(sys.argv) > 1 else "."

    # ── The ruler is the S92 ruler, asserted rather than assumed ─────────────────
    golden = json.load(open(f"{root}/tools/geo_distance_golden.json"))
    worst = 0.0
    for row in golden["rows"]:
        d = distance_miles(row["lat1"], row["long1"], row["lat2"], row["long2"])
        worst = max(worst, abs(d - row["expectedMiles"]))
        assert worst <= row["toleranceMiles"], "the haversine here is NOT S92's model"
    print(f"S92 ruler: {len(golden['rows'])} golden rows, worst error {worst:.3e} mi")

    world = json.load(open(f"{root}/worlds/stock-d1.world.json"))
    report = build_requests(world, STOCK_SEATED_20260720, STOCK_SHOWCASE_20260720)
    targeted = [r for r in report if not r["isIndependent"]]

    home = sum(r["home"] for r in targeted)
    neu = sum(r["neutral"] for r in targeted)
    rd = sum(r["road"] for r in targeted)
    print(f"S101 input: {len(targeted)} targeted schools, home {home}, neutral {neu}, "
          f"road {rd}, tokens {home + neu + rd}")

    # ── ★ The quantization margin, RE-MEASURED, never quoted ────────────────────
    place_by_id = {p["placeId"]: p for p in world["places"]}
    school_by_id = {s["id"]: s for s in world["schools"]}
    towns = sorted({school_by_id[r["schoolId"]]["placeId"] for r in targeted})
    worst_margin = None
    for a, b in itertools.combinations(towns, 2):
        pa, pb = place_by_id[a], place_by_id[b]
        d = distance_miles(pa["lat"], pa["long"], pb["lat"], pb["long"])
        margin = abs((d - math.floor(d)) - 0.5)
        if worst_margin is None or margin < worst_margin[0]:
            worst_margin = (margin, d, pa["name"], pb["name"])
    npairs = len(towns) * (len(towns) - 1) // 2
    print(f"DistanceKey margin: {npairs} town pairs, closest to a half-mile boundary is "
          f"{worst_margin[0]:.7f} mi ({worst_margin[2]} <-> {worst_margin[3]}, "
          f"{worst_margin[1]:.6f} mi)")

    m = match(world, report)
    prove(world, report, m, "stock-d1")

    # ── determinism, and input immutability ─────────────────────────────────────
    before = json.dumps(report, sort_keys=True)
    m2 = match(world, report)
    assert json.dumps(report, sort_keys=True) == before, "the matcher MUTATED its input"
    assert m2.pairs == m.pairs and m2.ledger == m.ledger, "not deterministic"
    print("  determinism: a second run reproduces the pairing exactly, input untouched")

    hosted = sum(1 for p in m.pairs if p["kind"] == "Hosted")
    neutral = sum(1 for p in m.pairs if p["kind"] == "Neutral")
    filler = sum(1 for p in m.pairs if p["kind"] == "Filler")
    terminal = sum(1 for p in m.pairs if p["kind"] == "Terminal")
    print(f"  pairs {len(m.pairs)}: hosted {hosted}, neutral {neutral}, filler {filler}, "
          f"terminal {terminal}; spills {m.spills}, converted neutrals "
          f"{m.converted_neutrals}, unrepaired {len(m.unrepaired)}")

    trips = sorted(p["distanceKey"] for p in m.pairs if p["kind"] in ("Hosted", "Terminal"))
    fill = sorted(p["distanceKey"] for p in m.pairs if p["kind"] == "Filler")
    print(f"  visitor trip: median {median_lower(trips)} p90 {nearest_rank_p90(trips)} "
          f"(n={len(trips)}); filler median {median_lower(fill)} (n={len(fill)})")

    by_class = {}
    class_of = {r["schoolId"]: r["className"] for r in targeted}
    for p in m.pairs:
        if p["kind"] == "Neutral":
            continue
        by_class.setdefault(class_of[p["visitorSchoolId"]], []).append(p["distanceKey"])
    print("  ★ road trips TAKEN, by the travelling school's class (C-40's axis):")
    for cls in CLASS_TRAVERSAL:
        v = sorted(by_class.get(cls, []))
        print(f"      {cls:8s} median {median_lower(v)} p90 {nearest_rank_p90(v)} (n={len(v)})")

    over = [(m.ledger[i]["schoolName"], m.ledger[i]["terminalExtra"])
            for i in sorted(m.ledger) if m.ledger[i]["terminalExtra"] > 0]
    print(f"  over target by exactly one: {over}")

    with open(f"{root}/worlds/stock-d1.world.json", "rb") as f:
        world_sha = hashlib.sha256(f.read()).hexdigest()
    with open(os.path.abspath(__file__), "rb") as f:
        oracle_sha = hashlib.sha256(f.read()).hexdigest()

    payload = {
        "schema": "s102-matching-v1",
        "note": "S102 matching golden. The pairing list is ORDERED and the order is part "
                "of the artifact; the C# port reproduces it pair for pair and ledger field "
                "for ledger field (Phase 93 C14). Same-platform integer artifact — every "
                "value is an integer or a string, no float is asserted, so literal "
                "equality is the right bar (CONVENTIONS section 2).",
        "provenance": {
            "world": "stock-d1.world.json",
            "worldFileSha256": world_sha,
            "seed": 20260720,
            "oracleSha256": oracle_sha,
            "distanceKeyFormula": "floor(GeoDistance.DistanceMiles(a,b) + 0.5)",
            "seatedCount": len(STOCK_SEATED_20260720),
            "inputReportFingerprint": report_fingerprint(report),
            "inputTokens": home + neu + rd,
            "pairCount": len(m.pairs),
            "ledgerChecksum": ledger_checksum(m),
        },
        "inputReport": report,
        "pairs": m.pairs,
        "ledger": [m.ledger[i] for i in sorted(m.ledger)],
        "unrepaired": sorted(m.unrepaired),
    }
    out = f"{root}/tools/matching_golden.json"
    with open(out, "w", newline="\n") as f:
        json.dump(payload, f, indent=1)
        f.write("\n")
    print(f"\nGOLDEN EMITTED: {out}")
    print(f"  report fingerprint {payload['provenance']['inputReportFingerprint'][:16]}…")
    print(f"  ledger checksum    {payload['provenance']['ledgerChecksum'][:16]}…")
    print(f"  oracle sha256      {oracle_sha[:16]}…")
