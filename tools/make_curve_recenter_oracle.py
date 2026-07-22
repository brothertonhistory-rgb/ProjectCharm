"""
Make-curve recenter — reference oracle (calibration arc, session 1).

Governing record: docs/make-curve-recenter-brief.md (v2, 2026-07-21). This oracle
executes the brief's §5 fit hierarchy over the five Roll H zone logistics and emits:

  * the fitted constants per zone (Floor/Ceiling/K/Midpoint), each moved constant
    tagged with the forcing miss that unlocked it (§5.4 — a move without a recorded
    miss is a spec violation);
  * the pin report: every P1/P2/P3 as a (raw rating -> effective rating -> make%)
    triplet, hard pins held to +/-0.5pp, provisional pins reported with their miss,
    diagnostics computed and shown (§4);
  * the archetype table (§6) over NAMED replayable pool players;
  * the per-zone shape audit + interval-gain table and the rating-0 guardrail flags;
  * full-precision fit JSON (tools/make_curve_recenter_fit.json) so the build session
    binds to emitted numbers, never hand-transcribed display output.

END-TO-END SCENARIO SEMANTICS (§4): "context-neutral vs a rating-50 defender at even
bodies" means the LIVE EffectiveRating path with every non-matchup channel at zero
contribution: skill gap live; physical gap 0 (even athleticism); height shift 0 (even
reach); discipline shave identity (defender D=50); C1..C7 / relief / tax / passing all
zero. P1 is therefore exactly the raw curve at effective 50 (the brief's definition),
and the S48 elite figure reproduces: eff(99 vs 50) = 122.0576 -> 45.0% on today's
curve vs the measured 44.8% (bench context nets ~-0.2pp via the screening/off-ball
cancellation) — the semantics are validated against a recorded live number.

THE POOL: the S63 world's cohort, BuildDivvyPool(347, divvySeed 20260720) — cohortSeed
= (int)(20260720 ^ 0x5EEDC0D3), P = 3,470 (verified by live replay 2026-07-21:
median Outside 27, three players at Outside >= 95: Pool_97 = 99, Pool_116 = 97,
Pool_863 = 96; p99 = 78). The brief's "one of the pool's six" was a draft-time
estimate; the replayed pool carries THREE at 95+ — divergence reported at the table.
Named-row ratings below were extracted from that replay and are the replay contract.

CONTEXT ROWS (Korver / mirror): the team scalars (TeamBaseOpenness, ConversionQuality,
attention share) were probed through the LIVE AttentionGenerator on the named pool
lineups at the stated usage shares (scratch harness probe, 2026-07-21); the Roll H
channel arithmetic below is a constant-for-constant mirror of RollHGenerator's
halfcourt chain (C1 -> C2 -> C3 -> relief -> shave -> C4 -> C5.5 -> C6/C7 -> clamp),
read from source this session. These rows are OBSERVATIONAL (the Korver row is the
relief/gravity session's entry criterion, never this build's pass/fail).
"""
import json, math, sys
from pathlib import Path

# ---- Live curve + gap constants (cross-checked vs config.json when run in-repo) ----
CURVE_OLD = {
    'Rim':   (0.3582, 0.9527, 0.024666,  68.7559),
    'Short': (0.1316, 0.7045, 0.021592,  75.9276),
    'Mid':   (0.1042, 0.6447, 0.021592,  90.3196),
    'Long':  (0.1934, 0.6034, 0.034190,  99.3440),
    'Three': (0.1608, 0.6328, 0.029646, 106.5661),
}
SKILL_STEEP, SKILL_EXP, REF_SCALE = 6.0, 2.0, 25.0
ZONES = ['Rim', 'Short', 'Mid', 'Long', 'Three']

