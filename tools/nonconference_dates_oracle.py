"""S106 — NIGHTS: every non-conference game gets a date (O-92 session 6a).

★ THIS FILE IS THE SPEC. The C# in Program.Season.NonConferenceDates.cs is its port,
and golden parity proves the port game for game. Where the two disagree, this is right.

WHAT THIS LAYER DOES, AND WHAT IT REFUSES TO DO
-----------------------------------------------
It takes a pairing set that already exists — who plays whom, who hosts, which games are
neutral — and gives every one of those games a NIGHT. It changes WHEN, as hard as it
needs to, and never WHO, never WHERE, never a result.

★ A2 — IT CONSUMES PAIRINGS AND NEVER CREATES, DISSOLVES OR RE-HOSTS ONE. There are no
cancellations (Emmett's ruling). A pairing that cannot be seated is REPORTED with its
failure class, never quietly dropped.

★ A3 — CONFERENCE DATES AND EVENT WINDOWS ARE IMMOVABLE. Nothing here nudges a league
game or an event to make room. They are read-only input.

THE RULES (Emmett, 2026-08-08)
------------------------------
R-n1  WEEKLY LOAD — at most THREE games in a Mon-Sun week, counting ALL of a team's
      non-event games, conference and non-conference together. Event games do not count.

R-n2  SPACING — for any ordinary non-conference game on date D, on BOTH teams' calendars,
      D - previousGame >= 2 and nextGame - D >= 2 calendar days, whatever kind of game is
      adjacent. Saturday league game -> Monday buy game is LEGAL; -> Sunday is not.
      ★ League-vs-league adjacency is NOT governed here. The conference dater owns it and
      the Ivy Friday/Saturday pair stays legal — this file never inspects a pair of
      conference games against each other.

R-n3  EVENT TRAVEL BUFFERS, symmetric — the previous ordinary game is on or before
      FirstDay - 3 and the next is on or after LastDay + 3: two clear days each side.
      Inside its own window the event is exempt from R-n1 and R-n2 entirely.
      ★ Because every seated team plays every round and the final round is asserted onto
      the window's LastDay (Program.Season.Brackets.cs), window + buffer is EXACT
      arithmetic rather than an estimate.

R-n4  THE CURVE IS AUTHORED AS WEIGHTS AND ALLOCATED AS EXACT QUOTAS. One national
      calendar-shaped weight curve; the deterministic allocator below turns it into each
      school's per-week quotas at any slate size.
      ★ SUPERSEDED IN PART (Emmett, 2026-08-08): quotas are hard BY DEFAULT and BEND ON
      FAILURE. See R-n8. The front-fill cure survives — measured, the authored shape is
      the shape produced.

R-n5  THE CONFERENCE OPENER IS A SOFT BOUNDARY. A non-conference game after a team's
      league opener is legal wherever the calendar allows; the curve simply runs light.

R-n6  INDEPENDENTS ARE FLEXIBLE SUPPLY, with no curve of their own. The conference-member
      endpoint supplies the quota slot; the Independent supplies availability only.
      ★ INDEPENDENT-VS-INDEPENDENT (Emmett's ruling, 2026-08-08): not before January 1.
      Those schools meet during everyone else's conference season, never in November or
      December.

R-n7  CONTRACT LEGS AND BOTH LEGS OF A HOME-AND-HOME ARE ORDINARY GAMES for dating. No
      priority, no authored separation beyond R-n2, each counting against both weeks.

R-n8  THE SEATING BEND (Emmett's ruling, 2026-08-08). A pairing whose quota week holds no
      night both calendars share slides to the NEAREST week that does: 0, +1, -1, +2, -2,
      +3, -3, Christmas always skipped, and never further. The cause is real and was
      measured before the rule was made: a school playing two league games in a week has
      its whole week closed by R-n2 except one night, and two such schools paired into the
      same week can hold no night in common. Hard quotas would fail those games; the bend
      seats them and PRINTS what it cost.

★ TWO DIFFERENT BENDS, DELIBERATELY NOT SHARING A NAME.
    allocationBend — how far a school's quotas were pushed off the pure weights by its own
                     calendar's capacity.  0.5 * sum |capacityAware - pureWeights|
    seatingBend    — how many week-steps games had to slide to find a night under R-n8.
  Both are printed on the season page. NEITHER is ever suite-asserted (page-only
  calibration).

★ NO RNG ANYWHERE, and no randomized search. Every ordering below is total and every
  tie-break is explicit, because a spec that anneals is not a spec.
"""

