"""S79 + S81 + S81.3 — the rim protector becomes real, his help is gated by WHO HE
GUARDS, and it is measured against WHOEVER IS SHOOTING. LOCKED ORACLE.

Emits tools/block_help_golden.json, which Phase 74 replays against the compiled
engine at 1e-12. This file is the sign-off medium and the source of truth for the
math; the C# binds to Matchup's named statics, never to a copy of these formulas.

THE MODEL
---------
RATE (located shot).  The matched-defender duel plus a zone-weighted help arm,
composed in PRE-TANH SHIFT SPACE so the existing per-zone floor/ceiling still bind:

    duelShift  = sw*GapFn(DefenseRating(z,matched) - OffenseRating(z,shooter))
               + lw*GapFn(LengthRating(matched)    - LengthRating(shooter))
    helpThreat = sw*GapFn(DefenseRating(z,d) - OffenseRating(z,shooter))    # S81.3
               + lw*GapFn(LengthRating(d)    - LengthRating(shooter))
    depth(d)   = wH*Height + wS*Strength                      # BODY ONLY, not Postness
    ready(d)   = (HelpDefense/100) * (1 + swing*tanh((depth(d) - meanDepth)/scale))
    helpShift  = max(0, helpThreat(d,shooter)) * ready(d)     # no-drag floor
    total      = duelShift + helpShare(z) * SUM_{d != matched} eGate(z,man(d))*helpShift(d)
    rate       = base + span*tanh(total / BlockReferenceShift)

S81.3 — THE HELP ARM COMPARES TO THE SHOOTER
--------------------------------------------
Until S81.3 the help arm read `threat(d) = ... - AttributeMidpoint`: every helper
measured against a permanent imaginary 50-rated player. The on-ball contest was
already a matchup; the help arm was a rating against a constant. Emmett's ruling
(S81.1): "It needs to be compared to whoever is shooting the ball." Both arms move
together, onto the SAME two reads duelShift already uses, so the door carries one
rule instead of two. The imaginary man was no neutral either — the league's real rim
shooters are far better finishers than a 50, so the fixed bar had been quietly
subsidizing every rim helper.

★ CREDIT DOES NOT MOVE (R2, Emmett 2026-07-29). `threat(d)` above is RETAINED,
unchanged, and now feeds credit_weights() and nothing else. Whether the shot gets
blocked follows the shooter; whose name goes on it stays about the defenders. The
reason is measured: S79 scored the matched man against this shooter and it came out
exactly zero 44% of the time (every possession the shooter wins), pinning the helpers
at 100% of credit at any dial. A rim protector should not collect fewer blocks in the
box score because the man shooting happened to be good.

RATE (putback).  Unchanged by S79 AND S81 — the five-defender stack of
PutbackBlockRate. A go-back-up is a scramble with everyone already inside;
assignment has broken down by definition (S81 ruling, prompt A3).

S81 — THE ASSIGNMENT GATE
-------------------------
A defender's help is scaled by how far his OWN MAN pulls him from the rim. This
is not a cap on what a small player can be; it prices the situations he is in.
The mirror binds with equal force: a 6'11" centre chasing a stretch five around
the arc also stops being a rim helper on those possessions.

    spacing(o) = 1 / (1 + exp(-(o.Outside - spacingMid) / spacingScale))   # C2b, RULED
    aGate(o)   = floor + (1 - floor) * (1 - spacing(o))
    eGate(z,o) = 1 - influence(z) * (1 - aGate(o))

spacing is a PROXY for possession location, not location itself: a stretch five who
posts up or crashes the glass is still scored as a spacer all possession. Accepted
deliberately — the engine has no possession-location layer, and a future spacing
layer replaces the proxy behind this same lookup without touching the call shape.

RULED VALUES (Emmett, 2026-07-28):
  * score      C2b, midpoint 45, scale 14.  Chosen over an arc-share (shot diet)
    read, which classified better (AUC 0.970 vs 0.944) but is BIMODAL on the
    current population — 67% of the league pins at 0 or 1, only 5% lands in the
    middle. Influence fades a graded quantity; a two-valued score reintroduces the
    hard boundary r4 explicitly withdrew. Revisit when shot diets spread out.
  * floor      0.30.  A 6'11" rim protector dragged onto a sniper falls from 40%
    of his team's rim blocks to 21%; on a post big he holds 39%.
  * influence  Rim 1.00, Short 1.00, Mid 0.50, Long 0.20, Three 0.00 (J1).

WHY INFLUENCE IS FLAT THROUGH THE PAINT.  helpShare only falls 0.50 -> 0.42 from
Rim to Short, so ANY influence fade steeper than that makes the COMBINED helper
multiplier (helpShare * eGate) rise from Rim to Short — a man glued to a sniper
would help MORE on a five-footer than on a layup. Three candidate vectors that
each looked monotone read down the column failed this. Rim and Short are the same
basketball question (can this man rotate into the lane), so they carry the same
influence. Checked in checks(): the combined curve must fall Rim -> Three for
every assignment.

WHY THE GATE SCALES THE WHOLE HELPER CREDIT TERM, LUCK FLOOR INCLUDED.  Gating only
the help ON TOP of the floor compresses everyone toward an untouched floor, and the
men with the most help to lose are the big men — measured league-wide, that version
moved rim-block credit TOWARD players under 6'3" (over-representation 0.80x -> 0.82x),
the exact opposite of the change's purpose. Scaling the whole term moves it the
intended way (0.80x -> 0.78x) and is the idiom helpShare already uses in the same
expression. The rate has no luck floor, so this choice cannot move block TOTALS —
only whose name goes on them.

TRANSITION IS EXEMPT.  On a fast break nobody is matched up; slot parity would
suppress a guard for "his man" being a shooter when he is chasing down a layup with
no man at all. Roll H and the picker pass offense=None on a break, which makes every
gate exactly 1.0. Transition assignment gets its own session (Emmett, 2026-07-28).

MEASURED, RECORDED, NOT ACTED ON.  The gate is NOT a meaningful correction to the
league block total. Deleting the entire help arm moves the rim block rate only from
14.32% to 13.02%; the gate cuts about a quarter of that arm, projecting ~4.4 -> ~4.31
blocks per game against a 3.5 target. S81's value is WHO gets the blocks, not how
many. Page-only calibration principle: this is a finding, not a dial to turn.

CREDIT.  Defender-only. The shooter decides WHETHER a shot is blocked, not WHICH
defender got there; scoring the matched man against this particular shooter is
exactly zero 44% of the time on a real population, which pins the help arm at 100%
of the credit in those cases and makes the split untunable at any share.

    matched:  luckFloor + max(0, threat(d))          # on the ball; no gate, no readiness
    helper :  helpShare(z) * eGate(z,man(d)) * (luckFloor + helpShift(d))
    putback:  luckFloor + max(0, putbackShift(d))    # no matched arm, no zone share, no gate

The matched man is NOT gated: he is on the ball, and the gate prices rotation
distance, which is not a question that applies to the man already there.

The luck floor is load-bearing, not cosmetic: max(0, threat) alone is a hard zero
for roughly half the population, and a lone elite big beside four average men took
100% of his team's block credit. It is also the concentration dial — 1.5 puts the
best rim protector in the country at ~half his lineup's rim blocks (Emmett's ruling).
Because eGate multiplies the whole helper term, the floor never reaches zero: at
floor 0.30 the weakest possible gate is ~0.32, so every populated defender stays
strictly drawable and the picker's no-zero-mass contract holds.
"""
import json, math, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CFG  = json.load(open(os.path.join(ROOT, "src", "Charm.Harness", "config.json")))
M, H = CFG["Matchup"], CFG["RollH"]
ZONES = ["Rim", "Short", "Mid", "Long", "Three"]

