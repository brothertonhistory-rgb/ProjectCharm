"""
S86 ORACLE (LOCKED) - the transition opportunity score and the coach bar.
Emmett approved the archetype tables produced by this file on 2026-07-30
("those all feel pretty good to me"). The C# port is held to golden parity
against these numbers. Do not retune dials without a new ruling.

Rulings encoded (S85 design conversation):
  R1  The coach is the GATE, not a tiebreaker: player inputs build an
      OPPORTUNITY score, the coach's pace sets the BAR it must clear.
      Grinders run only when the break is nearly free.
  R2  The rebounder's two escape routes (speed to lead it, passing to move
      it ahead) OVERLAP: elite-at-both beats elite-at-one by ~2 points of
      push, not double (the LeBron row).
  R3  A great outlet on a slow big creates breaks that weren't there before
      - a modest lift, not a substitute for legs (needs receivers: the two
      Unseld rows differ by ~7 points at neutral).
  R4  Teammates' SPEED specifically (not the 5-way athleticism blend) is
      the race term; the defense's speed getting back opposes it.
  R5  The bar is fixed per team NOW and is the single lever a future
      coaching brain moves (O-57 era). Player math and coach bar are never
      pre-fused.
"""
import math

# ── LOCKED DIALS ────────────────────────────────────────────────────────────
OVERLAP_CREDIT = 0.35   # R2: the second escape route is worth ~a third of the first
ESCAPE_WEIGHT  = 0.55   # rebounder-dominant blend (ruled: combo, ball-handler first)
RACE_WEIGHT    = 0.45
RACE_CENTER    = 0.50   # an even race contributes its half; keeps opp in ~[0,1]
BAR_BASE       = 0.475  # tuned so league-average at neutral pace = 33.5% (S85 realized)
BAR_PACE_SWING = 0.10   # grind bar 0.535 / neutral 0.475 / run 0.395
PUSH_SWING     = 0.22   # max settle<->push transfer before the engine's bounded clamp
MARGIN_SCALE   = 0.16   # tanh softness: comfortable clear -> runs; bad miss -> almost never

def escape(spd_r, pass_r):
    """The rebounder/stealer's best route plus overlap credit for the second, normed to [0,1]."""
    a, b = max(spd_r, pass_r) / 100.0, min(spd_r, pass_r) / 100.0
    return (a + OVERLAP_CREDIT * b) / (1.0 + OVERLAP_CREDIT)

def opportunity(spd_r, pass_r, mates_spd, def_spd):
    race = (mates_spd - def_spd) / 100.0
    return ESCAPE_WEIGHT * escape(spd_r, pass_r) + RACE_WEIGHT * (race + RACE_CENTER)

def bar(pace_bias_1_to_10):
    mapped = (pace_bias_1_to_10 - 5.0) / 5.0     # exactly today's pace mapping
    return BAR_BASE - mapped * BAR_PACE_SWING

def push_pct(base_push, base_settle, spd_r, pass_r, mates_spd, def_spd, pace):
    """Neutral rule: a caller with no ball-handler or no OffenseSide passes margin=0 -> base weights."""
    margin = opportunity(spd_r, pass_r, mates_spd, def_spd) - bar(pace)
    raw    = PUSH_SWING * math.tanh(margin / MARGIN_SCALE)
    t      = max(-base_push, min(base_settle, raw))   # the engine's exact bounded transfer
    return base_push + t

def emit_golden():
    """Full-precision push probabilities for the B1 fixture. Dials and shape are the lock;
    this print mode is additive. C# parity bar: absolute 1e-6 per cell."""
    cases = []
    for bp, bs, tag, rows in [
        (0.30, 0.60, "REB", [(85,55,60,50),(30,90,80,50),(30,90,40,50),(90,90,60,50),
                             (90,30,60,50),(30,30,50,50),(55,50,55,80),(52,45,52,52)]),
        (0.55, 0.35, "BCS", [(85,55,60,50),(30,35,50,50),(52,45,52,52),(55,50,55,80)]),
        (0.35, 0.55, "FCS", [(85,55,60,50),(30,35,50,50),(52,45,52,52)])]:
        for s_,p_,m_,d_ in rows:
            for pc in (2.0,5.0,9.0):
                cases.append((tag,s_,p_,m_,d_,pc,push_pct(bp,bs,s_,p_,m_,d_,pc)))
    for c in cases:
        print("%s spd=%d pass=%d mates=%d def=%d pace=%.0f push=%.12f" % c)

if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1 and sys.argv[1] == "--emit-golden":
        emit_golden(); sys.exit(0)
    def table(title, bp, bs, rows):
        print(f"\n{title}  (base push {100*bp:.1f}% flat today)")
        print(f"  {'archetype':34}{'grind-2':>9}{'neutral-5':>11}{'run-9':>8}")
        for n, s, p, m, d in rows:
            print(f"  {n:34}" + "".join(f"{100*push_pct(bp,bs,s,p,m,d,pc):8.1f}%" for pc in (2, 5, 9)))

    table("REBOUND", 0.30, 0.60, [
        ("Fast guard rips the board",     85, 55, 60, 50),
        ("Wes Unseld, four greyhounds",   30, 90, 80, 50),
        ("Wes Unseld, four plodders",     30, 90, 40, 50),
        ("LeBron (elite at both)",        90, 90, 60, 50),
        ("Fast guard, no passing",        90, 30, 60, 50),
        ("Plodding big, no outlet",       30, 30, 50, 50),
        ("Avg board vs FAST defense",     55, 50, 55, 80),
        ("League-average everything",     52, 45, 52, 52)])
    table("BACKCOURT STEAL", 0.55, 0.35, [
        ("Fast guard picks the pocket",   85, 55, 60, 50),
        ("Slow big deflects+gathers",     30, 35, 50, 50),
        ("League-average stealer",        52, 45, 52, 52),
        ("Avg steal vs FAST retreat",     55, 50, 55, 80)])
    table("FRONTCOURT STEAL", 0.35, 0.55, [
        ("Fast guard picks the pocket",   85, 55, 60, 50),
        ("Slow big deflects+gathers",     30, 35, 50, 50),
        ("League-average stealer",        52, 45, 52, 52)])
    print(f"\nbars: grind {bar(2):.3f} / neutral {bar(5):.3f} / run {bar(9):.3f}")
    print(f"neutral-rule anchor: margin=0 returns base weights exactly "
          f"(rebound {100*(0.30 + PUSH_SWING*math.tanh(0.0)):.1f}%)")
