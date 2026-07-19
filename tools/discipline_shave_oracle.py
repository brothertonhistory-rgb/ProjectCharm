#!/usr/bin/env python3
"""
S61 — DISCIPLINE make-% shave (Effect A): LOCKED oracle + golden emitter.

Discipline is a small, absolute, per-man defensive-restraint rating. Effect A lowers the
man's make% by a small RELATIVE amount, read off the DEFENDER's OWN Discipline (no shooter
term -> absolute), MULTIPLICATIVE so the proportional reduction is FLAT across every zone.

Locked form (mirrors RollHGenerator.ApplyDisciplineShave, ported constant-for-constant):
    progress = clamp((defenderDiscipline - 50) / 49, -1, +1)   # symmetric about the midpoint
    shave    = DisciplineMakeShaveScale * progress
    makePct  = clamp01(makePct * (1 - shave))

SYMMETRIC about 50 (Emmett's ruling, S61): D=99 shaves, D=50 neutral, D=0 (liability) yields
a cleaner look. Kill switch: scale=0 -> identity (no arithmetic). Null defender -> no shave
(caller-guarded). Magnitude is a CALIBRATION PLACEHOLDER; the golden pins the exact constant.

The C# build reads DisciplineMakeShaveScale from RollHConfig; the golden pins this exact
value and the harness cross-checks it against the loaded config before trusting parity.
"""
import json, math, os

MID = 50.0
SPAN = 49.0
DISCIPLINE_MAKE_SHAVE_SCALE = 0.015   # RollHConfig default / config.json — pinned by the golden


def progress(discipline):
    return max(-1.0, min(1.0, (discipline - MID) / SPAN))


def shave(discipline, scale=DISCIPLINE_MAKE_SHAVE_SCALE):
    if scale <= 0.0:
        return 0.0
    return scale * progress(discipline)


def apply(make_pct, discipline, scale=DISCIPLINE_MAKE_SHAVE_SCALE):
    if scale <= 0.0:
        return make_pct                       # true identity branch — no arithmetic
    raw = make_pct * (1.0 - shave(discipline, scale))
    return max(0.0, min(1.0, raw))


ZONES = [("Rim", 0.600), ("Short", 0.450), ("Mid", 0.400), ("Long", 0.380), ("Three", 0.340)]


def rel_pct(before, after):
    return (after / before - 1.0) * 100.0 if before > 0 else 0.0


def print_table():
    print(f"DISCIPLINE MAKE-% SHAVE (S61, Effect A) — SYMMETRIC, scale={DISCIPLINE_MAKE_SHAVE_SCALE}")
    print("Defender: 99 = lockdown, 50 = average (neutral), 0 = liability\n")
    print(f"{'Zone':<7}{'base':>8}{'D=99':>9}{'D=50':>9}{'D=0':>9}{'relD99':>9}{'relD0':>8}")
    rels99, rels0 = [], []
    for z, base in ZONES:
        a99, a50, a0 = apply(base, 99), apply(base, 50), apply(base, 0)
        rels99.append(rel_pct(base, a99)); rels0.append(rel_pct(base, a0))
        print(f"{z:<7}{base*100:>7.1f}%{a99*100:>8.1f}%{a50*100:>8.1f}%{a0*100:>8.1f}%"
              f"{rel_pct(base,a99):>8.2f}%{rel_pct(base,a0):>7.2f}%")
    print(f"\nFLAT-ACROSS-ZONES: relD@99 span={max(rels99)-min(rels99):.2e}%  "
          f"relD@0 span={max(rels0)-min(rels0):.2e}%  (both ~0 => constant proportional reduction)")


def _case(name, discipline, before, scale=DISCIPLINE_MAKE_SHAVE_SCALE, identity=False):
    after = apply(before, discipline, scale)
    raw = before * (1.0 - shave(discipline, scale)) if scale > 0.0 else before
    return dict(name=name, defenderDiscipline=discipline, scale=scale,
                makePctBefore=before, makePctAfter=after,
                clamped=(raw != after), identity=identity)


def emit_golden(path):
    cases = [
        _case("lockdown (D99) vs 60% rim finisher", 99, 0.600),
        _case("lockdown (D99) vs 34% shooter",      99, 0.340),
        _case("liability (D0) vs 60% rim finisher",  0, 0.600),
        _case("liability (D0) vs 34% shooter",       0, 0.340),
        _case("average (D50) is neutral", 50, 0.450, identity=True),
        _case("liability (D0) upper-clamp boundary", 0, 0.999),
        _case("kill switch scale=0, D99", 99, 0.550, scale=0.0, identity=True),
        _case("kill switch scale=0, D0",   0, 0.550, scale=0.0, identity=True),
    ]
    golden = dict(
        _comment=(
            "S61 Discipline make-% shave golden. Emitted from tools/discipline_shave_oracle.py. "
            "Locks the SYMMETRIC absolute per-man shave: progress = clamp((D-50)/49, -1, +1); "
            "makePct = clamp01(makePctBefore * (1 - scale*progress)). Reads the DEFENDER's own "
            "Discipline only (absolute) and no zone term (flat). scale 0 = kill-switch identity. "
            "DisciplineMakeShaveScale is a CALIBRATION PLACEHOLDER; the harness cross-checks the "
            "'constants' value against the loaded RollHConfig before trusting parity."
        ),
        constants=dict(DisciplineMakeShaveScale=DISCIPLINE_MAKE_SHAVE_SCALE),
        tolerance=1e-12,
        cases=cases,
    )
    with open(path, "w") as f:
        json.dump(golden, f, indent=2)
    print(f"\nwrote golden: {path}  ({len(cases)} cases)")
    return golden


if __name__ == "__main__":
    print_table()
    emit_golden(os.path.join(os.path.dirname(__file__), "discipline_shave_golden.json"))