# ── primitives (transcribed from Matchup.cs) ──────────────────────────────
def gapfn(g, steep, expo, scale):
    return (0.0 if g == 0 else math.copysign(1.0, g)) * steep * (abs(g) / scale) ** expo

def sk(g): return gapfn(g, M["SkillSteepness"],    M["SkillExponent"],    M["ReferenceScale"])
def ph(g): return gapfn(g, M["PhysicalSteepness"], M["PhysicalExponent"], M["ReferenceScale"])

def contest(z):      return (M[f"Block{z}Skill"], M[f"Block{z}Length"])
def blend(z):        return (M[f"{z}Perimeter"], M[f"{z}Post"], M[f"{z}Rim"])
def base_block(z):   return H[f"Block{z}"]
def floor_block(z):  return M[f"BlockFloor{z}"]
def ceil_block(z):   return M[f"BlockCeil{z}"]
def help_share(z):   return M[f"BlockHelpShare{z}"]

OFFENSE_ATTR = {"Rim": "Finishing", "Short": "Close", "Mid": "Mid", "Long": "Outside", "Three": "Outside"}

def def_rating(z, d):
    p, po, r = blend(z)
    return p * d["PerimeterDefense"] + po * d["PostDefense"] + r * d["RimProtection"]

def length_rating(d):
    return (M["LengthHeight"] * d["Height"] + M["LengthWingspan"] * d["Wingspan"]
          + M["LengthVertical"] * d["Vertical"])

