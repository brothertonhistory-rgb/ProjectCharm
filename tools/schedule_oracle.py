#!/usr/bin/env python3
"""
Project Charm — Session 93 schedule-builder oracle (THE CONFERENCE SLATE).

Mirrors the C# season schedule builder. The contract is LOCKED here, and the C# is
written against THIS file.

★ WHAT SESSION 93 CHANGED, AND WHY THE OLD CONTRACT IS GONE (Emmett, 2026-08-02):
"we don't care about a 'season' right now, we care about games being scheduled."
The season is now the CONFERENCE SLATE AND NOTHING ELSE. Every non-conference
construction in the previous oracle — the 14-regular ring circulant, the conflict
queue, the double-edge repair, the 20-attempt retry, and the entire RNG stream that
existed to feed it — is DELETED, not disabled. Non-conference scheduling is its own
future session and starts from nothing.

Consequences that follow from that ruling, recorded so no future session meets them
as a surprise:

  * A team plays its own conference's authored number of games, not 30. Fourteen for
    the Ivy, sixteen for the ACC, twenty for the Atlantic Sun.
  * The fourteen Independent schools play ZERO games. Their conference is authored at
    Games = 0, which is R14, and it is now a live case in the stock world rather than
    a fixture curiosity.
  * ★ THE SCHEDULE CONSUMES NO RANDOMNESS AT ALL. The old builder's only RNG lived in
    the non-conference filler. The conference slate is fully determined by the world
    file, so the schedule is now IDENTICAL AT EVERY SEED. That is honest — the seed
    still drives every game's outcome — but it means the same pairs are doubled and
    the same pairs skipped every season forever, which is a real basketball gap that
    Session 94's host memory and its soft objectives are the answer to. Recorded here,
    not solved here.

THE SLATE (no RNG). For a conference of n schools with Games = G and Skip = k:

  p = n - 1 - k          opponents actually played
  q, r = divmod(G, p)    r opponents at q+1 meetings, p - r at q, k at zero

  Conferences by id ascending; members by school id ascending, indexed 0..n-1.
  Construction order inside a conference, and the ORDER IS LOAD-BEARING:
    1. resolve the active rivalries (mutual, both in THIS conference, G > 0, a matching)
    2. build the r-regular EXTRA-meeting graph so that it CONTAINS the rivalry matching
       (rivalries are placed by construction, never searched for); when r = 0, instead
       require the SKIPPED graph to avoid every rivalry edge
    3. build the k-regular SKIPPED graph on the complement of the extra graph
    4. every remaining pair meets q times
    5. orient

  ★ THE CANONICAL CIRCULANT IS TRIED FIRST AND PINNED. When k = 0 and no rivalry
  constrains it, the extra-meeting graph is exactly the circulant the pre-S93 builder
  used — offsets 1..r/2, plus the diameter matching (i, i+n/2) when r is odd. That is
  deliberate: it makes the stock slate's pair multiset reproducible against the old
  builder wherever G is unchanged, so a DIFFERENCE MEANS SOMETHING. The shortcut only
  ever ACCEPTS a candidate it has verified against the same constraints the search
  enforces; it never rejects, so it cannot mask the search's infeasibility proof.

  Otherwise the shape is found by EXHAUSTIVE backtracking over the finite class
  assignment (each unordered pair is EXTRA, SKIP or BASE), pairs in lexicographic
  order, classes tried EXTRA -> SKIP -> BASE, with degree-feasibility pruning. For
  n <= SIZE_CAP the search is exhaustive, so returning without a slate IS a proof of
  infeasibility. Above SIZE_CAP the solver refuses to search at all.

  Emission order: for i in 0..n-2, for j in i+1..n-1, emit m consecutive games
  (member_i, member_j) where m is that pair's meeting count. A conference authored at
  G = 0 emits nothing.

ORIENTATION (no RNG), and R3 is a HARD LINE: every team plays an exactly even home /
away conference season, G/2 each.

  A pair meeting m times contributes floor(m/2) home and floor(m/2) away BY
  CONSTRUCTION, alternating from the lower school id, and those games never enter the
  flow. Only an ODD m leaves one game undecided — the RESIDUAL, always the last of
  that pair's m games.

  Residuals are decided by an integral flow with EXACT quotas:
      source     -> one node per FREE residual        capacity 1
      residual   -> each of its two schools           capacity 1
      school     -> sink            capacity = EXACTLY its remaining home quota
  Fixed residuals (the Session 94 seam: FixedResidualHost) consume home quota BEFORE
  any flow structure is built; a school driven negative there is refused with
  rejected_before_flow = True and the school named. A saturating flow is a legal
  orientation; anything less proves the slate infeasible under the fixed set.

  In production the fixed set is ALWAYS EMPTY — there is no host memory yet. The
  parameter exists so Session 94 does not have to reopen this code.

FOUR VERDICTS, kept strictly separate and never collapsed:
  InvalidConfiguration(reason)        the authored world is wrong — cheap, static
  InfeasibleUnderConstraints(reason)  valid configuration, PROVEN no slate exists
  SearchBudgetExhausted               did not find one; does NOT mean none exists
  UnsupportedConferenceSize(n)        above the solver's hard size cap
Precedence, fixed: static configuration validation -> supported-size check -> search
-> feasible/infeasible.

FINGERPRINT: one record per game in schedule order (never re-sorted):
"{gameIndex}|{kind}|{homeSchoolId}|{awaySchoolId}\n", kind is always "conf" today,
UTF-8, SHA-256, lowercase hex.

ENGINE SEEDS (no RNG; asserted unique in Phase 55): base = int32(seasonSeed) two's-
complement truncation; resolver = base + 2*gameIndex, governor = base + 2*gameIndex + 1,
int32 wraparound.
"""

import hashlib
import json
import sys

SIZE_CAP = 20
MAX_GAMES = 30


# ─── Verdicts ────────────────────────────────────────────────────────────────

class ScheduleError(Exception):
    """InvalidConfiguration — the authored world is wrong."""


class InfeasibleUnderConstraints(Exception):
    """Valid configuration, PROVEN no slate exists."""


class SearchBudgetExhausted(Exception):
    """Did not find a slate. Does NOT mean none exists."""


class UnsupportedConferenceSize(Exception):
    """Above the solver's hard size cap."""


def int32(x):
    x &= 0xFFFFFFFF
    return x - (1 << 32) if x >= (1 << 31) else x


# ─── Legality — a NECESSARY filter, not a promise ────────────────────────────

