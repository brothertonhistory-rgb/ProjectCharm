#!/usr/bin/env python3
"""
S46 — Rebounder-picker body floor + luck weight + saturating loose-ball floor:
the archetype-table / height-ladder derivation record (retained per the session prompt).

Models ONLY step 2 (the individual rebounder pickers), mirroring the C# weight formula
constant-for-constant.

THE LOCKED SPEC (Emmett's S46 + S46b rulings)
---------------------------------------------
Per populated player i in the five-man lineup:

    skill_i    = Rating_i * posWeight_i * wingspanMult_i * hustleMult_i
    bodyPull_i = BODY_PULL * max(0, ReboundPhysical_i - lineupMeanReboundPhysical)   # RELATIVE
    absFloor_i = FLOOR_CEIL * tanh( max(0, ReboundPhysical_i - FLOOR_REF) / FLOOR_SCALE )  # ABSOLUTE
    weight_i   = LUCK + skill_i + bodyPull_i + absFloor_i                (defensive picker)
    weight_i   = (LUCK + skill_i + bodyPull_i + absFloor_i) * shooterNerf (offensive picker)

  - LUCK = 5.0: every player's EQUAL claim on uncontested bounces. Replaces the retired
    max(1, ...) floor.
  - BODY_PULL = 0.35 (relative pull, one-sided): how much you out-size YOUR lineup. Rewards
    standing out; an average body on an average team earns nothing here (it ties with a
    small body — both sit at their lineup mean). This is why a second channel is needed.
  - FLOOR_CEIL = 4.0, FLOOR_SCALE = 40.0, FLOOR_REF = 22.5 (absolute saturating floor):
    raw size vs a FIXED reference, so a bigger body vacuums up more random loose balls
    regardless of teammates. Saturates (tanh) so a genuine big does not balloon — the
    freak's extra size pays through the relative pull, not here. FLOOR_REF = 22.5 is the
    physical of a ~5'2 extreme body (H/W/S ~ 15); at/below it the floor is 0.
  - Body composite = ReboundPhysical (0.525*Str + 0.4875*Ht + 0.4875*Wing) — the team
    battle's own body definition.
  - Offensive side: the shooter nerf multiplies the WHOLE weight (luck, both body terms
    included) — S46 ruling (nerf models reduced availability after shooting).

WHAT S46b FIXED (the zero-rating height ladder)
-----------------------------------------------
The relative-pull-only formula left the bottom of the ladder mushy: every zero-rating
player from ~5'2 to ~5'11 sat at ~0.7-0.85 boards, because none clears his lineup mean.
The saturating floor lifts and separates it into a clean monotone rise while barely
touching the tall guys (who already worked). Signed-said table below.

SHARE -> BOARDS/GAME CONVERSION BASIS (technical appendix)
----------------------------------------------------------
Each archetype's opportunity count is anchored to the ACTUAL shipped S46 sweep result
(pre-S46b) under the relative-pull-only formula; team totals for the height ladder are
interpolated from the three measured zero-rating team totals (body 15/50/99 -> 37.2/38.1/
39.6, A.TotR). DIRECTIONAL for the offensive side (putback loop). The AUTHORITATIVE gate
is the live sweep re-run (sweep_interaction.json + a zero-rating height-ladder walk,
baseSeed 1000, 2000 games/rung). Hustle 50 everywhere (hm=1); shooter nerf omitted
identically in before/after so it cancels.
"""
import math

# ── constants (mirror config.json / MatchupConfig, verified S46b) ────────────
LUCK        = 5.0     # ReboundLuckWeight
BODY_PULL   = 0.35    # ReboundBodyPullWeight       (relative, one-sided)
FLOOR_CEIL  = 4.0     # ReboundBodyFloorCeiling     (absolute saturating floor)
FLOOR_SCALE = 40.0    # ReboundBodyFloorScale
FLOOR_REF   = 22.5    # ReboundBodyFloorReference   (~body-15 physical)
POS_SWING, POS_SCALE = 0.2, 15.0
WS_SWING,  WS_SCALE  = 0.1, 15.0
RP_S, RP_H, RP_W     = 0.525, 0.4875, 0.4875
PN_THIRD             = 1.0 / 3.0

def postness(h, pd, s):    return PN_THIRD * (h + pd + s)
def reb_physical(h, w, s): return RP_S*s + RP_H*h + RP_W*w
def pos_weight(pn, mean):  return 1.0 + POS_SWING * math.tanh((pn - mean) / POS_SCALE)
def ws_mult(ws, mean):     return 1.0 + WS_SWING  * math.tanh((ws - mean) / WS_SCALE)

def lineup(body, rating):
    flat  = dict(H=50, PD=50, S=50, W=50, R=50)
    swept = dict(H=body, PD=50, S=body, W=body, R=rating)
    return [flat, flat, flat, flat, swept]

def weights(players, with_floor=True):
    pns = [postness(p['H'], p['PD'], p['S']) for p in players]
    wss = [p['W'] for p in players]
    rps = [reb_physical(p['H'], p['W'], p['S']) for p in players]
    mpn, mws, mrp = sum(pns)/5, sum(wss)/5, sum(rps)/5
    out = []
    for p, pn, ws, rp in zip(players, pns, wss, rps):
        skill = p['R'] * pos_weight(pn, mpn) * ws_mult(ws, mws)     # hm = 1
        pull  = BODY_PULL * max(0.0, rp - mrp)
        floor = FLOOR_CEIL * math.tanh(max(0.0, rp - FLOOR_REF) / FLOOR_SCALE) if with_floor else 0.0
        out.append(LUCK + skill + pull + floor)
    return out

