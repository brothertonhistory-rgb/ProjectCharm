# Project Charm — National Pool & Divvy: Design Brief (Roster Genesis)

Settled in the design conversation of 2026-07-02, immediately after World Structure
Pass 1 shipped. This is the design record for a **new pass inserted into the
world-structure arc**, ahead of the season loop: rosters come from a single national
talent pool divided among programs by prestige-weighted selection — not from per-team
generation off a prestige number. It is a brief, not a build prompt; the build prompt
gets drafted, audited, and reviewed per CONVENTIONS §6.

---

## 1. The frame — why the pool replaces per-team generation

Session 26's generator answers "what does a prestige-74 roster look like?" with a
hand-authored curve — one authored answer per prestige level, with no reality to check
any of them against. That was the right Pass 1 for proving the three-leg machinery; it
is the wrong foundation for a living universe. Emmett's framing, which this brief
adopts wholesale: the realism of the universe comes from **recruiting classes being
absorbed by hundreds of teams** — trying to artificially author what a 74 has versus a
27 is asking for problems.

The pool model flips what gets authored:

- **One authored distribution, not ninety-nine.** The country's talent shape is the
  only thing designed — how many stars exist, how deep the middle class runs — and it
  has real basketball anchors (All-American counts, rotation-body counts). Prestige
  stops being a quality dial and becomes what it actually is: **access to talent**.
- **Talent becomes zero-sum.** Under per-team generation, thirty 85-prestige teams
  meant thirty loaded rosters — national talent inflated with prestige. A pool makes
  elite players scarce because the pool says how many exist. This is the player-side
  twin of the prestige conservation force.
- **The Pass 3 variance assumption gets a natural home.** The dynamics loop's health
  depends on same-prestige teams varying and prestige levels overlapping. Under a
  divvy that variance *emerges* from selection noise — the 27 that lands the gem the
  74's scouts missed — instead of being authored. Recruiting misses are the realism,
  not a knob.
- **Ordering constraint (the decisive one):** this must exist **before the dynamics
  are tuned.** If Pass 3's forces were calibrated against per-team-generated rosters
  and the pool swapped in later, we would have tuned against a moving target — the
  standing principle this project exists to honor.

---

## 2. Standing rules (hold for this pass and after)

1. **The no-scalar wall stands, with one quarantined exception.** The divvy needs a
   preference order, so players carry a **scout rank** — a draft-ordering number.
   It lives *only* in the divvy (and, one day, recruiting). It is never an input to
   game resolution, never printed as an "overall," never used to sort a lineup.
   It is the star-rating of the recruiting world, not a team-strength scalar.
2. **Count-agnostic.** The pool size derives from the world file (schools × roster
   size, 10 for now). 20 schools or 1,600 work without code changes.
3. **Reproducible.** The pool and the divvy are deterministic from the world file
   plus a seed, same as world seeding — a shared file replays the identical draft.
4. **Prestige never touches the possession engine.** It shapes *access* (selection
   odds); game outcomes still emerge from player matchups only.
5. **The Session 26 machinery survives.** The three-leg model, the rating bands, the
   size scaling, the free-throw shape, the competency floors, the leg-health
   guarantee, the role vocabulary — all untouched. What changes is *where a player's
   leg profile comes from* (the national mix, not a per-team prestige curve) and
   *how a roster assembles* (a divvy, not ten local rolls).

---

## 3. The settled model

### 3a. The third-leg gradient (the design statement this pass is built around)

Emmett's call, verbatim in spirit: it is not that a player must have all three legs —
it is that **once you have two, every ounce of the third is super valuable.** A big
skilled player with *a little* athleticism unlocks a lot; a skilled athletic player
with *a little* size unlocks a lot.

Engine translation: today a leg you lack is a flat "ordinary" stamp (44–58, largely
undifferentiated). This pass makes the third leg a **real gradient**: among two-leg
players, the national pool controls not just how many exist but how their third leg
is distributed — most scarce, a meaningful minority raised into genuinely useful
territory, a rare few effectively "2.5-leg" players. That gradient is most of what
separates the pool's upper-middle class from its middle class, and it is honest to
how the engine already cashes it (athleticism is a ceiling-unlocker on skill; size
floors what a body earns at the rim), so the value is convex without anyone faking it.

