"""
PASS A — Perimeter-Defense DRIVE GATE: LOCKED oracle + golden emitter.

The signed-off SHAPE (Emmett, 2026-07-14). Magnitudes are CALIBRATION
PLACEHOLDERS (page-tuned after the build; never suite-asserted). The C# port is
golden-checked against tools/drive_gate_golden.json at 1e-12.

WHAT IT IS. A per-man location transform applied AFTER displacement: given a
shooter's post-displacement shot diet and the ONE matched perimeter defender, it
removes some of a perimeter driver's rim/short access and re-routes it to his
contested Long/Three. Shot DIET only — never make%.

THE RULINGS IT ENCODES.
  - First step BEATS him; handle UNLOCKS it. beat = w_fs*FirstStep + w_q*Quickness,
    times a handle unlock gate (0 at no-handle, full by ~average). Elite handle with
    no burst is walled like an average driver; a quick first step with no handle is
    walled hardest (and is turnover-prone elsewhere).
  - Suppression-PRIMARY: elite D suppresses, average ~neutral, poor does NOT (the
    weak-D "leak" is the absence of a wall, never an added bonus).
  - Drive-SPECIFIC: fires only on a perimeter-oriented shooter (low postness); the
    post route is untouched. A point-forward stays eligible.
  - Denied drive -> contested LONG/THREE (skip Mid), proportional to the shooter's
    own outer preference. (The "passed it / less usage" outcome is Pass B, not here.)
  - Wins over the soft-zone pull: a lockdown matched defender keeps his man out even
    when displacement invited him into a soft paint.
  - Conservation exact; neutral gate = the displaced pie unchanged; gate follows the
    matched slot; FastBreak / zero-defender bypass -> pie returned untouched.
"""
import json, math

Z = ["Rim","Short","Mid","Long","Three"]

# ===========================================================================
# CONFIG KNOBS — [ALL CALIBRATION PLACEHOLDERS], page-tuned after the build.
# The C# build reads these from config; the golden pins these exact values.
# ===========================================================================
FS_W, Q_W              = 0.62, 0.38     # first-step-led beat (FirstStep primary, Quickness support)
HANDLE_LO, HANDLE_HI   = 28.0, 48.0     # handle UNLOCK ramp: 0 at/below LO, full at/above HI
GATE_STEEP, GATE_EXP   = 1.0, 1.4       # suppression gap-fn (defender-edge side only)
GATE_TANH_REF          = 1.5            # tanh softness on the suppression shift
GATE_CAP               = 0.55           # max fraction of gate-eligible paint an elite wall removes
SHORT_ELIG             = 0.45           # Short is only partly drive-derived (floaters, not post/cuts)
POST_PIVOT, POST_RANGE = 46.0, 26.0     # orientation: perimeter=1 at/below pivot -> 0 by pivot+range
REF_SCALE              = 25.0           # shared gap-fn normalizer (mirrors Matchup.ReferenceScale)

def clamp01(x): return 0.0 if x < 0 else 1.0 if x > 1 else x
def gapfn(g, steep, exp, ref=REF_SCALE):
    s = 1.0 if g >= 0 else -1.0
    return s * steep * (abs(g)/ref)**exp

# ---- the drive-gate primitives (these become Matchup.cs statics) ----------
def drive_tools(sh):
    """First step beats him (Quickness supports), handle unlocks it (0 = no handle)."""
    beat   = FS_W*sh["FirstStep"] + Q_W*sh["Quickness"]
    unlock = clamp01((sh["BallHandling"] - HANDLE_LO) / (HANDLE_HI - HANDLE_LO))
    return beat * unlock

def orientation(sh):
    """Perimeter-oriented ONLY (low postness). Conversion is the composite's job."""
    postness = (sh["Height"] + sh["Strength"] + sh["PostMoves"]) / 3.0
    return clamp01(1.0 - (postness - POST_PIVOT) / POST_RANGE)

