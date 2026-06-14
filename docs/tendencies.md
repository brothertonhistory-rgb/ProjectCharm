# Project Charm — Player Tendency Catalogue

The third document of the player model, alongside the attribute catalogue (`attributes.md`)
and the four-axis matchup model (`axes.md`). Where attributes record *how good* a player is
and the axes record *how teams stack relatively*, tendencies record **how a player chooses to
get his offense** — which option he leans toward at a specific decision point.

**Status: skeleton / foundation, not a spec.** The list, the structure, and the interaction
principles are settled here. The MATH — how each tendency tilts a specific pie, and how the
player's lean negotiates against the coach's plan — is deferred, and several tendencies do
not fully express until a future engine node (the shot-creation classifier roll, below)
exists. This doc is honest about what is settled and what is open.

---

## Attribute vs. tendency (the category line)

- An **attribute** is *how good* a player is at a thing (finishing = 82). It sets the
  **quality** of an action.
- A **tendency** is *how often* a player chooses an action / *how he gets his offense*. It
  sets the **distribution** of which actions he takes — which pie fires, and how often — not
  the make percentage within a pie.

The two are **orthogonal and both real**, and they multiply: a high drive tendency *and* high
finishing → lots of rim attempts that go in; high drive tendency with poor finishing → lots of
rim attempts that miss. Mixing "how often" with "how good" in one catalogue is a category
error, which is why tendencies live in their own document.

**The tightest filter for what counts as a tendency:** a tendency only exists if there is a
**pie fork for it to redistribute** — a choice between options the engine actually rolls. An
attribute can be "real but dormant" (endurance, waiting on a stamina module); a tendency must
map to a branch of a roll the engine resolves. If a lean does not map to a fork the engine
fires, it is not expressible.

---

## Tendencies are PAIRED LEANS

Every player tendency is a **lean between two options at a specific decision point**, not an
independent toggle. This is a cleaner structure than the attribute list: each tendency is
literally "when this situation arises, which way does this player lean," which maps directly
onto a fork the engine can roll (mostly a *shot-location* / *shot-creation* distribution,
conditioned on context).

The four player tendencies:

| Tendency | The lean | The decision point |
|---|---|---|
| **Catch-and-shoot vs. off-the-dribble** | how a perimeter player gets his jumper | shot creation — spot up off a pass vs. create off the bounce |
| **Pop vs. roll** | what a screener does after the pick | pick-and-roll — pop for a jumper (Mid/Outside) vs. roll to the rim (Rim/Close) |
| **Post vs. face** | how an interior/iso player operates | back-to-basket post-up vs. face-up from the perimeter (outside-in) |
| **Cut vs. flare** | what an off-ball player does on a pass | cut to the rim vs. flare to the three-point line |

All four are **offensive**, all are paired leans, and all map to a shot-distribution fork.

> **Open — Post vs. face read in two contexts.** Post vs. face was raised for both the
> screener/pick-and-roll situation and the isolation situation. It may be *one* tendency read
> in two contexts (off a pick, and in iso) or *two* separate leans. Left open.

**Defensive behavior is deliberately NOT here.** Gamble vs. stay home, help vs. stick, crash
vs. retreat, switch vs. fight-through — these are **scheme decisions a coach dictates to the
whole unit**, so they live in the (future) **coaching-strategy** layer, not this catalogue.
The individual half of defensive inclination — a natural ball-hawk, a rim-protector's instinct
to help — is already captured by **defensive attributes** (steals/turnover-generation, rim
protection) in `attributes.md`. So there is no "defensive tendency" middle category: the
individual half is an attribute, the team half is coaching strategy.

---

## Tendencies are player-owned; coaching strategy NEGOTIATES against them

The give-and-take that defines how tendencies interact with the coach's plan:

**Tendencies are player-owned.** They are how *that player* is wired. The coach sets a target
(shoot 35 threes, attack the rim), but the players' own tendencies **push back**, and the
realized distribution is the **negotiation** between scheme and personnel. The coach has a
thumb on the scale, not a remote control — neither fully overrides the other.

Two illustrative bounds:
- A coach who wants to attack the rim and avoid threes, but recruits five jump-shooters,
  should **not** have a losing rim-attack identity *forced* onto a team poorly assembled for
  it — the players' tendencies protect them from the worst of a mismatched plan.
- A perimeter coach who wants 35 threes a game, paired with a dominant post player, still sees
  that player get his touches *because of who he is* — the other four will not refuse to feed
  him just because a setting says "shoot threes." The scheme cannot conjure an identity the
  personnel will not support.

So the negotiation is honest in **both** directions: scheme cannot override good players into
uselessness, and scheme cannot rescue a roster that lacks the personnel to run it. Both
directions are bounded by the players.

---

## Tendencies INTERACT — balance vs. interference

The load-bearing interaction, and the **tendency-layer version of the axes' no-linear-sum
law**: complementary tendencies *accentuate* each other's pies, and redundant tendencies
*interfere*. Stacking the same tendency has diminishing or even negative returns.

- **Four catch-and-shoot players drag each other down.** All want to *receive*; nobody
  *creates*; there is no one to get them the ball and no gravity to take advantage of the
  spacing they provide. Their pie odds fall.