def depth(d):
    return M["BlockHelpDepthHeight"] * d["Height"] + M["BlockHelpDepthStrength"] * d["Strength"]

def mean_depth(defs):
    live = [d for d in defs if d is not None]
    return sum(depth(d) for d in live) / len(live) if live else 0.0

def duel_shift(z, shooter, matched):
    s, l = contest(z)
    return (s * sk(def_rating(z, matched) - shooter[OFFENSE_ATTR[z]])
          + l * ph(length_rating(matched) - length_rating(shooter)))

def threat(z, d):
    """NEUTRAL bar. S81.3: this is the CREDIT yardstick and nothing else. Retained
    deliberately under R2 — see the module docstring. Do not point the rate at it."""
    s, l = contest(z)
    return (s * sk(def_rating(z, d) - M["AttributeMidpoint"])
          + l * ph(length_rating(d) - M["AttributeMidpoint"]))

def help_threat(z, d, shooter):
    """S81.3 — the RATE's help arm: a helper measured against the man actually shooting,
    on the same two reads duel_shift already uses."""
    s, l = contest(z)
    return (s * sk(def_rating(z, d) - shooter[OFFENSE_ATTR[z]])
          + l * ph(length_rating(d) - length_rating(shooter)))

def readiness(d, md):
    return (d["HelpDefense"] / 100.0) * (
        1.0 + M["BlockHelpPositionalSwing"] * math.tanh((depth(d) - md) / M["BlockHelpPositionalScale"]))

def help_shift(z, d, md):
    """NEUTRAL. Credit's helper arm only (S81.3)."""
    return max(0.0, threat(z, d)) * readiness(d, md)

def help_shift_vs_shooter(z, d, shooter, md):
    """S81.3 — the RATE's helper arm. Readiness still MULTIPLIES threat; the no-drag
    floor still fires, and now fires far more often, because real shooters outclass more
    helpers than an imaginary 50 did."""
    return max(0.0, help_threat(z, d, shooter)) * readiness(d, md)

# ── S81: the assignment gate ──────────────────────────────────────────────
# One named lookup, consumed in exactly two places (rate and credit), so a future
# coaching layer can replace slot parity without touching the gate formula.
def influence(z):    return M[f"BlockAssignmentInfluence{z}"]

def spacing(o):
    """How far this OFFENSIVE player pulls his defender from the rim. [0,1]."""
    return 1.0 / (1.0 + math.exp(-(o["Outside"] - M["BlockSpacingMidpoint"])
                                 / M["BlockSpacingScale"]))

def assignment_gate(o):
    f = M["BlockAssignmentFloor"]
    return f + (1.0 - f) * (1.0 - spacing(o))

def effective_gate(z, o):
    """1.0 means no suppression. o is None on a fast break (no assignments) or an
    unpopulated offensive slot — both mean the gate cannot apply."""
    if o is None:
        return 1.0
    return 1.0 - influence(z) * (1.0 - assignment_gate(o))

def help_sum(z, shooter, defs, matched_index, offense=None):
    """S81.3: shooter-first, mirroring BlockWeightWithHelp(zone, shooter, ...). There is
    no no-shooter form — every caller has one, and keeping a fallback would preserve two
    rate semantics indefinitely.

    offense is the OFFENSIVE five in slot order; defender i guards offense[i]
    (slot parity). None = ungated (transition, or no lineup available)."""
    md = mean_depth(defs)
    total = 0.0
    for i, d in enumerate(defs):
        if d is None or i == matched_index:
            continue
        g = 1.0 if offense is None else effective_gate(z, offense[i])
        total += g * help_shift_vs_shooter(z, d, shooter, md)
    return total