def share(ws, i): return ws[i] / sum(ws)

# ── S45 archetype anchors: (body, rating, S45 boards) + shipped-S46 measured ──
S45 = [
    ('freak, no hands (99/0)',     99,  0,  0.2,  4.56),
    ('freak, elite   (99/99)',     99, 99, 17.3, 17.99),
    ('weakling, elite(15/99)',     15, 99,  9.6,  9.00),
    ('weakling,no hnd(15/0)',      15,  0,  0.2,  0.71),
    ('average, elite (50/99)',     50, 99, 12.9, 12.50),
    ('average,no hnd (50/0)',      50,  0,  0.2,  0.85),
    ('flat-50 control',            50, 50,  7.7,  7.69),
]

def archetype_table():
    print(f"{'archetype':24} {'S45':>5} {'S46ship':>8} {'S46b':>6}")
    for label, body, rating, s45, ship in S45:
        # anchor opportunities to the shipped (relative-pull-only) result
        no_floor = share(weights(lineup(body, rating), with_floor=False), 4)
        opps = ship / no_floor
        after = share(weights(lineup(body, rating), with_floor=True), 4)
        print(f"{label:24} {s45:>5} {ship:>8} {after*opps:>6.2f}")

def height_ladder():
    tot = [(15,37.2),(50,38.1),(99,39.6)]  # zero-rating team totals (A.TotR)
    def team_total(b):
        for (b0,t0),(b1,t1) in zip(tot, tot[1:]):
            if b<=b1: return t0+(t1-t0)*(b-b0)/(b1-b0)
        return tot[-1][1]
    ladder=[(15,"~5'2 extreme"),(40,"5'8"),(48,"5'11"),(53,"6'0"),(62,"6'4"),
            (68,"6'6"),(75,"6'8"),(83,"6'11"),(90,"7'1"),(99,"7'3+")]
    print(f"\nZERO-RATING HEIGHT LADDER (boards/game)")
    print(f"{'body':>4} {'height':>13} {'shipped':>8} {'S46b':>6}")
    for b,h in ladder:
        nf = share(weights(lineup(b,0), with_floor=False), 4) * team_total(b)
        wf = share(weights(lineup(b,0), with_floor=True),  4) * team_total(b)
        print(f"{b:>4} {h:>13} {nf:>8.2f} {wf:>6.2f}")

def invariants():
    ok = True
    s = [share(weights(lineup(50,50)), i) for i in range(5)]
    if any(abs(x-0.2) > 1e-12 for x in s): ok=False; print("FAIL flat-50 uniform", s)
    prev=-1.0
    for b in range(0,100): 
        sh=share(weights(lineup(b,0)),4)
        if sh<prev-1e-12: ok=False; print("FAIL body-monotone", b)
        prev=sh
    prev=-1.0
    for r in range(0,100):
        sh=share(weights(lineup(50,r)),4)
        if sh<prev-1e-12: ok=False; print("FAIL rating-monotone", r)
        prev=sh
    for body in (15,50,99):
        for rating in (0,50,99):
            if min(weights(lineup(body,rating))) <= 0: ok=False; print("FAIL nonpos", body, rating)
    print("\ninvariants:", "ALL PASS" if ok else "FAILURES ABOVE")

def harness_bounds():
    def sh(players, idx, nerf_idx=None):
        pns=[postness(h,pd,s) for h,pd,s,w,r in players]; wss=[w for *_,w,r in players]
        rps=[reb_physical(h,w,s) for h,pd,s,w,r in players]
        mpn,mws=sum(pns)/5,sum(wss)/5
        out=[]
        for i,(h,pd,s,w,r) in enumerate(players):
            pull=BODY_PULL*max(0.0, rps[i]-sum(rps)/5)
            floor=FLOOR_CEIL*math.tanh(max(0.0,rps[i]-FLOOR_REF)/FLOOR_SCALE)
            base=LUCK+r*pos_weight(pns[i],mpn)*ws_mult(w,mws)+pull+floor
            if i==nerf_idx: base*=0.35
            out.append(base)
        return out[idx]/sum(out)
    weak,dom=(40,42,44,40,12),(90,80,88,50,85)
    m=sh([weak]*4+[dom],4)/sh([weak]*4+[dom],0)
    print(f"P31 dominant mult {m:.2f}x (>3) {'OK' if m>3 else 'FAIL'}")
    g,e=(38,42,42,50,10),(85,80,82,60,88)
    print(f"P31 buried guard {sh([g]+[e]*4,0):.1%} (<6%) {'OK' if sh([g]+[e]*4,0)<0.06 else 'FAIL'}")
    pgL=[(40,42,44,50,35),(45,44,46,50,42),(55,55,55,50,52),(72,70,72,50,72),(86,82,82,50,85)]
    print(f"P35 PG floor {sh(pgL,0):.1%} (>1%) {'OK' if sh(pgL,0)>0.01 else 'FAIL'}")
    p=[(50,50,50,50,50)]*4+[(90,80,88,50,85)]
    print(f"P31 nerf rim {sh(p,4):.1%} vs three {sh(p,4,4):.1%} {'OK' if sh(p,4,4)<sh(p,4) else 'FAIL'}")

if __name__ == "__main__":
    print("=== S46b locked: LUCK 5.0 | relative pull 0.35 | floor ceil 4.0 scale 40 ref 22.5 ===\n")
    archetype_table()
    height_ladder()
    invariants()
    print()
    harness_bounds()
