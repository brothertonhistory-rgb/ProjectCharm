# Project Charm — Skill-Derived Shot Tendencies: Design Brief

Settled in the design conversation of 2026-07-03, immediately after Session 33
(the OTHER-bucket fix) closed, and **revised the same day after an outside review**
(ChatGPT, per CONVENTIONS §6c) sharpened the math agenda — see the review-driven
changes marked through §§3–5 and §8. This is the design record for a
**player-generation Pass 2 family redesign**: the five shot-location tendencies
stop being authored from a fixed per-role table and become **derived from the
player's own skills**, with per-player variation. It is a brief, not a build
prompt, and it is not yet a locked spec — the intent is settled; the math is not.
A dedicated follow-up design conversation turns the intentions below into
mechanical claims, and only then does a build prompt get drafted, audited, and
reviewed per CONVENTIONS §6.

---

## 1. The frame — how this design was reached

The conversation started as a calibration question: the season card reads 3PA rate
0.21 vs the real 0.39 (the "shot-diet dial," deferred since Session 32). The
investigation traced the league's shot mix to its actual source and found:

- Tendencies come from a **fixed table**: `GenRoles[role].Tendencies` in
  `Program.Gen.cs` — nine hand-authored five-number vectors, one per role. Every
  player of a role gets the identical vector. VERIFIED against source: the divvy
  path (`Program.Divvy.cs` → `GenRatings(role, …)`) uses this same table, so the
  season's ~3,470 players carry exactly **nine distinct shot diets**, cloned.
- The first proposed fix was to recenter those nine vectors (less long two
  everywhere, less mid for the pure shooters). Emmett's successive corrections
  dismantled that framing in three steps:
  1. *"Too many long shots for virtually all players"* — the miscalibration is
     the long two, not the three-point column.
  2. *"Tendencies should be varied across players … there should be PGs that
     shoot 80% threes, and ones that drive all the time"* — the fixed table has
     zero within-role spread, which no vector edit can fix.
  3. *"It really depends on the other skills. A perimeter player with high
     three-point shooting and weak everything else should generate extreme
     three-point tendencies. The guard with handles and athleticism and no shot
     should roll a rim-heavy tendency spread"* — tendencies should be
     **downstream of skills**, not authored beside them.

The third step is the design. It dissolves the first two problems as side
effects: the averages problem (wrong table values) and the spread problem (no
within-role variation) both vanish when the table is deleted and tendencies are
derived from skills that already vary per player.

**Supersession, recorded loudly:** the "recenter the nine vectors" session is
**superseded, not deferred**. No future session should tune `GenRoles[…].Tendencies`
— the table is slated for deletion, not correction.

---

## 2. The design reversal this makes (must be blessed, not slid past)

The current model *deliberately* decouples tendency from skill. The
`ThreeTendency` docstring on `Player` states it explicitly: authored, 0–99,
INDEPENDENT of conversion skill — "Klay Thompson and Steph Curry can have similar
three-point conversion skill but very different ThreeTendency values." A high-
Outside/low-ThreeTendency player is a catch-and-shoot role player; a low-Outside/
high-ThreeTendency player is a volume chucker.

This brief **reverses that principle**: tendency becomes a function of skill
(plus variation). The reversal is Emmett's call, made in this conversation, and
the build must update the docstrings to match — a design principle stated in
source must never silently contradict the generator. What the old principle was
protecting (two same-skill players with different diets) is preserved by the
variation term (§4.2), not by independent authoring.

---

## 3. The derivation — per-zone attribute keys (intent settled; math open)

One source fact shapes everything: **the skill side has three shooting buckets
(Close / Mid / Outside) but the tendency side has five zones** — and the
`Outside` docstring says "threes and long twos folded into one bucket." There is
no separate long-two skill. So shooting skill alone cannot distinguish a long-two
shooter from a three shooter; the discriminator has to be something else. Emmett
confirmed the discriminator: **shot creation**.

**A principle the math pass must honor per zone (from the review): access is not
the same as reason.** Each zone's expectation has two separable signals — an
*ability-to-access* signal (can he get to this shot: creation, burst, handle)
and an *ability-to-make* signal (can he punish from here: the zone's shooting
skill). A player who can access a shot but not make it should not seek it in
volume. The long two is where this bites hardest (creation without a jumper),
but the split applies everywhere and the math should define both signals for
each zone rather than collapsing them into a single blended key.