def crosscheck_config():
    p = Path(__file__).resolve().parent.parent / 'src' / 'Charm.Harness' / 'config.json'
    if not p.exists():
        print("  (config.json not found beside repo tools/ — cross-check skipped)"); return
    cfg = json.load(open(p))['RollH']
    for z in ZONES:
        live = (cfg[f'{z}Floor'], cfg[f'{z}Ceiling'], cfg[f'{z}K'], cfg[f'{z}Midpoint'])
        assert all(abs(a-b) < 1e-12 for a, b in zip(live, CURVE_OLD[z])), \
            f"live config {z} curve != oracle constants — regenerate this oracle"
    m = json.load(open(p))['Matchup']
    assert (m['SkillSteepness'], m['SkillExponent'], m['ReferenceScale']) == (6.0, 2.0, 25.0)
    print("  config cross-check OK (5 zone curves + skill-gap constants match live)")

def gapfn(gap, steep=SKILL_STEEP, exp=SKILL_EXP, scale=REF_SCALE):
    return math.copysign(1, gap) * steep * (abs(gap) / scale) ** exp if gap != 0 else 0.0

def eff(raw, defense=50.0):
    """LIVE EffectiveRating at even bodies: baseline + skill shift; phys/height = 0."""
    return raw + gapfn(raw - defense)

def logistic(F, C, K, M, x):
    return F + (C - F) / (1.0 + math.exp(-K * (x - M)))

def logit_of(t, F, C):
    L = (t - F) / (C - F)
    assert 0.0 < L < 1.0, f"target {t} outside ({F},{C})"
    return math.log(L / (1.0 - L))

# ---- Pins (§4) — provenance-labelled ------------------------------------------------
# P2 raw ratings are NAMED pool players (the zone's driving rating, OffenseRating map:
# Three/Long -> Outside, Mid -> Mid, Short -> Close, Rim -> Finishing).
PINS = {
  # zone: P1 target(hard) | (P2 player, raw, target, hard?) | (P3 raw, target, hard?)
  'Three': dict(p1=0.34, p2=('Pool_97 (Outside 99)',  99, 0.45, True ), p3=(20, 0.27, True )),
  'Rim':   dict(p1=0.61, p2=('Pool_1037 (Finishing 99)', 99, 0.77, False), p3=(20, None, False)),  # 0.68->0.77 Emmett ruling 2026-07-21 (range 75-80, middle taken; the catch-and-shoot-guard tendency principle logged in the brief/journal)
  'Short': dict(p1=0.43, p2=('Pool_3248 (Close 90)',  90, 0.52, False), p3=(20, None, False)),
  'Mid':   dict(p1=0.39, p2=('Pool_1252 (Mid 92)',    92, 0.46, False), p3=(20, None, False)),
  'Long':  dict(p1=0.36, p2=('Pool_97 (Outside 99)',  99, 0.44, False), p3=(20, None, False)),
}
TOL = 0.005   # +/-0.5pp (§5.2 / §8)

def fit_stage1_exact(F, C, t1, e2, t2):
    """P1 hard + P2 exact: closed form for (M, K) with Floor/Ceiling fixed."""
    l1, l2 = logit_of(t1, F, C), logit_of(t2, F, C)
    # K(50 - M) = l1 ; K(e2 - M) = l2  ->  M = (50*l2 - e2*l1) / (l2 - l1)
    M = (50.0 * l2 - e2 * l1) / (l2 - l1)
    K = l1 / (50.0 - M)
    assert K > 0, f"infeasible stage-1 fit (K={K})"
    return M, K

def fit_stage1_min(F, C, t1, e2, t2, e3, t3):
    """P1 hard; minimize equal-weight squared error over P2 and P3 (Three's stage 1)."""
    l1 = logit_of(t1, F, C)
    def err(M):
        K = l1 / (50.0 - M)
        if K <= 0: return 1e9
        return (logistic(F,C,K,M,e2)-t2)**2 + (logistic(F,C,K,M,e3)-t3)**2
    # coarse grid then golden refine
    lo, hi = 50.5, 200.0
    Ms = [lo + i*(hi-lo)/4000 for i in range(4001)]
    M = min(Ms, key=err)
    a, b = M-0.1, M+0.1
    for _ in range(200):
        m1, m2 = a+(b-a)*0.382, a+(b-a)*0.618
        if err(m1) < err(m2): b = m2
        else: a = m1
    M = (a+b)/2
    return M, l1/(50.0-M)

