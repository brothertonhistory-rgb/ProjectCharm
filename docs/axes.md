# Project Charm — The Four-Axis Matchup Model

The derived layer that sits on top of the player attribute catalogue (`attributes.md`) and
turns raw player ratings into the *contextual terrain* a possession is resolved on. This is
the **Pass 2** design pass: it reads the catalogue, it does not re-list it.

**Status: conceptual / foundation, not a spec.** This document records the locked
conceptual decisions and the principles the model must honor. The actual MATH — how
attributes ladder into each axis, how a matchup computes a tilt, the non-linear coverage
formulas, the per-axis pie fingerprints — is genuinely undecided and is its own future
conversation. This doc is honest about what is settled and what is open, the same way the
Game Governor lived in `design.md` as "designed, not built" for several sessions before it
was real. Nothing here is wired; the wiring (Pass 3) is downstream of this.

---

## The four axes

Player and team quality is a composition across **four axes**:

1. **Athletic** — speed, quickness, first step, vertical, the explosive/movement physicals
   (i.e. the derived *athleticism* of the catalogue).
2. **Skilled** — shooting, handling, passing, finishing, the offensive skill family.
3. **Big** — height, wingspan, weight, strength; size for one's position.
4. **Experience / Cohesion** — the intangible, *time-developing* axis (see its own section
   below — it is materially different from the other three).

A player's TIER emerges from how many axes he clears, not from a single overall number:
none = not a college player; one ≈ D3, maybe D2; two ≈ high D2 / low D1; three ≈ solid D1;
all four ≈ power-conference. A player is not "a 78" — he is "athletic and big, not skilled
or experienced," and that *composition* places him.

**Basketball IQ is deliberately NOT one of the four axes.** It straddled individual and
team awkwardly, so it was pulled out and stays a **player attribute** (an individual
decision-family modifier, plus a possible rising-tide team check) in `attributes.md`. Its
exact behavior is a later conversation. Keeping it out lets the four axes each be clean
*relative-comparison* properties and lets the fourth axis be purely experience/cohesion.

**Roster FIT is also NOT an axis.** "Floor spacers around a downhill guard lift his finish
rate; he finishes worse when they sit" is real and wanted — but it is the **team-aggregate /
interaction layer** (gravity, spacing) tilting pies, lineup-dependent and on/off-court
sensitive, NOT a fourth-axis effect. Fit lives in the aggregates; the fourth axis is purely
reps and seasoning.

---

## The visualization (intuition, not formula)

The model is naturally pictured as a **radar / four-spoke chart**: each axis a spoke, a
team's profile the quadrilateral plotted across the four. A team strong on all four fills a
large, balanced diamond; a one-dimensional team is a spike — long on one spoke, collapsed
on the others. **Area ≈ overall talent; shape ≈ what kind of team you are.** Overlaying two
teams' quadrilaterals shows, at a glance, both who is better overall and *where each team's
edge is* — and the edge is the whole game.

**The asterisk that keeps the min-max out:** a radar chart's enclosed *area* is a
linear-ish sum, so taking area literally as "team strength" would be exactly the min-max
numbers battle the model forbids (maximize area = stack every spoke = stack the most rating
points). The chart is the right **intuition** and the wrong **formula**. Read it as
**coverage / shape**, not literal area. The real strength read must reward coverage (no
short edges) over raw area (biggest total) — see the hard constraint below.

The chart visualizes the *inputs* to the matchup, not its output: overlay two teams, and
the **per-spoke gap** on each axis is the per-axis edge that tilts specific pies. Where one
team's diamond bulges past the other on the "big" spoke, the size-driven pies tilt that way;
where it bulges on "skilled," the skill-driven pies tilt theirs.