- **Three** ← `Outside`, pushed *higher* by weakness elsewhere. The pure
  specialist — high Outside, low SelfCreation, low athleticism — lands at an
  extreme three tendency precisely because it is the only thing he does.
  (Interaction flag for the math pass: "pushed higher by weakness elsewhere" is
  itself a lopsidedness effect, so the specialist's three-spike must not be
  double-counted — once in the three signal and again in the peakedness
  transform (§4.1). One of the two owns it; the design pass decides which.)
- **Rim** ← `Finishing` + athleticism (`Speed` / `FirstStep` / `Vertical`) +
  `BallHandling`. The handle-and-hops-no-jumper guard rolls rim-heavy
  automatically.
- **Mid** ← `Mid`. The one clean 1:1.
- **Short** ← **two distinct routes into the same zone, not one sum.** A bruising
  post scorer and a small touch guard both live in the short paint but arrive by
  different skills, and a single `Close + PostMoves` sum would push every good
  floater guard (low PostMoves) out to rim-or-three:
  - *post-touch route* ← `Close` + `PostMoves` (hooks, short post, interior
    face-ups).
  - *floater route* ← `Close`/`Finishing` + `BallHandling` + `SelfCreation` /
    `FirstStep` (runners, teardrops from the small quick guard).
  - Short expectation = a combination of the two routes (the exact blend is open
    math; the point settled now is that both archetypes must have a path in).
