#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_pass3_replay_check.py  --  S69 replay round-trip proof for the two-plane budget fixture.

Reads the committed fixture (tools/gen_pass3_replay_fixture_s69.json) and replays every
recorded draws-row back through THE LOCKED ORACLE'S OWN generate_player, via the oracle's
own _ReplayR / _flat_draws (the draw-order contract's single home). This checker IMPORTS
the oracle's transforms and NEVER copies them -- the S69 ruling inverting the S42.2
from-scratch pattern: a copied second oracle can carry the same paste error as the fixture
generator, so the genuinely independent implementation is the C# port itself (Phase 69,
1e-9). What THIS check proves:

  1. constants tripwire -- the fixture's echo equals the live oracle module, itemized.
  2. round-trip -- recorded draws through the oracle reproduce every checkpoint for every
     player (ints EXACT; floats |diff| <= 1e-9, expected exactly 0 same-interpreter).
  3. the draw-order/count contract -- _ReplayR asserts each call's KIND in slot order and
     full consumption; a recorder bug that mis-slotted or dropped a draw dies loudly here.
  4. the inverse-CDF edge table -- every cumulative boundary at -eps / boundary / +eps
     through height_from_u; a '<' vs '<=' port mismatch cannot survive this.

Run:  python tools/gen_pass3_replay_check.py            (fixture read from beside this script)
      python tools/gen_pass3_replay_check.py <path>     (explicit fixture path)

Exit: 0 pass, 1 replay mismatch, 2 constants drift, 3 fixture missing/unreadable.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_pass3_budget_oracle as O   # the transforms of record -- called, never copied

TOL = 1e-9

FAILS = []
MAXDEV = [0.0]
CHECKS = [0]

def chk_int(idx, field, expected, got):
    CHECKS[0] += 1
    if expected != got:
        FAILS.append((idx, field, expected, got))

def chk_float(idx, field, expected, got):
    CHECKS[0] += 1
    d = abs(expected - got)
    if d > MAXDEV[0]:
        MAXDEV[0] = d
    if d > TOL:
        FAILS.append((idx, field, expected, got))

def chk_eq(idx, field, expected, got):
    CHECKS[0] += 1
    if expected != got:
        FAILS.append((idx, field, expected, got))

def check_constants(echo):
    """Every echoed constant must equal the live oracle module's value exactly."""
    drift = []
    for name, val in echo.items():
        if name == "HEIGHT_MARGINAL":
            live = {str(k): v for k, v in O.HEIGHT_MARGINAL.items()}
        elif name == "FAMILIES":
            live = {f: list(ks) for f, ks in O.FAMILIES.items()}
        elif name == "FAMILY_ORDER":
            live = list(O.FAMILIES.keys())
        elif name == "WS_NOISE_MEAN":
            live = 4.0     # inline at the oracle's draw site (generate_player)
        elif name == "WS_NOISE_SIGMA":
            live = 3.0
        elif name == "WEIGHT_NOISE_SIGMA":
            live = 6.0
        else:
            live = getattr(O, name)
        if isinstance(live, tuple):
            live = list(live)
        if live != val:
            drift.append((name, val, live))
    return drift