import datetime as _dt
import hashlib
import json
from collections import defaultdict, Counter

import schedule_oracle as _S


# ═══ calendar helpers — the Christmas week is schedule_oracle's, never a second one ═══

def monday(d):
    """Mon-Sun weeks, borrowed from the S94 oracle so the two layers cannot disagree."""
    return _S._monday(d)


def christmas_week(start_year):
    """★ REUSED, NOT RESTATED — the Mon-Sun week containing December 25."""
    return monday(_dt.date(start_year, 12, 25))


def season_floor(start_year):
    """R4 — November 1 is the first legal day of play."""
    return _dt.date(start_year, 11, 1)


def event_window_date(month_day, start_year):
    """An event's authored MM-DD resolved onto the season spine: months from July on
    belong to the opening civil year, the rest to the following one."""
    m, d = int(month_day[:2]), int(month_day[3:])
    return _dt.date(start_year if m >= 7 else start_year + 1, m, d)


# ═══ the world, as this layer sees it ════════════════════════════════════════════

class DateInputs:
    """Everything the dating layer reads. All of it is produced upstream and none of it
    is recomputed here — A3's immovability is a property of this being INPUT."""

    def __init__(self, schools, conference_games, event_seats, pairings, start_year):
        self.start_year = start_year
        self.schools = sorted(schools)                       # school ids
        self.conference_games = list(conference_games)       # (schoolA, schoolB, date)
        self.event_seats = dict(event_seats)                 # school -> [(first, last)]
        self.pairings = list(pairings)                       # (host, visitor) in matcher order
        self.is_independent = {}                             # school -> bool

    @staticmethod
    def from_json(path):
        raw = json.load(open(path))
        year = raw["startYear"]
        games = [(g["h"], g["a"], _dt.date(*map(int, g["d"].split("-"))))
                 for g in raw["conferenceGames"]]
        seats = defaultdict(list)
        for e in raw["events"]:
            f = event_window_date(e["first"], year)
            l = event_window_date(e["last"], year)
            for s in e["seats"]:
                seats[s].append((f, l))
        for s in seats:
            seats[s].sort()
        inputs = DateInputs([s["id"] for s in raw["schools"]], games, seats,
                            [(p["h"], p["v"]) for p in raw["pairs"]], year)
        member_games = {c["id"]: c["games"] for c in raw["conferences"]}
        inputs.is_independent = {s["id"]: member_games[s["conf"]] == 0
                                 for s in raw["schools"]}
        return inputs


# ═══ the calendar each school actually has ═══════════════════════════════════════

SPACING_CLEAR_DAYS = 1      # R-n2: one clear day either side == a gap of >= 2
EVENT_CLEAR_DAYS = 2        # R-n3: two clear days either side == a gap of >= 3
WEEKLY_LOAD_CEILING = 3     # R-n1


def fixed_dates(inputs):
    """Each school's immovable league nights."""
    out = defaultdict(set)
    for a, b, d in inputs.conference_games:
        out[a].add(d)
        out[b].add(d)
    return out


def blocked_dates(inputs, fixed):
    """Dates on which a school may not play an ORDINARY non-conference game, from the
    immovable calendar alone: R-n2 around every league night, R-n3 around every event
    window, and the window itself.

    ★ This never compares two league nights to each other — R-n2's league-vs-league
    exemption is structural here rather than a special case."""
    out = {}
    for s in inputs.schools:
        bad = set()
        for d in fixed[s]:
            for k in range(-SPACING_CLEAR_DAYS, SPACING_CLEAR_DAYS + 1):
                bad.add(d + _dt.timedelta(days=k))
        for (first, last) in inputs.event_seats.get(s, ()):
            d = first - _dt.timedelta(days=EVENT_CLEAR_DAYS)
            while d <= last + _dt.timedelta(days=EVENT_CLEAR_DAYS):
                bad.add(d)
                d += _dt.timedelta(days=1)
        out[s] = bad
    return out