> **Note for the math pass:** unlike the box-score four factors (eFG / TOV / ORB / FTr),
> which were chosen to be near-orthogonal, these four talent axes are **correlated** — "big"
> and "athletic" overlap (a long, explosive center scores on both), as do "skilled" and the
> read-dependent side of experience. A single trait can inflate two spokes. When defining how
> attributes ladder into axes, guard against double-counting one trait across two axes.

---

## Everything is RELATIVE — the load-bearing principle

The single most important correction this model rests on: **the matchup is relative, not
absolute, and basketball percentages are roughly static across levels.**

The radar comparison is never "are these players good on a universal scale" — it is "how do
these two teams stack against *each other*, on this floor, right now." The tilt comes from
the **gap between the two profiles**, not from their absolute height. Two D3 teams produce a
real, meaningful comparison even though neither would register against a power-conference
roster, because the advantage is the *difference* between them.

**There is NO per-level outcome baseline, and this was an explicit correction.** It is wrong
to think two evenly-matched D3 teams should shoot worse, in absolute terms, than two
evenly-matched D1 teams. They shoot about the same. A good shooter makes his shots at every
level — *shooting skill is abundant and roughly level-flat.* What separates the levels is not
how well an even matchup shoots; it is that as you climb, more opponents have the **size,
athleticism, and experience to contest and disrupt** that shooting and everything else.

So the same make-rate function runs at **every level** — one mechanism, not two. What looks
like "lower levels shoot worse" is really "lower levels have more evenly-mediocre disruption,
so games look normal, and only *mismatches* spike." A D3 team shoots ~70% against a high
school team not because of a level baseline but because the D3 team's relative edge in size,
speed, and skill is so large the high schoolers **cannot contest anything** — and uncontested
looks convert at a high rate at any level (capped only by the asymptotic ceiling; an open
shot still is not 100%). Drop that same D3 team against D1 and the percentage craters, because
now the defense *can* impose itself. The shooting talent did not change between those games;
the opponent's relative ability to take it away did.

This is the locked ceiling principle restated from the other end: **skill sets what you CAN
do; the disruption axes decide whether your opponent LETS you do it.** Athleticism is a
ceiling on skill *expression*, not the driver of outcomes.

---

## The axes are ASYMMETRIC

The four axes do not behave like interchangeable rating units. This asymmetry is the second
load-bearing finding and it shapes how attributes ladder into axes:

- **Skilled — abundant, roughly level-flat.** There are many genuinely skilled players at
  every level; a D2/D3 shooter can be as skilled as a D1 rotation player. Skill is the axis
  that is *common*.
- **Athletic / Big / Experience — scarce, concentrate upward, and primarily SUPPRESS an
  opponent's expression.** These are the axes that thin out as you climb, and they mostly act
  by *taking away* what an opponent's skill would otherwise produce — contest the shot,
  protect the rim, force the turnover, win the board, avoid the breakdown. They are the
  *disruption* axes.

**Each axis has a distinct compensation profile** — being down on one is a *different kind*
of problem than being down on another, so the axes cannot be summed symmetrically:
- **Size + athleticism can carry a zero-skill player** — the 7'1" rim-runner with no skill is
  viable at D1 because his value (blocks, rebounds, lob finishes) does not *require* skill to
  express; length and leaping alone deliver it.
- **Skill + IQ can carry an undersized player** — a 6'0" Chris Paul is viable because skill
  can partially *route around* a physical disadvantage.

So "down on athleticism" and "down on skill" are not the same deficit, and the model must
express that difference rather than treating axis points as fungible.

---

## The multi-front war (no fixed winner)

The axes are a **four-front war with no fixed beats-order** — not rock-paper-scissors with a
lookup table. Each axis *can* win an exchange, and every possession is a contest over which
axis got leverage:

- Five elite athletes can overwhelm with athleticism all over the floor.
- Five skilled players score, create, and hit tough shots regardless of size.
- Five players big for their position win on natural advantages that are hard to overcome.
- **The bigger the edge on any one axis, the more it dominates the others.**

