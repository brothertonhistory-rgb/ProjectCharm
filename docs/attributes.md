# Project Charm — Player Attribute Catalogue

The raw material of the player model: every attribute a player carries, organized by
umbrella, with each tagged by KIND. This document is the catalogue ONLY — the complete,
de-duplicated list of inputs. It deliberately does **not** describe how attributes mold
pie percentages, nor the four-axis matchup model. Those are separate, downstream design
passes (see "Deferred — the passes this catalogue feeds" at the bottom). Cataloguing the
inputs and wiring them are two different jobs; doing them at once is how the list either
narrows prematurely or stalls on wiring questions that have no answer yet.

This is the attribute-model sibling to `design.md`: a standing reference, updated as the
model is refined, recording WHAT the attributes are and WHY they are shaped that way —
not a task list.

---

## The four attribute KINDS (read first)

Every attribute is one of four kinds. The kind is a fact about the attribute itself —
true regardless of how it ever touches a pie — and it decides how the attribute is
authored and (later) consumed. This is the same author-vs-derived discipline the engine
uses for stats (primitives stored, derived computed on read).

1. **Authored individual.** A raw number typed in per player. Feeds an outcome directly.
   The bulk of the list (finishing, perimeter defense, strength, …).

2. **Derived.** Computed by the engine from other attributes — never authored. Athleticism
   is the archetype: it is the composite of the explosive/movement physicals, not a number
   you set. A derived attribute can itself depend on another derived attribute (transition
   depends on athleticism, which is already derived) — the dependency order must stay
   explicit so nothing is authored that should be computed.

3. **Team-aggregate.** Authored (or derived) per player, then **aggregated across the
   five on the floor** into a team value that produces a small *environmental* tilt on
   *other* players' pies — not a per-matchup effect. Spacing, gravity, help defense.
   Two rules distinguish this kind: the aggregation is **non-linear** (count/distribution
   of credible contributors matters more than peak rating or flat mean), and the members
   are **not uniform in leverage** (some are subtle season-felt flavor; some are
   strategy-enabling and need real weight — see the magnitude note under each).

4. **Modifier / amplifier.** Authored per player, but it does not produce an outcome on
   its own — it **scales a *family* of other attributes**. Hustle amplifies the effort
   family (rebounding, steals, loose balls); basketball IQ amplifies the decision family
   (playmaking, help defense). A modifier reads another attribute and multiplies it rather
   than feeding a pie directly. May also apply as a team-threshold.

A fifth tag, orthogonal to kind, marks **wiring status**:
- **live-on-arrival** — has a clear home in a pie that already exists; a real generator
  consumes it the moment one replaces the stub.
- **dormant-pending-module** — real and authored (a value per player), but no generator
  reads it until a named future module exists. The attribute-side analogue of Roll C's
  Session-24 expansion: seated and proven to exist before anything consumes it.

---

## Summary table — by kind

| Attribute | Umbrella | Kind | Wiring status |
|---|---|---|---|
| Close | Offense | Authored individual | live-on-arrival |
| Mid | Offense | Authored individual | live-on-arrival |
| Outside (3s + long 2s) | Offense | Authored individual | live-on-arrival |
| Finishing (the rim rating) | Offense | Authored individual | live-on-arrival |
| Free throw | Offense | Authored individual | live-on-arrival |
| Ball handling | Offense | Authored individual | live-on-arrival |
| Passing | Offense | Authored individual | live-on-arrival |
| Playmaking | Offense | Authored individual | live-on-arrival |
| Self-creation | Offense | Authored individual | live-on-arrival |
| Post moves | Offense | Authored individual | live-on-arrival |
| Off-ball movement | Offense | Authored individual | live-on-arrival |
| Screening | Offense | Authored individual | live-on-arrival |
| Offensive rebounding | Offense | Authored individual | live-on-arrival |
| Gravity | Offense | Team-aggregate | dormant-pending-module |
| Spacing | Offense | Team-aggregate (2-stage) | dormant-pending-module |
| Transition | Offense | Derived (from physicals) | dormant-pending-module |
| Perimeter defense | Defense | Authored individual | live-on-arrival |
| Post defense | Defense | Authored individual | live-on-arrival |
| Rim protection | Defense | Authored individual | live-on-arrival |
| Defensive rebounding | Defense | Authored individual | live-on-arrival |
| Steals / turnover generation | Defense | Authored individual | live-on-arrival |
| Help defense | Defense | Team-aggregate | dormant-pending-module |
| Off-ball defense | Defense | Team-aggregate | dormant-pending-module |
| Height | Physical | Authored individual | live-on-arrival |
| Wingspan | Physical | Authored individual | live-on-arrival |
| Weight | Physical | Body-input (drives Strength up, Quickness/Speed down at generation) | not-engine-consumed; generation-time only (spec locked, build pending player-gen module) |
| Strength | Physical | Authored individual | live-on-arrival |
| Speed | Physical | Authored individual | live-on-arrival |
| Quickness (lateral) | Physical | Authored individual | live-on-arrival |
| First step | Physical | Authored individual | live-on-arrival |
| Vertical | Physical | Authored individual | live-on-arrival |
| Endurance | Physical | Authored individual | dormant-pending-module (stamina) |
| Athleticism | Physical | Derived | n/a (composite ceiling) |
| Hustle | Intangible | Modifier (effort family) | live-on-arrival |
| Basketball IQ | Intangible | Modifier (decision family) | live-on-arrival |
| Discipline | Intangible | Authored individual | live-on-arrival |