def bend(z, total, base):
    span = (ceil_block(z) - base) if total >= 0.0 else (base - floor_block(z))
    return base + span * math.tanh(total / M["BlockReferenceShift"])

def block_rate_with_help(z, shooter, defs, matched_index, offense=None):
    return bend(z, duel_shift(z, shooter, defs[matched_index])
                 + help_share(z) * help_sum(z, shooter, defs, matched_index, offense),
                base_block(z))

def putback_shift(d):
    s, l = contest("Rim")
    return (s * sk(def_rating("Rim", d) - M["AttributeMidpoint"])
          + l * ph(length_rating(d) - M["AttributeMidpoint"]))

def credit_weights(z, defs, matched_index, offense=None):
    """S81: the helper term is scaled by eGate IN FULL, luck floor included — see the
    module docstring for why gating only the help above the floor moves league credit
    the wrong way. The matched man is never gated."""
    md, share, floor = mean_depth(defs), help_share(z), M["BlockCreditLuckFloor"]
    out = []
    for i, d in enumerate(defs):
        if d is None:
            out.append(0.0)
        elif i == matched_index:
            out.append(floor + max(0.0, threat(z, d)))
        else:
            g = 1.0 if offense is None else effective_gate(z, offense[i])
            out.append(share * g * (floor + help_shift(z, d, md)))
    return out

def putback_credit_weights(defs):
    floor = M["BlockCreditLuckFloor"]
    return [0.0 if d is None else floor + max(0.0, putback_shift(d)) for d in defs]

# ── fixture players ───────────────────────────────────────────────────────
def P(name, **kw):
    p = dict(name=name, Height=50, Wingspan=50, Vertical=50, Strength=50,
             PerimeterDefense=50, PostDefense=50, RimProtection=50, HelpDefense=50,
             Finishing=50, Close=50, Mid=50, Outside=50)
    p.update(kw)
    return p

MENACE   = P("menace",   RimProtection=95, PostDefense=88, Height=78, Wingspan=85, Vertical=70,
                         Strength=80, HelpDefense=85)
TOOLS    = P("tools",    RimProtection=88, PostDefense=70, Height=78, Wingspan=85, Vertical=70,
                         Strength=80, HelpDefense=15)   # same body, no instincts
LEAPER   = P("leaper",   RimProtection=60, PostDefense=35, Height=68, Wingspan=74, Vertical=92,
                         Strength=55, HelpDefense=70)
POST     = P("post",     RimProtection=55, PostDefense=80, Height=76, Wingspan=78, Vertical=32,
                         Strength=85, HelpDefense=45)
GUARD    = P("guard",    RimProtection=25, PostDefense=30, Height=38, Wingspan=40, Vertical=60,
                         Strength=32, HelpDefense=75)   # real instincts, no tools
ORDINARY = P("ordinary")
SHOOTER  = P("shooter",  Finishing=62, Close=58, Mid=55, Outside=54, Height=55, Wingspan=57, Vertical=62)
ELITE_SH = P("elite_shooter", Finishing=95, Close=90, Mid=88, Outside=92,
                         Height=70, Wingspan=76, Vertical=85)

# ── S81.3 — the two ARM-ISOLATING shooters ────────────────────────────────
# elite_shooter differs from shooter in BOTH skill and body, so a golden built on that
# pair alone would pass with one arm unwired — the other masks it. These two move
# exactly one arm each. This is the fixture half of A4: only the SAME defender against
# two DIFFERENT shooters can see the change at all.
SH_SKILL = P("shooter_skill_only",            # elite skills, ordinary shooter's body
             Finishing=95, Close=90, Mid=88, Outside=92,
             Height=55, Wingspan=57, Vertical=62)
SH_BODY  = P("shooter_length_only",           # ordinary shooter's skills, elite body
             Finishing=62, Close=58, Mid=55, Outside=54,
             Height=70, Wingspan=76, Vertical=85)

