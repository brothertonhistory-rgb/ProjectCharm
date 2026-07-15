"""
S60 — USAGE-RELIEF BONUS: LOCKED oracle + golden emitter.

The signed-off SHAPE (Emmett, 2026-07-14). Magnitude is a CALIBRATION
PLACEHOLDER (page-tuned after the build; never suite-asserted). The C# port is
golden-checked against tools/usage_relief_golden.json at 1e-12.

WHAT IT IS. The mirror of the Phase 17/27 volume tax. The tax charges a player
for carrying MORE than his equal share of the shots — pressure = max(0, share −
equalShare) — but pays NOTHING for carrying less: every below-share player reads
exactly 0, so a 13%-usage specialist shoots identically to a 20%-usage one. This
adds the other half: relief = max(0, equalShare − share), stamped by Roll E
beside the existing pressure, read by Roll H as a make% BONUS.

THE RULINGS IT ENCODES.
  - A light load lets anyone shoot "open shots" and be somewhat efficient
    regardless of ratings; fed usage, the ratings show.
  - The bonus fades to exactly zero AT equal share, by construction — the tax and
    the relief meet at the same pivot and neither exists on the other's side.
  - The tax side does NOT move (the anchor ruling: ~31.7% FG at 43% usage is
    plausibly correct — anchor the top, lift the bottom).
  - The SAME pivot and the SAME shares as the tax: equalShare = 1.0 / populated,
    computed on POST-floor/rail shares in the same one-pass calculation. A relief
    computed on a different basis would disagree with the tax about where "equal
    share" sits.
  - Multiplicative on make%, not additive: relief scales the probability the
    player already earned from his matchup, so a bad shooter on a light load is
    still a bad shooter — he is just a somewhat more efficient bad shooter.
  - Attention does NOT amplify relief. C3AttentionAmplifier scales the PENALTIES
    for an above-share, above-attention shooter. Relief is shot-selection and is
    deliberately independent: bonus = relief x UsageReliefBonusScale, no
    attention read.
  - Gravity (C1/C4) and relief COMPOUND intentionally. They read different
    inputs — C1/C4 read TEAM openness/gravity/spacing (who is around you); relief
    reads the shooter's OWN final share (how little load he carries). A player
    with both gets both, multiplicatively. That is the design, not double
    attribution.

THE INPUT STAGE IS NAMED AND LOCKED. makePctBeforeRelief is the live Roll H make
probability AFTER the C3 penalty block and BEFORE the C4 passing converter. Every
golden case carries it explicitly, so parity can never be "achieved" against the
wrong stage. (The S59.2 archetype table applied the multiplier to a measured
aggregate 3P%; that arithmetic holds only because the multiplier is
share-constant across a player's shots — the golden is defined at the formula
stage.)
"""
import json, math

# ===========================================================================
# CONFIG KNOBS — [CALIBRATION PLACEHOLDER], page-tuned after the build.
# The C# build reads this from RollHConfig; the golden pins this exact value.
# 0.0 is the LEGAL KILL SWITCH (identity branch, not a harmless x1.0).
# ===========================================================================
USAGE_RELIEF_BONUS_SCALE = 1.0     # medium — the signed-off archetype column

# Live-config cross-check reference (RollHConfig.cs:180 / config.json).
# RimCeiling x (1 + 0.11 x 1.0) = 0.9527 x 1.11 = 1.0575 -> the clamp zone is
# REACHABLE at the rim, so the clamp branch is production code and gets proven.
RIM_CEILING = 0.9527


def clamp01(x):
    return 0.0 if x < 0.0 else 1.0 if x > 1.0 else x


# ---- the two primitives ----------------------------------------------------
def relief_of(final_share, populated):
    """Stamped by Roll E. equalShare = 1.0/populated, on POST-rail shares —
    the SAME pivot and the SAME array the tax reads."""
    equal_share = 1.0 / populated
    return max(0.0, equal_share - final_share)


