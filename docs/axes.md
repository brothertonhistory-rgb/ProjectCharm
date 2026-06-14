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

## The three tangible fingerprints (the precise bundles)

Pass 2's starting instruction — *"defining the four fingerprints is where the math conversation
should START"* — worked through for the three computable-now axes (athletic, skilled, big). The
experience/cohesion fingerprint is deliberately left open; it waits on the persistence layer.
**Structure and direction only: which pies each axis tilts, which way, by what mechanism, and the
shape of the gap -> tilt response. No magnitudes** — every number is a calibration-pass concern,
tuned by watching games.

### How a tilt enters a pie (the composition mechanism)

- **Make/miss is an effective-rating SHIFT, not a curve change.** A matchup tilt slides the
  shooter's *effective rating* along the one per-zone make-curve everyone shoots on (the Phase 2
  `RollHConfig.MakeProbability(zone, rating)` logistic), reused untouched. The curve is never
  reshaped, its midpoint/ceiling never moved per matchup, the output make% never multiplied. A
  contest is just the shooter sliding up or down the shared scale.
- **The zero point is AVERAGE defense, not an open shot.** No modifier = an utterly average
  contest, so a league-average shooter with no modifier shoots league-average. A *positive* modifier
  means the defense on that shot is worse than average (more open) -> higher up the curve; a
  *negative* modifier means tighter than average -> lower. Average sits in the *middle* of the
  scale, not the bottom.
- **Categorical pies are direct slice reweights.** Location, rebound, turnover, transition, and the
  tip have no make-curve — they are distributions, so a tilt reweights their slices directly (more
  rim share, more live steals, a heavier tip-win slice).

### The gap -> tilt response is ACCELERATING (a deliberate refinement)

The per-matchup gap on an axis is an abstract signed input here (its computation is the laddering
pass). The shape of gap -> tilt is **accelerating / convex, not saturating**: a marginal edge
(barely quicker, barely more skilled) is imperceptible; the effect grows *faster* than the gap as it
widens; a true mismatch produces cartoonish results. The only ceiling is **physical reality** — a
make% cannot exceed the curve's own asymptote (an uncontested shot is not 110%), a frequency cannot
exceed 100%. That bound is not an imposed cap (which would manufacture parity — forbidden by the
no-artificial-limits principle); it is the edge of what is possible, and the make-curve already
bends toward it on its own. Edges on different axes **compound** — each tilts its *own* pies, so a
team ahead on all of them has every pie leaning its way, multiplying on the scoreboard into a
blowout (correct, not a flaw). Absurd matchups are kept rare by **realistic scheduling**, not by
softening the math.

> This refines the conceptual sketch's "credible beats elite" lean *for the single-axis
> gap-response*: the bottom end holds (a sub-credible edge does almost nothing), but the top end
> does **not** saturate toward parity. The coverage / "no weakness beats one peak" *roster* read is
> a separate question — the strength-read math of the laddering pass.

### Two efficiency doors

A matchup moves scoring efficiency through two independent channels, and the engine already keeps
them as separate steps (where the shot comes from, then whether it falls):
1. **The shot mix** — which zone the attempt comes from.
2. **The make%** — whether that shot falls.
A quickness edge swings *both* (more rim attempts AND a higher make on everything, same beaten
defender). A quickness *deficit* against an equal-sized defender swings mainly the **mix** — fewer
rim, more jumpers — so efficiency drops through a worse shot *diet* even when each individual shot's
make is unchanged. Underneath, the mix has a standing pull toward the rim ("everyone wants the rim
if they can get there"); the matchup decides how much access is granted.

### Three independent dials set "how open"

How contested a shot is has three separate sources, kept in separate drawers so they never
pre-fuse:
1. **The defender matchup** — the four-axis terrain (this pass).
2. **Team fit** — gravity, spacing, and the passing aggregate (the team-aggregate layer).
3. **Role / usage** — who shoots when (tendencies).
Only the first is a fingerprint. The other two are real, wanted, and their own later passes.

### Athletic — a relative tilt

- **Make/miss (effective-rating shift, both ends, all zones):** a quickness edge buys a sliver of
  separation on *every* shot type — off a screen, relocating, attacking — so the shooter's make
  slides up; the mirror on defense slides the man he guards down. Strongest at the rim, real
  everywhere. There is no open/contested state in the engine, so this is the *average* separation
  edge expressed straight into the make%.
- **Shot location (direct reweight):** an offensive edge pulls the mix toward the **rim** (you get
  downhill); a defensive edge pushes the opponent's mix **out** (walled off the paint, settling for
  jumpers).