# ── S81 offensive fixture players — the MEN BEING GUARDED ─────────────────
# Only Outside is read by spacing(), but these carry full cards so the fixture
# stays a legal Player everywhere and a future spacing layer can widen the read
# without reshaping the golden.
O_POST    = P("o_post",    Outside=12, PostMoves=70, Close=65, Finishing=60, Height=78, Wingspan=80)
O_CONNECT = P("o_connect", Outside=32, Mid=40, Close=50, Finishing=52, Height=64, Wingspan=66)
O_STRETCH = P("o_stretch", Outside=62, Mid=58, Close=45, Finishing=48, Height=76, Wingspan=78)
O_SNIPER  = P("o_sniper",  Outside=85, Mid=72, Close=44, Finishing=45, Height=56, Wingspan=58)

OFFENSES = {
    "off_all_post":    [O_POST] * 5,
    "off_all_snipers": [O_SNIPER] * 5,
    "off_mixed":       [O_SNIPER, O_SNIPER, O_CONNECT, O_STRETCH, O_POST],
    "off_five_out":    [O_SNIPER, O_STRETCH, O_STRETCH, O_STRETCH, O_STRETCH],
    "off_short":       [O_SNIPER, O_POST, O_STRETCH, None, None],
}

LINEUPS = {
    "all_ordinary":   [ORDINARY] * 5,
    "one_menace":     [ORDINARY, ORDINARY, ORDINARY, ORDINARY, MENACE],
    "two_menace":     [ORDINARY, ORDINARY, ORDINARY, MENACE,   MENACE],
    "menace_matched": [MENACE,   ORDINARY, ORDINARY, ORDINARY, ORDINARY],
    "tools_no_help":  [ORDINARY, ORDINARY, ORDINARY, ORDINARY, TOOLS],
    "leaper":         [ORDINARY, ORDINARY, ORDINARY, ORDINARY, LEAPER],
    "below_rim_post": [ORDINARY, ORDINARY, ORDINARY, ORDINARY, POST],
    "instinct_guard": [ORDINARY, ORDINARY, ORDINARY, ORDINARY, GUARD],
    "all_weak":       [GUARD] * 5,
    "short_lineup":   [ORDINARY, ORDINARY, MENACE, None, None],
}

# The four fixture shooters. The last two isolate one arm each (see above).
SHOOTERS = ((SHOOTER,  "average"), (ELITE_SH, "elite"),
            (SH_SKILL, "skill_only"), (SH_BODY, "length_only"))

def emit_rate():
    """S81.3 — the RATE contract only. Credit is NOT emitted here; it is copied from the
    frozen file (see load_frozen_credit). Splitting the schema by name is what stops a
    regenerated fixture from quietly becoming a snapshot of whatever the engine does."""
    rows = []
    # offense=None reproduces S79 EXACTLY (every gate 1.0) and is also the live
    # transition path; the named offenses exercise S81's gate.
    off_cases = [(None, "none")] + [(v, k) for k, v in OFFENSES.items()]
    for lname, defs in LINEUPS.items():
        for z in ZONES:
            for shooter, sname in SHOOTERS:
                for mi in (0, -1):
                    if mi == 0 and defs[0] is None:
                        continue
                    for offense, oname in off_cases:
                        hs = help_sum(z, shooter, defs, mi, offense)
                        rate = (block_rate_with_help(z, shooter, defs, mi, offense)
                                if mi >= 0
                                else bend(z, help_share(z) * hs, base_block(z)))
                        rows.append(dict(
                            lineup=lname, offense=oname, zone=z, shooter=sname,
                            matched_index=mi,
                            duel_shift=(duel_shift(z, shooter, defs[mi]) if mi >= 0 else 0.0),
                            help_sum=hs,
                            mean_depth=mean_depth(defs),
                            rate=rate))
    return rows