**Moved to the Tendencies doc (NOT attributes):** aggression (shot / drive / gamble
frequency), shot selection (questionable-shot tendency). A tendency is *how often you
choose to do a thing*; an attribute is *how good you are at it*. The two are different
inputs and live in different documents.

**Cut / absorbed during cataloguing:** standing reach (→ wingspan), and from the physical
menu: hand size, durability, recovery rate, reaction time, flexibility, balance, agility,
acceleration-vs-top-speed. Durability/recovery are universe-layer (season availability),
not possession-pie movers; reaction time has no event to roll against in an outcome-first
engine; the rest were redundant or unexpressible.

---

## Offense

### Scoring zones — Close / Mid / Outside
Three authored shooting ratings by distance. **There is deliberately no separate "Rim"
shooting value** — rim conversion is the **Finishing** rating below, so a fourth zone
would have been the same number twice. Outside folds the three and the long two together
(one bucket for now; corner-vs-above-the-break is a cheap future slice-split, not
front-loaded). Each zone is kept to one clean meaning so each has a real-world FG% to
calibrate against — the same localized-bucket discipline Roll G uses for shot location.

### Finishing
The rim-attempt rating — converting the look at the basket. Distinct from raw athleticism
(which gates *how high* you can finish) and distinct from "and-1 ability" (finishing
*through contact*, which the engine's `MadeAndFouled` outcome makes a separable thing).
Finishing IS the engine's "rim" scoring rating, standing on its own rather than as a shot
zone.

### Free throw
The cleanest 1:1 in the whole model — a 72-rated shooter makes ~72% per attempt, no
gravity term, no matchup, no logistic. Roll L (free-throw resolution) is already shaped
for exactly this: a flat make/miss pie whose make probability is the shooter's rating.

### Ball handling / Passing / Playmaking / Self-creation
The four with the fuzziest internal borders, kept separate because each plausibly moves
different pies:
- **Ball handling** — security and control of the ball (turnover resistance, beating
  pressure).
- **Passing** — delivering the ball accurately (assist quality, turnover avoidance on the
  pass).
- **Playmaking** — reading the floor and creating *for others* (the decision skill, the
  thing IQ amplifies).
- **Self-creation** — generating one's *own* shot off the dribble (creating with the ball,
  the counterpart to off-ball movement's creating *without* it).

### Post moves
Back-to-basket scoring as a STYLE that cuts across close range — kept separate from the
"Close" zone because it is a distinct skill population (footwork, leverage) rather than a
distance bucket.

### Off-ball movement
Getting open *without* the ball (cutting, relocating, catch-and-finish) — the counterpart
to self-creation. The catch-radius / open-look family.

### Screening
The screener's quality affects what the ball-handler gets off the screen. Physical-adjacent
(strength + timing) but authored as a skill.

### Offensive rebounding
Pursuing and securing the team's own misses. Feeds Roll I / Roll K's offensive-board arms;
its rate is also a possession-count calibration knob (too many offensive boards inflate
possessions per team above the ~67-70 anchor).

### Gravity — *team-aggregate, derived-per-player contribution*
The defensive attention a player's scoring threat commands. **Derived per player** (a
player's gravity-contribution is a read on their scoring threat / shooting), then
aggregated into a team value. The team value drives **matchup quality**: high team gravity
(five credible threats) distributes attention so nobody can be doubled, producing more
clean one-on-one (or better) looks across the roster; low team gravity (one star, four
passengers) concentrates attention on the one threat, and *that* is where opposing scheme,
help defense, and off-ball defense bite and the star's percentages fall. Non-linear by
nature (it is about distribution of attention, not a flat mean).

### Spacing — *team-aggregate, two-stage*
How much the floor opens because of shooting threat on it. A **two-stage** value:
derived per player (a player's spacing-contribution is their outside shooting — a
non-shooter does not space the floor), *then* aggregated across the lineup into a team
number. Non-linear and about **count of credible threats**: three great shooters + two
non-shooters spaces very differently from one great shooter + four mediocre ones, even at
a similar average, because spacing is whether the defense can sag/help or must stay honest.
The tilt lands on *other* players' interior looks (the post scorer, the driver) — spacing
helps the players who are NOT the shooters.

> **Gravity and help defense are the same coin from opposite sides.** Gravity is the
> offense distributing attention; help defense is the defense's ability to collapse on a
> concentrated threat. They meet at the same shot pies and push against each other — one
> team's high gravity *resists* the other team's help defense — an emergent matchup
> interaction that falls out of two independently-authored team numbers, never pre-fused.

### Transition — *derived from physicals*
Open-floor scoring. **Derived** as the confluence of athleticism (itself already derived)
and finishing — a derived-from-a-derived, which is fine as long as the dependency order
stays explicit so it is never authored directly. The engine's existing `FastBreak` marker
is what makes transition a separable context.

---

## Defense

### Perimeter defense
Contesting and staying with ball-handlers and shooters on the perimeter (quickness +
discipline against the drive and the jumper).

### Post defense
Defending the interior scorer — a different physical/skill blend from perimeter defense
(strength + position vs. quickness), so kept separate.

### Rim protection
Contesting and altering shots at the basket. The clearest existing home of any defensive
attribute: Roll H's **block weight is already zone-aware** (Rim highest, Three lowest), and
rim protection is precisely what should tilt it.

### Defensive rebounding
Securing the opponent's misses. Feeds Roll I and Roll M's board split.

### Steals / turnover generation
Forcing live-ball turnovers. Feeds Roll C's live-strip arms, Roll J/K's live-turnover
paths, and Roll B's strip.

### Help defense — *team-aggregate*
The diffuse, team-level ability to rotate and collapse on a threat. **Authored per player,
summed across five.** It has no individual off-ball event to roll against in an
outcome-first engine, so it expresses as a small *environmental* tilt: five strong
help-defenders apply a ~1-2% decrease to the success of *certain kinds of shots* (the ones
help has a defensive story for — drives, rim attempts, second chances), NOT a flat global
decrease to every shot (which would just be a worse-team knob in disguise). Small enough to
be invisible in one game, real over a 30-40 game season. **Magnitude note:** subtle flavor
end of the team-aggregate bin.

### Off-ball defense — *team-aggregate*
Denials, closeouts, rotations away from the ball. Same shape and same problem as help
defense — expresses through events the outcome-first engine does not roll individually, so
it lands as a team-aggregate environmental tilt. Dormant until a team-defense / scheme
layer exists.

---

## Physical

### Authored physical attributes
- **Height** — the direct vertical-size rating.
- **Wingspan** — reach. **Absorbs "standing reach"** entirely; generators read wingspan
  directly rather than a separate derived reach value.
- **Weight** — mass, expressed *relative to height* (mass-for-frame). NOT read by the
  possession engine. A body-input that, at player generation, slides the Strength range up
  and the Quickness/Speed ranges down — a reliable population tendency with real spread and
  outliers (the heavy-but-quick "freak" is an emergent high roll, not a special case). Spec
  locked; build deferred to the player-generation module. See design.md, "Weight as a
  Body-Input to Strength and Quickness."
- **Strength** — force (rebounding through contact, finishing through contact, holding
  position).
- **Speed** — straight-line, north-south.
- **Quickness (lateral)** — side-to-side, east-west; the defensive movement trait.
- **First step** — initial acceleration / blow-by burst.
- **Vertical** — explosiveness off the floor (rim finishing, blocks, the glass).
- **Endurance** — *dormant-pending-module.* A stamina-based attribute, authored per player,
  but no generator reads it until the **stamina module** is built. Authored-now, parked.

### Athleticism — *derived*
**Not authored.** The composite of the explosive/movement physicals (strength + speed +
quickness + first step + vertical). This is the first derived attribute and the one the
locked **ceiling principle** refers to: *athleticism acts as a ceiling that limits how far
skill can express against a given competition level — not the other way around.* It is a
computed value, never a number you type, so the "athleticism as ceiling" rule stays intact
while the redundant authored umbrella comes off the list.

---

## Intangible

### Hustle — *modifier (effort family)*
How hard a player competes. **Does not produce an outcome on its own — it amplifies the
effort family**: rebounding, steals, defense, loose balls. A high-hustle elite rebounder
puts up numbers; high hustle on a poor rebounder is still a poor rebounder, just nudged.
Authored per player, multiplicative on the grindy outcomes rather than a standalone source.

### Basketball IQ — *modifier (decision family)*
The hardest attribute to keep honest — it must do something **concrete** the specific
ratings do not already do, or it is a redundant umbrella over them. Resolved as a
**modifier on the decision family**: playmaking, help defense, set execution. Its
load-bearing role is enabling a real **build path** — five smart, skilled players with
lackluster athleticism that is *competitive*, the low-level-basketball truth that a
cohesive, high-IQ team beats a more athletic but disorganized one. This ties to the ceiling
principle: a team that plays *within* its skill ceiling (smart shot selection, no
turnovers, good sets) does not need the athleticism that would gate skill it is not trying
to force. **Magnitude note:** strategy-enabling end of the modifier/aggregate spectrum —
IQ's effect must be more than a rounding error or the smart-team build does not actually
exist. The team-aggregate/modifier bins are explicitly **not uniform in leverage** (subtle
flavor like help defense vs. strategy-enabling like IQ).

### Discipline — *authored individual*
Foul avoidance and staying sound — NOT the same as shot selection. The "takes questionable
shots / inflated usage" half is a **tendency** and lives in the Tendencies doc; discipline
here is strictly the foul-avoidance attribute, kept from becoming a grab-bag.

---

## Two ceiling concepts (surfaced during cataloguing)

These are not attributes; they are properties of how attributes map to outcomes, recorded
here because they constrain the later wiring pass.

1. **Skill-expression ceiling (the locked principle).** Athleticism (the derived composite)
   caps how far skill can express against a given competition level. Skill is the primary
   driver of outcomes; athleticism is the ceiling on its expression, not the driver.

2. **Team-output ceiling (asymptotic mapping).** Stacked favorable inputs (high gravity +
   high spacing + a favorable individual matchup) compound on the same pie, so an elite,
   balanced roster produces an *emergent* high team FG% (e.g. an all-time roster in the low
   50s) — a *consequence* of the inputs, never a number anyone sets. But the rating→outcome
   mapping is **asymptotic, not linear**: every shot type has a realistic conversion ceiling
   (and floor) grounded in real basketball (even four elite shooters around a dominant big
   shoot high-40s from three, not 64%; a dominant finisher is not a 95% make). Stacked
   inputs move a player *toward* that ceiling along a flattening curve and never breach it.
   This is **not** a violation of the no-artificial-limits principle: that principle forbids
   *manufacturing* results (capping a good team's wins, nerfing the star in the clutch to
   force drama), not *respecting reality's ceilings*. The bound lives in the **shape of the
   mapping function** (a logistic asymptote — the "bounded logistic make-%" Roll H's design
   already references), so nothing in the engine ever reads `max = X`; the curve's asymptote
   *is* the ceiling. A corollary: because the team-aggregates compound on the same pies, the
   wiring pass must bound their *combined* lift (a great offense tops out at *great*, not
   *impossible*) — their magnitudes cannot be set in isolation.

---

## Deferred — the passes this catalogue feeds

This document is the raw material. Two named design passes consume it, in order, and are
explicitly **out of scope here** so the raw inputs stay clean of the derived read and the
wiring (the premature-fusion failure mode the project guards against). They are recorded so
the dependency order is unambiguous, not specified.

### Pass 2 — The four-axis model (the derived read on the catalogue)
Player quality is a composition across **four axes — athletic / skilled / big /
experience-cohesion** — and a player's tier *emerges* from how many axes he clears (none =
not a college player; one ≈ D3; two ≈ low D1; three ≈ solid D1; all four ≈ power-conference).
This is a **derived read** on the catalogue: each attribute ladders into an axis, and a
**matchup** compares two lineups' axis profiles — *relatively* — into the *contextual terrain*
a possession resolves on.