- **Usage (selection reweight) + the cascade:** a large athletic mismatch chokes the out-athleted
  player's *scoring touches* — he cannot get into his action — not just his make%. Forcing the ball
  to him anyway (strategy) bogs the offense down and spills usage to players never meant to carry
  it, dropping their efficiency too. The cascade is *emergent*: the matchup chokes the usage,
  strategy insists, personnel eats the spillover — nobody scripts "the offense breaks."
- **Turnovers / steals (slice reweight):** a big athletic gap manufactures **live** turnovers and
  steals on defense (a full-court press is strategy *weaponizing* this edge). The authored steal
  talent (quick hands) and basketball IQ on the offensive side are personnel inputs stacking on top,
  not part of this tilt.
- **Rebounding (secondary):** motor, leaping, quickness to the ball — rebounding above one's size,
  the second jump.
- **Transition:** the matchup owns the **efficiency** (rim-heavy mix, high finish — the open floor
  is already low-defense and the athletic edge cashes in); **frequency** is mostly a coaching choice,
  only nudged by the gap (athletes run when they can beat everyone down the floor).

### Big — a relative tilt

- **Make/miss (effective-rating shift):** rim protection slides the opponent's rim attempts
  **down**; finishing over and through a smaller body slides one's own rim **up** (plus a smaller
  bump shooting *over* a shorter man out on the floor). Length contests everywhere, weighted to the
  rim.
- **Blocks (slice reweight):** a length edge in a direct matchup raises the block slice, weighted to
  the rim and fading outward; a size *deficit* raises one's *own* blocked rate (the tiny finisher
  gets swatted more). The engine carves block off the top first (size-driven), then resolves
  make/miss among the rest (skill-driven) — so a small, skilled finisher reads as a high blocked
  rate *and* a solid make on what he gets off, both true at once.
- **Shot location (direct reweight):** a strength edge pulls the mix **inside** (close / rim —
  leaning on it to get into the paint).
- **Rebounding (primary, both ends):** size, length, and strength own the glass — securing the
  opponent's misses and grabbing one's own for second chances (each offensive board a *fresh
  possession*, compounding on the scoreboard). The rebounding *skill* (box-out, timing, positioning)
  is the baseline this terrain gates — decisive when the physical battle is even, dominant and
  compounding when it is not.
- **The tip:** a weighted roll on the wingspan / reach gap (even reach ~ 50/50; the win probability
  climbs an accelerating S-curve as the gap grows), consuming the centers' matchup. The seam already
  exists in `JumpBall.cs`.

### Skilled — a baseline AND an active contest (the keynote, corrected)

> **Correction to Session 33.** The first pass called skilled "the baseline, NOT a tilt" — a passive
> floor the physical axes slide around. That was half right and is now amended: skill *is* the baseline
> the shift starts from, **and** it is an active **skill-vs-skill contest** in its own right. The
> passive-only framing is retired.

Make/miss is not just the shooter's touch slid by a physical gap. It is a **contest between the
shooter's shooting rating and the *defender's* defending**, resolved as the same effective-rating
shift on the one shared per-zone curve. A great perimeter defender slides the shooter *down* even at
**equal physicals**; a weak one slides him *up*. So the effective-rating shift on any shot has **two
independent sources**, both moving the single effective rating, neither a separate roll:
1. **The physical matchup** — the athletic / big gap (the fingerprints above).
2. **The skill matchup** — the offense's relevant offensive skill against the defense's relevant
   defensive skill.

**There is no simulated "did he get open" step.** The quality of the look is an *input* to the shift
— set by the matchup now, and by the team's attention allocation later — never its own simulated
event. The engine resolves *odds*, not the play unfolding. "Openness" is just the name for the
**physical** side of that input; it is a contributor to the one make%, not a thing the engine rolls
(rolling "is he open," then "does the open shot fall," is the event-by-event simulation the project
rejected). This is the resolution of the earlier "two faucets" sketch: openness = getting the look
(the physical contribution) and conversion = making it (the skill-vs-skill contribution) are two
*inputs to the same shift*, not two sequential rolls.