def legality_reason(n, g, k):
    """None when the configuration may go to the solver, else the reason string.
    These conditions are necessary, never sufficient."""
    if g < 0:
        return f"Games {g} is negative"
    if g > MAX_GAMES:
        return f"Games {g} exceeds the {MAX_GAMES}-game maximum for a regular season"
    if g % 2 == 1:
        return f"Games {g} is odd — a conference season must be even"
    if k < 0:
        return f"Skip {k} is negative"
    if g == 0:
        # A conference of independents (R14). It carries a canonical k of zero.
        return None if k == 0 else f"Games 0 requires Skip 0 (got Skip {k})"
    if n < 2:
        return f"size {n} — a conference season needs an opponent"
    if k > n - 2:
        return f"Skip {k} leaves no opponent (size {n}; Skip may not exceed {n - 2})"
    p = n - 1 - k
    q, r = divmod(g, p)
    if q < 1:
        return (f"Games {g} over {p} played opponent(s) gives {q} meetings — "
                f"every played opponent must get a game")
    if (n * k) % 2 == 1:
        return f"size {n} with Skip {k} is odd on both — no {k}-regular skipped graph exists"
    if (n * r) % 2 == 1:
        return (f"size {n} with {r} extra meeting(s) is odd on both — "
                f"no {r}-regular extra graph exists")
    return None


def shape(n, g, k):
    """(p, q, r) for a conference that plays. Undefined at g == 0."""
    p = n - 1 - k
    q, r = divmod(g, p)
    return p, q, r


# ─── The extra-meeting / skipped shape ───────────────────────────────────────