def gate(before, sh, matched_perimD, bypass=False):
    """Pure transform: post-displacement pie -> post-gate pie. bypass = FastBreak / zero-defender."""
    if bypass:
        return dict(before), dict(comp=0.0, gap=0.0, orient=0.0, mult=1.0, removed=0.0, bypass=True)
    comp   = drive_tools(sh)
    gap    = comp - matched_perimD                       # +: offense beats the man; -: walled
    supp   = gapfn(max(0.0, -gap), GATE_STEEP, GATE_EXP)  # suppression-primary: only the wall side
    mult   = 1.0 - GATE_CAP * math.tanh(supp / GATE_TANH_REF)   # 1.0 = no suppression
    orient = orientation(sh)
    rim_rem   = before["Rim"]              * orient * (1.0 - mult)
    short_rem = before["Short"]*SHORT_ELIG * orient * (1.0 - mult)
    removed   = rim_rem + short_rem
    after = dict(before); after["Rim"] -= rim_rem; after["Short"] -= short_rem
    # redistribute to contested Long/Three ONLY, proportional to the shooter's outer preference
    ob = {z: before[z] for z in ("Long","Three")}; obs = sum(ob.values())
    if obs <= 0: ob = {"Long":1.0,"Three":1.0}; obs = 2.0
    for z in ("Long","Three"): after[z] += removed * ob[z]/obs
    s = sum(after.values()); after = {z: after[z]/s for z in Z}   # renormalize (exact; mass conserved)
    return after, dict(comp=comp, gap=gap, orient=orient, mult=mult, removed=removed, bypass=False)

# ===========================================================================
# DISPLACEMENT (mirrored from tools/displacement_oracle.py) — used ONLY to
# generate realistic post-displacement "before" pies for the archetype cases.
# The golden stores those pies as fixed inputs, so the gate is tested in
# isolation and the displacement regression stays the S36 fixture's job.
# ===========================================================================
SKILL_STEEP,SKILL_EXP,DREF_SCALE = 6.0,2.0,25.0
DPHYS_EXP=1.75; LOC_MAX_MULT,LOC_REF_SHIFT=2.5,20.0; LOC_BLEND=(0.55,0.30,0.15)
DEF_BLEND={"Rim":(0.00,0.35,0.65),"Short":(0.15,0.85,0.00),"Mid":(0.50,0.50,0.00),
           "Long":(0.85,0.15,0.00),"Three":(1.00,0.00,0.00)}
OFF_KEY={"Rim":"Finishing","Short":"Close","Mid":"Mid","Long":"Outside","Three":"Outside"}
DISP_PHYS_STEEP=3.0; DISP_REF=20.0; DISP_MAX=0.35; DISP_USAGE_SCALE=3.0
LADDER={"Rim":+2.0,"Short":+1.0,"Mid":0.0,"Long":-1.0,"Three":-2.0}
GATE_RIM=(38.0,72.0); GATE_SHORT=(36.0,68.0)
def _clamp(x,lo,hi): return lo if x<lo else hi if x>hi else x
def _g(x,lo,hi): return _clamp((x-lo)/(hi-lo),0.0,1.0)
def _gf(g,steep=SKILL_STEEP,exp=SKILL_EXP,ref=DREF_SCALE):
    s=1.0 if g>=0 else -1.0; return s*steep*(abs(g)/ref)**exp
def _ath(p): return (p["Strength"]+p["Speed"]+p["Quickness"]+p["FirstStep"]+p["Vertical"])/5.0
def _dr(z,d):
    pw,po,ri=DEF_BLEND[z]; return pw*d["PerimeterDefense"]+po*d["PostDefense"]+ri*d["RimProtection"]
def _res(z,ds):
    sc=sorted((_dr(z,d) for d in ds),reverse=True)[:3]; w=LOC_BLEND[:len(sc)]; ws=sum(w)
    return sum((w[i]/ws)*sc[i] for i in range(len(sc)))
