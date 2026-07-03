#!/usr/bin/env python3
"""
Project Charm — Session 30 schedule-builder oracle.

Mirrors the C# season schedule builder bit-for-bit. The contract (locked here,
the C# is written against THIS file):

RNG: WorldRng = SplitMix64 (identical to Program.World.cs). The schedule stream
is WorldRng(seasonSeed ^ 0x5EA5C4ED) — its own stream, decoupled from the divvy
(the committed sample-sheet XOR pattern). NextInt(n) = int(NextDouble() * n).
Consumption order: (1) one Fisher-Yates shuffle of ring positions, n-1 draws,
i = n-1 down to 1, j = NextInt(i+1); (2) one draw per ACTUAL conflict repair
(the scan start offset) — stale queue entries consume nothing. Conference
slates and orientation consume NO randomness.

CONFERENCE SLATES (no RNG): conferences by id ascending, members by school id
ascending indexed 0..s-1. base = 16 // (s-1); r = 16 - base*(s-1). The extra-
meeting graph is the canonical circulant on member indices: r even -> offsets
1..r/2; r odd (s even, guaranteed by parity) -> offsets 1..(r-1)/2 plus the
diameter matching (i, i+s/2). Emission order: for i in 0..s-2, for j in
i+1..s-1, emit (base + extra(i,j)) consecutive games (id_i, id_j).

NON-CONFERENCE (RNG): a 14-regular SIMPLE graph, no conference-mates.
Construction: shuffle school indices into a ring; edges ring[i]—ring[(i+k)%n]
for i in 0..n-1, k in 1..7 (insertion order = the canonical edge-list order);
collect conference-mate conflicts in scan order (FIFO); repair each live
conflict (a,b) by a double-edge swap: start = NextInt(edgeCount), scan forward,
skip candidates sharing an endpoint, try rewiring R1 (a,c)+(b,d) then R2
(a,d)+(b,c); a rewiring is legal iff both new pairs are non-mates and absent
from the graph. Apply the first legal one: slot of (a,b) <- first new edge,
slot of (c,d) <- second; adjacency updated. If some conflict finds no legal
swap across a full scan, the ATTEMPT fails: construction restarts with a fresh
shuffle drawn from the same continuing RNG stream (nothing is reseeded), up to
20 attempts total; 20 failed attempts -> fail loudly naming the last stuck
pairing. Edges stored (loId, hiId).

ORIENTATION (no RNG): the full multigraph (conf block then nonconf block,
game index ascending) has every degree 30 (even). Per component (components by
lowest school id, schools scanned id-ascending), run iterative Hierholzer with
per-vertex adjacency in game-index order and a per-vertex pointer; each edge is
oriented in its consumption direction (from = HOME). A closed Eulerian circuit
gives out = in = 15 at every vertex: exactly 15 home / 15 away.

FINGERPRINT: one record per game in schedule order (never re-sorted):
"{gameIndex}|{kind}|{homeSchoolId}|{awaySchoolId}\n", kind in {conf, nonconf},
UTF-8, SHA-256, lowercase hex.

ENGINE SEEDS (no RNG; asserted unique in Phase 55): base =
int32(seasonSeed) two's-complement truncation (the smoke sim's
unchecked((int)seed) pattern); resolver = base + 2*gameIndex, governor =
base + 2*gameIndex + 1, int32 wraparound.
"""

import hashlib
import json
import sys

MASK64 = (1 << 64) - 1
SCHED_XOR = 0x5EA5C4ED


class WorldRng:
    """SplitMix64, mirroring Program.World.cs bit-for-bit."""

    def __init__(self, seed):
        self.state = seed & MASK64

    def next_u64(self):
        self.state = (self.state + 0x9E3779B97F4A7C15) & MASK64
        z = self.state
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK64
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK64
        return z ^ (z >> 31)

    def next_double(self):
        return (self.next_u64() >> 11) * (1.0 / (1 << 53))

    def next_int(self, n):
        return int(self.next_double() * n)


def int32(x):
    x &= 0xFFFFFFFF
    return x - (1 << 32) if x >= (1 << 31) else x


class ScheduleError(Exception):
    pass


class AttemptFailed(Exception):
    pass