def fit_stage2_floor(C, t1, e2, t2, e3, t3):
    """Unlock Floor; solve the three equalities exactly (bisect F, closed-form M,K).
    Returns (F, M, K, residual_at_bound) — residual is None when an interior F solves
    the pins; otherwise F is clamped at its 0 bound and the residual is the best
    achievable P3 miss with Floor alone (the step-3 forcing miss)."""
    def resid(F):
        M, K = fit_stage1_exact(F, C, t1, e2, t2)
        return logistic(F, C, K, M, e3) - t3
    lo, hi = 0.0, min(t1, t3) - 1e-6
    rlo, rhi = resid(lo), resid(hi)
    if rlo * rhi >= 0:
        # P3 unreachable with Floor alone: the best (lowest) P3 sits at the F=0 bound.
        F = 0.0
        M, K = fit_stage1_exact(F, C, t1, e2, t2)
        return F, M, K, rlo
    for _ in range(200):
        mid = (lo + hi) / 2
        if resid(lo) * resid(mid) <= 0: hi = mid
        else: lo = mid
    F = (lo + hi) / 2
    M, K = fit_stage1_exact(F, C, t1, e2, t2)
    return F, M, K, None

def fit_stage3_ceiling(F, C0, t1, e2, t2, e3, t3):
    """Step 3 closure (deterministic, reported): Floor stays at the bound the step-2
    solve clamped it to; Ceiling moves the MINIMUM residual amount to close P3 while
    P1/P2 stay exact (closed-form (M,K) per candidate C; bisect C downward)."""
    def resid(C):
        M, K = fit_stage1_exact(F, C, t1, e2, t2)
        return logistic(F, C, K, M, e3) - t3
    lo, hi = t2 + 0.01, C0        # Ceiling must clear the elite target
    assert resid(lo) * resid(hi) < 0, "no Ceiling bracket — inspect"
    for _ in range(200):
        mid = (lo + hi) / 2
        if resid(lo) * resid(mid) <= 0: hi = mid
        else: lo = mid
    C = (lo + hi) / 2
    M, K = fit_stage1_exact(F, C, t1, e2, t2)
    return C, M, K

def run_fit():
    fits = {}
    for z in ZONES:
        F0, C0, K0, M0 = CURVE_OLD[z]
        pin = PINS[z]
        t1 = pin['p1']
        name2, raw2, t2, hard2 = pin['p2']
        raw3, t3, hard3 = pin['p3']
        e2, e3 = eff(raw2), eff(raw3)
        moved, forcing = ['Midpoint', 'K'], None
        if z == 'Three':
            # stage 1: P1 hard, minimize over hard P2 + hard P3
            M, K = fit_stage1_min(F0, C0, t1, e2, t2, e3, t3)
            F, C = F0, C0
            miss2 = logistic(F,C,K,M,e2) - t2
            miss3 = logistic(F,C,K,M,e3) - t3
            if abs(miss2) > TOL or abs(miss3) > TOL:
                if abs(miss3) >= abs(miss2):
                    forcing = f"stage-1 misses: elite {miss2*100:+.2f}pp, poor {miss3*100:+.2f}pp (poor dominates) -> Floor unlocked"
                    F, M, K, bound_resid = fit_stage2_floor(C0, t1, e2, t2, e3, t3)
                    moved.append('Floor')
                    if bound_resid is not None and abs(bound_resid) > TOL:
                        forcing += (f"; Floor clamped at its 0 bound with P3 still {bound_resid*100:+.2f}pp"
                                    f" -> Ceiling unlocked (step 3, minimum-move closure)")
                        C, M, K = fit_stage3_ceiling(F, C0, t1, e2, t2, e3, t3)
                        moved.append('Ceiling')
                else:
                    raise AssertionError("elite-end dominance would unlock Ceiling — not expected; inspect")
        else:
            # stage 1: P1 hard + fit toward P2 only (provisional weight) — exact solve
            M, K = fit_stage1_exact(F0, C0, t1, e2, t2)
            F, C = F0, C0
        fits[z] = dict(F=F, C=C, K=K, M=M, moved=moved, forcing=forcing,
                       e2=e2, e3=e3)
    return fits