### 3b. The national pool

One pool of **10 × (school count)** unclassified players (no freshman/sophomore
classes — a bootstrap roster stands in for four absorbed recruiting classes; classes
and ages are recruiting-arc design, deliberately parked). The pool's shape is the one
authored distribution: the national **leg-count mix** (how many three-leg stars, how
many two-leg players, the one-leg mass) plus the **third-leg gradient** within the
two-leg population, with positions drawn to a national shape (~4 guards / 3 wings /
3 bigs per 10, same vocabulary as today).

**Magnitudes are deliberately not fixed in this brief.** Emmett's stated position:
we don't know how to decide these weights at the beginning, and that is fine — the
pool model is exactly what makes it fine, because there is now *one* distribution to
tune and an instrument to look at it with. The rhythm is Session 26's: concrete
numbers proposed at prompt time, judged by Emmett in basketball terms ("too many
stars," "the middle class is too thin"), proven by a Python oracle over thousands of
generated pools before any C#, then adjusted against roster sheets and sims after.
Everything ships marked placeholder.

### 3c. The scout rank

A single ordering number per player, computed from his generated profile, used only
to sort the draft board. Two properties bind:

- **Convex in the third leg** — a two-leg player's rank climbs fast with every point
  of the third leg (3a), mirroring real scouting ("he'd be a lottery pick if he could
  move").
- **Only roughly right.** Rank-versus-actual-sim-value disagreement is not a bug to
  minimize; it is the recruiting miss, and the divvy adds per-team noise on top (3d).
  The low-prestige school drafting the player whose third leg the rank undervalued is
  the Gonzaga origin story, generated for free.

The exact formula is engineering (Claude's lane), flagged at prompt time, oracle-shown
before build.

### 3d. The divvy

Pick by pick until every roster holds ten:

- **Who picks:** each pick is awarded by **prestige-weighted odds** among programs
  with roster space. The weighting curve's steepness is a named dial — steep means
  blue bloods nearly always pick first (rigid pyramid), shallow means real leakage
  downward. **This is a constitutional dial of the universe, not a flavor setting:**
  too steep and the pool collapses into the old authored prestige curve with more
  steps; the whole payoff is a high-prestige program getting *more cracks* at the
  best players while still occasionally losing one. Placeholder until burn-in, tuned
  alongside the prestige forces.
- **What they pick:** generic, no styles or coaching preferences (no coaching layer
  exists yet — its arrival is exactly when picking gets personality). Need-aware via
  the existing coverage vocabulary — a team must end with the lead handler / wing
  defender / interior body roles covered and a playable ~4/3/3 positional shape — then
  best-available by scout rank **with noise**: a per-team scouting error on the rank,
  so two teams do not see the same board. Need + rank + noise, nothing else.
  **Board noise is stable within a divvy:** each program's perturbation of each
  player's rank is drawn once, deterministically from the seed + program + player,
  and never rerolls from pick to pick — the noise is a coherent *alternate scouting
  board* (Program A genuinely loves him, Program B genuinely underrates him, and the
  disagreement persists), never a per-pick coin flip that makes a team's own
  evaluation incoherent between selections.
- **Coverage is a hard constraint, never a preference score.** Two failure modes are
  designed out, not penalized: a team's late picks are *constrained* the moment an
  unconstrained pick would make its own coverage mathematically unsatisfiable (eight
  happy picks then an impossible final two is the trap); and the **pool's positional
  totals must make every roster's coverage globally satisfiable** — the pyramid-vs-
  floors feasibility check wearing a different hat, validated loudly before the first
  pick, never discovered as a stranded team at pick 3,400.
- **Every roster ends legal.** The Session 26 coverage guarantees move from
  per-roster generation into the divvy's need logic; a nonsense roster (six guards,
  no big) remains impossible by construction, and `Player.Validate()` still binds.

### 3e. What replaces the prestige→leg-count curve

Nothing, and that is the point: prestige no longer *generates* anything. The
Session 26 per-team mode (`gen`) survives as a lab instrument — two hand-specified
programs remain useful for isolated experiments — but the world's rosters come from
the pool, and the prestige→depth relationship the old curve authored now **emerges**
from access. The readout must show it emerged: that is this pass's proof.

---

## 4. The proof (this pass's readout)

Same lineage as the roster sheet and the world report — the divvy is only done when
the page shows:

- **The pool on the page:** the national leg-count mix and third-leg gradient as
  generated, against the authored targets.
- **The draft legible:** picks by prestige band (did access work), notable
  "misses" (high-rank players landing low, low-rank gems ranking high in hindsight).
- **The emergent depth story:** roster sheets across the prestige range — the
  Session 26 headline (high prestige stays two-leg deep into the rotation, low
  prestige craters after the top man) must now *emerge* from the divvy rather than
  be authored, and the sheet is where we check it did.
- **Variance and overlap visible, with the two sources kept separate:** same-
  prestige-band teams differing meaningfully; adjacent bands overlapping — the
  recorded Pass 3 assumption, observed early. **Access variance** (who got the
  picks) and **evaluation variance** (per-team board noise) are distinct dials and
  the readout keeps them distinguishable, so each can be tuned independently later.

**One legible world sheet is the page; many seeds are the proof.** A divvy can look
excellent in one seed and still carry a bad expected prestige/depth relationship —
so the oracle's acceptance runs across many seeded worlds (the world-brief's "many
seeds, never one attractive universe" rule, applied to rosters), with the single
sheet shown because it is readable, never because it is the evidence.

Sim smoke test (a handful of drafted teams through the engine) belongs here only as
a sanity check; the full population-scale season is the next pass.

---

## 5. The arc map amendment

The world-structure arc (world-structure-brief.md §4) gains a step. New order:

- **Pass 1 — the static skeleton.** SHIPPED (Session 28).
- **Pass 1.5 — national pool & divvy (this brief).** Rosters for every program in a
  world file, from one pool, by prestige-weighted access.
- **Pass 2 — minimal season loop.** Now runs on divvied rosters (design as
  previously discussed: placeholder scheduling, real engine games, standings +
  prestige-vs-wins proof; details settled at its own prompt time).
- **Pass 3 — the dynamics.** Unchanged, now tuned against pool-built populations —
  the ordering constraint in §1 satisfied.
- **Pass 4 — the burn-in readout.** Unchanged.

**Deferred out of this pass:** classifications/ages and multi-class pools, real
recruiting (the divvy is its bootstrap stand-in, not its design), coaching styles
and preference-driven picking, transfers, per-conference or regional draft biases,
engine-side migration of generation, and any calibration of pool magnitudes beyond
"looks like college basketball on the sheet" (real calibration follows the season
loop, per the standing principle).

---

## 6. Open items (flagged, not blocking)

- **The pool's magnitudes** (3b) — proposed at prompt time, Emmett judges as
  basketball, oracle proves, ships placeholder.
- **The scout-rank formula** (3c) — engineering, convexity property binding, shown
  not slipped.
- **The odds curve** (3d) — the Gonzaga dial; placeholder until burn-in.
- **Where the divvy lives** — harness mode shape (a new mode vs. an extension of
  `gen`/`world`) is build-prompt engineering; the constraint is only that the world
  file is the input and the suite proves determinism.
- **Whether one pool draw needs stratifying** — if pure odds produce degenerate
  boards at small n, the fix is engineering, named at prompt time if needed. Either
  way, **small-N oracle fixtures are required, not assumed**: a 20-school world is
  not a miniature 1,600-school world (randomness is louder, positional scarcity
  bites harder, one strong pool draw can distort a whole prestige band), so the
  oracle proves the divvy at fixture scale explicitly rather than trusting the
  production logic to generalize down.