def load_frozen_credit():
    """Read the FROZEN neutral credit vectors and prove the oracle still reproduces them
    EXACTLY before emitting. R2 says credit does not move; if this assertion ever fails,
    the oracle itself has forked wrong and the fixture must not be written."""
    path = os.path.join(ROOT, "tools", "block_credit_neutral_frozen.json")
    frozen = json.load(open(path))
    assert frozen["schema"] == "s81-3-credit-neutral-frozen", frozen["schema"]

    pl = {k: v for k, v in frozen["players"].items()}
    def lineup(name):  return [pl[n] if n else None for n in frozen["lineups"][name]]
    def offense(name): return None if name == "none" else \
                              [pl[n] if n else None for n in frozen["offenses"][name]]

    worst = 0.0
    for r in frozen["rows"]:
        defs = lineup(r["lineup"])
        w = (putback_credit_weights(defs) if r["putback"]
             else credit_weights(r["zone"], defs, r["matched_index"], offense(r["offense"])))
        for a, b in zip(w, r["credit_weights"]):
            worst = max(worst, abs(a - b))
            if a != b:
                raise SystemExit(
                    f"FROZEN CREDIT DRIFT at {r['lineup']}/{r['offense']}/{r['zone']}"
                    f"/mi={r['matched_index']}: oracle {a!r} vs frozen {b!r}. "
                    "R2 says block credit does not move in S81.3. Golden NOT written.")
    return frozen, worst