def report(fits):
    print("=" * 96)
    print("MAKE-CURVE RECENTER — FIT REPORT (fit hierarchy §5; every moved constant tagged)")
    print("=" * 96)
    crosscheck_config()
    rows = []
    for z in ZONES:
        f = fits[z]; F0, C0, K0, M0 = CURVE_OLD[z]; pin = PINS[z]
        print(f"\n{z}:")
        print(f"  old  F={F0:.4f} C={C0:.4f} K={K0:.6f} M={M0:.4f}")
        print(f"  new  F={f['F']:.6f} C={f['C']:.6f} K={f['K']:.6f} M={f['M']:.4f}   moved: {', '.join(f['moved'])}")
        if f['forcing']: print(f"  FORCING MISS: {f['forcing']}")
        t1 = pin['p1']; name2, raw2, t2, hard2 = pin['p2']; raw3, t3, hard3 = pin['p3']
        def m(x): return logistic(f['F'], f['C'], f['K'], f['M'], x)
        p1v, p2v, p3v = m(50.0), m(f['e2']), m(f['e3'])
        print(f"  P1 hard      raw 50 -> eff  50.00 -> {p1v*100:6.2f}%   target {t1*100:.0f}   miss {100*(p1v-t1):+.3f}pp")
        tag2 = 'hard' if hard2 else 'provisional'
        print(f"  P2 {tag2:11s} {name2}: raw {raw2} -> eff {f['e2']:7.2f} -> {p2v*100:6.2f}%   target {t2*100:.0f}   miss {100*(p2v-t2):+.3f}pp")
        if t3 is not None:
            print(f"  P3 hard      raw {raw3} -> eff {f['e3']:7.2f} -> {p3v*100:6.2f}%   target {t3*100:.0f}   miss {100*(p3v-t3):+.3f}pp")
        else:
            print(f"  P3 diagnostic raw {raw3} -> eff {f['e3']:7.2f} -> {p3v*100:6.2f}%   (shown, not fitted)")
        # hard-pin acceptance
        assert abs(p1v - t1) <= TOL + 1e-12, f"{z} P1 outside +/-0.5pp"
        if hard2: assert abs(p2v - t2) <= TOL + 1e-12, f"{z} P2 outside +/-0.5pp"
        if hard3 and t3 is not None: assert abs(p3v - t3) <= TOL + 1e-12, f"{z} P3 outside +/-0.5pp"
        rows.append((z, f))
    return rows

def shape_audit(fits):
    print("\n" + "=" * 96)
    print("SHAPE AUDIT (raw-curve make% at effective ratings; interval gains before -> after)")
    print("=" * 96)
    pts = [0, 10, 20, 50, 76, 95, 99]
    for z in ZONES:
        F0, C0, K0, M0 = CURVE_OLD[z]; f = fits[z]
        old = [logistic(F0, C0, K0, M0, x) for x in pts]
        new = [logistic(f['F'], f['C'], f['K'], f['M'], x) for x in pts]
        print(f"\n{z}:  eff      " + "  ".join(f"{x:>6d}" for x in pts))
        print(f"      old %    " + "  ".join(f"{100*v:6.1f}" for v in old))
        print(f"      new %    " + "  ".join(f"{100*v:6.1f}" for v in new))
        ivals = [(20, 50), (50, 76), (76, 95), (95, 99)]
        go = {f"{a}->{b}": 100*(logistic(F0,C0,K0,M0,b)-logistic(F0,C0,K0,M0,a)) for a,b in ivals}
        gn = {f"{a}->{b}": 100*(logistic(f['F'],f['C'],f['K'],f['M'],b)-logistic(f['F'],f['C'],f['K'],f['M'],a)) for a,b in ivals}
        print( "      gain     " + "  ".join(f"{k}: {go[k]:5.1f} -> {gn[k]:5.1f} (D{gn[k]-go[k]:+5.1f})" for k in go))