def season_weeks(inputs):
    """Every Mon-Sun week from the week containing the season floor through the last
    league night in the world. The horizon is READ, never authored."""
    horizon = max(d for _, _, d in inputs.conference_games)
    w = monday(season_floor(inputs.start_year))
    out = []
    while w <= horizon:
        out.append(w)
        w += _dt.timedelta(days=7)
    return out, horizon


def week_capacity(inputs, fixed, blocked, weeks, horizon):
    """The exact most ordinary games a school could play inside one Mon-Sun week on its
    own: the weekly ceiling less its league games, and no more than the longest
    non-adjacent run of nights actually open to it.

    ★ NEVER a raw count of free nights — three open nights in a row seat one game, not
    three."""
    floor = season_floor(inputs.start_year)
    out = {}
    for s in inputs.schools:
        per = {}
        for w in weeks:
            days = [w + _dt.timedelta(days=i) for i in range(7)]
            league = sum(1 for d in days if d in fixed[s])
            open_nights = [d for d in days
                           if floor <= d <= horizon and d not in blocked[s]]
            run, last = 0, None
            for d in open_nights:
                if last is None or (d - last).days >= 2:
                    run += 1
                    last = d
            per[w] = max(0, min(WEEKLY_LOAD_CEILING - league, run))
        out[s] = per
    return out


# ═══ R-n4 — the allocator, pinned exactly so parity cannot fail on a tie ══════════

def allocate(total, capacity, weights, weeks, xmas):
    """(1) drop zero-capacity weeks; (2) force the Christmas week to 0; (3) normalize the
    positive weights over what remains; (4) floor the proportional shares; (5) hand out
    the remainder by fractional part DESCENDING, then EARLIEST WEEK first; (6) a week at
    capacity spills its excess by the same rule over remaining capacity, repeating until
    placed; (7) report a shortfall if total capacity cannot hold the games owed."""
    live = [i for i, w in enumerate(weeks)
            if capacity[w] > 0 and w != xmas and weights[i] > 0]
    if not live:
        return {}, total
    weight_sum = sum(weights[i] for i in live)
    exact = {i: total * weights[i] / weight_sum for i in live}
    quota = {i: int(exact[i]) for i in live}
    remainder = total - sum(quota.values())
    for i in sorted(live, key=lambda i: (-(exact[i] - int(exact[i])), i))[:remainder]:
        quota[i] += 1
    for _ in range(len(live) + 2):
        over = [i for i in live if quota[i] > capacity[weeks[i]]]
        if not over:
            return quota, 0
        for i in over:
            excess = quota[i] - capacity[weeks[i]]
            quota[i] = capacity[weeks[i]]
            room = [j for j in live if quota[j] < capacity[weeks[j]]]
            if not room:
                return quota, excess
            rw = sum(weights[j] for j in room)
            share = {j: excess * weights[j] / rw for j in room}
            add = {j: int(share[j]) for j in room}
            left = excess - sum(add.values())
            for j in sorted(room, key=lambda j: (-(share[j] - int(share[j])), j))[:left]:
                add[j] += 1
            for j in room:
                quota[j] += add[j]
    return quota, sum(max(0, quota[i] - capacity[weeks[i]]) for i in live)


def allocation_bend(quota, pure, live):
    """★ HOW FAR THE CALENDAR PUSHED THE QUOTAS OFF THE PURE CURVE. Half the total
    absolute difference, so one unit moved from week A to week B counts once."""
    return 0.5 * sum(abs(quota.get(i, 0) - pure.get(i, 0)) for i in live)


# ═══ the search — deterministic, most-constrained-first, bounded repair ═══════════

def canonical_game_order(pairings, owed):
    """A total order with no ties left to chance: the busiest pairs first, then by the
    two school ids, then by position in the matcher's own list."""
    return sorted(range(len(pairings)),
                  key=lambda gi: (-(owed[pairings[gi][0]] + owed[pairings[gi][1]]),
                                  min(pairings[gi]), max(pairings[gi]), gi))