The full Pass 2 design — the four axes, the relativity principle, the abundant-skill /
scarce-disruption asymmetry, the multi-front-war (no-fixed-winner) model, the no-linear-sum
hard constraint, emergent game character, the per-axis pie fingerprints, the hidden-not-public
decision, and the special experience/cohesion axis with its cross-game persistence dependency
— lives in its own standing document: **`docs/axes.md`**. (Note: basketball IQ and roster fit
are deliberately NOT axes — IQ stays a player attribute in this catalogue; fit is the
team-aggregate layer.)

### Pass 3 — Wiring (how attributes mold pie percentages)
The generators consume **three independent inputs** to shape a pie, composing without ever
being pre-blended (the locked "strategy and matchup modifiers stay independent inputs"
principle, extended from two inputs to three): **matchup** (the four-axis terrain — derived
per matchup, relative), **coaching strategy** (what the team does with that terrain), and
**personnel / attributes / tendencies** (how well it is executed within it). Macro terrain
first, micro execution within it. This pass also owns the mechanics that keep the war a war
and not a min-max — the diminishing-returns / coverage / threshold formulas and the asymptotic
make-rate mapping that delivers the team-output ceiling. It is the hardest pass and the last,
gated on Passes 1 and 2. Its open questions are catalogued in `docs/axes.md`.