# ── the oracle's own checks — these run before the golden is written ───────
def checks():
    fails = []
    def ck(name, ok, detail=""):
        print(f"[{'OK' if ok else 'FAIL'}] {name}" + (f"  {detail}" if detail else ""))
        if not ok: fails.append(name)

    # 1. spacing and the gate are bounded, and the gate never reaches zero.
    outs = list(range(0, 100))
    sp = [spacing(P("x", Outside=o)) for o in outs]
    ck("spacing in [0,1]", all(0.0 <= s <= 1.0 for s in sp))
    ck("spacing strictly increasing in Outside", all(sp[i] < sp[i+1] for i in range(len(sp)-1)))
    gates = [effective_gate(z, P("x", Outside=o)) for z in ZONES for o in outs]
    ck("effective gate in (0,1]", all(0.0 < g <= 1.0 + 1e-15 for g in gates))
    ck("weakest gate stays clear of zero", min(gates) > 0.05, f"min {min(gates):.4f}")

    # 2. influence is in [0,1] and non-increasing Rim -> Three; Three is exactly 0.
    inf = [influence(z) for z in ZONES]
    ck("influence in [0,1]", all(0.0 <= v <= 1.0 for v in inf))
    ck("influence non-increasing Rim->Three", all(inf[i] >= inf[i+1] for i in range(4)),
       " ".join(f"{z}={v}" for z, v in zip(ZONES, inf)))
    ck("influence at Three is exactly 0", inf[-1] == 0.0)

    # 3. THE COMBINED CURVE. helpShare * eGate must FALL from Rim to Three for every
    #    assignment. An influence vector that is monotone read down the column can
    #    still rise here, because helpShare only drops 0.50 -> 0.42 Rim->Short.
    for o in (O_POST, O_CONNECT, O_STRETCH, O_SNIPER):
        curve = [help_share(z) * effective_gate(z, o) for z in ZONES]
        ck(f"combined helper multiplier falls Rim->Three for {o['name']}",
           all(curve[i] > curve[i+1] for i in range(4)),
           " ".join(f"{v:.4f}" for v in curve))

    # 4. Direction (the sign check A5 exists for): a defender guarding a SPACER must
    #    help LESS than the same defender guarding a POST player.
    for z in ("Rim", "Short", "Mid", "Long"):
        a = effective_gate(z, O_SNIPER); b = effective_gate(z, O_POST)
        ck(f"{z}: guarding a sniper suppresses more than guarding a post big", a < b,
           f"sniper {a:.4f} < post {b:.4f}")
    ck("Three: gate is inert (influence 0)",
       effective_gate("Three", O_SNIPER) == 1.0 == effective_gate("Three", O_POST))

    # 5. offense=None is EXACTLY the S79 tree — the transition path and the identity
    #    anchor. Every gate is 1.0, so every rate and weight must be bit-identical to
    #    the ungated formula.
    same = True
    for lname, defs in LINEUPS.items():
        for z in ZONES:
            for mi in (0, -1):
                if mi == 0 and defs[0] is None: continue
                md = mean_depth(defs)
                for shooter, _ in SHOOTERS:
                    ungated = sum(help_shift_vs_shooter(z, d, shooter, md)
                                  for i, d in enumerate(defs)
                                  if d is not None and i != mi)
                    if help_sum(z, shooter, defs, mi, None) != ungated: same = False
    ck("offense=None reproduces the ungated help sum exactly", same)

    # 6. Putback credit is untouched by anything in S81.
    ck("putback credit takes no offense argument",
       "offense" not in putback_credit_weights.__code__.co_varnames)

    # ── S81.3 ─────────────────────────────────────────────────────────────
    # 7. THE DISCRIMINATING SIGNAL, one arm at a time. The combined pair can pass with
    #    one arm unwired because the other masks it (A4). All three forms required.
    big = P("big", Height=84, Wingspan=88, Vertical=68, Strength=80,
            PostDefense=85, RimProtection=92, HelpDefense=80)
    md0 = mean_depth([big] * 5)

    skill_ok = True
    for z in ZONES:
        lo = P("lo", Height=55, Wingspan=57, Vertical=62, **{OFFENSE_ATTR[z]: 25})
        hi = P("hi", Height=55, Wingspan=57, Vertical=62, **{OFFENSE_ATTR[z]: 85})
        if not help_shift_vs_shooter(z, big, hi, md0) < help_shift_vs_shooter(z, big, lo, md0):
            skill_ok = False
    ck("SKILL arm: the same helper helps LESS against the better shooter, at every zone",
       skill_ok, "the zone's own attribute is swept — Finishing at Three would test nothing")

    short_reach = P("short", Height=40, Wingspan=42, Vertical=45, Finishing=55, Close=55, Mid=55, Outside=55)
    long_reach  = P("long",  Height=88, Wingspan=92, Vertical=85, Finishing=55, Close=55, Mid=55, Outside=55)
    ck("LENGTH arm: the same helper helps LESS against the longer shooter, at every zone",
       all(help_shift_vs_shooter(z, big, long_reach, md0)
           < help_shift_vs_shooter(z, big, short_reach, md0) for z in ZONES))

    ck("COMBINED: helps more against a small weak-skilled shooter than a big skilled one",
       all(help_shift_vs_shooter(z, big, SHOOTER, md0)
           > help_shift_vs_shooter(z, big, ELITE_SH, md0) for z in ZONES))

    # 8. LEVEL RELATIVITY, stated directly — the whole point of the no-scalar wall.
    d3  = P("d3_shooter",   Height=48, Wingspan=50, Vertical=52,
            Finishing=30, Close=30, Mid=30, Outside=30)
    hm  = P("high_major",   Height=72, Wingspan=78, Vertical=80,
            Finishing=88, Close=85, Mid=82, Outside=84)
    ck("the SAME seven-footer helps materially more against a D3 shooter than a high-major one",
       help_shift_vs_shooter("Rim", big, d3, md0) > 1.5 * help_shift_vs_shooter("Rim", big, hm, md0),
       f"D3 {help_shift_vs_shooter('Rim', big, d3, md0):.3f} vs "
       f"high-major {help_shift_vs_shooter('Rim', big, hm, md0):.3f}")

    # 9. THE NEUTRAL POINT, four cases. The first three are EXACT (a zero gap through
    #    GapFn is exactly zero, sign(0) = 0). The fourth is NOT asserted as a literal
    #    0.0 from player-derived inputs: the two arms run different weights, steepnesses
    #    and exponents through **, and a solved cancellation lands ULPs off. Composition
    #    is proved by DECOMPOSITION instead, which is exact and says the same thing.
    z = "Rim"
    s, l = contest(z)
    mirror_skill = P("ms", Finishing=def_rating(z, big))                 # skill gap exactly 0
    ck("skill gap zero -> skill arm exactly 0",
       s * sk(def_rating(z, big) - mirror_skill[OFFENSE_ATTR[z]]) == 0.0)

    ck("length gap zero -> length arm exactly 0",
       l * ph(length_rating(big) - length_rating(big)) == 0.0)

    both = P("both", Finishing=def_rating(z, big), Height=big["Height"],
             Wingspan=big["Wingspan"], Vertical=big["Vertical"])
    ck("both gaps zero -> threat exactly 0, help exactly 0",
       help_threat(z, big, both) == 0.0 and help_shift_vs_shooter(z, big, both, md0) == 0.0)

    decomp_ok = True
    for zz in ZONES:
        for sh, _ in SHOOTERS:
            ss, ll = contest(zz)
            arm_s = ss * sk(def_rating(zz, big) - sh[OFFENSE_ATTR[zz]])
            arm_l = ll * ph(length_rating(big) - length_rating(sh))
            if help_threat(zz, big, sh) != arm_s + arm_l:
                decomp_ok = False
    ck("threat is EXACTLY the weighted skill arm plus the weighted length arm", decomp_ok,
       "weighted-additive composition proved by decomposition, not by a solved cancellation")

    # 10. Arms are SEPARATELY signed and neither is inert.
    ck("the skill arm alone moves the threat", help_threat("Rim", big, SH_SKILL) != help_threat("Rim", big, SHOOTER))
    ck("the length arm alone moves the threat", help_threat("Rim", big, SH_BODY)  != help_threat("Rim", big, SHOOTER))

    # 11. The no-drag floor still binds, and the gate still multiplies the NEW threat.
    weak = P("weak", Height=38, Wingspan=40, Vertical=60, Strength=32,
             PerimeterDefense=30, PostDefense=30, RimProtection=25, HelpDefense=75)
    ck("a helper outclassed by the shooter contributes exactly zero, not a negative",
       help_shift_vs_shooter("Rim", weak, ELITE_SH, mean_depth([weak] * 5)) == 0.0)

    ck("the fast break keeps shooter-relativity (offense=None gates at 1.0 but the bar stays)",
       help_sum("Rim", SHOOTER, [big] * 5, 0, None) > help_sum("Rim", ELITE_SH, [big] * 5, 0, None))

    return fails