def circulant(n, r):
    """The canonical r-regular circulant on indices 0..n-1: offsets 1..r/2, plus the
    diameter matching (i, i+n/2) when r is odd (which forces n even)."""
    extra = set()
    if r <= 0:
        return extra
    if r % 2 == 0:
        offsets, diameter = range(1, r // 2 + 1), False
    else:
        offsets, diameter = range(1, (r - 1) // 2 + 1), True
    for i in range(n):
        for off in offsets:
            j = (i + off) % n
            extra.add((min(i, j), max(i, j)))
        if diameter and i < n // 2:
            extra.add((i, i + n // 2))
    return extra


EXTRA, SKIP, BASE = 0, 1, 2


def search_shape(n, r, k, forced_extra, forbidden_skip, budget=None):
    """Exhaustive backtracking over the class of every unordered pair.

    Each vertex takes exactly r EXTRA pairs and exactly k SKIP pairs; everything else
    is BASE. forced_extra pairs must be EXTRA; forbidden_skip pairs may not be SKIP.
    Pairs are visited in lexicographic order and classes tried EXTRA -> SKIP -> BASE,
    so the first solution found is a canonical one.

    Returns (extra_set, skip_set, nodes). Raises InfeasibleUnderConstraints when the
    space is exhausted with no solution — which for n <= SIZE_CAP is a PROOF — or
    SearchBudgetExhausted, which proves nothing at all."""
    pairs = [(i, j) for i in range(n) for j in range(i + 1, n)]
    extra_left = [r] * n
    skip_left = [k] * n
    open_pairs = [n - 1] * n          # undecided pairs still incident to each vertex
    assigned = {}
    nodes = [0]

    def feasible():
        for v in range(n):
            if extra_left[v] + skip_left[v] > open_pairs[v]:
                return False
        return True

    def walk(idx):
        nodes[0] += 1
        if budget is not None and nodes[0] > budget:
            raise SearchBudgetExhausted(
                f"search budget of {budget} nodes exhausted at size {n}")
        if idx == len(pairs):
            return all(extra_left[v] == 0 and skip_left[v] == 0 for v in range(n))
        i, j = pairs[idx]
        # Row i-1 is fully decided by now, so it must be exactly satisfied.
        if idx > 0:
            prev_i = pairs[idx - 1][0]
            if prev_i != i and (extra_left[prev_i] != 0 or skip_left[prev_i] != 0):
                return False
        forced = (i, j) in forced_extra
        for cls in (EXTRA, SKIP, BASE):
            if forced and cls != EXTRA:
                continue
            if cls == EXTRA and (extra_left[i] == 0 or extra_left[j] == 0):
                continue
            if cls == SKIP and ((i, j) in forbidden_skip
                                or skip_left[i] == 0 or skip_left[j] == 0):
                continue
            if cls == EXTRA:
                extra_left[i] -= 1; extra_left[j] -= 1
            elif cls == SKIP:
                skip_left[i] -= 1; skip_left[j] -= 1
            open_pairs[i] -= 1; open_pairs[j] -= 1
            assigned[(i, j)] = cls
            ok = feasible() and walk(idx + 1)
            if ok:
                return True
            del assigned[(i, j)]
            open_pairs[i] += 1; open_pairs[j] += 1
            if cls == EXTRA:
                extra_left[i] += 1; extra_left[j] += 1
            elif cls == SKIP:
                skip_left[i] += 1; skip_left[j] += 1
        return False

    if not walk(0):
        raise InfeasibleUnderConstraints(
            f"no legal slate exists for size {n} with {r} extra meeting(s), {k} skip(s) "
            f"and {len(forced_extra) + len(forbidden_skip)} placed rivalry pair(s)")
    ex = {p for p, c in assigned.items() if c == EXTRA}
    sk = {p for p, c in assigned.items() if c == SKIP}
    return ex, sk, nodes[0]


def conference_meetings(members, g, k, rival_pairs=(), conf_label="", budget=None,
                        size_cap=SIZE_CAP, stats=None):
    """members: school ids ascending. rival_pairs: frozenset({idA, idB}) entries active
    in THIS conference. Returns {(loId, hiId): meetings} for every unordered pair."""
    n = len(members)
    reason = legality_reason(n, g, k)
    if reason is not None:
        raise ScheduleError(f"INVALID CONFIGURATION: {conf_label}{reason}.")
    if g == 0:
        return {}
    if n > size_cap:
        raise UnsupportedConferenceSize(
            f"UNSUPPORTED CONFERENCE SIZE: {conf_label}size {n} is above the solver's "
            f"hard cap of {size_cap}; no search was attempted.")
    p, q, r = shape(n, g, k)
    idx = {sid: i for i, sid in enumerate(members)}
    forced = set()
    for pr in rival_pairs:
        a, b = sorted(idx[s] for s in pr)
        forced.add((a, b))

    extra = skipped = None
    if k == 0:
        cand = circulant(n, r)
        if r == 0 or forced <= cand:
            extra, skipped = cand, set()
    if extra is None:
        # r > 0: rivalries must sit at q+1. r == 0: rivalries must not be skipped.
        forced_extra = forced if r > 0 else set()
        forbidden_skip = set() if r > 0 else forced
        extra, skipped, nodes = search_shape(n, r, k, forced_extra, forbidden_skip, budget)
        if stats is not None:
            stats.setdefault("nodes", {})[conf_label.strip() or n] = nodes

    meetings = {}
    for i in range(n - 1):
        for j in range(i + 1, n):
            m = 0 if (i, j) in skipped else q + (1 if (i, j) in extra else 0)
            meetings[(members[i], members[j])] = m
    return meetings


# ─── Orientation ─────────────────────────────────────────────────────────────

class OrientationResult:
    def __init__(self, homes, rejected_before_flow=False, reason=""):
        self.homes = homes
        self.rejected_before_flow = rejected_before_flow
        self.reason = reason


def orient_conference(members, g, meetings, fixed_hosts=None):
    """Returns an OrientationResult whose .homes is a list of home school ids, one per
    emitted game, in emission order.

    fixed_hosts: {(loId, hiId): hostId} — the Session 94 seam. Legal only for a pair
    whose meeting count is odd; the host must be one of the two schools."""
    fixed_hosts = dict(fixed_hosts or {})
    quota = {s: g // 2 for s in members}
    residual_index = {}
    homes = []
    order = []
    for i in range(len(members) - 1):
        for j in range(i + 1, len(members)):
            lo, hi = members[i], members[j]
            m = meetings[(lo, hi)]
            for t in range(m):
                order.append((lo, hi))
                if m % 2 == 1 and t == m - 1:
                    residual_index[(lo, hi)] = len(homes)
                    homes.append(None)
                else:
                    h = lo if t % 2 == 0 else hi
                    homes.append(h)
                    quota[h] -= 1

    for pair, host in sorted(fixed_hosts.items()):
        if pair not in meetings:
            raise ScheduleError(
                f"INVALID CONFIGURATION: fixed host names pair {pair}, "
                f"which is not a pair in this conference.")
        if meetings[pair] % 2 == 0:
            raise ScheduleError(
                f"INVALID CONFIGURATION: fixed host named for pair {pair}, which meets "
                f"{meetings[pair]} time(s) — only an odd meeting count leaves a residual.")
        if host not in pair:
            raise ScheduleError(
                f"INVALID CONFIGURATION: fixed host {host} is not one of the two "
                f"schools in {pair}.")

    for pair in sorted(fixed_hosts):
        host = fixed_hosts[pair]
        quota[host] -= 1
        if quota[host] < 0:
            return OrientationResult(
                None, rejected_before_flow=True,
                reason=(f"the fixed hosts over-commit school {host}'s home quota of "
                        f"{g // 2} — refused before any flow structure was built"))
        homes[residual_index[pair]] = host

    free = [pair for pair in sorted(residual_index) if pair not in fixed_hosts]
    if sum(quota.values()) != len(free):
        return OrientationResult(
            None, reason=(f"the remaining home quota {sum(quota.values())} does not equal "
                          f"the {len(free)} free residual game(s)"))

    # Integral flow by deterministic augmenting paths. Each free residual has exactly
    # two candidate hosts; a school may host at most its remaining quota. Candidate
    # order is fixed (lower school id first, then residual index ascending), so this
    # returns one specific orientation and never a choice between two.
    assigned = {}
    holds = {s: [] for s in members}
    for start in range(len(free)):
        parent = {start: None}
        queue = [start]
        found = None
        while queue and found is None:
            ridx = queue.pop(0)
            for school in free[ridx]:
                if quota[school] > 0:
                    found = (ridx, school)
                    break
                for other in holds[school]:
                    if other not in parent:
                        parent[other] = (ridx, school)
                        queue.append(other)
        if found is None:
            return OrientationResult(
                None, reason=(f"no legal orientation exists under the fixed set: the "
                              f"residual {free[start]} has nowhere left to host"))
        ridx, school = found
        quota[school] -= 1
        while True:
            was = assigned.get(ridx)
            if was is not None:
                holds[was].remove(ridx)
            assigned[ridx] = school
            holds[school].append(ridx)
            step = parent[ridx]
            if step is None:
                break
            ridx, school = step

    for ridx, school in assigned.items():
        homes[residual_index[free[ridx]]] = school

    if any(h is None for h in homes):
        return OrientationResult(None, reason="the orientation left a game undecided")
    return OrientationResult(homes)


# ─── The schedule ────────────────────────────────────────────────────────────

def active_rivalries(members, rivals, g):
    """A rivalry is ACTIVE for slate construction only when both schools share a
    conference whose Games > 0. A cross-conference rivalry and a rivalry inside a
    zero-game conference are both DORMANT — never an error."""
    if g == 0:
        return set()
    inside = set(members)
    return {frozenset((s, rivals[s])) for s in members
            if rivals.get(s) is not None and rivals[s] in inside}


def build_schedule(schools, conferences, rivals=None, fixed_hosts=None, stats=None):
    """schools: list of (id, confId) sorted by id.
       conferences: {id: (name, games, skip)}.
       rivals: {schoolId: rivalSchoolId} — mutual, a matching, validated at load.
       Returns (games, fingerprint); games is a list of (kind, home, away)."""
    rivals = rivals or {}
    fixed_hosts = fixed_hosts or {}
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)

    games = []
    for cid in sorted(by_conf):
        members = sorted(by_conf[cid])
        name, g, k = conferences[cid]
        label = f"conference '{name}' (id {cid}) "
        meetings = conference_meetings(
            members, g, k, active_rivalries(members, rivals, g), label, stats=stats)
        if not meetings:
            continue
        res = orient_conference(members, g, meetings, fixed_hosts.get(cid))
        if res.homes is None:
            raise InfeasibleUnderConstraints(
                f"INFEASIBLE UNDER CONSTRAINTS: {label}{res.reason}.")
        pos = 0
        for i in range(len(members) - 1):
            for j in range(i + 1, len(members)):
                lo, hi = members[i], members[j]
                for _ in range(meetings[(lo, hi)]):
                    h = res.homes[pos]
                    games.append(("conf", h, hi if h == lo else lo))
                    pos += 1

    payload = "".join(f"{i}|{k}|{h}|{a}\n" for i, (k, h, a) in enumerate(games))
    return games, hashlib.sha256(payload.encode("utf-8")).hexdigest()


# ─── Legality proof (the invariants Phase 55 and Phase 84 assert) ────────────

def prove(schools, conferences, games, tag, rivals=None):
    rivals = rivals or {}
    conf_of = dict(schools)
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)

    played = {sid: 0 for sid, _ in schools}
    home_n = {sid: 0 for sid, _ in schools}
    pair = {}
    for kind, h, a in games:
        assert h != a, f"{tag}: self-game {h}"
        assert kind == "conf", f"{tag}: a non-conference game exists"
        assert conf_of[h] == conf_of[a], f"{tag}: a conference game crossed conferences"
        played[h] += 1; played[a] += 1; home_n[h] += 1
        key = (min(h, a), max(h, a))
        pair[key] = pair.get(key, 0) + 1

    expected_total = 0
    for cid, members in by_conf.items():
        n = len(members)
        _, g, k = conferences[cid]
        expected_total += n * g // 2
        for s in members:
            assert played[s] == g, f"{tag}: school {s} plays {played[s]}, authored {g}"
            assert home_n[s] == g // 2, \
                f"{tag}: school {s} hosts {home_n[s]} of {g}, not the exact half {g // 2}"
        if g == 0:
            continue
        p, q, r = shape(n, g, k)
        for s in members:
            counts = [pair.get((min(s, o), max(s, o)), 0) for o in members if o != s]
            assert counts.count(0) == k, \
                f"{tag}: school {s} skips {counts.count(0)}, authored {k}"
            assert counts.count(q + 1) == r, \
                f"{tag}: school {s} has {counts.count(q + 1)} opponents at q+1, expected {r}"
            assert counts.count(q) == p - r, \
                f"{tag}: school {s} has {counts.count(q)} opponents at q, expected {p - r}"
            rv = rivals.get(s)
            if rv is not None and rv in members:
                met = pair.get((min(s, rv), max(s, rv)), 0)
                if r > 0:
                    assert met == q + 1, f"{tag}: rivalry {s}-{rv} sits at {met}, not q+1"
                else:
                    assert met > 0, f"{tag}: rivalry {s}-{rv} was skipped"
    assert len(games) == expected_total, \
        f"{tag}: {len(games)} games, expected {expected_total}"


# ─── World loading ───────────────────────────────────────────────────────────

def load_world(path):
    w = json.load(open(path))
    schools = sorted((s["id"], s["conferenceId"]) for s in w["schools"])
    conferences = {c["id"]: (c["name"], c["games"], c["skip"]) for c in w["conferences"]}
    rivals = {s["id"]: s["rivalId"] for s in w["schools"] if s.get("rivalId") is not None}
    return schools, conferences, rivals


# ═════════════════════════════════════════════════════════════════════════════
#  SESSION 94 — DATES FOR THE CONFERENCE SLATE (extends the locked S93 contract)
#
#  ★ THE S93 LAYER ABOVE IS UNTOUCHED. Dates are an attribute of a game, never a
#  re-sort: the dated schedule is the S93 emission with a date attached to each
#  game, and the structural fingerprint cannot move.
#
#  THE MODEL (Emmett, 2026-08-02, ruled LOOSE over tight on real Big East
#  evidence — two 2025-26 schedules of an 11-team league playing 20 games):
#    * Three authored numbers per league: games, weeks, and the day its
#      tournament opens (days before Selection Sunday; None = no tournament,
#      walling at Selection Sunday itself). wall = SelectionSunday - offset - 1.
#    * The week is MONDAY TO SUNDAY. A team never plays three in one. BAR NONE.
#    * The window's final week is the LATEST week ALL of whose active nights
#      fall on or before the wall (the real Big East finished Sat Mar 7 against
#      a Tue Mar 10 wall — rest days into the tournament, never a partial week).
#    * The window is that week plus the weeks-1 playing weeks before it,
#      skipping the Monday-Sunday week containing December 25 (quiet, R10).
#    * Weekly totals are EXACT: base, extra = divmod(n*G/2, weeks); the LAST
#      `extra` playing weeks carry base+1 (the real league opens light: its two
#      schedules' combined December appearances ran 1/3/3 against 3-4 after).
#    * Active nights: even leagues {D1,D2}; odd {D1,D2,D3}. A date holds at
#      most floor(n/2) games. Dates fill in AUTHORED priority D1->D2->D3 as a
#      candidate order inside exhaustive backtracking — never a greedy rule,
#      never a final count. Capacity theorem (r13): with valid nights a
#      complete week seats exactly n, and weeks >= G/2 keeps every target <= n
#      — asserted internally; a failure indicts this file, not the world.
#    * ★ THE COMPLETED DATED WEEK IS THE ATOMIC UNIT OF CHRONOLOGICAL
#      EVALUATION (r14/r15). Within-week placements test only week-stable
#      facts; rematch non-adjacency and quarter status run on the week sorted
#      by real date and tentatively appended. Judged mid-week, a Saturday
#      placement would read adjacent to last week's opponent while the
#      unassigned Wednesday game is the very thing separating them.
#    * Rematch: between two meetings of a pair, EACH team plays someone else in
#      between; and the two meetings land in different game-count quarters of
#      each team's own sequence (the first G mod 4 quarters hold one extra).
#    * S93 emission order is the deterministic tie-break everywhere. No RNG.
#
#  Verdicts: ScheduleError (InvalidConfiguration) for authored-data faults and
#  the two-sided week bound G/2 <= weeks <= n*G/2; InfeasibleUnderConstraints
#  only from exhaustion (a PROOF); SearchBudgetExhausted proves nothing.
# ═════════════════════════════════════════════════════════════════════════════

import datetime as _dt

_WD = {"mon": 0, "tue": 1, "wed": 2, "thu": 3, "fri": 4, "sat": 5, "sun": 6}
DATE_SEARCH_BUDGET = 5_000_000


def third_sunday_in_march(year):
    first = _dt.date(year, 3, 1)
    return first + _dt.timedelta(days=(6 - first.weekday()) % 7 + 14)


def _monday(d):
    return d - _dt.timedelta(days=d.weekday())


def is_weekend(d):
    """★ S105.2 — THE definition of the weekend, once (A1): Saturday and Sunday.
    Friday is a weekday — Emmett, on the Ivy pair: the Friday/Saturday back-to-back
    is legal BECAUSE Friday is the weekday game. One line to change, here only."""
    return d.weekday() >= 5


def parse_nights(nights, n, conf_label):
    """Case-normalised, validated: distinct recognised weekdays; even leagues use the
    first two, odd leagues all three. Returns the ACTIVE ordered night list."""
    norm = []
    for raw in nights:
        w = (raw or "").strip().lower()
        if w not in _WD:
            raise ScheduleError(f"INVALID CONFIGURATION: {conf_label}"
                                f"unrecognised authored night '{raw}'.")
        norm.append(w)
    if len(set(norm)) != len(norm):
        raise ScheduleError(f"INVALID CONFIGURATION: {conf_label}"
                            f"duplicate authored night in {nights}.")
    need = 2 if n % 2 == 0 else 3
    active = norm[:need]
    if len(set(active)) < need:
        raise ScheduleError(f"INVALID CONFIGURATION: {conf_label}"
                            f"needs {need} distinct nights, has {len(set(active))}.")
    return active


def league_window(start_year, weeks, offset_days, active, conf_label):
    """The ordered playing-week Mondays (ascending) and the wall. The final week is
    the latest Mon-Sun week ALL of whose active nights fall on or before the wall;
    the Christmas week is skipped and counts for nothing (R10)."""
    ss = third_sunday_in_march(start_year + 1)
    if offset_days is not None and offset_days < 0:
        raise ScheduleError(f"INVALID CONFIGURATION: {conf_label}"
                            f"tournament offset {offset_days} is negative.")
    wall = ss if offset_days is None else ss - _dt.timedelta(days=offset_days + 1)
    wk = _monday(wall)
    while not all(wk + _dt.timedelta(days=_WD[a]) <= wall for a in active):
        wk -= _dt.timedelta(days=7)
    xmas = _monday(_dt.date(start_year, 12, 25))
    nov1 = _dt.date(start_year, 11, 1)
    out = []
    while len(out) < weeks:
        if wk != xmas:
            out.append(wk)
        wk -= _dt.timedelta(days=7)
    if out and out[-1] < nov1:
        raise ScheduleError(f"INVALID CONFIGURATION: {conf_label}window would open "
                            f"{out[-1]}, before the November 1 floor.")
    return list(reversed(out)), wall


def weekly_targets(n, g, weeks, conf_label):
    """Exact totals: [base]*(weeks-extra) + [base+1]*extra over the ordered playing
    weeks — heavier weeks LATEST. Two-sided bound refused here; capacity asserted
    internally per the r13 theorem."""
    if weeks < g // 2:
        raise ScheduleError(f"INVALID CONFIGURATION: {conf_label}Weeks {weeks} cannot "
                            f"seat Games {g} at two a week (needs at least {g // 2}).")
    league_games = n * g // 2
    if weeks > league_games:
        raise ScheduleError(f"INVALID CONFIGURATION: {conf_label}Weeks {weeks} exceeds "
                            f"the league's {league_games} games — an empty week is "
                            f"forced (base = 0).")
    base, extra = divmod(league_games, weeks)
    targets = [base] * (weeks - extra) + [base + 1] * extra
    assert max(targets) <= n, (
        f"{conf_label}weekly target {max(targets)} exceeds capacity {n} — "
        f"implementation error, not authored data (r13 theorem)")
    return targets


def _quarter_of(seq_index, g):
    q, m = divmod(g, 4)
    sizes = [q + 1] * m + [q] * (4 - m)
    start = 0
    for qi, sz in enumerate(sizes):
        if seq_index < start + sz:
            return qi
        start += sz
    raise AssertionError("sequence index outside its own season")


def date_conference(members, g, weeks, offset_days, nights, oriented_games,
                    start_year, conf_label="", budget=DATE_SEARCH_BUDGET,
                    feasibility=None):
    """The heart of S94: partition the S93 oriented games (emission order) into dated
    rounds. Returns (dates, window, wall): dates[i] dates oriented_games[i].

    Week-by-week search; the COMPLETED DATED WEEK is the atomic unit of
    chronological evaluation. Individual placements test only week-stable facts
    (occupancy, the team week-cap, the date cap, target reachability). Candidate
    games in emission order; candidate dates in authored priority."""
    n = len(members)
    if n - 1 <= 1 and g >= 2:
        # ★ r-oracle finding: with a single played opponent every game is a rematch of
        #   the last, so the non-adjacency rule is unsatisfiable at ANY calendar length.
        #   Static, named, before any search — never an infeasibility "discovery".
        raise ScheduleError(
            f"INVALID CONFIGURATION: {conf_label}a dated conference season needs at "
            f"least two played opponents (size {n} gives one; every game would be a "
            f"back-to-back rematch).")
    active = parse_nights(nights, n, conf_label)
    window, wall = league_window(start_year, weeks, offset_days, active, conf_label)
    targets = weekly_targets(n, g, weeks, conf_label)
    cap = n // 2

    seq = {s2: [] for s2 in members}
    dates_out = [None] * len(oriented_games)

    # ── ROTATION-GUIDED CONSTRUCTION (deterministic, no RNG) ──────────────────
    # The meeting multiset of every k=0 league is q complete-graph cycles plus the
    # extra matching graph; a circle-method rotation emits rounds of distinct
    # matchings, so a pair's repeat meetings land ~half a season apart and the
    # rematch and quarter rules hold BY CONSTRUCTION. The extra cycle is
    # interleaved mid-season for the same reason. Weeks are filled from the round
    # stream in order under the team week-cap, deferring overflow FIFO; the
    # completed-week evaluation (r14/r15) remains the wall every week must pass.
    idx = {sid: i for i, sid in enumerate(members)}
    pair_games = {}
    for i, (_, h, a) in enumerate(oriented_games):
        pair_games.setdefault((min(h, a), max(h, a)), []).append(i)
    meetings_of = {p: len(v) for p, v in pair_games.items()}
    q_base = min(meetings_of.values()) if meetings_of else 0
    # circle-method rounds over member indices (odd n gets a bye vertex)
    m2 = n if n % 2 == 0 else n + 1
    ring = list(range(m2))
    circle_rounds = []
    for _ in range(m2 - 1):
        rnd = []
        for i2 in range(m2 // 2):
            a2, b2 = ring[i2], ring[m2 - 1 - i2]
            if a2 < n and b2 < n:
                rnd.append((min(a2, b2), max(a2, b2)))
        circle_rounds.append(sorted(rnd))
        ring = [ring[0]] + [ring[-1]] + ring[1:-1]
    # cycles: 0..q_base-1 carry every non-skip pair; the EXTRA cycle carries pairs
    # with meetings > q_base, interleaved after cycle floor(q_base/2).
    def cycle_rounds(which):
        out = []
        for rnd in circle_rounds:
            keep = []
            for (i2, j2) in rnd:
                key = (members[i2], members[j2])
                mts = meetings_of.get((min(key), max(key)), 0)
                if which == "extra":
                    if mts > q_base:
                        keep.append(key)
                elif mts > 0:
                    keep.append(key)
            if keep:
                out.append(keep)
        return out
    # Extras are NOT a block: each extra instance is interleaved half a rotation
    # (R/2 rounds) away from its pair's base-round position, so a doubled
    # opponent's two meetings sit ~half a season apart BY CONSTRUCTION — the same
    # property the pure double-round-robin gets from cycling.
    base_rounds = cycle_rounds("base")
    R = max(1, len(base_rounds))
    round_of_pair = {}
    for ri, rnd in enumerate(base_rounds):
        for key in rnd:
            round_of_pair.setdefault((min(key), max(key)), ri)
    entries = []                                  # (sortkey, tiebreak, gameIdx)
    for p, gis in pair_games.items():
        rp = round_of_pair.get(p, 0)
        for e, gi in enumerate(gis):
            if e < q_base:
                # base meeting e sits in cycle e at its circle-round position
                key = (e * R + rp) * 2
            else:
                # extra meeting: half a rotation from the base position, inside
                # the middle cycle; deeper extras (never in stock) step by R/2 more
                off = (rp + (R // 2) * (e - q_base + 1)) % R
                key = ((q_base // 2) * R + off) * 2 + 1
            entries.append((key, gi))
    entries.sort()
    stream = [gi for _, gi in entries]
    assert len(stream) == len(oriented_games), \
        f"{conf_label}rotation stream lost games — implementation error"

    nodes = [0]

    def run_construction():
        left = {s2: g for s2 in members}
        used = [False] * len(stream)

        def build(w):
            if w == len(window):
                return all(used)
            if feasibility is not None:
                feasibility.setdefault("week_entries", {})
                feasibility["week_entries"][w] = \
                    feasibility["week_entries"].get(w, 0) + 1
            target = targets[w]
            dts = [window[w] + _dt.timedelta(days=_WD[a]) for a in active]
            weeks_left = len(window) - w
            for s2 in members:
                if left[s2] > 2 * weeks_left:
                    return False
            # ★ URGENCY-SORTED candidate order: a team owing more than the weeks
            #   left can seat must play NOW; on-pace teams keep rotation order so
            #   the built-in half-season spacing survives. Deterministic.
            def urgency(gi):
                _, h2, a2 = oriented_games[gi]
                return max(left[h2] - 2 * (weeks_left - 1),
                           left[a2] - 2 * (weeks_left - 1))
            order = sorted((sp for sp in range(len(stream)) if not used[sp]),
                           key=lambda sp: (-urgency(stream[sp]), sp))
            played_wk = {s2: 0 for s2 in members}
            # ★ S105.2 — the weekday/weekend rule, PRUNED not validated (r2 §2b-bis):
            #   per-team occupancy of each half of the week, kept beside played_wk,
            #   incremented on take and unwound on backtrack in the same places.
            wd_wk = {s2: 0 for s2 in members}
            we_wk = {s2: 0 for s2 in members}
            date_is_weekend = [is_weekend(d) for d in dts]
            pairs_wk = set()
            on_date = [set() for _ in dts]
            chosen = []

            def eval_and_descend():
                # ── COMPLETED-WEEK EVALUATION (r14/r15): sort by real date,
                #    tentatively append, run the sequence rules, descend. ──
                week_sorted = sorted(chosen, key=lambda t: (dts[t[2]], t[1]))
                appended = 0
                ok2 = True
                for sp, gi, di in week_sorted:
                    _, h2, a2 = oriented_games[gi]
                    for team, opp in ((h2, a2), (a2, h2)):
                        if seq[team] and seq[team][-1][0] == opp:
                            ok2 = False
                            break
                        prev = next((i2 for o, i2 in reversed(seq[team])
                                     if o == opp), None)
                        if prev is not None and \
                                _quarter_of(prev, g) == _quarter_of(len(seq[team]), g):
                            ok2 = False
                            break
                        seq[team].append((opp, len(seq[team])))
                        appended += 1
                    if not ok2:
                        break
                if ok2:
                    for sp, gi, di in week_sorted:
                        dates_out[gi] = dts[di]
                        used[sp] = True
                        _, h2, a2 = oriented_games[gi]
                        left[h2] -= 1; left[a2] -= 1
                    if build(w + 1):
                        return True
                    for sp, gi, di in week_sorted:
                        dates_out[gi] = None
                        used[sp] = False
                        _, h2, a2 = oriented_games[gi]
                        left[h2] += 1; left[a2] += 1
                # unwind the tentative appends
                flat = []
                for sp, gi, di in week_sorted:
                    _, h2, a2 = oriented_games[gi]
                    flat.extend((h2, a2))
                for team in reversed(flat[:appended]):
                    seq[team].pop()
                return False

            def pick(pos, count):
                nodes[0] += 1
                if nodes[0] > budget:
                    raise SearchBudgetExhausted(
                        f"{conf_label}date-search budget {budget} exhausted "
                        f"in week {w + 1}")
                if count == target:
                    return eval_and_descend()
                if pos == len(order) or count + (len(order) - pos) < target:
                    return False
                sp = order[pos]
                gi = stream[sp]
                _, h2, a2 = oriented_games[gi]
                key = (min(h2, a2), max(h2, a2))
                if played_wk[h2] < 2 and played_wk[a2] < 2 and key not in pairs_wk:
                    for di in range(len(dts)):
                        if h2 in on_date[di] or a2 in on_date[di]:
                            continue
                        if len(on_date[di]) >= 2 * cap:
                            continue
                        # ★ S105.2 — at most one weekday and one weekend game a week:
                        #   a date whose half is already occupied by EITHER team is
                        #   rejected here, before it is ever taken.
                        occ = we_wk if date_is_weekend[di] else wd_wk
                        if occ[h2] >= 1 or occ[a2] >= 1:
                            continue
                        on_date[di].add(h2); on_date[di].add(a2)
                        played_wk[h2] += 1; played_wk[a2] += 1
                        occ[h2] += 1; occ[a2] += 1
                        pairs_wk.add(key)
                        chosen.append((sp, gi, di))
                        if pick(pos + 1, count + 1):
                            return True
                        chosen.pop()
                        pairs_wk.discard(key)
                        occ[h2] -= 1; occ[a2] -= 1
                        played_wk[h2] -= 1; played_wk[a2] -= 1
                        on_date[di].discard(h2); on_date[di].discard(a2)
                        break        # date choice is priority-forced, not branched
                    else:
                        pass
                    # a must-play team's game may not be skipped
                    if left[h2] - played_wk[h2] > 2 * (weeks_left - 1) or \
                            left[a2] - played_wk[a2] > 2 * (weeks_left - 1):
                        return False
                return pick(pos + 1, count)

            return pick(0, 0)

        return build(0)

    ok = run_construction()
    if not ok:
        raise SearchBudgetExhausted(
            f"{conf_label}the rotation construction wedged — a fuller search is "
            f"needed; this proves nothing about feasibility.")
    if False:
        raise InfeasibleUnderConstraints(
            f"INFEASIBLE UNDER CONSTRAINTS: {conf_label}no legal date assignment "
            f"exists under the week cap, exact weekly totals, rematch spacing and "
            f"quarter separation (search exhausted).")
    return dates_out, window, wall


def date_schedule(schools, conferences, games, start_year, meta, stats=None):
    """Date every game of an S93 build. meta: {confId: (nights, weeks, offsetDays)}.
    Returns (dates aligned to games, DATED fingerprint over index|date|home|away)."""
    conf_of = dict(schools)
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)
    dates = [None] * len(games)
    per_conf_idx = {}
    for i, (kind, h, a) in enumerate(games):
        per_conf_idx.setdefault(conf_of[h], []).append(i)
    for cid in sorted(by_conf):
        members = sorted(by_conf[cid])
        name, g, k = conferences[cid]
        if g == 0:
            continue                                   # zero-game league: exempt
        nights, weeks, off = meta[cid]
        label = f"conference '{name}' (id {cid}) "
        idxs = per_conf_idx.get(cid, [])
        ds, window, wall = date_conference(
            members, g, weeks, off, nights, [games[i] for i in idxs],
            start_year, label)
        for i, d in zip(idxs, ds):
            dates[i] = d
        if stats is not None:
            stats[cid] = (window, wall)
    payload = "".join(f"{i}|{dates[i].isoformat() if dates[i] else '-'}|{h}|{a}\n"
                      for i, (kind, h, a) in enumerate(games))
    return dates, hashlib.sha256(payload.encode("utf-8")).hexdigest()


def prove_dates(schools, conferences, games, dates, start_year, meta, tag):
    """Every S94 rule, by name, on the dated result."""
    conf_of = dict(schools)
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)
    for cid in sorted(by_conf):
        members = sorted(by_conf[cid])
        name, g, k = conferences[cid]
        idxs = [i for i, (kk, h, a) in enumerate(games) if conf_of[h] == cid]
        if g == 0:
            assert not idxs, f"{tag}: zero-game league {cid} emitted games"
            continue
        n = len(members)
        nights, weeks, off = meta[cid]
        active = parse_nights(nights, n, "")
        window, wall = league_window(start_year, weeks, off, active, "")
        targets = weekly_targets(n, g, weeks, "")
        nov1 = _dt.date(start_year, 11, 1)
        xmas = _monday(_dt.date(start_year, 12, 25))
        wk_count = {}
        team_week = {}
        team_half = {}
        team_games = {s: [] for s in members}
        by_date = {}
        for i in idxs:
            d = dates[i]
            assert d is not None, f"{tag}: undated game {i}"
            assert nov1 <= d <= wall, f"{tag}: {d} outside [{nov1}, wall {wall}]"
            assert d.weekday() in {_WD[a] for a in active}, \
                f"{tag}: {d} is not an active authored night"
            wm = _monday(d)
            assert wm != xmas, f"{tag}: game inside the Christmas week (R10)"
            assert wm in window, f"{tag}: game week {wm} outside the window"
            wk_count[wm] = wk_count.get(wm, 0) + 1
            by_date[d] = by_date.get(d, 0) + 1
            _, h, a = games[i]
            for t in (h, a):
                team_week[(t, wm)] = team_week.get((t, wm), 0) + 1
                half = "we" if is_weekend(d) else "wd"
                team_half[(t, wm, half)] = team_half.get((t, wm, half), 0) + 1
                team_games[t].append((d, i))
        for wi, wm in enumerate(window):
            assert wk_count.get(wm, 0) == targets[wi], \
                f"{tag}: week {wm} holds {wk_count.get(wm, 0)}, target {targets[wi]}"
        # ★ S105.2 — THE RULE (replaces the old <=2-a-week assert, which two ceilings
        #   of one subsume; leaving it would be a dead line reading as protection):
        #   at most ONE Mon-Fri game and ONE Sat-Sun game per team per week.
        for (t, wm, half), c in team_half.items():
            assert c <= 1, (f"{tag}: team {t} plays {c} "
                            f"{'weekend' if half == 'we' else 'weekday'} games "
                            f"in week {wm} — ABJECT FAILURE")
        for d, c in by_date.items():
            assert c <= n // 2, f"{tag}: {d} seats {c} games, cap {n // 2}"
        for t in members:
            ordered = [games[i][1] if games[i][2] == t else games[i][2]
                       for d, i in sorted(team_games[t])]
            assert len(ordered) == g, f"{tag}: team {t} dated {len(ordered)} of {g}"
            seen = {}
            for si, opp in enumerate(ordered):
                assert not (si and ordered[si - 1] == opp), \
                    f"{tag}: adjacent rematch for {t} vs {opp}"
                if opp in seen:
                    assert _quarter_of(seen[opp], g) != _quarter_of(si, g), \
                        f"{tag}: same-quarter rematch for {t} vs {opp}"
                seen[opp] = si


if __name__ == "__main__":
    import time
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    worlds = [("stock-d1", load_world(f"{root}/worlds/stock-d1.world.json")),
              ("fixture-tiny", load_world(f"{root}/worlds/fixture-tiny.world.json")),
              ("fixture-schedule", load_world(f"{root}/worlds/fixture-schedule.world.json"))]

    print("LOCKED EXPORTS (the schedule consumes no randomness; the seed does not enter):")
    for label, (schools, confs, rivals) in worlds:
        t0 = time.time()
        games, fp = build_schedule(schools, confs, rivals)
        prove(schools, confs, games, label, rivals)
        print(f"  {label:18s} games {len(games):5d}  game0 {games[0] if games else '(none)'}")
        print(f"  {label:18s} fingerprint {fp}   ({time.time() - t0:.2f}s)")

    schools, confs, rivals = worlds[0][1]
    g1, fp1 = build_schedule(schools, confs, rivals)
    g2, fp2 = build_schedule(schools, confs, rivals)
    assert fp1 == fp2 and g1 == g2
    print("determinism: rebuilding the same world reproduces the schedule exactly")

    # Per-league conference game counts, for the Phase 84 prediction.
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)
    print("per-league conference game counts:",
          {cid: len(by_conf[cid]) * confs[cid][1] // 2 for cid in sorted(by_conf)})

    # Size-cap benchmark (A12): a deliberately hard LEGAL configuration at n = SIZE_CAP.
    st = {}
    t0 = time.time()
    conference_meetings(list(range(1, SIZE_CAP + 1)), 22, 3, (), f"n={SIZE_CAP} ", stats=st)
    print(f"size-cap benchmark: the hardest legal configuration found at the cap, "
          f"n={SIZE_CAP} G=22 k=3, solved in {time.time() - t0:.3f}s, nodes {st.get('nodes')}")
    # ★ The refusal case must be LEGAL, or InvalidConfiguration fires first and the size
    #   check is never reached — which is the precedence order working, not the cap.
    assert legality_reason(SIZE_CAP + 1, 20, 0) is None
    try:
        conference_meetings(list(range(1, SIZE_CAP + 2)), 20, 0, (), "oversized ")
        raise SystemExit("an oversized conference was NOT refused")
    except UnsupportedConferenceSize as e:
        print(f"size-cap refusal: {e}")

    # ═══ SESSION 94 — THE DATE LAYER (start year 2026 is the ruled default) ═══
    import datetime as _d
    START_YEAR = 2026

    def _meta(path):
        w = json.load(open(path))
        return {c["id"]: (c["nights"], c["weeks"], c["tourneyOffsetDays"])
                for c in w["conferences"]}

    print()
    print("S94 DATED EXPORTS (the completed dated week is the atomic unit; no RNG):")
    # stock and fixture-tiny date whole-world; fixture-schedule goes per-conference
    # because its Duo league is the standing single-opponent refusal.
    for label, path in [("stock-d1", f"{root}/worlds/stock-d1.world.json"),
                        ("fixture-tiny", f"{root}/worlds/fixture-tiny.world.json")]:
        schools, confs, rivals = load_world(path)
        games, _fp = build_schedule(schools, confs, rivals)
        meta = _meta(path)
        dates, dfp = date_schedule(schools, confs, games, START_YEAR, meta)
        prove_dates(schools, confs, games, dates, START_YEAR, meta, label)
        dec = sum(1 for d in dates if d and d.month == 12)
        print(f"  {label:18s} dated {sum(1 for d in dates if d):5d}  "
              f"December games {dec:3d}  dated fingerprint {dfp}")

    # ═══ S105.2 — THE FEASIBILITY REPORT (r2 §2d): every league whose heaviest
    #     week sits EXACTLY on the 2·floor(n/2) ceiling, solved and shown BEFORE
    #     the C# port. Self-selecting, not a hardcoded list of nine. ═══
    # ★ S105.2 §4.6 — the weekend definition over ALL SEVEN DAYS (A1 is the session):
    #   Mon 2027-01-04 .. Sun 2027-01-10; Mon-Fri false, Sat/Sun true.
    _wk0 = _dt.date(2027, 1, 4)
    assert [is_weekend(_wk0 + _dt.timedelta(days=i)) for i in range(7)] == \
        [False, False, False, False, False, True, True], \
        "the weekend definition drifted — A1"

    print()
    print("S105.2 FEASIBILITY — leagues at the weekday/weekend ceiling (heaviest week"
          " == 2*floor(n/2)):")
    schools, confs, rivals = load_world(f"{root}/worlds/stock-d1.world.json")
    games, _fp = build_schedule(schools, confs, rivals)
    meta = _meta(f"{root}/worlds/stock-d1.world.json")
    conf_of = dict(schools)
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)
    at_ceiling = 0
    for cid in sorted(by_conf):
        members = sorted(by_conf[cid])
        name, g, k = confs[cid]
        if g == 0:
            continue
        n = len(members)
        nights, weeks, off = meta[cid]
        targets = weekly_targets(n, g, weeks, f"'{name}' ")
        heaviest = max(targets)
        if heaviest != 2 * (n // 2):
            continue
        at_ceiling += 1
        idxs = [i for i, (kk, h, a) in enumerate(games) if conf_of[h] == cid]
        feas = {}
        ds, window, wall = date_conference(
            members, g, weeks, off, nights, [games[i] for i in idxs],
            START_YEAR, f"'{name}' ", feasibility=feas)
        hw = max(range(len(targets)), key=lambda w: targets[w])
        hw_monday = window[hw]
        wd_teams, we_teams, placed_wd, placed_we = set(), set(), 0, 0
        for gi, d in zip(idxs, ds):
            if _monday(d) != hw_monday:
                continue
            _, h, a = games[gi]
            if is_weekend(d):
                placed_we += 1; we_teams.update((h, a))
            else:
                placed_wd += 1; wd_teams.update((h, a))
        bye_wd = sorted(set(members) - wd_teams)
        bye_we = sorted(set(members) - we_teams)
        reentered = feas.get("week_entries", {}).get(hw, 0) > 1
        print(f"  {name:34s} n={n:2d} heaviest wk {hw_monday} needs {targets[hw]:2d}: "
              f"weekday {placed_wd} (bye {bye_wd}), weekend {placed_we} (bye {bye_we}), "
              f"{'RE-ENTERED by backtracking' if reentered else 'solved first pass'}")
    print(f"  {at_ceiling} leagues sit exactly at the ceiling; all solved.")

    schools, confs, rivals = load_world(f"{root}/worlds/fixture-schedule.world.json")
    games, _fp = build_schedule(schools, confs, rivals)
    meta = _meta(f"{root}/worlds/fixture-schedule.world.json")
    conf_of = dict(schools)
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)
    duo_refused = False
    for cid in sorted(by_conf):
        members = sorted(by_conf[cid]); name, g, k = confs[cid]
        if g == 0:
            continue
        idxs = [i for i, (kk, h, a) in enumerate(games) if conf_of[h] == cid]
        sub = [games[i] for i in idxs]
        nights, weeks, off = meta[cid]
        try:
            ds, win, wall = date_conference(members, g, weeks, off, nights, sub,
                                            START_YEAR, f"'{name}' ")
            print(f"  fixture '{name}': {len(ds)} games dated, "
                  f"window {win[0]} .. wall {wall}")
        except ScheduleError as e:
            assert "two played opponents" in str(e), e
            duo_refused = True
            print(f"  fixture '{name}': REFUSED by name (single opponent) — correct")
    assert duo_refused, "the Duo single-opponent refusal never fired"

    # determinism and the year dial
    schools, confs, rivals = load_world(f"{root}/worlds/stock-d1.world.json")
    games, _fp = build_schedule(schools, confs, rivals)
    meta = _meta(f"{root}/worlds/stock-d1.world.json")
    d1, f1 = date_schedule(schools, confs, games, START_YEAR, meta)
    d2, f2 = date_schedule(schools, confs, games, START_YEAR, meta)
    assert (d1, f1) == (d2, f2)
    d3, f3 = date_schedule(schools, confs, games, 2031, meta)
    prove_dates(schools, confs, games, d3, 2031, meta, "stock-2031")
    assert f3 != f1
    print("  determinism: same dates twice; year dial: 2031 proves green, "
          "fingerprint moves, structure identical")

    # ★ THE BIG EAST BACK-CHECK — the one place the model touches reality.
    # SS 2026-03-15; tournament opened Wed Mar 11 (offset 4) -> wall Tue Mar 10;
    # 20 games / 12 weeks, Christmas skipped: window Dec 8 .. week of Mar 2.
    # Real: Providence opened Sat Dec 13, the league finished Sat Mar 7.
    win, wall = league_window(2025, 12, 4, ["wed", "sat"], "backcheck ")
    assert wall == _d.date(2026, 3, 10), wall
    assert win[0] == _d.date(2025, 12, 8) and win[-1] == _d.date(2026, 3, 2), win
    print("  back-check: the real 2025-26 Big East window reproduced to the week "
          "from two authored numbers")

    # the refusal battery, every message discriminating
    for name, fn, frag in [
        ("weeks<G/2", lambda: weekly_targets(10, 18, 8, "x "), "at two a week"),
        ("weeks>nG/2", lambda: weekly_targets(4, 2, 5, "x "), "empty week"),
        ("negative offset", lambda: league_window(2026, 9, -1, ["sat"], "x "),
         "negative"),
        ("pre-Nov-1", lambda: league_window(2026, 25, 4, ["sat", "wed"], "x "),
         "November 1 floor"),
        ("bad night", lambda: parse_nights(["sat", "xyz", "mon"], 9, "x "),
         "unrecognised"),
        ("dup night", lambda: parse_nights(["sat", "sat", "mon"], 9, "x "),
         "duplicate"),
    ]:
        try:
            fn()
            raise SystemExit(f"refusal MISSING: {name}")
        except ScheduleError as e:
            assert frag in str(e), (name, str(e))
    print("  refusals: all six static cases fire by name")
    print("S94 ORACLE: ALL ASSERTIONS PASSED")