This is what actually drives the locked **"skill decides the even game":** when the physical battle is
even — which, with realistic scheduling, is the vast majority of D1 games — the physical contribution
is ~0, so the **skill-vs-skill contest is the entire signal.** Skill goes *loud* precisely when the
physical tilts cancel. Not "what is passively left showing" — an active shooter-skill-against-
defender-skill fight that decides the game.

What the first pass got right and stays true:
- **Shooting touch** (close / mid / outside / finishing) is the make/miss **baseline** — the shooter's
  own rating, already wired in Phase 2. Both contests above slide it; touch sets where it slides *from*.
- **Ball security** (handling, passing) sets the **baseline turnover rate** the athletic steal-pressure
  pushes on — and this too is a skill-vs-skill contest (ball-handling against the defender's steal
  skill), the same shape as the make/miss one. Basketball IQ amplifies the decision side (a personnel
  modifier, not an axis).
- **Skill's creation side is a team aggregate, not a self matchup.** A team with strong passing earns a
  small make **bump** on teammates' shots — the alley-oop, the backdoor read — baked into the percentage
  because the engine cannot simulate the pass itself. This behaves like gravity and spacing (more good
  passers -> higher efficiency for everyone; five elite passers lift the whole floor), so it lives in
  the team-aggregate layer beside them, hidden, surfaced only in outcomes. It is the lifeline by which a
  smaller, less athletic but skilled team passes itself open and competes — skill routing around a
  physical deficit. (Distinct from self-creation, which is the athletic matchup's job.)

> **The pairing map is Phase 4.** Each offensive skill contests a *specific* defensive skill —
> shooting ↔ perimeter defense, ball-handling ↔ steal skill, finishing ↔ rim protection, and so on.
> Enumerating those **counter-attribute pairings** (and where defensive skill itself ladders) is the
> laddering pass, not settled here. This section settles only that make/miss (and the other skill pies)
> *are* skill-vs-skill contests, not a one-sided baseline.

**The asymmetry still holds, now stated correctly:** the physical axes aggressively *push* pies; skill
sets the level they push against *and* fights its own opposite number on top. Skill is what you *can*
do; the physical axes decide whether you are *let* to. It is not symmetric — a large physical edge
erases skill far more effectively than a large skill edge climbs out of a physical hole — which is why
the physical contribution, when it is large, swamps the skill contest, and why the skill contest only
becomes the whole story once the physical gap closes.

### Correlated-axis overlaps, resolved

- **Athletic vs. big at the rim:** athletic is *horizontal* (separation — beating your man to get
  open / to the rim); big is *vertical* (finishing and shooting over the top, walling off the rim).
  Different traits, no shared tilt.
- **Self-creation** (getting one's *own* shot open) is the athletic matchup's job, not a skill
  make-bump — the engine cannot model the coverage-breakdown event, so the physical gap is the
  lever.
- **Passing / playmaking** (creating a *teammate's* shot) *is* a make-bump, but a team-aggregate
  one, sitting with gravity and spacing — distinct from self-creation.
- **Rebounding skill** is its own ability gated by the size / athletic terrain, not the offensive
  skilled axis.
- **The tip** is a size (wingspan) contest, not athletic, though leaping nudges it.

### Dependencies (named, not designed here)

These fingerprints assume an opposing lineup exists to produce the gap. Still owed by later passes:
**defender identification** (who is matched on whom at a shot — the build pass), the **per-matchup
gap computation**, the **attribute -> axis laddering** (anti-double-count) and the **coverage /
strength-read** math (the laddering pass), all **magnitudes** (the calibration pass), the
**strategy layer** that multiplies the terrain, and the **team-aggregate** build (gravity, spacing,
the passing aggregate). Each fingerprint is also written to read in the **box score** — the user's
only window into the hidden layer.

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
- The three tangible fingerprints (athletic, skilled, big) are defined as precise per-pie
  bundles with directions and composition mechanisms (see "The three tangible fingerprints").
- Make/miss composes as an effective-rating SHIFT on the shared per-zone curve (reused
  untouched); the zero point is AVERAGE defense; categorical pies reweight slices directly.
- The gap -> tilt response is ACCELERATING (not saturating), bounded only by physical reality;
  edges on different axes compound; absurd matchups are kept rare by scheduling, not by caps.
- Skilled is a BASELINE *and* an active skill-vs-skill contest (corrected from Session 33's
  "baseline, not a tilt"). Make/miss is the shooter's shooting vs the defender's defending; the
  effective-rating shift has two sources (physical matchup + skill matchup), neither a separate
  roll. There is NO simulated "did he get open" step — openness is the physical *input* to the
  shift, not an event. Skill becomes the whole signal when the physical battle is even (most D1
  games). Touch still sets where the shift starts from; ball security is itself a handling-vs-steal
  contest; the creation/passing make-bump stays a hidden team aggregate beside gravity and spacing.
- The correlated-axis overlaps are resolved (athletic horizontal vs big vertical;
  self-creation -> athletic; passing -> team aggregate; rebounding skill its own ability;
  tip -> size/wingspan).
- Three composing inputs (matchup / strategy / personnel), never pre-blended; matchup is
  derived-per-matchup and relative.
- The axes are hidden engine internals, surfaced only through outcomes; outcome legibility is a
  hard requirement.
- The fourth axis has two components (individual experience carried by the player + team
  cohesion grown over a season) and a hard dependency on cross-game persistence in the future
  franchise layer.
- The attribute -> axis laddering is defined (Phase 4): athletic = horizontal separation
  (speed / quickness / first step); big = vertical reach + mass (height / wingspan / weight /
  strength / vertical); derived athleticism stays the full composite as the locked ceiling,
  distinct from the axis; named defensive ratings are the skill layer only (their physicals live
  in the axes); modifiers ride duels; discipline -> foul pies; no trait double-counted.
- The counter-attribute pairing map is defined per door (Phase 4): make (touch ↔ the
  perimeter→post→rim gradient), location (self-creation / handling ↔ on-ball D), turnovers
  (handling ↔ steals), the glass (off-reb ↔ def-reb), blocks (finishing ↔ rim protection), tip
  (wingspan). Team-aggregate / interaction items parked.
- The per-matchup gap function is defined (Phase 4): one convex gap→shift primitive (no imposed
  cap, only the curve's asymptote), per-axis physical gaps + per-pairing skill gaps summed
  additively onto the baseline; the curve's floor + flattening deliver the residual / clawback and
  "skill never extinguished," the convex physical delivers "size insurmountable"; categorical pies
  reweight slices; experience/cohesion enters as a hard-zero stub.

**Open (future conversations, NOT decided here):**
- The non-linear strength read — the actual diminishing-returns / coverage / threshold math.
- The experience/cohesion fingerprint — its pie tilts and gap-response. (The three tangible
  fingerprints — athletic, skilled, big — are now settled above; this fourth one is gated on
  the persistence layer.)
- The asymptotic make-rate mapping and the *combined* ceiling on stacked aggregate tilts.
- How strategy multiplies the terrain (the strategy-layer design).
- The cross-game persistence subsystem (career counters + co-appearance logging) and the
  growth/decay model for cohesion — gated on the franchise layer.

---

## Phase 4 — the individual-matchup tilt (laddering, pairings, gap function)

This completes the **Dependencies** block's "attribute → axis laddering (anti-double-count)" and
"per-matchup gap computation" for the case of **one attacker against one defender.** The roster
coverage / strength-read (no-weakness-beats-one-peak, diminishing returns, credible-beats-elite) is
**Phase 5**; the defender-ID picker and the generator wiring are **Phase 6.** Structure and direction
only — every number is a calibration-pass concern, tuned by watching games. (Phase numbering; for
these items it supersedes the older "Pass 2" framing per the append-only rule, newest canonical.)

### The two-layer shape (the spine this rests on)

Every door resolves the same way: a **baseline** plus a **physical layer** plus a **skill layer**,
summed onto the shooter's effective rating and read off the one shared per-zone curve
(`RollHConfig.MakeProbability`, reused untouched); categorical pies reweight their slices by the same
signed magnitude. Intuitively the **physical layer sets the scene** — how open is everyone if the game
were resolved on athleticism and size alone (equal → near-flat) — the coaching layer then leans on
that scene (deferred), and **skill goes to war inside it.** Arithmetically the layers are signed
nudges that **sum**; "physical first" is intuition, not a sequence of rolls. Zero gap = **average**
defense = the flat modifier (not an open shot). The gap → tilt response is **convex / accelerating**,
bounded only by the curve's own asymptote (and 100% for frequencies) — no imposed cap.

**The double-count line.** Offensive skill enters **once** — as the baseline (the shooter's own touch,
where he starts on the curve). The defender's defense enters **once** — as a nudge anchored at average
defensive stature. The physical openness gap slides him along that scale. No attribute is charged
twice: touch is the scale, the defensive rating is the nudge, the physical gap is the slide.

### Laddering — attributes → axes

The anti-double-count rule: **a physical trait feeds exactly one axis, and the named skill ratings are
a separate skill layer — never the same trait twice.** The physical axes are read off the raw
physicals; a defensive *rating* carries only the technique / timing on top of the physical tools the
axes already hold.

- **Athletic axis — horizontal separation:** speed, quickness (lateral), first step. (Getting open /
  getting downhill / staying in front.)
- **Big / size axis — vertical reach + mass disruption:** height, wingspan, weight, **strength,
  vertical.** The organizing question: *does the player's size and the heights he can reach disrupt the
  opponent's skill advantage?* (Finishing and shooting over the top, walling off the rim, the glass,
  contesting at the rim.) Strength and vertical land **here, not on athletic** — the resolved
  horizontal/vertical split.
- **Derived athleticism stays the FULL composite** (strength + speed + quickness + first step +
  vertical) **purely as the locked ceiling concept** — distinct from the athletic *matchup axis*, so
  the axis never re-counts the composite's parts. Same name, two objects: the axis is the
  horizontal-separation read; the composite is the skill-expression ceiling.
- **Offensive skill — the door baselines (not axis members):** the touch ratings (Close / Mid /
  Outside / Finishing), ball-handling, self-creation, offensive rebounding, post moves, passing. These
  set where a player *starts* on each door; the matchup slides them.
- **Defensive skill — the skill-layer defensive faces (technique / timing / discipline only):**
  perimeter D, post D, rim protection, defensive rebounding, steals. Their physical underpinnings
  (quickness, length, size, strength) live in the axes; the rating is the residual skill on top, so
  perimeter D never secretly re-counts the quickness the athletic axis already used.
- **Modifiers ride duels, never axis members:** hustle amplifies the effort family (the glass,
  steals); IQ the decision family (creation / playmaking).
- **Discipline feeds the foul pies directly** — not an axis member, not a matchup duel.
- **Free throw is matchup-immune** — the flat 1:1 in Roll L; no defender contests it.
- **Composition within an axis** (flat composite vs. weighted) is left a calibration choice; the
  structure keeps the member weights configurable.

### The counter-attribute pairing map (per door)

Each door is one offensive-skill baseline contested by a specific defensive-skill counter, over a
physical-axis scene — the same baseline → physical → skill shape on every door:

- **Make %** — touch (by zone) ↔ a defensive **gradient**; physical = athletic + big openness. The
  gradient slides with shot distance: **Outside ↔ perimeter D · Mid ↔ perimeter D + post D · Close ↔
  post D + rim protection · Rim / Finishing ↔ rim protection · Post moves ↔ post D.** (Perimeter →
  perimeter+post → post+rim → rim, moving out-to-in; the composite blend weights are calibration. The
  mid-range pairing is a 3/4-type read: a mid scorer punishes a small defender in the post or drags a
  post defender out to test his foot speed, so a switchy perimeter+post defender is the counter.)
- **Shot location / mix** — self-creation + ball-handling (control) ↔ perimeter / on-ball D; physical
  = athletic (separation → rim) and big (strength → inside). **Self-creation gates shot *type*:** low →
  limited to catch-and-shoot and entry-to-position looks; high → manufactures his own off the dribble
  and beats on-ball pressure. (A low-self-creation scorer therefore leans on team scaffolding — gravity,
  spacing, the passing bump — to get manufactured looks; a self-creator is self-sufficient.)
- **Turnovers** — ball-handling (security) + passing ↔ steals; physical = athletic (hands / quickness).
- **The glass** — offensive rebounding ↔ defensive rebounding; physical = big (primary), athletic
  secondary (motor / second jump).
- **Blocks** (carved off the top *before* make resolves) — finishing / shooter size ↔ rim protection;
  physical = big (length).
- **The tip** — a wingspan / reach contest; physical = big. (The existing `JumpBall` seam.)
- **Transition** — athletic owns the *efficiency* (open floor, rim-heavy, high finish); *frequency* is
  mostly a coaching choice (deferred).

**Parked — team-aggregate or interaction effects, not one-on-one duels:** passing's creation
make-bump, gravity, spacing, help defense, off-ball defense (the team-aggregate layer); screening (a
screener-helps-handler interaction); playmaking's creating-for-others (IQ-amplified, team-aggregate
side); off-ball movement, whose real counter is off-ball D (team-aggregate) — its individual catch
rides perimeter D and the rest is deferred.

### The gap function (shape, not magnitudes)

1. **One convex primitive.** A single accelerating gap → shift curve (a power / exponential on the
   signed gap): near-flat for a marginal gap, steepening as the gap widens, cartoonish at a true
   mismatch. **No imposed cap** — the only ceiling is the make-curve's own asymptote (and 100% for
   frequencies), which it already bends toward. The primitive is reused everywhere; **per-axis and
   per-pairing weights** parameterize it (surface variety as parameterized inputs to one contest type,
   not new contest types). Knob: the steepness / exponent, tuned **steeper for physical than for
   skill** — the "physical louder than skill" asymmetry living in the shape itself.
2. **Two outputs.** Curve pies (make / miss): the shift is an **effective-rating delta** added to the
   baseline and fed straight into `MakeProbability` — the curve is never reshaped, its output never
   multiplied. Categorical pies (location, turnovers, rebound, transition, tip): the same signed
   magnitude is a **slice reweight**.
3. **The gap split.** The **physical** gap is taken **per-axis** (an athletic gap, a big gap, as
   bundles, which **compound** on a door where both apply). The **skill** gap is taken **per-pairing**
   (this offensive rating vs its defensive counter), anchored at **average** defense so a player facing
   an average defender gets the flat modifier.
4. **Composition — additive, no separate recovery term.** Physical (Σ athletic + big) and skill (the
   pairing duel) are signed shifts that **sum onto the baseline.** The recovery behaviors the design
   demands — *talent claws back a few against an overwhelming physical edge*, *athleticism can never
   fully extinguish skill*, *size can never be completely defeated* — are **not** a bolted-on clawback
   term; they fall out of the structure already in hand:
   - **Skill lives in the baseline,** always present and scaling with skill, so pure athleticism never
     zeroes it — a monster physical nudge drags a high baseline down hard, but it is still *his* high
     baseline being dragged.
   - **The make-curve has its own floor and flattens near it.** A monster physical edge crushes a
     player toward the bottom of the curve, where (a) make% never reaches 0 and (b) the make%-distance
     between him and a scrub **compresses** — so athleticism "wipes out the skill gap" in the box score
     even though the rating contributions are unchanged, while the skilled player still edges the scrub
     and hits a few.
   - **The physical nudge is convex,** so at an extreme gap it swamps even a high baseline (**size
     insurmountable**), and at a small gap it is ~0 so the skill duel is the whole signal (**skill
     decides the even game**). Tuning the physical convexity steep enough that an extreme gap outruns
     the whole rating scale guarantees "size can't be completely defeated" by the *shape*, not a cap.
   - Beneath all of it sits the **per-possession floor** (see `design.md` — the squirts-free /
     defender-falls residue of events the engine does not model): skill-agnostic, everyone gets a
     sliver. A residual-floor config knob can be exposed purely for calibration headroom, but it is not
     structurally required.
   - *Why additive over multiplicative `gap × (1 − recovery)`:* additive is one mechanism,
     double-count-free, and gives "skill decides the even game" automatically (physical = 0 → net =
     skill), whereas the multiplicative form needs a second additive skill term bolted on for the even
     case.
5. **The 4th-axis stub (experience / cohesion).** It enters the sum as a **hard-zero-gap term** — the
   slot exists in the gap function's signature and contributes nothing until the cross-game persistence
   layer (career counters + co-appearance logging) feeds it. Neutral now, wired later; no shape designed
   here.

### What Phase 4 does NOT do (the walls held)

- **No roster coverage / strength-read** (no-weakness-beats-one-peak, diminishing returns,
  credible-beats-elite) — Phase 5.
- **No defender-ID picker and no wiring** — Phase 6; Phase 4 assumes a defender is given.
- **No team aggregates** (gravity, spacing, help / off-ball defense, the passing make-bump) — later
  layers, parked above.
- **No strategy, tendencies, usage / attention, streaks, fatigue, game-state.** The out-athleted
  player's **volume choke** ("he'd never shoot 20 times") is the **usage / cascade** lever and is *not*
  here — Phase 4 owns only the per-shot tilt (his looks are poor and few of them fall); how many touches
  he gets is the usage layer.
- **No magnitudes.** Anything wanting a number is logged for the calibration pass.
- **`MakeProbability` is untouched** — reused exactly as Phase 2 built it.