def assign_weeks(inputs, quotas, capacity, weeks, weights, xmas, owed):
    """Every pairing gets a WEEK. Members are held to their exact quota; an Independent
    is held to its availability only (R-n6), and an Independent-vs-Independent game may
    not be seated before January 1."""
    jan1 = _dt.date(inputs.start_year + 1, 1, 1)
    active = [i for i, w in enumerate(weeks) if weights[i] > 0 and w != xmas]
    january = [i for i, w in enumerate(weeks) if w >= jan1 and w != xmas]

    def legal_weeks(gi):
        a, b = inputs.pairings[gi]
        if inputs.is_independent[a] and inputs.is_independent[b]:
            return [i for i in january
                    if capacity[a][weeks[i]] > 0 and capacity[b][weeks[i]] > 0]
        return active

    def target(s, i):
        return (capacity[s][weeks[i]] if inputs.is_independent[s]
                else quotas[s].get(i, 0))

    load = defaultdict(int)
    assign = {}
    for gi in canonical_game_order(inputs.pairings, owed):
        a, b = inputs.pairings[gi]
        best = None
        for i in legal_weeks(gi):
            room = min(target(a, i) - load[(a, i)], target(b, i) - load[(b, i)])
            key = (room, weights[i], -i)
            if best is None or key > best[0]:
                best = (key, i)
        i = best[1]
        assign[gi] = i
        load[(a, i)] += 1
        load[(b, i)] += 1

    # ── bounded deterministic repair: walk the schools in id order and move a game out
    #    of every over-quota week into the first under-quota week that accepts it ──
    incident = defaultdict(list)
    for gi, (a, b) in enumerate(inputs.pairings):
        incident[a].append(gi)
        incident[b].append(gi)
    for _ in range(8):
        moved = False
        for s in inputs.schools:
            if inputs.is_independent[s]:
                continue
            for i in sorted(active):
                while load[(s, i)] > quotas[s].get(i, 0):
                    candidates = [gi for gi in incident[s] if assign[gi] == i]
                    placed_one = False
                    for gi in sorted(candidates):
                        a, b = inputs.pairings[gi]
                        for j in legal_weeks(gi):
                            if j == i:
                                continue
                            if (load[(a, j)] < target(a, j)
                                    and load[(b, j)] < target(b, j)):
                                load[(a, i)] -= 1
                                load[(b, i)] -= 1
                                load[(a, j)] += 1
                                load[(b, j)] += 1
                                assign[gi] = j
                                placed_one = moved = True
                                break
                        if placed_one:
                            break
                    if not placed_one:
                        break
        if not moved:
            break
    return assign, load


SLIDE_RADIUS = 3            # R-n8


def slide_order(home, weeks, xmas):
    """★ R-n8's order, exactly: the quota week first, then outward one week at a time,
    later before earlier, Christmas always skipped."""
    out = [home]
    for k in range(1, SLIDE_RADIUS + 1):
        for j in (home + k, home - k):
            if 0 <= j < len(weeks) and weeks[j] != xmas:
                out.append(j)
    return out


def seat_nights(inputs, assign, blocked, fixed, weeks, xmas, horizon, owed):
    """Every pairing gets a NIGHT. Games are seated in canonical order; each takes the
    first legal night in the first week its slide order allows."""
    floor = season_floor(inputs.start_year)
    seated = {}
    played = defaultdict(set)        # school -> ordinary nights already taken
    week_load = defaultdict(int)     # (school, weekMonday) -> ordinary games taken
    league_in_week = defaultdict(int)
    for s in inputs.schools:
        for d in fixed[s]:
            league_in_week[(s, monday(d))] += 1
    unseated = []
    bend = Counter()

    order = sorted(canonical_game_order(inputs.pairings, owed), key=lambda gi: assign[gi])
    for gi in order:
        a, b = inputs.pairings[gi]
        chosen = None
        for i in slide_order(assign[gi], weeks, xmas):
            w = weeks[i]
            for k in range(7):
                d = w + _dt.timedelta(days=k)
                if not (floor <= d <= horizon):
                    continue
                if d in blocked[a] or d in blocked[b]:
                    continue
                if any(abs((d - x).days) <= SPACING_CLEAR_DAYS for x in played[a]):
                    continue
                if any(abs((d - x).days) <= SPACING_CLEAR_DAYS for x in played[b]):
                    continue
                if (week_load[(a, w)] + league_in_week[(a, w)] >= WEEKLY_LOAD_CEILING
                        or week_load[(b, w)] + league_in_week[(b, w)] >= WEEKLY_LOAD_CEILING):
                    continue
                chosen = (i, d)
                break
            if chosen:
                break
        if not chosen:
            unseated.append(gi)
            continue
        i, d = chosen
        seated[gi] = d
        played[a].add(d)
        played[b].add(d)
        week_load[(a, weeks[i])] += 1
        week_load[(b, weeks[i])] += 1
        bend[abs(i - assign[gi])] += 1
    return seated, unseated, bend