def pressure_of(final_share, populated):
    """The EXISTING tax term, mirrored here ONLY to prove the pivot is shared.
    The tax itself does not move this session."""
    equal_share = 1.0 / populated
    return max(0.0, final_share - equal_share)


def apply_relief(make_pct_before, relief, scale=None):
    """Pure transform: makePctBeforeRelief -> makePctAfterRelief.

    IDENTITY BRANCH (matches the S57/S59 standard): relief 0 OR scale 0 returns
    the input UNTOUCHED — no multiply-by-1.0, no clamp. Bit-identical for the
    right reason, not by a harmless arithmetic no-op."""
    s = USAGE_RELIEF_BONUS_SCALE if scale is None else scale
    if relief <= 0.0 or s <= 0.0:
        return make_pct_before, dict(multiplier=1.0, bonus=0.0, clamped=False, identity=True)
    multiplier = 1.0 + relief * s
    raw = make_pct_before * multiplier
    after = clamp01(raw)
    return after, dict(multiplier=multiplier, bonus=after - make_pct_before,
                       clamped=raw > 1.0, identity=False)


# ===========================================================================
# ARCHETYPE CASES — the signed-off table (S59.2 close).
#   (name, populated, finalShare, makePctBeforeRelief, scale-or-None)
# ===========================================================================
ARCH = [
    ("equal-share player (five-man, pivot)",        5, 0.200, 0.4500, None),
    ("13.5%-usage SNIPER (the signed-off row)",     5, 0.135, 0.3710, None),
    ("five-man 9%-share player (low-share bound)",  5, 0.090, 0.4200, None),
    ("four-man lineup, 9% share (pivot moves)",     4, 0.090, 0.4200, None),
    ("above-share star (tax side — relief 0)",      5, 0.430, 0.3170, None),
    ("KILL SWITCH scale=0 at a real relief",        5, 0.135, 0.3710, 0.0),
    ("BOUNDARY synthetic clamp (0.95 x 1.11)",      5, 0.090, 0.9500, None),
    ("KILL SWITCH at the clamping-capable input",   5, 0.090, 0.9500, 0.0),
]


def table():
    print("S60 USAGE-RELIEF — locked archetype table")
    print(f"relief = max(0, 1/populated - finalShare);  "
          f"makeAfter = clamp01(makeBefore x (1 + relief x {USAGE_RELIEF_BONUS_SCALE}))  [PLACEHOLDER]\n")
    print(f"  {'case':<44} {'pop':>3} {'share':>7} {'relief':>7} {'mult':>7} "
          f"{'before':>8} {'after':>8} {'delta':>7}")
    for name, pop, share, before, scale in ARCH:
        r = relief_of(share, pop)
        after, d = apply_relief(before, r, scale)
        flag = "  [CLAMPED]" if d["clamped"] else ("  [IDENTITY]" if d["identity"] else "")
        print(f"  {name:<44} {pop:>3} {share*100:>6.1f}% {r:>7.4f} {d['multiplier']:>7.4f} "
              f"{before*100:>7.2f}% {after*100:>7.2f}% {(after-before)*100:>+6.2f}pp{flag}")
    print()