def _lm(g): return math.exp(math.log(LOC_MAX_MULT)*math.tanh(_gf(g)/LOC_REF_SHIFT))
def displaced(diet,sh,ds,up):
    tot=sum(diet.values()); base={z:diet[z]/tot for z in Z}
    gaps={z:sh[OFF_KEY[z]]-_res(z,ds) for z in Z}; sLvl=sum(base[z]*gaps[z] for z in Z)
    lm=sum(_ath(d) for d in ds)/len(ds); level=sLvl+_gf(_ath(sh)-lm,DISP_PHYS_STEEP,DPHYS_EXP)
    resid={z:gaps[z]-sLvl for z in Z}
    bent={z:base[z]*_lm(resid[z]) for z in Z}; sb=sum(bent.values()); bent={z:bent[z]/sb for z in Z}
    d9={z:bent[z]-base[z] for z in Z}
    mag=DISP_MAX*math.tanh(level/DISP_REF)*min(1.0,DISP_USAGE_SCALE*up)
    L=dict(LADDER)
    if mag>0: L["Rim"]=2.0*_g(sh["Finishing"],*GATE_RIM); L["Short"]=1.0*_g(sh["Close"],*GATE_SHORT)
    disp={z:max(0.0,base[z]*(1.0+mag*L[z])) for z in Z}; sd=sum(disp.values()); disp={z:disp[z]/sd for z in Z}
    out={z:max(0.0,base[z]+d9[z]+(disp[z]-base[z])) for z in Z}; so=sum(out.values())
    return {z:out[z]/so for z in Z}

# ---- builders --------------------------------------------------------------
def SH(name,diet,fin,clo,mid,out,bh,fs,q,H=44,St=44,pm=25,ath=None):
    a=ath if ath is not None else (fs+q)//2
    return dict(name=name,diet=diet,Finishing=fin,Close=clo,Mid=mid,Outside=out,
                BallHandling=bh,FirstStep=fs,Quickness=q,Height=H,Strength=St,PostMoves=pm,Speed=a,Vertical=a)
def DEFN(pe,po,ri,a=50): return dict(PerimeterDefense=pe,PostDefense=po,RimProtection=ri,Strength=a,Speed=a,Quickness=a,FirstStep=a,Vertical=a)
def five(d): return [d]*5

GUARD=dict(Rim=0.28,Short=0.15,Mid=0.14,Long=0.11,Three=0.32)
PF   =dict(Rim=0.34,Short=0.16,Mid=0.13,Long=0.10,Three=0.27)
POSTM=dict(Rim=0.46,Short=0.24,Mid=0.12,Long=0.08,Three=0.10)
ELITE=DEFN(85,55,50,58); AVG=DEFN(50,50,50,50); POOR=DEFN(25,40,45,45); SOFT=[DEFN(85,50,30,58)]*5

# Each archetype: (name, shooter, matched defender, lineup for the "before" pie, usage, bypass)
ARCH=[
 ("avg creator vs AVG",            SH("avg",GUARD,50,50,50,50,50,50,50), AVG,  five(AVG),0.15,False),
 ("avg creator vs ELITE",          SH("avg",GUARD,50,50,50,50,50,50,50), ELITE,five(ELITE),0.15,False),
 ("ELITE handle/avg burst vs ELITE",SH("eh",GUARD,55,55,50,52,88,50,50), ELITE,five(ELITE),0.15,False),
 ("avg handle/ELITE firststep vs ELITE",SH("efs",GUARD,55,52,50,50,50,88,72),ELITE,five(ELITE),0.15,False),
 ("NO handle/quick vs AVG",        SH("nh",GUARD,50,50,50,50,28,85,72), AVG,  five(AVG),0.15,False),
 ("ELITE handle+burst vs ELITE",   SH("ec",GUARD,60,58,52,55,88,88,85), ELITE,five(ELITE),0.15,False),
 ("elite creator vs POOR",         SH("ec",GUARD,60,58,52,55,88,88,85), POOR, five(POOR),0.15,False),
 ("POST scorer vs ELITE",          SH("post",POSTM,72,70,45,30,32,45,45,H=82,St=80,pm=85),ELITE,five(ELITE),0.15,False),
 ("point-FORWARD vs ELITE",        SH("pf",PF,66,60,52,54,74,72,66,H=62,St=60,pm=40),ELITE,five(ELITE),0.15,False),
 ("OVERRIDE soft paint + ELITE",   SH("avg",GUARD,50,50,50,52,50,50,50), ELITE,SOFT,0.15,False),
 ("reassigned to WEAK matched",    SH("avg",GUARD,50,50,50,50,50,50,50), POOR, five(AVG),0.15,False),
 ("BYPASS fastbreak/zero-def",     SH("avg",GUARD,50,50,50,50,50,50,50), AVG,  five(AVG),0.15,True),
]