# ═══ A2's failure classification — only one of the four is an implementation bug ══

def classify_failure(inputs, gi, assign, blocked, weeks, horizon, capacity, quotas):
    """A dead end is a STOP AND DIAGNOSE, never automatically a bug."""
    a, b = inputs.pairings[gi]
    floor = season_floor(inputs.start_year)
    for i in slide_order(assign[gi], weeks, christmas_week(inputs.start_year)):
        days = [weeks[i] + _dt.timedelta(days=k) for k in range(7)]
        if any(floor <= d <= horizon and d not in blocked[a] and d not in blocked[b]
               for d in days):
            return "search contention"
    for s in (a, b):
        if sum(capacity[s].values()) < sum(1 for p in inputs.pairings if s in p):
            return "endpoint physical capacity"
    if not any(quotas[a].get(i, 0) > 0 and quotas[b].get(i, 0) > 0
               for i in range(len(weeks))):
        return "quota incompatibility"
    return "empty pairwise date intersection"


# ═══ the whole layer, and its fingerprint ════════════════════════════════════════

SOURCE_RANK = {"conference": 0, "tournament": 1, "showcase": 2,
               "contract": 3, "exchange": 4, "matched": 5}


def date_non_conference(inputs, weights_by_monday):
    fixed = fixed_dates(inputs)
    blocked = blocked_dates(inputs, fixed)
    weeks, horizon = season_weeks(inputs)
    xmas = christmas_week(inputs.start_year)
    capacity = week_capacity(inputs, fixed, blocked, weeks, horizon)
    weights = [weights_by_monday.get(w, 0) for w in weeks]

    owed = Counter()
    for a, b in inputs.pairings:
        owed[a] += 1
        owed[b] += 1

    quotas, shortfalls, alloc_bend = {}, [], 0.0
    for s in inputs.schools:
        if owed[s] == 0:
            continue
        q, short = allocate(owed[s], capacity[s], weights, weeks, xmas)
        quotas[s] = defaultdict(int, q)
        if short:
            shortfalls.append((s, short))
        # ★ "PURE WEIGHTS" MEANS THE CURVE ALONE — capacity ignored entirely, so the
        #   comparison is against the shape Emmett authored rather than against a
        #   shape his calendars had already edited. Both sides run the SAME allocator
        #   with capacity unlimited; the difference is then wholly attributable to
        #   capacity, which is the only thing this number is meant to measure.
        unlimited = {w: 10 ** 9 for w in weeks}
        pure, _ = allocate(owed[s], unlimited, weights, weeks, xmas)
        alloc_bend += allocation_bend(q, pure, range(len(weeks)))

    assign, load = assign_weeks(inputs, quotas, capacity, weeks, weights, xmas, owed)
    seated, unseated, bend = seat_nights(inputs, assign, blocked, fixed,
                                         weeks, xmas, horizon, owed)
    failures = [(gi, classify_failure(inputs, gi, assign, blocked, weeks,
                                      horizon, capacity, quotas))
                for gi in unseated]
    return {
        "weeks": weeks, "xmas": xmas, "horizon": horizon, "capacity": capacity,
        "quotas": quotas, "assign": assign, "seated": seated, "failures": failures,
        "allocationBend": alloc_bend,
        "seatingBend": sum(k * v for k, v in bend.items()),
        "bendHistogram": dict(sorted(bend.items())),
        "allocationShortfalls": shortfalls,
    }


def fingerprint(inputs, result):
    """★ THE SORT KEY IS LOCKED AND EXCLUDES SITE DATA. S107 adds cities as enrichment
    and must not be able to reorder this: (date, canonical source RANK, ordered school
    ids, host designation, event identity with a fixed empty token)."""
    lines = []
    for gi, d in result["seated"].items():
        a, b = inputs.pairings[gi]
        lo, hi = min(a, b), max(a, b)
        lines.append("|".join([
            d.isoformat(), str(SOURCE_RANK["matched"]),
            str(lo), str(hi), str(a), "-",
        ]))
    lines.sort()
    return hashlib.sha256("\n".join(lines).encode("utf-8")).hexdigest()


