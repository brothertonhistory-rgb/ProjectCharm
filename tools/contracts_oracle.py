#!/usr/bin/env python3
"""S103 — the contract window state machine. THIS DOCSTRING IS THE SPECIFICATION;
the C# in Program.Season.Contracts.cs is its port, and Phase 94 C1 replays the
golden this file emits through the C# pure core, trajectory for trajectory.

THE STATE. A live contract is (gamesRemaining, windowRemaining), both positive
integers, with gamesRemaining <= windowRemaining always (a shape where games
exceed window is refused at authoring and at load — it is not a state).
gamesRemaining is DERIVED from the outstanding-leg count and never stored;
windowRemaining is the one stored counter.

THE SEASON, in order (Emmett's rulings, brief r3 §2 R22a):
  1. The window INCLUDES the current season. The decision is taken against the
     state as loaded — the decrement has NOT happened yet.
  2. FORCED iff gamesRemaining == windowRemaining. Forced contracts always
     exercise. Otherwise the executor consults its policy and may exercise one
     leg or decline freely.
  3. Exercising completes exactly one leg: gamesRemaining -= 1.
  4. If gamesRemaining == 0 the contract is COMPLETE and is omitted from the
     next season's record — it is never written forward.
  5. ROLLOVER: every surviving contract's window decrements by exactly one:
     windowRemaining -= 1. The decrement happens after the decision, never
     before it (the alternate convention forces season one of a three-in-four
     and silently deletes a year of flexibility).

WHAT THE MACHINE GUARANTEES (each proved below over the full grid, not assumed):
  A. Every contract completes inside its window under EVERY policy, because a
     declined season moves the state one step closer to the forced diagonal
     (g stays, w falls) and the diagonal always exercises.
  B. Forcing depends on the LIVE RATIO, never a hard-coded contract year: an
     early exercise pushes the first forced season LATER (five-in-eight
     declining forces in season 4; exercise once in season 1 and it forces in
     season 5).
  C. No surviving contract is ever written forward with windowRemaining < 1 —
     a window of zero or less on disk is damage, not a state.
  D. Completion does NOT discriminate this convention from decrement-first for
     contracts with slack, but the state table does — and an EXACT-window
     contract (games == window at authoring, e.g. a home-and-home in two years)
     discriminates by feasibility alone: decrement-first makes it impossible.

THE GOLDEN. contracts_golden.json carries named trajectories (the prompt §5
tables) plus a full grid over games 1..5, window games..8, under three
policies: decline-throughout, exercise-season-1-only, exercise-always. Each
season row records the loaded state, whether it was forced, the decision, the
state after exercise, and the state written forward (or "complete"). All
integers — there is no float anywhere in this machine, so cross-language parity
is exact by construction, not ULP-bounded.
"""

import hashlib
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GOLDEN = os.path.join(HERE, "contracts_golden.json")

# ── the machine ──────────────────────────────────────────────────────────────


def run_contract(games, window, wants_exercise, decrement_first=False, cap=64):
    """Walk one contract to completion. wants_exercise(season)->bool is the
    optional policy; forced seasons ignore it. Returns (rows, verdict) where
    verdict is 'complete' or 'INFEASIBLE' (reachable only under the wrong,
    decrement-first convention)."""
    rows = []
    g, w = games, window
    for season in range(1, cap + 1):
        if decrement_first:
            w -= 1
            if w < g:
                # the state the wrong convention manufactures: more games owed
                # than seasons left before any decision could be taken
                return rows, "INFEASIBLE"
        forced = g == w
        do = forced or wants_exercise(season)
        g_after = g - 1 if do else g
        row = {
            "season": season,
            "startGames": g,
            "startWindow": w,
            "forced": forced,
            "decision": "forced" if forced else ("exercise" if do else "decline"),
            "afterGames": g_after,
            "afterWindow": w,
        }
        if g_after == 0:
            row["rollGames"] = 0
            row["rollWindow"] = None  # complete — never written forward
            rows.append(row)
            return rows, "complete"
        w_next = w if decrement_first else w - 1
        row["rollGames"] = g_after
        row["rollWindow"] = w_next
        rows.append(row)
        g, w = g_after, w_next
    return rows, "ran-out"


POLICIES = {
    "decline": lambda season: False,
    "exercise-season-1": lambda season: season == 1,
    "always": lambda season: True,
}


def first_forced(rows):
    for r in rows:
        if r["forced"]:
            return r["season"]
    return None


# ── self-checks: the oracle proves itself before it emits ────────────────────