if __name__ == "__main__":
    fails = checks()
    if fails:
        raise SystemExit(f"\nORACLE CHECKS FAILED: {fails}\nGolden NOT written.")

    frozen, worst = load_frozen_credit()
    print(f"\n[OK] frozen credit vectors reproduced EXACTLY "
          f"({len(frozen['rows'])} rows, worst |delta| = {worst!r})")

    players = {p["name"]: p for p in
               (MENACE, TOOLS, LEAPER, POST, GUARD, ORDINARY, SHOOTER, ELITE_SH,
                SH_SKILL, SH_BODY, O_POST, O_CONNECT, O_STRETCH, O_SNIPER)}
    fixture = dict(
        schema="s81-3",
        float_tolerance=1e-12,
        constants={k: M[k] for k in (
            "SkillSteepness", "SkillExponent", "PhysicalSteepness", "PhysicalExponent",
            "ReferenceScale", "AttributeMidpoint", "BlockReferenceShift",
            "BlockHelpDepthHeight", "BlockHelpDepthStrength",
            "BlockHelpPositionalSwing", "BlockHelpPositionalScale",
            "BlockHelpShareRim", "BlockHelpShareShort", "BlockHelpShareMid",
            "BlockHelpShareLong", "BlockHelpShareThree", "BlockCreditLuckFloor",
            "LengthHeight", "LengthWingspan", "LengthVertical",
            # S81
            "BlockSpacingMidpoint", "BlockSpacingScale", "BlockAssignmentFloor",
            "BlockAssignmentInfluenceRim", "BlockAssignmentInfluenceShort",
            "BlockAssignmentInfluenceMid", "BlockAssignmentInfluenceLong",
            "BlockAssignmentInfluenceThree")},
        config_section_sha256=frozen["config_section_sha256"],
        players=players,
        lineups={k: [p["name"] if p else None for p in v] for k, v in LINEUPS.items()},
        offenses={k: [p["name"] if p else None for p in v] for k, v in OFFENSES.items()},

        # ── the two contracts, separated BY NAME ──────────────────────────
        # rate_vs_shooter: new rows, intentionally different from the s81-1 fixture.
        # credit_neutral:  copied byte-for-byte from the frozen file, never re-derived.
        rate_vs_shooter=emit_rate(),
        credit_neutral=dict(
            note=frozen["provenance"],
            shooter_pairs_proved_identical=frozen["shooter_pairs_proved_identical"],
            rows=frozen["rows"]))

    out = os.path.join(ROOT, "tools", "block_help_golden.json")
    with open(out, "w") as f:
        json.dump(fixture, f, indent=1)
        f.write("\n")
    print(f"\nwrote {out}: {len(fixture['rate_vs_shooter'])} rate rows, "
          f"{len(fixture['credit_neutral']['rows'])} frozen credit rows")