A team that is genuinely better on **all four** is favored every possession and, barring
unlucky variance, wins by a substantial margin nearly every time — and that is correct, not a
flaw. The interesting cases are the lopsided ones:

- **Offsetting edges.** Team A is more skilled and athletic; Team B is bigger and more
  experienced. Neither dominates the radar — similar area, different direction — so the game
  is *live*, decided by which axis's pies matter most and by strategy and personnel inside
  that. You cannot just count whose total is higher, because the *direction* of an advantage
  interacts with what each team does with it.
- **The all-in (spike) build.** Going hard on one axis is a real strategy with real risk: it
  crushes opponents with no answer on that axis and gets dismantled by balanced teams that
  punish the three punted axes. High-variance, strong-but-exploitable.

> **HARD CONSTRAINT (recorded so the wiring pass cannot violate it):** team strength is **NOT
> the linear sum of rating points.** Pure addition *is* the min-max numbers battle the model
> forbids. Making a *balanced* roster able to overcome one or two outsized but one-dimensional
> talents requires **diminishing returns** (the fifth elite athlete adds less than the first)
> and/or **coverage / threshold mechanics** (being *credible* on an axis matters more than
> being elite on it; having no weakness beats having one peak). Linear sums are ruled out by
> the design intent itself.

---

## Emergent game CHARACTER

Because everything is relative and the axes are a war, the *character* of a game emerges from
how two profiles collide — and the same two teams can produce very different games:

- **Mutual can't-stop-each-other → shootout.** Each team's strength lands on the axis the
  other defends poorly; neither has the answer; both offensive pies tilt up; track meet.
- **Strengths land where the other defends well → rock fight.** Both offensive pies tilt down;
  a grind in the 50s.

Crucially, those opposite outcomes can come from teams of **identical overall talent** —
equal radar *area*, different *shape-alignment*. The quality did not change; the matchup
geometry did. The mechanism is that each axis tilts *different* pies, so two teams with edges
on different axes are not fighting over one number — they each win their own set of pies, and
the game is the sum of all those independently-tilted pies resolving. This is the
emergent-not-scripted property the project has wanted from the start, now at the game level:
you author two rosters and let their collision *discover* the game's character. It is also
what makes a *season* textured — stylistic matchups, bad games for good teams, the rock fight
an underdog steals by dragging a superior opponent into the one game where its profile
competes.

---

## Each axis is a PIE FINGERPRINT (the bridge from concept to buildable)

The form the model must take to be buildable: **an axis advantage is not a blurry "+X% to
everything." Each axis is a named bundle of specific pie tilts, in specific directions.** This
is what makes an advantage (a) **quantifiable** — it shows up in specific stats, so you can
read the result and tell which axis won; (b) **targetable** — strategy can amplify or suppress
*that specific bundle*; and (c) **offsettable** — different axes hit *different* pies, so two
edges on different axes can both be live in one game without cancelling.

Illustrative fingerprints (directional sketches, not final numbers — the math pass owns the
actual pies and magnitudes):
- **Athletic advantage** ≈ {rim-attempt share ↑ (first step beats the closeout, gets downhill
  and to the rim), transition frequency ↑, live-ball steals ↑, and those rim attempts convert
  high because they go *uncontested* against a beaten defender}. An athletic mismatch *looks
  like* a rim-heavy shot chart and an inflated steal count.
- **Big advantage** ≈ {close/rim attempt share ↑, offensive rebounding ↑, finishing-through-
  contact ↑, opponent rim conversion ↓ via rim protection}.
