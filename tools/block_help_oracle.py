"""S79 — the rim protector becomes real. LOCKED ORACLE.

Emits tools/block_help_golden.json, which Phase 74 replays against the compiled
engine at 1e-12. This file is the sign-off medium and the source of truth for the
math; the C# binds to Matchup's named statics, never to a copy of these formulas.

THE MODEL
---------
RATE (located shot).  The matched-defender duel plus a zone-weighted help arm,
composed in PRE-TANH SHIFT SPACE so the existing per-zone floor/ceiling still bind:

    duelShift  = sw*GapFn(DefenseRating(z,matched) - OffenseRating(z,shooter))
               + lw*GapFn(LengthRating(matched)    - LengthRating(shooter))
    threat(d)  = sw*GapFn(DefenseRating(z,d) - 50) + lw*GapFn(LengthRating(d) - 50)
    depth(d)   = wH*Height + wS*Strength                      # BODY ONLY, not Postness
    ready(d)   = (HelpDefense/100) * (1 + swing*tanh((depth(d) - meanDepth)/scale))
    helpShift  = max(0, threat(d)) * ready(d)                 # no-drag floor
    total      = duelShift + helpShare(z) * SUM_{d != matched} helpShift(d)
    rate       = base + span*tanh(total / BlockReferenceShift)

RATE (putback).  Unchanged by S79 — the five-defender stack of PutbackBlockRate.

CREDIT.  Defender-only. The shooter decides WHETHER a shot is blocked, not WHICH
defender got there; scoring the matched man against this particular shooter is
exactly zero 44% of the time on a real population, which pins the help arm at 100%
of the credit in those cases and makes the split untunable at any share.

    matched:  luckFloor + max(0, threat(d))          # on the ball; no readiness factor
    helper :  helpShare(z) * (luckFloor + helpShift(d))
    putback:  luckFloor + max(0, putbackShift(d))    # no matched arm, no zone share

The luck floor is load-bearing, not cosmetic: max(0, threat) alone is a hard zero
for roughly half the population, and a lone elite big beside four average men took
100% of his team's block credit. It is also the concentration dial — 1.5 puts the
best rim protector in the country at ~half his lineup's rim blocks (Emmett's ruling).
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
    s, l = contest(z)
    return (s * sk(def_rating(z, d) - M["AttributeMidpoint"])
          + l * ph(length_rating(d) - M["AttributeMidpoint"]))

def readiness(d, md):
    return (d["HelpDefense"] / 100.0) * (
        1.0 + M["BlockHelpPositionalSwing"] * math.tanh((depth(d) - md) / M["BlockHelpPositionalScale"]))

def help_shift(z, d, md):
    return max(0.0, threat(z, d)) * readiness(d, md)

def help_sum(z, defs, matched_index):
    md = mean_depth(defs)
    return sum(help_shift(z, d, md) for i, d in enumerate(defs)
               if d is not None and i != matched_index)

def bend(z, total, base):
    span = (ceil_block(z) - base) if total >= 0.0 else (base - floor_block(z))
    return base + span * math.tanh(total / M["BlockReferenceShift"])

def block_rate_with_help(z, shooter, defs, matched_index):
    return bend(z, duel_shift(z, shooter, defs[matched_index])
                 + help_share(z) * help_sum(z, defs, matched_index), base_block(z))

def putback_shift(d):
    s, l = contest("Rim")
    return (s * sk(def_rating("Rim", d) - M["AttributeMidpoint"])
          + l * ph(length_rating(d) - M["AttributeMidpoint"]))

def credit_weights(z, defs, matched_index):
    md, share, floor = mean_depth(defs), help_share(z), M["BlockCreditLuckFloor"]
    out = []
    for i, d in enumerate(defs):
        if d is None:                 out.append(0.0)
        elif i == matched_index:      out.append(floor + max(0.0, threat(z, d)))
        else:                         out.append(share * (floor + help_shift(z, d, md)))
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

def emit():
    rows = []
    for lname, defs in LINEUPS.items():
        for z in ZONES:
            for shooter, sname in ((SHOOTER, "average"), (ELITE_SH, "elite")):
                for mi in (0, -1):
                    if mi == 0 and defs[0] is None:
                        continue
                    rate = (block_rate_with_help(z, shooter, defs, mi)
                            if mi >= 0 else bend(z, help_share(z) * help_sum(z, defs, -1), base_block(z)))
                    w = credit_weights(z, defs, mi)
                    tot = sum(w)
                    rows.append(dict(
                        lineup=lname, zone=z, shooter=sname, matched_index=mi,
                        duel_shift=(duel_shift(z, shooter, defs[mi]) if mi >= 0 else 0.0),
                        help_sum=help_sum(z, defs, mi),
                        mean_depth=mean_depth(defs),
                        rate=rate,
                        credit_weights=w,
                        credit_shares=[x / tot for x in w] if tot > 0 else [0.0] * 5))
    for lname, defs in LINEUPS.items():
        w = putback_credit_weights(defs); tot = sum(w)
        rows.append(dict(lineup=lname, zone="Rim", shooter="putback", matched_index=-1,
                         duel_shift=0.0, help_sum=0.0, mean_depth=mean_depth(defs),
                         rate=None, credit_weights=w,
                         credit_shares=[x / tot for x in w] if tot > 0 else [0.0] * 5,
                         putback=True))
    return rows

if __name__ == "__main__":
    players = {p["name"]: p for p in
               (MENACE, TOOLS, LEAPER, POST, GUARD, ORDINARY, SHOOTER, ELITE_SH)}
    fixture = dict(
        schema="s79-1",
        float_tolerance=1e-12,
        constants={k: M[k] for k in (
            "SkillSteepness", "SkillExponent", "PhysicalSteepness", "PhysicalExponent",
            "ReferenceScale", "AttributeMidpoint", "BlockReferenceShift",
            "BlockHelpDepthHeight", "BlockHelpDepthStrength",
            "BlockHelpPositionalSwing", "BlockHelpPositionalScale",
            "BlockHelpShareRim", "BlockHelpShareShort", "BlockHelpShareMid",
            "BlockHelpShareLong", "BlockHelpShareThree", "BlockCreditLuckFloor",
            "LengthHeight", "LengthWingspan", "LengthVertical")},
        players=players,
        lineups={k: [p["name"] if p else None for p in v] for k, v in LINEUPS.items()},
        rows=emit())
    out = os.path.join(ROOT, "tools", "block_help_golden.json")
    with open(out, "w") as f:
        json.dump(fixture, f, indent=1)
        f.write("\n")
    print(f"wrote {out}: {len(fixture['rows'])} rows")