# ===========================================================================
# STRUCTURAL INVARIANTS (become the Phase 66 harness checks)
# ===========================================================================
def checks():
    ok = True

    def chk(name, cond, d=""):
        nonlocal ok
        ok = ok and cond
        print(f"  [{'OK' if cond else 'FAIL'}] {name}" + (f" — {d}" if d else ""))

    # --- the signed-off row reproduces exactly ------------------------------
    r = relief_of(0.135, 5)
    after, _ = apply_relief(0.3710, r)
    chk("signed-off sniper row: 0.371 x 1.065 = 0.395115",
        abs(after - 0.395115) < 1e-12, f"got {after:.6f}")

    # --- relief is exactly 0 at and above equal share -----------------------
    z = all(relief_of(s, 5) == 0.0 for s in (0.20, 0.2000000001, 0.25, 0.43, 1.0))
    chk("relief exactly 0.0 at and above equal share", z)

    # --- the pivot is SHARED with the tax, both lineup sizes ----------------
    piv = True
    for pop in (5, 4):
        eq = 1.0 / pop
        eps = 1e-9
        piv = piv and relief_of(eq - eps, pop) > 0.0 and pressure_of(eq - eps, pop) == 0.0
        piv = piv and relief_of(eq, pop) == 0.0 and pressure_of(eq, pop) == 0.0
        piv = piv and relief_of(eq + eps, pop) == 0.0 and pressure_of(eq + eps, pop) > 0.0
    chk("pivot consistency: below -> relief only; at -> both 0; above -> tax only (pop 5 and 4)", piv)

    # --- the pivot MOVES with populated count ------------------------------
    chk("pivot follows populated count (four-man 9% relief 0.16 > five-man 0.11)",
        abs(relief_of(0.09, 4) - 0.16) < 1e-12 and abs(relief_of(0.09, 5) - 0.11) < 1e-12,
        f"4man={relief_of(0.09,4):.4f} 5man={relief_of(0.09,5):.4f}")

    # --- three monotonicities, share rising (equalShare/scale/before fixed) --
    mono_r = mono_m = mono_a = True
    pr = pm = pa = None
    for share in [0.00, 0.05, 0.09, 0.135, 0.18, 0.20, 0.25, 0.43]:
        r = relief_of(share, 5)
        a, d = apply_relief(0.45, r)
        if pr is not None:
            mono_r = mono_r and r <= pr + 1e-15
            mono_m = mono_m and d["multiplier"] <= pm + 1e-15
            mono_a = mono_a and a <= pa + 1e-15
        pr, pm, pa = r, d["multiplier"], a
    chk("relief non-increasing as final share rises", mono_r)
    chk("multiplier non-increasing as final share rises", mono_m)
    chk("makePctAfterRelief non-increasing as final share rises", mono_a)

    # --- monotone at a SATURATED input: flat region permitted, never rising --
    sat = True
    ps = None
    for share in [0.00, 0.05, 0.09, 0.135, 0.20]:
        a, _ = apply_relief(0.95, relief_of(share, 5))
        if ps is not None:
            sat = sat and a <= ps + 1e-15
        ps = a
    chk("monotone holds through the clamp (flat saturated region permitted)", sat)

    # --- equal-share player is a BIT identity on a LIVE scale ---------------
    before = 0.4500
    a, d = apply_relief(before, relief_of(0.20, 5))
    chk("equal-share player -> BIT-identity on a live scale (identity branch)",
        a == before and d["identity"], f"delta={a-before:.1e}")

    # --- kill switch is a BIT identity at EVERY relief ----------------------
    ks = True
    for share in [0.00, 0.05, 0.09, 0.135, 0.20, 0.43]:
        for before in [0.10, 0.4500, 0.95, RIM_CEILING]:
            a, d = apply_relief(before, relief_of(share, 5), scale=0.0)
            ks = ks and a == before and d["identity"]
    chk("kill switch (scale=0) -> BIT-identity at every relief and every input", ks)

    # --- the approved non-boundary archetypes do NOT clamp ------------------
    nc = True
    for name, pop, share, before, scale in ARCH:
        if "BOUNDARY" in name or "clamping-capable" in name:
            continue
        _, d = apply_relief(before, relief_of(share, pop), scale)
        nc = nc and not d["clamped"]
    chk("approved non-boundary archetypes do not clamp at the placeholder scale", nc)

    # --- but the CONFIGURED make domain CAN reach the clamp -----------------
    raw = RIM_CEILING * (1.0 + relief_of(0.09, 5) * USAGE_RELIEF_BONUS_SCALE)
    chk("configured make domain CAN clamp (RimCeiling x 1.11 > 1.0) -> boundary case is real",
        raw > 1.0, f"{RIM_CEILING} x 1.11 = {raw:.4f}")

    # --- the clamp saturates at EXACTLY 1.0 --------------------------------
    a, d = apply_relief(0.95, relief_of(0.09, 5))
    chk("synthetic clamp boundary: 0.95 x 1.11 = 1.0545 -> exactly 1.0",
        a == 1.0 and d["clamped"], f"after={a!r}")

    # --- bonus-only guarantee ----------------------------------------------
    bo = True
    for share in [0.00, 0.05, 0.09, 0.135, 0.20, 0.43]:
        for before in [0.0, 0.10, 0.4500, 0.95, 1.0]:
            a, _ = apply_relief(before, relief_of(share, 5))
            bo = bo and a >= before - 1e-15
    chk("bonus-only: makePct never falls", bo)

    # --- true scale-zero identity: no arithmetic, not a harmless x1.0 -------
    a, d = apply_relief(0.3710, relief_of(0.135, 5), scale=0.0)
    chk("scale-zero takes the IDENTITY branch (no multiply, no clamp)", d["identity"] and d["bonus"] == 0.0)

    print(f"\n  INVARIANTS: {'ALL OK' if ok else 'FAIL'}")
    return ok