def preflight(schools, conf_names):
    """schools: list of (id, confId) sorted by id. Raises ScheduleError naming
    the school/conference and the unmet requirement."""
    n = len(schools)
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)
    for cid in sorted(by_conf):
        s = len(by_conf[cid])
        name = conf_names.get(cid, f"conference {cid}")
        if s < 2:
            raise ScheduleError(
                f"SEASON PREFLIGHT INFEASIBLE: conference '{name}' (id {cid}) has "
                f"{s} school(s) — a 16-game conference slate needs an opponent (s-1 = 0).")
        base = 16 // (s - 1)
        r = 16 - base * (s - 1)
        if (r * s) % 2 == 1:
            raise ScheduleError(
                f"SEASON PREFLIGHT INFEASIBLE: conference '{name}' (id {cid}, size {s}) — "
                f"extra-meeting condition violated (r={r}, r*s odd; no r-regular graph exists).")
    for sid, cid in schools:
        eligible = n - len(by_conf[cid])
        if eligible < 14:
            raise ScheduleError(
                f"SEASON PREFLIGHT INFEASIBLE: school id {sid} (conference id {cid}) has only "
                f"{eligible} eligible non-conference opponents — 14 required.")
    if (n * 14) % 2 == 1:
        raise ScheduleError(
            f"SEASON PREFLIGHT INFEASIBLE: total non-conference degree {n}*14 is odd.")
    return by_conf