- **Long** ← the special one, and it needs **both access and reason**, not
  access alone. Creation gets a player *to* the pull-up space; it does not
  justify taking the shot. The expectation is a product:
  **creation access × perimeter pull-up ability × small stretch-post exception.**
  - *creation access* ← primarily `SelfCreation`, with a smaller `BallHandling`
    contribution (can he get separation).
  - *perimeter pull-up ability* ← `Mid`, with some `Outside` (the engine folds
    long-two skill into the perimeter family), i.e. can he actually punish from
    there. **A high-SelfCreation / weak-Mid / weak-Outside player does NOT become
    a long-two shooter** — he drives, gets to the rim, maybe floats; he is not a
    bad long-two artist. This is the exact pathology the whole session exists to
    remove, and creation-as-sole-gate would have reintroduced it.
  - *stretch-post exception* ← `PostMoves` × shooting ability (not the vague
    "post with real shooting ability" of the first draft — a concrete product).
    Even then "he's typically not shooting unless he's wide open," so the term is
    small. A post *without* shooting stays short/rim, never long.
  - Everyone lacking both a creation path and the stretch-post profile collapses
    to near-zero long, with the freed volume flowing to three (if he can shoot)
    or rim (if he can't). In modern offenses nobody else stands in that spot
    waiting for a pass.

All inputs already exist on `Player`. Nothing new needs to be authored — the
attribute surface was checked against source in this conversation and confirmed
sufficient.

---

## 4. The two shape properties (the hard parts)

### 4.1 Peakedness = relative shape AND absolute capability (not lopsidedness alone)

Emmett: skilled offensive players who can score from multiple levels have
**more flattened-out tendencies** — the flexibility is itself the weapon; they
take what the defense gives. The specialist is spiky (one weapon); the complete
scorer is flat (all of them).

The review sharpened this with a trap the first draft walked into: if flatness
came from *relative shape alone* (low variance among the five signals), a
merely-average, low-ceiling player whose mediocre skills happen to be even would
read as an "unguardable multi-level scorer" — nonsense. He is not versatile; he
is mediocre everywhere. **Peakedness therefore needs two inputs, not one:**

1. **Relative shape** — how uneven are the player's zone-relevant offensive
   tools? (A lopsided profile spikes toward its strength.)
2. **Absolute capability** — are those tools actually good enough to make him a
   credible threat in several places? (Evenness only earns flatness when the
   even tools are *good*.)

The complete scorer flattens because he has **multiple credible weapons**, not
merely because his numbers have low variance. The resulting diet shapes:

| Player | Skill pattern | Correct diet shape |
|---|---|---|
| Pure shooter | Outside excellent, rest limited | highly three-peaked |
| Rim athlete | Finishing/burst/handle excellent, shooting limited | highly rim-peaked |
| Multi-level scorer | several strong zone pathways | broad / flatter |
| Even but mediocre | several *average* pathways | somewhat balanced, but NOT "unguardable" |
| Weak player | no strong pathway | mostly constrained toward the least-bad accessible attempts, small variation |

The weak player's correct shape is *not* flat — it is constrained toward whatever
he can least-badly access, which is itself a form of peaking. Flatness is
reserved for genuine multi-zone capability.

This connects forward: the flat-tendency multi-level scorer is the player whose
diet the defense cannot scheme against, which is the "reacting to what the
defense is giving" realism Emmett named — Roll G's existing defensive-resistance
bend already supplies the in-game reaction; the flat spread is what gives that
bend multiple plausible destinations to move his shots toward.

### 4.2 Same ratings must NOT mean same tendencies — but variation needs discipline

The derivation is not a pure function of the ratings. Two players with identical
offensive numbers must come out with different diets — genuine per-player
variation layered on the derived spread. This is what replaces the old
independent-authoring principle (§2): the Klay-vs-Curry distinction survives as a
seeded style draw around the skill-derived expectation, not as a hand-authored
number.

The review's caution, accepted: **naive independent noise on five zone weights
would reintroduce the exact pathology we are removing** — one player could
accidentally become a 70% long-two shooter despite having no creation path. The
variation needs a disciplined home. The pipeline shape for the math pass:

```
skill-derived zone signals   (the expectation, §3)
  → correlated player-style variation   (the identity draw)
  → peakedness transform   (§4.1, relative shape × absolute capability)
  → normalize five weights to sum ~100   (the tendency values)
```

Guardrails on the variation term:

- **Correlated, not five unrelated dice.** A style draw shifts a coherent
  identity, not five independent knobs.
- **Narrower where basketball logic is strongest.** A zero-creation, low-Mid
  player must not randomly acquire a meaningful long two; the variation band
  collapses where the skill signal forbids the shot. Wider where several
  plausible pathways exist — that is where genuine Klay/Curry-type style
  distinction lives.
- **Player-stable and reproducible.** The seed is stable to the player, not
  dependent on incidental roster-generation ordering (the same
  order-independence discipline Roster Genesis already enforces).

Mental model for the design pass — not "random tendency noise" but a **latent
shot-identity draw**: spot-up specialist, downhill attacker, pull-up scorer,
interior technician, versatile opportunist. These need not be authored as
explicit types, but the variation should *feel* like it produces those
identities, bounded by what the skills allow.

---

## 5. The heave — DECIDED: not a generation-floor mechanic (deferred to Roll G)

The standing rule stands, restated by Emmett with the canonical example: even
Shaq with zero three-point skill needs a ~0.05% chance of a three — the
desperation heave off a broken play — taking **virtually the max shot-clock
time** (~28 seconds in). The question was whether a nonzero tendency floor at
generation is the right home for it. The review supplied a decisive argument, and
this is now a **decision, not an open question:**

**A generation floor cannot represent a heave, and calling it one is wrong on two
counts.**

1. *Semantic:* a static tendency floor produces a tiny chance of an
   **ill-timed *normal* offensive selection** — an early-clock Shaq three — not a
   late-clock desperation heave. A Shaq taking a broken-play buzzer heave is real;
   a Shaq receiving a regular early-clock three ticket because every tendency must
   be nonzero is a different and unrealistic thing.
2. *Representational:* the tendency fields are integer-ish values on a 0–99
   surface (the current role vectors are whole integers copied straight into the
   player fields). A "0.05% chance" is **not naturally representable** as one of
   those weights without knowing exactly how the downstream pie normalizes them —
   it does not live in that integer surface.

**The ruling:**
- Do **not** call a generation floor a heave mechanic.
- For this pass, allow **zero** zone tendencies — or, only if the engine
  structurally requires nonzero zone values (to be checked at build against the
  pie/normalization), a harmless numerical minimum with no pretense of being a
  heave.
- **True late-clock heaves are recorded as a Roll G / clock-context behavior**
  for the later possession-length work — a runtime mechanic that reads the shot
  clock, not a generation-time weight. A heave possession is a long possession,
  so this also ties into the deferred turnover-clock / possession-length dial.

This is the one place the brief favors a clean deferral over an approximate
first-pass representation.

---

## 6. Engineering shape (Claude's calls, flagged)

- **Derive at generation time; the `Player` surface is unchanged.** The five
  tendency attributes stay on `Player` exactly as they are; what changes is how
  the generator authors them (derivation instead of table lookup). Roll G, the
  coaching seam, the defensive bend, and every downstream consumer are untouched.
  This keeps the change fenced inside player generation.
- **The nine-role vocabulary survives; the tendency column dies.** Roles still
  drive skill emphasis, position quotas, lead-handler reservations, and the
  divvy's coverage logic — none of that is touched. Only
  `GenRoleDef.Tendencies` is deleted.
- **Oracle-first is a hard gate.** The derivation is deterministic math over
  drawn ratings plus a seeded variation draw — exactly the shape the Python
  oracle discipline exists for. The oracle mirrors the derivation
  constant-for-constant and proves the **structural** distributional claims
  (specialists spiky, multi-level scorers flat, no-creation players suppressed at
  long, weights conserve) across seeds before any C# is written. League 3PA rate
  is printed as a diagnostic, not gated on — see §8.
- **The stress-test archetype buckets keep working.** The archetype rosters are
  a lab instrument, not the population; their fixed hand-authored tendencies in
  `Program.Stress.cs` are independent of `GenRoles` and can stay as controlled
  fixtures — or be migrated to the derivation later. Not this pass's problem;
  recorded so the build doesn't "helpfully" touch them.

---

## 7. What this pass explicitly does NOT do (scope walls, provisional)

- **No calibration of the make curves or any Roll H parameter.** The Session 32
  midpoints stand; if the derived diets shift zone volumes enough to move
  per-zone FG%, that is a reading, not a thing to chase mid-pass.
- **No Roll G changes.** The clock-conditioned heave is deferred to a future Roll
  G / possession-length pass (§5), not smuggled into this one.
- **No coach-layer work.** The coaching ShotSelectionBias nudge continues to
  operate on whatever tendencies the player carries.
- **No tweener-post or versatility work.** Those remain their own parked Pass 2
  items (see the deferred design notes of 2026-07-03); this pass and those share
  a family but not a session.

---

## 8. The design-math agenda (ordered) and the oracle's proof obligations

The intent above is settled. The math is not. The follow-up design conversation
answers these **in order** — each builds on the one before:

1. **Per-zone signals, make vs access, defined separately.** For each of the five
   zones, define its ability-to-access signal and its ability-to-make signal as
   two things, not one blended key (§3 principle).
2. **Long two = creation × pull-up competence, not creation alone** (§3). Settle
   the creation-access curve (SelfCreation + some BallHandling), the pull-up term
   (Mid + some Outside), and the concrete stretch-post exception
   (PostMoves × shooting), including its small size.
3. **Short = post-touch route + floater route** (§3). Settle both routes and how
   they combine so neither the bruising post nor the small touch guard is pushed
   out of the zone.
4. **Peakedness from relative shape AND absolute capability** (§4.1). Settle how
   each is measured and how they jointly set the spread — and resolve the
   double-count flag: the specialist's three-spike must be owned by *either* the
   three signal *or* the peakedness transform, not both.
5. **A reproducible correlated variation model** (§4.2), player-stable seed,
   bands that narrow where the skill signal forbids a shot — while the
   clock-conditioned heave stays deferred (§5).

### The oracle's proof obligations (structural claims, NOT a calibration gate)

Oracle-first is a hard gate, but the review corrected *what* it proves. The
oracle mirrors the derivation constant-for-constant and proves **structural**
claims across seeds before any C# is written:

- High Outside + weak alternatives → high three tendency.
- Strong handle/burst/finishing + weak shooting → high rim tendency.
- High SelfCreation *without* pull-up shooting → does **not** produce excessive
  long two (the pathology guard).
- Multi-level strong players are flatter than true specialists; even-but-mediocre
  players are NOT flat (§4.1).
- Same ratings + different seeded identity draws → varied diets, all inside
  logical bounds.
- Long-two share is sharply suppressed for low-creation, non-stretch players.
- All five weights conserve (sum ~100) and pass `Player.Validate()`.

**League-aggregate 3PA rate is an oracle *diagnostic*, printed as a sanity read —
NOT a pre-build acceptance assertion.** This is a firm ruling, not an open
question. If the oracle gated on "the redesign succeeds only if league 3PA
reaches 0.39," the generation pass would quietly become a **calibration** pass
before the coach layer and selection mechanics have had their own turns — exactly
the tune-against-a-moving-target trap this project exists to avoid. The standing
principle (calibration proper waits for a real population) applies with full
force, and *this pass is what makes the population real*. Print the number; do not
tune to it.