def table():
    def pct(p): return "  ".join(f"{z[:3]}{p[z]*100:5.1f}" for z in Z)
    print("PASS A DRIVE GATE — locked archetype table  (shot diet %, before = post-displacement)")
    print(f"beat={FS_W}*FS+{Q_W}*Q x handle-unlock[{HANDLE_LO}..{HANDLE_HI}]  CAP={GATE_CAP}  SHORT_ELIG={SHORT_ELIG}  [PLACEHOLDERS]\n")
    for name,sh,md,ln,up,bp in ARCH:
        before=displaced(sh["diet"],sh,ln,up); after,d=gate(before,sh,md["PerimeterDefense"],bp)
        print(f"── {name}")
        print(f"    comp={d['comp']:5.1f} perimD={md['PerimeterDefense']:5.1f} gap={d['gap']:+6.1f} orient={d['orient']:.2f} mult={d['mult']:.3f} removed={d['removed']*100:4.1f}pp"+("  [BYPASS]" if bp else ""))
        print(f"    before {pct(before)}")
        print(f"    after  {pct(after)}   Δrim={(after['Rim']-before['Rim'])*100:+5.1f} Δmid={(after['Mid']-before['Mid'])*100:+4.1f} Δthree={(after['Three']-before['Three'])*100:+5.1f}  Σ={sum(after.values()):.12f}")
    print()

# ===========================================================================
# STRUCTURAL INVARIANTS (become the Phase-N harness checks)
# ===========================================================================
def checks():
    ok=True
    def chk(name,cond,d=""):
        nonlocal ok; ok=ok and cond
        print(f"  [{'OK' if cond else 'FAIL'}] {name}"+(f" — {d}" if d else ""))
    P=dict(Rim=0.28,Short=0.15,Mid=0.14,Long=0.11,Three=0.32)
    drv=lambda bh,fs,q,H=44,St=44,pm=25: dict(BallHandling=bh,FirstStep=fs,Quickness=q,Height=H,Strength=St,PostMoves=pm)

    # neutrality: even matchup -> untouched
    a,d=gate(P,drv(50,50,50),50.0)
    chk("neutral (comp==perimD) -> pie unchanged", all(abs(a[z]-P[z])<1e-12 for z in Z), f"mult={d['mult']:.3f}")
    # conservation across a grid
    cons=True; mono_pd=True; mono_comp=True; asym=True
    prev=None
    for pd in [25,40,50,65,85,99]:
        a,d=gate(P,drv(50,50,50),float(pd))
        cons=cons and abs(sum(a.values())-1.0)<1e-12
        if prev is not None: mono_pd=mono_pd and (d['removed']>=prev-1e-12)   # more perimD -> >= removal
        prev=d['removed']
        if d['gap']>=0: asym=asym and d['removed']<1e-12                      # offense edge -> no removal
    chk("conservation (Σafter==1) across perimD grid", cons)
    chk("monotone in perimD (removal non-decreasing)", mono_pd)
    chk("suppression-primary (offense edge -> zero removal)", asym)
    # monotone in drive tools (fix elite defender, raise tools -> removal non-increasing)
    prev=None
    for tv in [30,50,70,88,99]:
        a,d=gate(P,drv(tv,tv,tv),85.0)
        if prev is not None: mono_comp=mono_comp and (d['removed']<=prev+1e-12)
        prev=d['removed']
    chk("monotone in drive tools (removal non-increasing)", mono_comp)
    # first-step-beats-handle: elite handle/avg burst walled >= avg handle/elite firststep
    _,dh=gate(P,drv(88,50,50),85.0); _,df=gate(P,drv(50,88,72),85.0)
    chk("first step beats, handle only unlocks (eliteHandle removal >= eliteFirstStep)",
        dh['removed']>=df['removed'], f"eH={dh['removed']*100:.1f}pp eFS={df['removed']*100:.1f}pp")
    # no-handle quick -> composite 0 -> walled when perimD>0
    _,dn=gate(P,drv(28,85,72),50.0)
    chk("no-handle quick -> zero drive tools -> walled", dn['comp']<1e-9 and dn['removed']>0, f"comp={dn['comp']:.2f}")
    # orientation: post immune, guard eligible
    _,dp=gate(P,drv(32,45,45,H=82,St=80,pm=85),85.0)
    _,dg=gate(P,drv(50,50,50),85.0)
    chk("post-oriented immune (orient 0 -> zero removal)", dp['orient']<1e-12 and dp['removed']<1e-12)
    chk("perimeter guard eligible (orient>0)", dg['orient']>0)
    # redistribution: Mid never changes; Rim/Short only fall; goes to Long/Three
    a,d=gate(P,drv(50,50,50),85.0)
    chk("Mid untouched; Rim/Short fall; Long/Three rise",
        abs(a["Mid"]-P["Mid"])<1e-12 and a["Rim"]<P["Rim"] and a["Short"]<P["Short"]
        and a["Long"]>P["Long"] and a["Three"]>P["Three"])
    # kill switch: CAP 0 -> identity
    global GATE_CAP; keep=GATE_CAP; GATE_CAP=0.0
    a,d=gate(P,drv(50,50,50),85.0); chk("kill switch (CAP=0) -> pie unchanged", all(abs(a[z]-P[z])<1e-12 for z in Z)); GATE_CAP=keep
    # bypass
    a,d=gate(P,drv(50,50,50),85.0,bypass=True); chk("bypass (fastbreak/zero-def) -> pie unchanged", all(abs(a[z]-P[z])<1e-12 for z in Z))
    print(f"\n  INVARIANTS: {'ALL OK' if ok else 'FAIL'}")
    return ok