def conference_games(by_conf):
    """Deterministic, no RNG. Returns list of (loId, hiId) in schedule order."""
    games = []
    for cid in sorted(by_conf):
        members = sorted(by_conf[cid])
        s = len(members)
        base = 16 // (s - 1)
        r = 16 - base * (s - 1)
        extra = set()
        if r > 0:
            if r % 2 == 0:
                offsets, diameter = range(1, r // 2 + 1), False
            else:
                offsets, diameter = range(1, (r - 1) // 2 + 1), True
            for i in range(s):
                for k in offsets:
                    j = (i + k) % s
                    extra.add((min(i, j), max(i, j)))
                if diameter and i < s // 2:
                    extra.add((i, i + s // 2))
        for i in range(s - 1):
            for j in range(i + 1, s):
                m = base + (1 if (i, j) in extra else 0)
                for _ in range(m):
                    games.append((members[i], members[j]))
    return games


def nonconference_edges(schools, rng, seed_label=""):
    """schools: list of (id, confId) sorted by id. Returns edge list of
    (loId, hiId) in canonical (construction + in-place swap) order."""
    n = len(schools)
    ids = [s[0] for s in schools]
    conf = {sid: cid for sid, cid in schools}

    ring = list(range(n))
    for i in range(n - 1, 0, -1):
        j = rng.next_int(i + 1)
        ring[i], ring[j] = ring[j], ring[i]

    def norm(a, b):
        return (a, b) if a < b else (b, a)

    edges = []
    for i in range(n):
        for k in range(1, 8):
            u = ids[ring[i]]
            v = ids[ring[(i + k) % n]]
            edges.append(norm(u, v))
    adj = set(edges)
    if len(adj) != len(edges):
        raise ScheduleError("SEASON SCHEDULE BUG: circulant produced a duplicate edge.")
    index_of = {e: i for i, e in enumerate(edges)}

    queue = [e for e in edges if conf[e[0]] == conf[e[1]]]

    for ab in queue:
        if ab not in adj:
            continue  # rewired away by an earlier repair; no RNG consumed
        a, b = ab
        off = rng.next_int(len(edges))
        repaired = False
        for m in range(len(edges)):
            cd = edges[(off + m) % len(edges)]
            c, d = cd
            if c == a or c == b or d == a or d == b:
                continue
            for (n1, n2) in (((a, c), (b, d)), ((a, d), (b, c))):
                p1, p2 = norm(*n1), norm(*n2)
                if conf[n1[0]] == conf[n1[1]] or conf[n2[0]] == conf[n2[1]]:
                    continue
                if p1 in adj or p2 in adj:
                    continue
                i_ab, i_cd = index_of[ab], index_of[cd]
                adj.discard(ab); adj.discard(cd)
                adj.add(p1); adj.add(p2)
                edges[i_ab], edges[i_cd] = p1, p2
                del index_of[ab]; del index_of[cd]
                index_of[p1], index_of[p2] = i_ab, i_cd
                repaired = True
                break
            if repaired:
                break
        if not repaired:
            raise AttemptFailed(
                f"non-conference repair found no legal swap for the "
                f"conference-mate pairing school {a} vs school {b}")
    return edges


MAX_ATTEMPTS = 20


def nonconference_slate(schools, rng, seed_label=""):
    """Up to MAX_ATTEMPTS construction attempts on the one continuing RNG
    stream; the first attempt that completes wins."""
    last = None
    for _ in range(MAX_ATTEMPTS):
        try:
            return nonconference_edges(schools, rng, seed_label)
        except AttemptFailed as e:
            last = e
    raise ScheduleError(
        f"SEASON SCHEDULE BUILD FAILED{seed_label}: {MAX_ATTEMPTS} construction "
        f"attempts exhausted; last failure: {last}.")


def orient(all_games, school_ids):
    """all_games: list of (x, y) in schedule order. Returns list of
    (home, away) in the same order. Hierholzer per component; each edge is
    oriented in its consumption direction (from = home)."""
    adj = {sid: [] for sid in school_ids}
    for g, (x, y) in enumerate(all_games):
        adj[x].append((y, g))
        adj[y].append((x, g))
    used = [False] * len(all_games)
    home = [None] * len(all_games)
    ptr = {sid: 0 for sid in school_ids}
    visited = set()
    for start in school_ids:  # id ascending
        if start in visited or not adj[start]:
            continue
        stack = [start]
        while stack:
            v = stack[-1]
            visited.add(v)
            a = adj[v]
            while ptr[v] < len(a) and used[a[ptr[v]][1]]:
                ptr[v] += 1
            if ptr[v] == len(a):
                stack.pop()
            else:
                w, g = a[ptr[v]]
                used[g] = True
                home[g] = v
                stack.append(w)
    return [(h, x if h == y else y) for (x, y), h in zip(all_games, home)]


def build_schedule(schools, conf_names, season_seed):
    """schools: list of (id, confId) sorted by id. Returns
    (games, fingerprint_hex) where games is a list of (kind, home, away)."""
    by_conf = preflight(schools, conf_names)
    rng = WorldRng((season_seed ^ SCHED_XOR) & MASK64)
    conf_g = conference_games(by_conf)
    nonconf_g = nonconference_slate(schools, rng, f" at seed {season_seed}")
    all_undirected = conf_g + nonconf_g
    oriented = orient(all_undirected, [s[0] for s in schools])
    kinds = ["conf"] * len(conf_g) + ["nonconf"] * len(nonconf_g)
    games = [(k, h, a) for k, (h, a) in zip(kinds, oriented)]
    payload = "".join(f"{i}|{k}|{h}|{a}\n" for i, (k, h, a) in enumerate(games))
    fp = hashlib.sha256(payload.encode("utf-8")).hexdigest()
    return games, fp


# ─── Legality proof (the invariants Phase 55 asserts at stock) ───────────────

def prove(schools, games, tag):
    n = len(schools)
    conf = {sid: cid for sid, cid in schools}
    by_conf = {}
    for sid, cid in schools:
        by_conf.setdefault(cid, []).append(sid)

    total = {sid: 0 for sid, _ in schools}
    confN = {sid: 0 for sid, _ in schools}
    nonconfN = {sid: 0 for sid, _ in schools}
    homeN = {sid: 0 for sid, _ in schools}
    pair_conf = {}
    pair_nonconf = {}
    for k, h, a in games:
        assert h != a, f"{tag}: self-game {h}"
        total[h] += 1; total[a] += 1; homeN[h] += 1
        key = (min(h, a), max(h, a))
        if k == "conf":
            confN[h] += 1; confN[a] += 1
            assert conf[h] == conf[a], f"{tag}: conf game across conferences {key}"
            pair_conf[key] = pair_conf.get(key, 0) + 1
        else:
            nonconfN[h] += 1; nonconfN[a] += 1
            assert conf[h] != conf[a], f"{tag}: nonconf game between mates {key}"
            pair_nonconf[key] = pair_nonconf.get(key, 0) + 1

    assert all(total[s] == 30 for s in total), f"{tag}: not every team 30 games"
    assert all(confN[s] == 16 for s in confN), f"{tag}: not every team 16 conf"
    assert all(nonconfN[s] == 14 for s in nonconfN), f"{tag}: not every team 14 nonconf"
    assert all(homeN[s] == 15 for s in homeN), f"{tag}: home not exactly 15"
    assert all(c == 1 for c in pair_nonconf.values()), f"{tag}: nonconf pair met twice"
    assert len(games) == n * 30 // 2, f"{tag}: total games {len(games)}"

    for cid, members in by_conf.items():
        s = len(members)
        base = 16 // (s - 1)
        for i, x in enumerate(members):
            for y in members[i + 1:]:
                c = pair_conf.get((min(x, y), max(x, y)), 0)
                assert c in (base, base + 1), \
                    f"{tag}: conf pair ({x},{y}) meets {c}, expected {base} or {base + 1}"


def load_world(path):
    w = json.load(open(path))
    schools = sorted((s["id"], s["conferenceId"]) for s in w["schools"])
    conf_names = {c["id"]: c["name"] for c in w["conferences"]}
    return schools, conf_names


if __name__ == "__main__":
    # Run from the repo root: python tools/schedule_oracle.py [stock-seed-count]
    root = sys.argv[2] if len(sys.argv) > 2 else "."
    stock, stock_names = load_world(f"{root}/worlds/stock-d1.world.json")
    tiny, tiny_names = load_world(f"{root}/worlds/fixture-tiny.world.json")

    FIXED = 20260703

    # Acceptance sweep: many seeds, both scales, every invariant.
    n_stock, n_tiny = int(sys.argv[1]) if len(sys.argv) > 1 else 40, 200
    for s in range(FIXED, FIXED + n_stock):
        g, _ = build_schedule(stock, stock_names, s)
        prove(stock, g, f"stock seed {s}")
    print(f"stock: {n_stock} seeds green ({len(g)} games each)")
    for s in range(FIXED, FIXED + n_tiny):
        g, _ = build_schedule(tiny, tiny_names, s)
        prove(tiny, g, f"tiny seed {s}")
    print(f"fixture: {n_tiny} seeds green ({len(g)} games each)")

    # Determinism at the fixed seed.
    g1, fp1 = build_schedule(stock, stock_names, FIXED)
    g2, fp2 = build_schedule(stock, stock_names, FIXED)
    assert g1 == g2 and fp1 == fp2
    t1, tfp1 = build_schedule(tiny, tiny_names, FIXED)
    t2, tfp2 = build_schedule(tiny, tiny_names, FIXED)
    assert t1 == t2 and tfp1 == tfp2
    _, fp_diff = build_schedule(stock, stock_names, FIXED + 1)
    assert fp_diff != fp1
    print("determinism: same seed identical, different seed different")

    # Preflight rejection: a rigged one-school conference.
    rigged = list(tiny)
    rigged[0] = (rigged[0][0], 999)  # school 1 alone in conference 999
    try:
        build_schedule(rigged, {**tiny_names, 999: "Lonely"}, FIXED)
        raise SystemExit("rigged world was NOT rejected")
    except ScheduleError as e:
        assert "Lonely" in str(e) and "s-1 = 0" in str(e)
        print(f"preflight rejection: {e}")

    # Exports for Phase 55.
    print()
    print(f"FIXED SEED {FIXED} EXPORTS:")
    print(f"  stock fingerprint:   {fp1}")
    print(f"  stock games total:   {len(g1)}")
    conf_counts = {}
    conf_of = dict(stock)
    for k, h, a in g1:
        if k == "conf":
            conf_counts[conf_of[h]] = conf_counts.get(conf_of[h], 0) + 1
    print(f"  stock conf-game counts by conference id: {dict(sorted(conf_counts.items()))}")
    print(f"  fixture fingerprint: {tfp1}")
    print(f"  fixture games total: {len(t1)}")
    print(f"  fixture game 0:      {t1[0]}")
    print(f"  stock game 0:        {g1[0]}")
    # RNG-consumption pulse: conflicts repaired at the fixed seeds.
