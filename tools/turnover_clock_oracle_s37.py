# Session 37 pace/turnover-clock pre-validation oracle. Reproduces ClockDraw.Sample
# exactly; validates the model against the current page (no-bands -> pace 65.5), then
# predicts pace and the raw band means once the court-aware bands are introduced.
# Ruling: Center stays 17.0 (the bands close the pace gap). Run: python3 tools/turnover_clock_oracle_s37.py
import math, random

def sample(center, sd, floor, ceiling, rng):
    for _ in range(100):
        u1 = rng.random(); u2 = rng.random()
        if u1 < 1e-12: u1 = 1e-12
        z = math.sqrt(-2.0*math.log(u1))*math.cos(2.0*math.pi*u2)
        x = center + sd*z
        if floor <= x < ceiling: return x
    return min(max(center, floor), math.nextafter(ceiling, floor))

FLOOR, FULL, RESET, SD = 4.0, 30.0, 20.0, 4.5
RESET_SCALE = RESET/FULL
BK=(5.0,2.0,1.0,10.0); FC=(14.5,5.5,6.0,30.0)
N=300_000

rng=random.Random(20260703)
bk=sum(sample(*BK,rng) for _ in range(N))/N
fc=sum(sample(*FC,rng) for _ in range(N))/N
print(f"raw backcourt-turnover  mean = {bk:.2f}s  (band [1,10), c=5.0  sd=2.0)")
print(f"raw frontcourt-turnover mean = {fc:.2f}s  (band [6,30), c=14.5 sd=5.5)")
print()

def muS(center, reset_rate, n=300_000):
    r=random.Random(11); tot=0.0
    for _ in range(n):
        s=sample(center,SD,FLOOR,FULL,r)
        if r.random()<reset_rate: s+=sample(center*RESET_SCALE,SD*RESET_SCALE,FLOOR,RESET,r)
        tot+=s
    return tot/n

# fit muS(C) as a line over the working range at reset 0.12 (and endpoints for reset sweep)
def fit(rr):
    xs=[15.0,17.0,19.0]; ys=[muS(c,rr) for c in xs]
    # linear fit
    n=len(xs); sx=sum(xs); sy=sum(ys); sxx=sum(x*x for x in xs); sxy=sum(x*y for x,y in zip(xs,ys))
    m=(n*sxy-sx*sy)/(n*sxx-sx*sx); b=(sy-m*sx)/n
    return m,b,ys[1]
lines={rr:fit(rr) for rr in (0.10,0.12,0.14)}
for rr in (0.10,0.12,0.14):
    m,b,y17=lines[rr]
    print(f"  muS(17.0, reset_rate={rr:.2f}) = {y17:.2f}s   [page anchor ~18.5]   (muS≈{m:.3f}·C+{b:.2f})")
print()

TO=0.204
def solve(rr,bf,invF,muI):
    m,b,_=lines[rr]
    def muSf(C): return m*C+b
    wI=TO*invF; wB=TO-wI; wS=1-TO; muB=bf*bk+(1-bf)*fc
    def mean_all(C): return wS*muSf(C)+wB*muB+wI*muI
    target=1200.0/69.0
    lo,hi=10.0,24.0
    for _ in range(40):
        mid=(lo+hi)/2
        if mean_all(mid)<target: lo=mid
        else: hi=mid
    Cstar=(lo+hi)/2
    return Cstar, 1200.0/mean_all(17.0), muB

print("at CURRENT Center=17 once bands are in, and the Center that hits pace 69:")
print(f"{'reset':>5} {'bf':>5} {'invF':>5} {'muI':>5} | {'muB':>6} {'pace@17':>8} {'C* for 69':>10}")
central=None
for bf in (0.10,0.25,0.40):
    Cs,p17,muB=solve(0.12,bf,0.05,15.3)
    tag="  <- central" if bf==0.25 else ""
    if bf==0.25: central=(Cs,p17,muB)
    print(f"{0.12:>5.2f} {bf:>5.2f} {0.05:>5.2f} {15.3:>5.1f} | {muB:>6.2f} {p17:>8.2f} {Cs:>10.2f}{tag}")
print()

Cs=[solve(rr,bf,iv,mi)[0] for rr in (0.10,0.12,0.14) for bf in (0.10,0.20,0.25,0.30,0.40)
    for iv in (0.03,0.05,0.08) for mi in (12.0,15.3,18.0)]
print(f"C* range across full sweep (reset10-14/bf10-40/invF3-8/muI12-18): {min(Cs):.2f} .. {max(Cs):.2f}  median {sorted(Cs)[len(Cs)//2]:.2f}")

Cc=central[0]
print(f"\npace sensitivity to backcourt split at Center={Cc:.2f} (reset0.12 invF0.05):")
for bf in (0.10,0.20,0.30,0.40):
    m,b,_=lines[0.12]; wI=TO*0.05; wB=TO-wI; wS=1-TO; muB=bf*bk+(1-bf)*fc
    p=1200.0/(wS*(m*Cc+b)+wB*muB+wI*15.3)
    print(f"  bf={bf:.2f}: muB={muB:.2f}  pace={p:.2f}")

# consistency: NO bands at Center 17 should reproduce ~65.5
m,b,_=lines[0.12]; wI=TO*0.05
p_nobands=1200.0/((1-wI)*(m*17+b)+wI*15.3)
print(f"\nconsistency check — NO bands, Center 17 (turnovers draw shared): pace={p_nobands:.2f}  [page: 65.5]")
