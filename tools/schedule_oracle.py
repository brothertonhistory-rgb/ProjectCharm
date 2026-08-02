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