# ===========================================================================
# GOLDEN EMITTER
# ===========================================================================
def emit_golden(path):
    consts=dict(FS_W=FS_W,Q_W=Q_W,HANDLE_LO=HANDLE_LO,HANDLE_HI=HANDLE_HI,GATE_STEEP=GATE_STEEP,
                GATE_EXP=GATE_EXP,GATE_TANH_REF=GATE_TANH_REF,GATE_CAP=GATE_CAP,SHORT_ELIG=SHORT_ELIG,
                POST_PIVOT=POST_PIVOT,POST_RANGE=POST_RANGE,REF_SCALE=REF_SCALE)
    cases=[]
    for name,sh,md,ln,up,bp in ARCH:
        before=displaced(sh["diet"],sh,ln,up); after,d=gate(before,sh,md["PerimeterDefense"],bp)
        cases.append(dict(name=name,bypass=bp,
            shooter={k:sh[k] for k in ("FirstStep","Quickness","BallHandling","Height","Strength","PostMoves")},
            matchedPerimeterDefense=md["PerimeterDefense"],
            before={z:before[z] for z in Z},
            driveComposite=d["comp"], orient=d["orient"], suppressionMult=d["mult"], removed=d["removed"],
            after={z:after[z] for z in Z}))
    golden=dict(_comment="PASS A drive-gate golden. Emitted from tools/drive_gate_oracle.py. Locks the "
                "gate TRANSFORM (before->after) + internals per case; 'before' pies are fixed inputs so "
                "the gate is tested in isolation (displacement regression stays the S36 fixture). "
                "Magnitudes are placeholders; constants cross-checked vs config before use.",
                constants=consts, tolerance=1e-12, cases=cases)
    json.dump(golden, open(path,"w"), indent=2)
    return golden

if __name__=="__main__":
    table()
    print("--- structural invariants ---")
    allok=checks()
    g=emit_golden("tools/drive_gate_golden.json")
    print(f"\nwrote tools/drive_gate_golden.json — {len(g['cases'])} cases, tol {g['tolerance']}")
    # self-verify: re-read golden and re-apply the gate to each stored 'before'; confirm parity
    worst=0.0
    for c in g["cases"]:
        sh=dict(c["shooter"]); a,d=gate(c["before"],sh,c["matchedPerimeterDefense"],c["bypass"])
        for z in Z: worst=max(worst,abs(a[z]-c["after"][z]))
        worst=max(worst,abs(d["comp"]-c["driveComposite"]),abs(d["mult"]-c["suppressionMult"]),abs(d["removed"]-c["removed"]))
    print(f"golden self-parity worst |Δ| = {worst:.2e}  ({'OK' if worst<1e-12 else 'FAIL'})")
    print("ALL GOOD" if allok and worst<1e-12 else "PROBLEM")
