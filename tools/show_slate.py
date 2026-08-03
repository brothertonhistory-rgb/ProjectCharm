#!/usr/bin/env python3
"""Project Charm — S94 slate viewer. READ-ONLY: imports the locked oracle, dates the
world, and prints a league's season week by week. Changes nothing, asserts nothing.

    python tools\\show_slate.py <league name fragment> [more fragments...]
    python tools\\show_slate.py --team <school id>
"""
import sys, json, datetime as dt, importlib.util
spec = importlib.util.spec_from_file_location("schedule_oracle", "tools/schedule_oracle.py")
so = importlib.util.module_from_spec(spec)
spec.loader.exec_module(so)              # not __main__, so the oracle's self-check stays quiet

START_YEAR = 2026
w = json.load(open("worlds/stock-d1.world.json"))
names = {s["id"]: s["name"] for s in w["schools"]}
meta = {c["id"]: (c["nights"], c["weeks"], c["tourneyOffsetDays"]) for c in w["conferences"]}
schools, confs, rivals = so.load_world("worlds/stock-d1.world.json")
games, _ = so.build_schedule(schools, confs, rivals)
dates, _ = so.date_schedule(schools, confs, games, START_YEAR, meta)
conf_of = dict(schools)

def show_league(cid):
    name, g, k = confs[cid]
    idxs = [i for i in range(len(games)) if conf_of[games[i][1]] == cid]
    if not idxs:
        print(f"{name}: plays no games"); return
    by_week = {}
    for i in idxs:
        d = dates[i]
        by_week.setdefault(d - dt.timedelta(days=d.weekday()), []).append(i)
    n = len({s for s, c in schools if c == cid})
    print(f"\n═══ {name} — {n} teams, {g} games each, "
          f"{len(by_week)} playing weeks ═══")
    for wk in sorted(by_week):
        rows = sorted(by_week[wk], key=lambda i: (dates[i], i))
        print(f"  Week of {wk:%b %d}  ({len(rows)} games)")
        for i in rows:
            _, h, a = games[i]
            print(f"    {dates[i]:%a %b %d}   {names[a][:24]:<24} at {names[h][:24]}")

def show_team(sid):
    mine = [(dates[i], i) for i in range(len(games))
            if games[i][1] == sid or games[i][2] == sid]
    print(f"\n═══ {names[sid]} — {len(mine)} conference games ═══")
    for d, i in sorted(mine):
        _, h, a = games[i]
        if h == sid: print(f"  {d:%a %b %d}   vs {names[a]}")
        else:        print(f"  {d:%a %b %d}   at {names[h]}")

args = sys.argv[1:]
if args and args[0] == "--team":
    show_team(int(args[1]))
elif args:
    for frag in args:
        for cid in sorted(confs):
            if frag.lower() in confs[cid][0].lower():
                show_league(cid)
else:
    print("name a league, e.g.:  python tools\\show_slate.py \"Atlantic Sun\" ACC")