def guardrail(fits):
    print("\n" + "=" * 96)
    print("RATING-0 GUARDRAIL (end-to-end raw ratings through the live eff path — flags only)")
    print("=" * 96)
    flags = []
    for z in ZONES:
        f = fits[z]; pin = PINS[z]
        def m(x): return logistic(f['F'], f['C'], f['K'], f['M'], eff(x))
        m0, m20, m50 = m(0), m(20), m(50)
        poor = m20   # the poor scenario IS raw-20 end-to-end
        g0_20, g20_50 = m20 - m0, m50 - m20
        f1 = abs(m0 - poor) < 0.03
        f2 = g0_20 < g20_50 / 3.0
        print(f"{z}: raw0 {m0*100:5.1f}%  raw20 {m20*100:5.1f}%  raw50 {m50*100:5.1f}%   "
              f"0->20 gain {g0_20*100:4.1f}pp vs 1/3 of 20->50 = {g20_50*100/3:4.1f}pp"
              f"   {'FLAG' if (f1 or f2) else 'ok'}")
        if f1: flags.append(f"{z}: make(0) within 3pp of the poor scenario")
        if f2: flags.append(f"{z}: 0->20 gain below a third of 20->50")
    if flags:
        print("FLAGS RAISED:"); [print("  * " + s) for s in flags]
    else:
        print("No guardrail flags — a true non-shooter stays visibly worse than a poor shooter.")
    return flags

# ---- Archetype table (§6) — NAMED replayable pool rows ------------------------------
ARCHETYPES = [
    # (label, zone, raw rating)
    ("Pool_26   the median shooter (Outside 27)",   'Three', 27),
    ("synthetic the 50-rated shooter (anchor row)", 'Three', 50),
    ("Pool_704  the p99 shooter (Outside 78)",      'Three', 78),
    ("Pool_97   the elite (Outside 99)",            'Three', 99),
    ("Pool_3270 rim-running big — Rim (Fin 81)",    'Rim',   81),
    ("Pool_3270 rim-running big — Short (Close 60)",'Short', 60),
]

def archetype_table(fits):
    print("\n" + "=" * 96)
    print("ARCHETYPE TABLE (context-neutral, vs a rating-50 defender at even bodies, end-to-end)")
    print("=" * 96)
    for label, z, raw in ARCHETYPES:
        F0, C0, K0, M0 = CURVE_OLD[z]; f = fits[z]
        x = eff(raw)
        before = logistic(F0, C0, K0, M0, x)
        after  = logistic(f['F'], f['C'], f['K'], f['M'], x)
        print(f"  {label:47s} {z:5s}: eff {x:7.2f}   {before*100:5.1f}% -> {after*100:5.1f}%   D{100*(after-before):+5.1f}pp")

# ---- Context rows: Korver + mirror (observational) ---------------------------------
# Live-probed team scalars (AttentionGenerator on the named pool lineups, 2026-07-21).
KORVER = dict(  # Pool_97 + Pool_2182/316/1389/2406, usage share 0.10
    open_=0.6399878192565049, grav=0.7396108029783179, spac=0.8204046717171716,
    convQ=0.30704471319588583, attn=0.1452641118858073, share=0.10,
    mates_screening=[72, 43, 56, 63], shooter_screening=29)
MIRROR = dict(  # Pool_97 + Pool_26/1000/2000/3000 (an unremarkable supporting cast), share 0.45
    open_=0.20624703795493274, grav=0.3386379397444666, spac=0.4787317613636364,
    convQ=0.11714961250730449, attn=0.3399957698815567, share=0.45,
    mates_screening=[8, 44, 27, 44], shooter_screening=29)