# ═══ the proof — every rule asserted BY NAME on the dated result ═════════════════

def prove(inputs, result, tag):
    seated, weeks = result["seated"], result["weeks"]
    fixed = fixed_dates(inputs)
    xmas, horizon = result["xmas"], result["horizon"]
    floor = season_floor(inputs.start_year)
    jan1 = _dt.date(inputs.start_year + 1, 1, 1)

    played = defaultdict(set)
    for gi, d in seated.items():
        a, b = inputs.pairings[gi]
        assert d not in played[a], f"{tag}: {a} twice on {d}"
        assert d not in played[b], f"{tag}: {b} twice on {d}"
        played[a].add(d)
        played[b].add(d)

    for gi, d in seated.items():
        assert floor <= d <= horizon, f"{tag}: {d} outside the season (R4)"
        assert monday(d) != xmas, f"{tag}: game inside the Christmas week (R-n4)"

    # R-n1 — three non-event games in a Mon-Sun week, conference and non-conference
    for s in inputs.schools:
        per = Counter(monday(d) for d in played[s] | fixed[s])
        for w, n in per.items():
            assert n <= WEEKLY_LOAD_CEILING, \
                f"{tag}: {s} plays {n} in the week of {w} (R-n1)"

    # R-n2 — spacing around every ordinary game, whatever kind of game is adjacent;
    #        league-vs-league adjacency is NOT inspected (the conference dater owns it)
    for s in inputs.schools:
        allnights = sorted(played[s] | fixed[s])
        for x, y in zip(allnights, allnights[1:]):
            if (y - x).days >= 2:
                continue
            assert x in fixed[s] and y in fixed[s], \
                f"{tag}: {s} plays {x} then {y} and one is a buy game (R-n2)"

    # R-n3 — two clear days either side of every event window
    for s, windows in inputs.event_seats.items():
        for (first, last) in windows:
            for d in played[s]:
                assert not (first - _dt.timedelta(days=EVENT_CLEAR_DAYS)
                            <= d <= last + _dt.timedelta(days=EVENT_CLEAR_DAYS)), \
                    f"{tag}: {s} plays {d} against a window {first}..{last} (R-n3)"

    # R-n6 — no Independent meets another before January 1
    for gi, d in seated.items():
        a, b = inputs.pairings[gi]
        if inputs.is_independent[a] and inputs.is_independent[b]:
            assert d >= jan1, f"{tag}: Independents meet on {d}, before January (R-n6)"

    # R-n8 — nothing slid further than the radius allows
    for gi, d in seated.items():
        home = result["assign"][gi]
        landed = weeks.index(monday(d))
        assert abs(landed - home) <= SLIDE_RADIUS, \
            f"{tag}: game {gi} slid {abs(landed-home)} weeks (R-n8)"

    # A2 — the pairing set is conserved exactly: consumed, never created or dissolved
    assert len(seated) + len(result["failures"]) == len(inputs.pairings), \
        f"{tag}: pairing count moved (A2)"
    seen = Counter()
    for gi in seated:
        seen[tuple(sorted(inputs.pairings[gi]))] += 1
    want = Counter(tuple(sorted(p)) for p in inputs.pairings)
    for k, v in seen.items():
        assert v <= want[k], f"{tag}: pairing {k} multiplied (A2)"
    return True