def replay_player(row):
    idx = row["index"]
    cp = row["checkpoints"]
    rr = O._ReplayR(O._flat_draws(row["draws"]))
    p = O.generate_player(rr)               # the oracle's own generator; no local math
    chk_eq(idx, "draws fully consumed", True, rr.fully_consumed())

    chk_int(idx, "Height", cp["Height"], p["Height"])
    chk_int(idx, "Wingspan", cp["Wingspan"], p["Wingspan"])
    chk_int(idx, "Weight", cp["Weight"], p["Weight"])
    for k in O.ATH_KEYS:
        chk_int(idx, "ath.%s" % k, cp["ath"][k], p["ath"][k])
    chk_float(idx, "dplane", cp["dplane"], p["dplane"])
    chk_eq(idx, "dcat", cp["dcat"], p["dcat"])
    chk_eq(idx, "role", cp["role"], p["role"])
    chk_float(idx, "budget", cp["budget"], p["budget"])
    chk_float(idx, "gamma", cp["gamma"], p["gamma"])
    for f in O.FAMILIES:
        chk_float(idx, "pulls.%s" % f, cp["pulls"][f], p["pulls"][f])
        chk_float(idx, "fam_share.%s" % f, cp["fam_share"][f], p["fam_share"][f])
    for k in O.SPEND_SKILLS:
        chk_float(idx, "spend.%s" % k, cp["spend"][k], p["spend"][k])
        chk_float(idx, "caps.%s" % k, cp["caps"][k], p["caps"][k])
        chk_int(idx, "latent.%s" % k, cp["latent"][k], p["latent"][k])
        chk_int(idx, "current.%s" % k, cp["current"][k], p["current"][k])
    chk_int(idx, "latent_ft", cp["latent_ft"], p["latent_ft"])
    chk_int(idx, "current_ft", cp["current_ft"], p["current_ft"])
    chk_float(idx, "arrival", cp["arrival"], p["arrival"])
    chk_float(idx, "e", cp["e"], p["e"])
    chk_int(idx, "runway_total", cp["runway_total"], p["runway_total"])
    parts = O.rscore_parts(p)
    chk_float(idx, "rscore", cp["rscore"], parts["rscore"])
    chk_eq(idx, "rscore_which", cp["rscore_which"], parts["which"])

def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "gen_pass3_replay_fixture_s69.json")
    try:
        with open(path) as f:
            fx = json.load(f)
    except Exception as ex:
        print("REPLAY CHECK: cannot read fixture at %s (%s)" % (path, ex))
        sys.exit(3)

    schema, players, edges = fx["schema"], fx["players"], fx["edge_table"]
    print("=" * 92)
    print("S69 REPLAY ROUND-TRIP CHECK -- fixture %s (schema %s, seed %d, %d players, %d edge rows)"
          % (os.path.basename(path), schema["schema_version"], schema["seed"], len(players), len(edges)))
    print("  recorded draws replayed through the LOCKED ORACLE'S OWN transforms (imported, not copied).")
    print("=" * 92)

    drift = check_constants(schema["constants"])
    if drift:
        print("CONSTANTS TRIPWIRE: %d mismatch(es) between the fixture echo and the oracle module --" % len(drift))
        for name, echoed, live in drift:
            print("    %-24s fixture echo=%r   oracle=%r" % (name, echoed, live))
        print("VERDICT: FAIL (constants drift; replay not run)")
        sys.exit(2)
    print("constants echo vs oracle module: %d/%d match -- tripwire clear" %
          (len(schema["constants"]), len(schema["constants"])))

    for row in players:
        replay_player(row)

    edge_fails = 0
    for row in edges:
        CHECKS[0] += 1
        got = O.height_from_u(row["u"])
        if got != row["expected_height"]:
            edge_fails += 1
            FAILS.append(("edge", "height_from_u(%r)" % row["u"], row["expected_height"], got))
    print("inverse-CDF edge table: %d boundary probes, %d failures" % (len(edges), edge_fails))

    print("players replayed: %d   field checks: %d   failures: %d" % (len(players), CHECKS[0], len(FAILS)))
    print("max float deviation observed: %.3e   (contract tolerance %.0e; same-interpreter replay "
          "is expected to be exactly 0)" % (MAXDEV[0], TOL))
    if FAILS:
        print("FIRST %d FAILURES:" % min(20, len(FAILS)))
        for idx, field, expected, got in FAILS[:20]:
            print("    player %-6s %-28s expected=%r  got=%r" % (idx, field, expected, got))
        print("VERDICT: FAIL")
        sys.exit(1)
    print("VERDICT: PASS -- every checkpoint reproduced for every player from recorded draws alone,")
    print("draw-order/count contract held, every inverse-CDF boundary lands in its ruled bin.")
    sys.exit(0)

if __name__ == "__main__":
    main()