CH = dict(C1ReliefScale=2.0, C2ImbalanceScale=0.08, PressureVolumeTaxScale=0.12,
          PressureResidualPenaltyScale=2.0, C3AttentionAmplifier=1.5,
          UsageReliefBonusScale=1.0, PassingOpportunityFloor=0.1, MaxPassingBonus=0.08,
          ScreeningBonusScale=0.15, ScreeningAggregateExponent=2.0,
          OffBallDefenseSuppressionScale=0.15, OffBallDefenseAggregateExponent=2.0)

def stacked_three(base, ctx):
    """RollHGenerator halfcourt chain at Three, constant-for-constant (read from source
    this session): C1 -> C2 -> C3 -> relief -> shave(identity, D=50) -> C4 -> C5.5 ->
    C7 -> clamp. UsageResidual assumed 0 (self-created star, share within capacity)."""
    steps = {}
    m = base
    c1 = min(ctx['open_'] * max(0.0, 0.20 - ctx['attn']) * CH['C1ReliefScale'], 1.0)
    m = min(m + c1, 1.0); steps['C1 openness'] = c1
    c2 = max(0.0, ctx['spac'] - ctx['grav']) * CH['C2ImbalanceScale']
    m = max(m - c2, 0.0); steps['C2 imbalance'] = -c2
    pressure = max(0.0, ctx['share'] - 0.20)
    if pressure > 0:
        amp = 1.0 + max(0.0, ctx['attn'] - 0.20) * CH['C3AttentionAmplifier']
        tax = pressure * CH['PressureVolumeTaxScale'] * amp
        steps['C3 volume tax'] = -m * tax
        m = min(max(m * (1.0 - tax), 0.0), 1.0)
    relief = max(0.0, 0.20 - ctx['share'])
    if relief > 0:
        steps['usage relief'] = m * relief * CH['UsageReliefBonusScale']
        m = min(m * (1.0 + relief * CH['UsageReliefBonusScale']), 1.0)
    gate = CH['PassingOpportunityFloor'] + (1 - CH['PassingOpportunityFloor']) * ctx['open_']
    c4 = CH['MaxPassingBonus'] * ctx['convQ'] * gate
    m = min(m + c4, 1.0); steps['C4 passing'] = c4
    scr = (ctx['shooter_screening'] + sum(ctx['mates_screening'])) / 500.0
    c55 = CH['ScreeningBonusScale'] * scr ** CH['ScreeningAggregateExponent']
    m += c55; steps['C5.5 screening'] = c55
    c7 = CH['OffBallDefenseSuppressionScale'] * (200.0/400.0) ** CH['OffBallDefenseAggregateExponent']
    m -= c7; steps['C7 off-ball D (flat-50)'] = -c7
    return min(max(m, 0.0), 1.0), steps

def context_rows(fits):
    print("\n" + "=" * 96)
    print("CONTEXT ROWS — observational (the Korver gap is the relief/gravity session's entry criterion)")
    print("=" * 96)
    F0, C0, K0, M0 = CURVE_OLD['Three']; f = fits['Three']
    x = eff(99)
    for name, ctx, note in [("KORVER (elite, 10% share, elite mates, gravity on)", KORVER, ">= 50% requirement"),
                            ("MIRROR (same elite, 45% share, bad team, no gravity)", MIRROR, "should sit low-40s")]:
        b_old, _  = stacked_three(logistic(F0,C0,K0,M0,x), ctx)
        b_new, st = stacked_three(logistic(f['F'],f['C'],f['K'],f['M'],x), ctx)
        print(f"\n  {name}   [{note}]")
        print(f"    curve base {logistic(f['F'],f['C'],f['K'],f['M'],x)*100:.1f}%  ->  stacked {b_new*100:.1f}%   (old curve stacked: {b_old*100:.1f}%)")
        for k, v in st.items(): print(f"      {k:26s} {v*100:+5.2f}pp")
        if "KORVER" in name:
            gap_pp = 50.0 - b_new*100
            print(f"    KORVER GAP vs >=50%: {gap_pp:+.1f}pp ({100*gap_pp/(b_new*100):+.1f}% relative) — record in status.md")