if __name__ == "__main__":
    import sys, time
    inputs = DateInputs.from_json(sys.argv[1] if len(sys.argv) > 1
                                  else "/tmp/s106-in.json")
    Y = inputs.start_year
    CURVE = {
        _dt.date(Y, 11, 2): 10, _dt.date(Y, 11, 9): 13, _dt.date(Y, 11, 16): 13,
        _dt.date(Y, 11, 23): 11, _dt.date(Y, 11, 30): 11, _dt.date(Y, 12, 7): 8,
        _dt.date(Y, 12, 14): 5, _dt.date(Y, 12, 21): 0, _dt.date(Y, 12, 28): 6,
        _dt.date(Y + 1, 1, 4): 5, _dt.date(Y + 1, 1, 11): 4, _dt.date(Y + 1, 1, 18): 4,
        _dt.date(Y + 1, 1, 25): 3, _dt.date(Y + 1, 2, 1): 3,
    }
    t0 = time.time()
    res = date_non_conference(inputs, CURVE)
    prove(inputs, res, "stock")
    fp = fingerprint(inputs, res)
    again = date_non_conference(inputs, CURVE)
    assert fingerprint(inputs, again) == fp, "the oracle is not deterministic"
    print(f"S106 ORACLE — {len(res['seated'])}/{len(inputs.pairings)} games dated "
          f"in {time.time()-t0:.2f}s, every rule asserted, determinism confirmed")
    per_week = Counter(monday(d) for d in res["seated"].values())
    for w in res["weeks"]:
        if per_week.get(w) or w == res["xmas"]:
            note = "   <- Christmas" if w == res["xmas"] else ""
            print(f"   week of {w}  {per_week.get(w, 0):4d}{note}")
    print(f"   allocation bend {res['allocationBend']:.1f}   "
          f"seating bend {res['seatingBend']} week-steps {res['bendHistogram']}")
    print(f"   dated fingerprint {fp}")
    print("S106 ORACLE: ALL ASSERTIONS PASSED")



def emit_golden(inputs, result, curve, world_path):
    """★ The golden Phase 97 replays. Every value is an INTEGER or a STRING — no float is
    asserted anywhere — so parity is LITERAL equality and never a ULP bound. The two bend
    numbers are the only floats S106 computes and they are deliberately absent: they are
    page-only calibration and asserting them here would make a tuning number a red line."""
    weeks = result["weeks"]
    rows = []
    for gi in sorted(result["seated"]):
        host, visitor = inputs.pairings[gi]
        d = result["seated"][gi]
        rows.append({
            "pairIndex": gi, "host": host, "visitor": visitor,
            "date": d.isoformat(),
            "weeksSlid": abs(weeks.index(monday(d)) - result["assign"][gi]),
        })
    world_bytes = open(world_path, "rb").read()
    return {
        "schema": "s106-nonconference-dates-v1",
        "note": ("S106 dating golden. The row list is ORDERED BY PAIR INDEX and the order "
                 "is part of the artifact; the C# port reproduces it row for row. Emitted "
                 "by tools/nonconference_dates_oracle.py, whose docstring is the "
                 "specification. Integers and strings only — literal equality is the right "
                 "bar (CONVENTIONS section 2). The allocation and seating bends are "
                 "page-only and are NOT in this file on purpose."),
        "provenance": {
            "world": "stock-d1.world.json",
            "worldFileSha256": hashlib.sha256(world_bytes).hexdigest(),
            "oracleSha256": hashlib.sha256(open(__file__, "rb").read()).hexdigest(),
            "startYear": inputs.start_year,
            "curve": [[m, d, w] for (m, d, w) in curve],
            "weeklyLoadCeiling": WEEKLY_LOAD_CEILING,
            "spacingClearDays": SPACING_CLEAR_DAYS,
            "eventClearDays": EVENT_CLEAR_DAYS,
            "slideRadius": SLIDE_RADIUS,
            "pairingCount": len(inputs.pairings),
        },
        "datedFingerprint": fingerprint(inputs, result),
        "games": rows,
        "unseated": [{"pairIndex": gi, "class": cls} for gi, cls in result["failures"]],
    }


if __name__ == "__main__":
    # ★ S108 — EMIT THE GOLDEN FROM HERE, not by hand. emit_golden has existed since S106
    #   and nothing ever called it: the golden was written out in a scratch session and the
    #   recipe left with the session, so the first time the matching moved (S108) the file
    #   could not be regenerated at all. Together with the harness's `dates-input` mode the
    #   whole round trip is now two committed commands. The path is optional so the plain
    #   run stays a pure assertion pass.
    if len(sys.argv) > 2:
        world_path = sys.argv[3] if len(sys.argv) > 3 else "worlds/stock-d1.world.json"
        curve_rows = [(d.month, d.day, w) for d, w in sorted(CURVE.items())]
        payload = emit_golden(inputs, res, curve_rows, world_path)
        with open(sys.argv[2], "w") as f:
            json.dump(payload, f, indent=1)
            f.write("\n")
        print(f"GOLDEN EMITTED: {sys.argv[2]}")
        print(f"  {len(payload['games'])} dated row(s), "
              f"{len(payload['unseated'])} unseated, "
              f"fingerprint {payload['datedFingerprint'][:16]}…")