- **Skill advantage** ≈ {shot quality ↑ across zones, turnovers ↓, assisted looks ↑}.
- **Experience/cohesion advantage** ≈ {turnovers ↓ (especially vs. pressure), breakdowns ↓,
  composure on the road / in big moments ↑, exploits a greener opponent's mistakes}.

The worked example that ties it together: drop one genuinely athletic player into D3 and his
numbers go cartoonish — not because the engine buffs him, but because every roll he is in
resolves against opponents who cannot contest him. His shot-location distribution skews to
rim/close (he is *at the basket* constantly), and those rim attempts convert high *because no
one can challenge them*; his rebounding spikes for the same reason. Take the same player to
high-major D1 and the numbers normalize — same attributes, the *opposition* moved. Dominance
emerges from the mismatch; the make-rate function never changed.

**Defining the four fingerprints is where the Pass 2 math conversation should START** — not
"what are the axes" (settled here) but "what specific pies does each axis tilt, in which
direction, and how does the per-matchup gap scale the tilt." Once the four bundles are
written, the relative comparison and the emergent game character largely compute themselves.

---

## The three composing inputs (where this meets the pies — Pass 3)

The generators consume **three independent inputs** to shape a pie, composing *without ever
being pre-blended* (the locked "strategy and matchup modifiers stay independent inputs"
principle, extended from two inputs to three):

1. **Matchup (this four-axis model)** — sets the *contextual terrain*: which pies tilt which
   way, based on how the two teams' axes stack. *Where* the advantage is. **Derived per
   matchup, relative** — recomputed for each opponent, never stored as a fixed team property
   (the size tilt against a small team and against a huge one are not the same tilt).
2. **Coaching strategy** — tilts on top: what the team chooses to *do* with that terrain
   (press to attack an athleticism edge; slow it to a crawl to neutralize one). The *plan*.
   Strategy is a multiplier on the terrain — lean into your winning axis, or drag the game
   toward the axis where you win.
3. **Personnel / attributes / tendencies** — operate *inside* the already-adjusted
   percentages: *how well* it is executed and whether an axis edge is a slight lean or a
   blowout. *Who* is doing it.

Macro terrain first (the axes set the adjusted percentages), micro execution within it (the
granular player interactions, matchups, and tendencies play out inside that adjusted
envelope). Three signals, one pie, none pre-blended, contradicting at the output while blind
to each other at the input.

---

## Hidden, not public — surfaced only through outcomes

The four axes are **engine internals, not a UI panel.** Users do not see "your athleticism
axis is 82." They experience the axes through *consequences* — box scores, made percentages,
who wins the matchups — not by reading a number off a screen. Reasons this is the right call:

- **It keeps the experience emergent, not gamified.** Public axis ratings would invite
  direct min-maxing of a visible meter. Hidden axes mean users build teams the *real* way —
  recruit good players, develop continuity, construct sensible rosters — and the axes *emerge*
  from those natural choices. The anti-min-max intent, enforced at the *information* level,
  not just the math level.
- **It matches how the axes are computed.** They are *derived* — an internal synthesis of the
  underlying attributes and the roster/continuity situation, not authored numbers. Showing
  them would be showing the engine's scratch work.
- **It makes the experience/cohesion axis work properly** — that one especially should be
  invisible and emergent, felt through a team getting sharper over a season, not a "cohesion
  bar" filling up.

> **Consequence for the wiring pass:** hidden-and-emergent raises the bar on **outcome
> legibility.** If users cannot see the axes, the *results* must telegraph them clearly enough
> that a thoughtful user can *infer* what is happening ("my slasher's finishing dropped when I
> benched my shooters — keep spacing around him"). The per-axis pie fingerprints therefore do
> double duty: they make the engine work *and* they are the user's only window into the hidden
> layer, so they must read clearly in the box score and game flow.

---

## The experience / cohesion axis — the special fourth axis

The fourth axis is materially different from the other three and deserves its own treatment.
It is the **intangible, time-developing** axis, and it has **two components**:

1. **Individual experience** — accumulated career games / battle-testing, **carried by the
   player wherever he goes.** A five-year senior transfer who has played 100 college games
   brings that with him to a brand-new team — he has seen the rigors, he will not be rattled
   by a hostile road crowd or a March atmosphere — *even though this specific lineup has not
   logged a minute together.* He raises the team's experience floor the day he arrives.
2. **Team cohesion** — how long *this specific group* has played together, **grown over a
   season** as the unit logs games.

The axis is a combination of the two, which explains the full range:
- Grizzled transfers who just met → high individual experience, low team cohesion.
- Sophomores who have started together two years → moderate individual experience, high
  cohesion.
- The "Fab Five" five-star freshmen → **low on both** (young AND new) — the cautionary case.
  An organized, less-talented senior team can compete with (and sometimes beat) them by
  *exploiting* the gap: the freshmen blow un-rehearsed rotations, turn it over against
  pressure they have never seen, take bad shots because nobody has established the pecking
  order. Cohesion *suppresses the talented team's expression* — which makes this axis behave
  like the other disruption axes (it takes away what raw talent should produce), not like the
  abundant skill axis. And a young team gets *better as the season goes on* — same talent,
  rising cohesion, the spike filling in over time.

### The hard infrastructure dependency

This is the **one axis that cannot be computed from current state alone.** Athletic / skilled
/ big read off attributes that exist the moment a player exists. Experience/cohesion reads off
**history that only exists once seasons are being played** — so it has a hard dependency on a
cross-game persistence subsystem that does not exist yet:

- **Per-player career counters** — accumulated games/minutes a player has logged across his
  career (for the individual-experience component, carried with the player).
- **Lineup co-appearance logging** — a record of which players shared the floor, accumulated
  over games, so the engine can derive "these four started 70 games together" and feed it into
  the cohesion component.

This is **universe-layer / persistence** infrastructure, not possession-engine infrastructure.
Everything in the engine to date is *within-game*; cohesion is the first thing needing
*cross-game memory*. It is therefore structurally **downstream of the Game Governor AND the
future season/franchise layer** (which runs games in sequence and remembers them) — there are
no "prior games" to log against until that layer is running. So when the axis model is built,
**three axes are computable-now and the fourth is a forward-declaration waiting on
persistence.** The axis can be *designed* here; its data source is a franchise-layer subsystem
that comes later.

---

## What is settled vs. open

**Settled (this doc):**
- The four axes: athletic, skilled, big, experience/cohesion. IQ and roster fit are explicitly
  NOT axes (IQ → player attribute; fit → team-aggregate layer).
- Tier emerges from axis-count, not a single overall number.
- The model is purely relative; no per-level outcome baseline; one make-rate function at all
  levels.
- The axes are asymmetric: skill abundant/level-flat; athletic/big/experience scarce,
  upward-concentrating, suppressive, with distinct compensation profiles.
- Multi-front war, no fixed winner; better-on-all-four wins big (correct); offsetting edges and
  the all-in spike are intended strategic paths.
- Team strength is NOT a linear sum (hard constraint).
- Game *character* is emergent from profile collision.
- Each axis is a pie fingerprint; defining the four fingerprints is where the math pass starts.
- Three composing inputs (matchup / strategy / personnel), never pre-blended; matchup is
  derived-per-matchup and relative.
- The axes are hidden engine internals, surfaced only through outcomes; outcome legibility is a
  hard requirement.
- The fourth axis has two components (individual experience carried by the player + team
  cohesion grown over a season) and a hard dependency on cross-game persistence in the future
  franchise layer.

**Open (future conversations, NOT decided here):**
- How exactly each attribute ladders into each axis (and how to avoid double-counting a trait
  across correlated axes).
- The non-linear strength read — the actual diminishing-returns / coverage / threshold math.
- The four pie fingerprints — which specific pies each axis tilts, in which direction, and how
  the per-matchup gap scales the tilt.
- The asymptotic make-rate mapping and the *combined* ceiling on stacked aggregate tilts.
- How strategy multiplies the terrain (the strategy-layer design).
- The cross-game persistence subsystem (career counters + co-appearance logging) and the
  growth/decay model for cohesion — gated on the franchise layer.