# ===========================================================================
# GOLDEN EMITTER
# ===========================================================================
def emit_golden(path):
    consts = dict(UsageReliefBonusScale=USAGE_RELIEF_BONUS_SCALE)
    cases = []
    for name, pop, share, before, scale in ARCH:
        s = USAGE_RELIEF_BONUS_SCALE if scale is None else scale
        r = relief_of(share, pop)
        after, d = apply_relief(before, r, scale)
        cases.append(dict(
            name=name,
            populated=pop,
            finalShare=share,
            equalShare=1.0 / pop,
            scale=s,
            relief=r,
            pressure=pressure_of(share, pop),
            multiplier=d["multiplier"],
            makePctBeforeRelief=before,
            makePctAfterRelief=after,
            clamped=d["clamped"],
            identity=d["identity"],
        ))
    golden = dict(
        _comment="S60 usage-relief golden. Emitted from tools/usage_relief_oracle.py. Locks the "
                 "relief TRANSFORM at a NAMED input stage: makePctBeforeRelief is the live Roll H "
                 "make probability AFTER the C3 penalty block and BEFORE the C4 passing converter. "
                 "'relief' is what Roll E stamps (max(0, 1/populated - finalShare), post-floor/rail "
                 "shares, the SAME pivot the tax reads); 'pressure' is the EXISTING tax term, carried "
                 "only to pin the shared pivot — the tax does not move this session. Magnitude is a "
                 "CALIBRATION PLACEHOLDER; constants cross-checked vs loaded config before use.",
        constants=consts, tolerance=1e-12, cases=cases)
    json.dump(golden, open(path, "w"), indent=2)
    return golden


if __name__ == "__main__":
    table()
    print("--- structural invariants ---")
    allok = checks()
    g = emit_golden("tools/usage_relief_golden.json")
    print(f"\nwrote tools/usage_relief_golden.json — {len(g['cases'])} cases, tol {g['tolerance']}")
    # self-verify: re-read golden and re-apply the transform to each stored input
    worst = 0.0
    exact = True
    for c in g["cases"]:
        r = relief_of(c["finalShare"], c["populated"])
        a, d = apply_relief(c["makePctBeforeRelief"], r, c["scale"])
        worst = max(worst, abs(r - c["relief"]), abs(a - c["makePctAfterRelief"]),
                    abs(d["multiplier"] - c["multiplier"]),
                    abs(pressure_of(c["finalShare"], c["populated"]) - c["pressure"]))
        if c["identity"]:
            exact = exact and a == c["makePctBeforeRelief"]
    print(f"golden self-parity worst |Δ| = {worst:.2e}  ({'OK' if worst < 1e-12 else 'FAIL'})")
    print(f"identity cases bit-exact = {exact}")
    print("ALL GOOD" if allok and worst < 1e-12 and exact else "PROBLEM")