- **Four off-the-dribble players hurt each other too.** They have gravity but no spacing —
  the rim is congested, and they clog each other's driving lanes, lowering everyone's rim-pie
  odds.
- So teams want to **balance** the two, even with very different players — and the same logic
  is bidirectional across the other pairs (all poppers and no rollers, all cutters and no
  flares, etc. each starve the spacing/gravity the other would provide).

This is the "three Westbrooks make each other worse" principle generalized from identical
*players* to identical *tendencies*: five of the same lean is worse than five that fit, even at
equal individual ratings.

> **The coupling that makes this work:** tendencies feed the **team-aggregates** (gravity,
> spacing) from `attributes.md`, which then tilt everyone's pies. Tendency balance is what
> makes those aggregates *pay off*; redundant tendencies *starve* them. The tendency layer and
> the aggregate layer are **coupled**, not separate. Good roster construction is **tendencies
> that interlock** — a creator's passing feeds the catch-and-shooter's spot-up lean; the
> shooter's gravity opens the creator's driving lanes — and it shows up as elevated pies for
> *both* players.

---

## Misfit is EMERGENT and NARRATIVE

Because tendencies negotiate against scheme and interact with each other, **misusing or
mis-pairing players is a real failure the engine surfaces through outcomes**, not a popup:

- An elite catch-and-shoot wing paired with a point guard who cannot create and generates no
  assists is **wasted** — the wing's tendency wants spot-up looks, nobody generates them, so he
  is forced into off-the-dribble shots his lean (and likely his self-creation rating) are not
  built for. His numbers suffer, and the cause is a tendency with no complementary teammate to
  feed it.
- This becomes an underlying **narrative the game engine tells**: the coach who misuses his
  players, the wasted shooter, the redundant ball-hogs. Same hidden-but-legible principle as
  the axes — the misfit shows up in the box score and the wing's bad numbers, and a thoughtful
  user reads it and fixes their rotation or their roster.

---

## OUT OF SCOPE — the shot-creation classifier roll (engine architecture, not a tendency)

Several of these tendencies — cut vs. flare especially — only **fully express** if the engine
*resolves and records how a shot was created*, not just where it came from and whether it went
in. The desired behavior: an athletic wing with a high cut tendency should have some of his
made baskets actually *be* sharp cuts off a good passer.

That requires a **new engine node** — a shot-creation classifier roll that, after a shot, files
*how it was created*: catch-and-shoot, drive-and-dump, iso, created-own-shot, cut-off-a-pass,
etc. — the same way Roll G classifies *location* and Roll H classifies *make/miss*. **This is
engine architecture, not a tendency**, so it belongs in the engine roadmap (`design.md`), not
this catalogue. But it is the **consumer** that gives several of these tendencies their full
pie home: tendencies are the *inputs* that tilt this classifier's pie, and the classifier is
where "this player's cut tendency" becomes "this bucket was a cut."

Consequence: cut/flare (and the creation half of catch-and-shoot vs. off-the-dribble) are
**real but partially awaiting a roll that does not exist yet** — the tendency analogue of
`attributes.md`'s dormant-pending-module tag. Pop/roll and post/face express more directly as
shot-*location* leans (which Roll G already owns), so they are less dependent on the new node.

---

## Forward hooks (franchise-layer, parked)

- **Training module** — tendencies are player-owned but not permanently fixed. A future
  training system lets a team try to **shift** a player's tendencies over time (turn a
  shot-hunter into a more willing passer; raise a catch-and-shoot player's pull-up lean).
  Player-owned-but-slowly-malleable. Franchise-layer.
- **Recruiting** — coaches should **recruit to their scheme**. A perimeter coach should be
  drawn to shooters; a post-oriented coach to interior players. The negotiation principle is
  exactly *why* recruiting matters: you assemble personnel whose tendencies *fit* the identity
  you want to run (or mismatch, with the emergent consequences above). Franchise-layer.

---

## What is settled vs. open

**Settled (this doc):**
- Tendency = *how often / how he gets his offense* (distribution of actions), distinct from
  attribute = *how good* (quality of an action). Orthogonal; they multiply.
- A tendency must map to a pie fork the engine rolls; otherwise it does not exist.
- The four player tendencies, all paired offensive leans: catch-and-shoot/off-the-dribble,
  pop/roll, post/face, cut/flare.
- Defensive behavior is coaching strategy (team scheme) or already an attribute (individual
  inclination) — there is no defensive-tendency category.
- Tendencies are player-owned; coaching strategy negotiates against them (thumb on the scale,
  not a remote control); neither fully overrides the other.
- Tendencies interact: complementary leans accentuate each other's pies, redundant leans
  interfere (no-linear-sum, tendency-side); coupled to the gravity/spacing aggregates.
- Misfit is emergent and narrative, surfaced through outcomes.

**Open (future conversations / passes, NOT decided here):**
- Whether post/face is one tendency read in two contexts (PnR + iso) or two separate leans.
- How each tendency tilts a specific pie, and the negotiation math between player lean and
  coach target.
- The interference math — how redundant tendencies suppress the aggregates / each other's pies.
- The **shot-creation classifier roll** (engine architecture, `design.md`) that gives cut/flare
  and the creation tendencies their full pie home.
- The training (tendency-shift) and recruiting (coach-to-player match) systems — franchise-layer.