def self_check():
    failures = []

    def check(name, ok, detail=""):
        print(f"  [{'OK' if ok else 'FAIL'}] {name}" + (f" — {detail}" if detail else ""))
        if not ok:
            failures.append(name)

    # A: completion under every policy, whole grid, correct convention.
    incomplete = 0
    for g0 in range(1, 6):
        for w0 in range(g0, 9):
            for pol in POLICIES.values():
                rows, verdict = run_contract(g0, w0, pol)
                if verdict != "complete":
                    incomplete += 1
    check("A: every (games,window,policy) on the grid completes inside its window",
          incomplete == 0, f"{incomplete} incomplete")

    # A': ...and completes in exactly `window` seasons or fewer, never more.
    over = 0
    for g0 in range(1, 6):
        for w0 in range(g0, 9):
            for pol in POLICIES.values():
                rows, _ = run_contract(g0, w0, pol)
                if rows[-1]["season"] > w0:
                    over += 1
    check("A': no contract takes more seasons than its authored window", over == 0)

    # B: forcing follows the live ratio — the prompt's two five-in-eight paths.
    rows_d, _ = run_contract(5, 8, POLICIES["decline"])
    rows_e, _ = run_contract(5, 8, POLICIES["exercise-season-1"])
    check("B: five-in-eight declining forces in season 4", first_forced(rows_d) == 4,
          f"season {first_forced(rows_d)}")
    check("B: five-in-eight exercising once in season 1 forces in season 5",
          first_forced(rows_e) == 5, f"season {first_forced(rows_e)}")

    # The prompt §5 three-in-four table, row for row (decline s1, then forced).
    rows_t, _ = run_contract(3, 4, POLICIES["decline"])
    expect = [(3, 4, "decline", 3, 3), (3, 3, "forced", 2, 2),
              (2, 2, "forced", 1, 1), (1, 1, "forced", 0, None)]
    got = [(r["startGames"], r["startWindow"], r["decision"], r["rollGames"], r["rollWindow"])
           for r in rows_t]
    check("B': the three-in-four table reproduces row for row", got == expect, str(got))

    # C: no survivor is ever written forward below window 1.
    bad = 0
    for g0 in range(1, 6):
        for w0 in range(g0, 9):
            for pol in POLICIES.values():
                rows, _ = run_contract(g0, w0, pol)
                bad += sum(1 for r in rows if r["rollWindow"] is not None and r["rollWindow"] < 1)
    check("C: no surviving contract is written forward with window < 1", bad == 0)

    # D: the wrong convention. Completion fails to discriminate where slack
    # exists (both complete), but the state tables differ everywhere, and an
    # exact-window contract is INFEASIBLE under decrement-first.
    slack_diverge = 0
    slack_both_complete = 0
    for g0 in range(1, 6):
        for w0 in range(g0 + 1, 9):  # slack only
            r_a, v_a = run_contract(g0, w0, POLICIES["decline"])
            r_b, v_b = run_contract(g0, w0, POLICIES["decline"], decrement_first=True)
            if v_a == v_b == "complete":
                slack_both_complete += 1
            if first_forced(r_a) != first_forced(r_b):
                slack_diverge += 1
    check("D: with slack, BOTH conventions complete (completion proves nothing)",
          slack_both_complete == sum(1 for g0 in range(1, 6) for w0 in range(g0 + 1, 9)))
    check("D: ...but the first forced season differs in EVERY slack case (the table discriminates)",
          slack_diverge == sum(1 for g0 in range(1, 6) for w0 in range(g0 + 1, 9)))
    exact_bad = sum(1 for g0 in range(1, 6)
                    if run_contract(g0, g0, POLICIES["decline"], decrement_first=True)[1]
                    != "complete")
    check("D: every exact-window contract is INFEASIBLE under decrement-first "
          "(the equal-host fixture is a free discriminator)", exact_bad == 5)

    # E: determinism — a second full pass reproduces the first byte for byte.
    check("E: a second run reproduces the first exactly",
          emit_payload() == emit_payload())

    return failures


# ── the golden ───────────────────────────────────────────────────────────────


def emit_payload():
    named = [
        ("three-in-four-decline", 3, 4, "decline"),
        ("three-in-four-exercise-s1", 3, 4, "exercise-season-1"),
        ("five-in-eight-decline", 5, 8, "decline"),
        ("five-in-eight-exercise-s1", 5, 8, "exercise-season-1"),
        ("home-and-home", 2, 2, "decline"),
        ("two-for-one-in-three", 3, 3, "decline"),
        ("neutral-series-4-in-6", 4, 6, "always"),
    ]
    trajectories = []
    for name, g, w, pol in named:
        rows, verdict = run_contract(g, w, POLICIES[pol])
        trajectories.append({"name": name, "games": g, "window": w, "policy": pol,
                             "verdict": verdict, "seasons": rows})
    for g in range(1, 6):
        for w in range(g, 9):
            for pol in sorted(POLICIES):
                rows, verdict = run_contract(g, w, POLICIES[pol])
                trajectories.append({"name": f"grid-{g}in{w}-{pol}", "games": g,
                                     "window": w, "policy": pol, "verdict": verdict,
                                     "seasons": rows})
    return {
        "provenance": {
            "session": "S103",
            "spec": "tools/contracts_oracle.py — the docstring is the specification",
            "convention": "window includes the current season; forced iff games==window; "
                          "decrement at rollover, after the decision",
        },
        "trajectories": trajectories,
    }


def main():
    print("== contracts_oracle — the window state machine proves itself ==")
    failures = self_check()
    if failures:
        print(f"\nSELF-CHECK FAILED ({len(failures)}): {failures}")
        return 1
    payload = emit_payload()
    text = json.dumps(payload, indent=2) + "\n"
    with open(GOLDEN, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    digest = hashlib.sha256(text.encode("utf-8")).hexdigest()
    n = len(payload["trajectories"])
    print(f"\nwrote {GOLDEN}: {n} trajectories, sha256 {digest[:16]}…")
    return 0


if __name__ == "__main__":
    sys.exit(main())