# ---- League-line prediction (§8 — an approximation, stated as such) -----------------
# Predicted league 3P% = sum_o w(o)·make(eff(o)) / sum_o w(o), where w(o) =
# o × S3[o] and S3[o] is the pool-wide sum of stamped ThreeTendency over players with
# Outside = o (extracted from the live BuildDivvyPool replay — embedded below so the
# prediction replays without the dump). The o× factor is a usage proxy (attempts follow
# skill); the defender is the rating-50 anchor. VALIDATION: the same weighting under
# TODAY'S curve predicts 21.6%% vs the recorded S63 league line of 22.0% (-0.4pp method
# bias) — the after-line carries the same ~half-point health warning. League 3P% is NOT
# expected to reach 34 until the diet session (the 0.69 three-rate routes attempts to
# the incapable); this line is what the season page is held to, never the curve's test.
S3_BY_OUTSIDE = [0, 0, 0, 0, 0, 0, 0, 0, 16708, 2101, 2634, 2785, 3220, 3483, 3147, 6568, 5368, 7056, 9313, 6499, 7987, 8854, 8370, 10066, 7695, 7972, 8901, 7701, 9052, 7478, 7454, 8511, 6643, 6722, 5854, 5664, 5807, 4494, 4308, 4077, 3532, 4246, 3451, 4159, 3587, 3043, 3082, 2449, 2122, 2256, 1370, 1350, 877, 1223, 941, 1379, 882, 959, 1045, 622, 708, 354, 472, 805, 806, 397, 174, 430, 525, 474, 498, 261, 433, 0, 435, 87, 87, 68, 261, 432, 241, 260, 248, 171, 155, 87, 174, 256, 250, 87, 0, 0, 67, 163, 0, 0, 83, 87, 0, 81]

def league_line(fits):
    print("\n" + "=" * 96)
    print("PREDICTED LEAGUE 3P% (population + tendency weighted; approximation — see header)")
    print("=" * 96)
    F0, C0, K0, M0 = CURVE_OLD['Three']; f = fits['Three']
    num_o = num_n = den = 0.0
    for o, s3 in enumerate(S3_BY_OUTSIDE):
        if s3 == 0: continue
        w = max(1, o) * s3
        x = eff(o)
        num_o += w * logistic(F0, C0, K0, M0, x)
        num_n += w * logistic(f['F'], f['C'], f['K'], f['M'], x)
        den   += w
    print(f"  before: predicted {100*num_o/den:.1f}%  (recorded S63 league line: 22.0% — method bias -0.4pp)")
    print(f"  after:  predicted {100*num_n/den:.1f}%  (the what-to-watch line for the season page)")

def emit_json(fits):
    out = {"_comment": "Make-curve recenter fit (session 2026-07-21). Emitted by "
           "tools/make_curve_recenter_oracle.py — the build session binds config.json's "
           "RollH zone constants to THESE values, never hand-transcribed display output. "
           "Pins/provenance per the brief §4; every moved constant carries its forcing miss.",
           "zones": {}}
    for z in ZONES:
        f = fits[z]
        out["zones"][z] = {
            "old": dict(zip(("Floor","Ceiling","K","Midpoint"), CURVE_OLD[z])),
            "new": {"Floor": f['F'], "Ceiling": f['C'], "K": f['K'], "Midpoint": f['M']},
            "moved": f['moved'], "forcingMiss": f['forcing'],
        }
    p = Path(__file__).resolve().parent / 'make_curve_recenter_fit.json'
    json.dump(out, open(p, 'w'), indent=2)
    print(f"\nfit JSON -> {p}")

if __name__ == '__main__':
    fits = run_fit()
    report(fits)
    shape_audit(fits)
    flags = guardrail(fits)
    archetype_table(fits)
    context_rows(fits)
    league_line(fits)
    emit_json(fits)
    print("\nORACLE COMPLETE" + (" — GUARDRAIL FLAGS ABOVE" if flags else " — no flags"))
