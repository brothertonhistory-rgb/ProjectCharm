# Project Charm — Design Document

Why things are built the way they are. This document records rationale, not
task lists (those live in the journal). It is updated every session.

---

## The on-court slot layer

The slot layer is the set of ten on-court identities — five per team — that
selection and attribution both need to exist before they can be built. Selection
chooses *which* on-court player gets the possession; attribution credits a stat
*to* one. Neither can point at anything until there is a set of nameable on-court
identities. So the slot layer is their shared dependency, built once as its own
unit rather than smuggled in as a side effect of the selection roll.

### A slot is identity, not substance
A `Slot` is `(TeamSide Side, int Number)` and nothing more — a stable on-court
position that can be *named*, carrying no attributes, no fill, no rating, not even
an inert modifier hook. It mirrors `TeamSide`: the cleanest identity in the
codebase, owned by nothing and referenced by everything. The rated player that
fills a slot is data that flows in later and attaches *to* the slot; the slot
does not pre-carry anything for it. This is deliberate discipline — the moment a
slot holds a rate-touching field, it has become a player model, which is the
premature-crystallization failure mode the project has hit before. The slot stays
empty so it stays safe.

### Numbered 1–5, but the number is identity, not role
Slots are numbered 1–5 to mimic basketball's addressing, but the number carries
no positional meaning: slot 1 is not structurally "the point guard." *What kind
of player belongs in a slot* is a lineup-assignment decision made later, in a
layer above this one. Keeping role out of the slot is what lets management nodes
(lineup-setting, substitution, rotation, matchup assignment) stack on top as
clean consumers — none of them has to fight a meaning baked into the slot, and a
positionless or small-ball lineup is no special case. The fixed number gives the
stability subs/rotations need (slot 3 is a stable address all game); role lives
above it as assignment.

### The number is intrinsic and stable
"Home slot 3" is the same position for the whole game. A substitution swaps *who
fills* slot 3, never what slot 3 *is*, so a stat attributed to a slot stays
coherent across subs. This is the same move Roll D makes when it charges a fouls
to the fixed `TeamSide` identity rather than to a moving ball-handler:
attribution rides on a stable identity by design. Making the slot travel with the
player instead (slot ≈ proto-player) would make attribution chase a moving
target, and would invite attributes onto the slot — both rejected.

### Scope: one Lineup per team on GameState
There appeared to be three scopes — the roster, the on-court five, and which slot
has the ball this possession — but they collapse to the right two. The roster and
the on-court five are one owned object: a `Lineup` per team, living on
`GameState`. It is persistent game-scoped state that *mutates* within a game via
substitutions — the same shape as the foul count (persistent and changing), not
team identity (fixed). Crucially it is **per-team**, not a shared both-sides
bundle like `FoulTracker`: team fouls are a thin shared concern with nothing
per-team to grow, whereas a lineup is the attachment point every heavy
per-team/per-player system hangs off later (stat lines, the rated players, the
selection roll), so each team owns its own and grows independently. This mirrors
how `PossessionState` carries `Offense`/`Defense` as two parallel-but-independent
identities. The third scope — which slot has the ball *this* possession — is
per-possession and deferred to `PossessionState`, added as a slot *reference*
(into the game-scoped lineup) when the selection roll is built; `PossessionState`
references a slot the way it already references a `TeamSide`, never owning it.

### The seam selection and attributes will consume
`GameState.LineupFor(TeamSide)` is the road the future attribute generator walks:
possession role → `LineupFor` → `SlotAt(n)` → (later) the player filling that slot
→ that player's attributes → a pie with shifted weights. The roll, the resolver,
and the slot never change; only the generator gets smarter — the same
stub-to-real swap the pie generators already promise. Adding attributes later is
*architecturally* non-disruptive for this reason, even though the attribute model
itself is a large design effort. The slot being empty now is precisely what makes
that later add a clean plug-in rather than a teardown.

---

## The uniform roll contract

Every roll follows the same shape:

1. **receives** the current `PossessionState` plus a finished `Pie` (weighted odds
   that already sum to 1);
2. **consumes** the pie — it rolls against it and never computes its own odds;
3. **returns one typed result** — a `Terminal` (possession ends) or a `Continue`
   (state carries forward);
4. **names no successor.** The `Resolver` routes a `Continue`.

The point of the contract is tunability and legibility: odds are produced in one
place (a generator) and consumed in another (a roll), with a validated seam
between them, so signal flows one direction and nothing feeds back into itself.

### Pie — the odds currency
`Pie<TOutcome>` is generic over a roll's outcome enum. It **validates on
construction**: every outcome must have a non-negative, finite weight, and the
weights must sum to 1 within `Epsilon`. Because an invalid pie cannot be
constructed, "validate on receipt" is enforced by the type — a roll that holds a
pie holds a valid one, and a future buggy generator fails loud at the moment of
hand-off instead of silently rolling garbage. Slices are walked in enum
declaration order so a given RNG draw always maps to the same outcome.

### Result — Terminal vs. Continue
`RollResult` is a sealed pair. `Terminal(Reason, State)` ends the possession;
`Continue(Next, State)` carries forward. `ElapsedSeconds` lives on the base
result and is:
- **null** = time not yet apportioned; a future *time roll* owns it (the normal case);
- **non-null** = the elapsed time is invariant and already known here.

Only the shot-clock violation sets it (to the full clock). This keeps timekeeping
out of the rolls while letting the one invariant-time outcome stamp itself.

### "Names no successor", precisely
A `Continue` carries a `ContinuationKind` — a *category of its own result*, not
a reference to a node. The `Resolver` is the sole place that maps a kind to an
actual successor. When a real node replaces a stub, only that mapping changes;
the roll that emitted the continuation is never reopened. This is the seam that
lets any roll be built before its successors exist.

### RNG
Rolls draw from an injected `IRng`, seedable, so batch runs and observability are
reproducible.

---

## The Resolver — conductor with a loop

The `Resolver` is the conductor. It owns all routing and walks the full
possession chain: route a ticket → run that station → take its new ticket →
route again → until a terminal ends the possession.

This loop is the only place the chain is defined. Adding a new roll is two steps:
write the station, add one line to the resolver's routing table. Nothing else
changes. Rolls never call each other and never know what comes next.

---

## GameState — persistent infrastructure

`GameState` holds state that survives ACROSS possessions, unlike
`PossessionState` (per-possession). It exists because some outcomes set
conditions consumed later — most immediately, the **possession arrow**, which a
jump ball in one possession uses to decide who gets the ball in another.

The **possession arrow is now real and complete** (Session 5). It is
three-valued (`ArrowState`: `Off` / `Home` / `Away`), not two-valued, because the
opening tip is a genuine contest with no prior arrow to read: the arrow is `Off`
until the first jump ball turns it on. `SetPossessionArrow` turns it on (the tip
points it at the loser), `FlipPossessionArrow` reverses it (and throws if Off —
you cannot flip what a tip has not yet set), `ResetPossessionArrow` returns it to
Off for overtime. **Team fouls are now real too** (Session 6), via a
`FoulTracker` (see Roll D) — incremented on each foul and read for bonus routing.
Score and timeouts remain placeholder fields — typed and named, not yet read or
written during possession resolution.

**Team identity vs. role (Session 6).** `TeamSide` (`Home` / `Away`) is a team's
*identity* — fixed for the whole game. Offense/defense is a per-possession *role*
layered over identity. These are different facts, and earlier `PossessionState`
conflated them by storing `Offense`/`Defense` as strings. They are now
`TeamSide`, so the foul lands on the correct half-counter with no string mapping —
the wrong-counter failure mode (wrong counter → wrong bonus → wrong game) is now
unrepresentable. Every game, neutral court included, stamps both teams Home/Away
up front; on a neutral floor the label is arbitrary but stable, which is all the
engine needs. Team fouls accumulate against *identity*, correctly independent of
who holds the ball moment to moment. (Actual neutral-court label assignment is
game-setup, deferred.)

**Why the arrow stayed simple.** Real NCAA alternating-possession rules carry
edge cases — a throw-in violation flips the arrow anyway, a foul during the
throw-in does not, the arrow flips at halftime for the court switch. None of
these are modeled. The arrow only *awards and flips*; a jump-ball award sends a
team into a normal inbound, and any violation or foul that follows flows through
the turnover and foul rolls that already exist. The halftime flip is omitted
specifically because Charm models offense/defense as roles with no spatial court
ends — there is no side-switch to cancel, so flipping would corrupt who is owed.
This is the same modeling decision that removes backcourt/frontcourt and
sideline/baseline distinctions elsewhere.

---

## Jump ball — the shared arrow node

**Simulates:** any held-ball situation. Every roll's `JumpBall` exit routes
here. There is no pie — a jump ball is not a weighted-outcome roll, it is a state
operation on the possession arrow.

**Two behaviors, by arrow state:**

| Arrow state | Behavior |
|---|---|
| `Off` (opening / OT tip) | A real contest. 50/50 coin flip; winner gets the ball; arrow turned ON pointing at the LOSER (they are owed the next award). |
| `Home` / `Away` | Routine alternating possession. The pointed-at team is awarded the ball; the arrow flips away from them. Deterministic. |

**Why it ends the possession.** A held ball ends the current possession by
definition. The awarded team's ensuing possession is a *new* possession (future
work), so the resolver's `ResolveJumpBall` case resolves the arrow and emits a
`Terminal` — it does not chain into an inbound here. It integrates like the
turnover route: resolve, then terminate.

**Why no config.** The coin flip is 50/50 *by rule* — there is no basketball
knob to tune at this stage, so nothing lives in config. The one place real
basketball will eventually enter is the tip contest.

**FUTURE SEAM — height-driven tip contest.** The 50/50 flip is the placeholder
for the single true contest in this node. The intended model: tip-win
probability driven by the centers' height differential, non-linear — a 1" edge
is a near-negligible bump, a large gap (~8") approaches near-certainty (an
S-curve on height-diff, not linear). It plugs in exactly at the tip branch once
a player/attribute layer exists; the node still returns "which team won" and the
arrow still consumes it, so nothing else changes. Deferred because the tip is one
event per game (the lowest-leverage attribution in the engine) and it needs
player objects that do not exist yet — same seam discipline as the stub pie
generators.

---

## Roll A — Entry: Inbounds (Dead Ball)

**Simulates:** the first touch of a possession that begins with a dead-ball
inbound pass.

**Pie shape:** five slices over `EntryOutcome` — `CleanEntry`, `Turnover`,
`ShotClockViolation`, `Foul`, `JumpBall`. (Stub weights today; see below.)

**Five exits:**

| Outcome | Result | Routes to |
|---|---|---|
| Clean entry | `Continue(IntoHalfcourtSet)` | Roll B |
| Turnover | `Continue(ResolveTurnoverType)` | turnover-type resolver *(stub)* |
| Shot-clock violation | `Terminal("ShotClockViolation")`, elapsed = full clock | possession ends, ball switches |
| Foul | `Continue(ResolveFoulType)` | foul-type resolver *(stub)* |
| Jump ball | `Continue(ResolveJumpBall)` | jump-ball resolver *(stub)* |

**Why the violation is the only terminal.** A shot-clock violation is invariant:
no shot, the full clock off, never any more or less. There is no path variance to
simulate on the way to it, so collapsing it into a single terminal at entry is
strictly cheaper and loses nothing — and it is why its elapsed time can be
stamped on the result with no time roll.

**Why foul and jump ball are continues, not terminals.** Both have real variance
in what they become. A foul still needs its type decided (defensive non-shooting
vs. offensive) and what that triggers. A jump ball needs the possession arrow
consulted. Roll A only classifies that the outcome occurred and hands off; the
downstream resolver does the deciding.

**The pie generator is stubbed.** `StubPieGenerator` returns the configured base
weights with one live wire: a single 0–1 `pressure` scalar nudges the turnover
slice (then renormalizes). Placeholder to prove the seam carries signal — not
basketball logic.

---

## Roll B — Halfcourt Initiation

**Simulates:** the first beat after the offense is cleanly into its halfcourt
set. A pure gate: decides whether the possession advances to player selection or
is interrupted by a foul or dead-ball turnover before any action occurs.

**Pie shape:** three slices over `HalfcourtOutcome` — `Proceed`, `Foul`,
`DeadBallTurnover`. (Stub weights today; see below.)

**Three exits:**

| Outcome | Result | Routes to |
|---|---|---|
| Proceed | `Continue(IntoPlayerSelection)` | player-selection roll *(stub)* |
| Foul | `Continue(ResolveFoulType)` | foul-type resolver *(stub)* |
| Dead-ball turnover | `Continue(ResolveTurnoverType)` | turnover-type resolver *(stub)* |

**No terminal.** Roll B has no terminal outcome. Every result is a continue
because every outcome here has downstream variance to resolve. The possession
cannot end at this beat.

**Shared foul resolver.** Roll B's foul exit reuses `ResolveFoulType` — the same
kind Roll A already emits. Many rolls feed one resolver; the resolver is never
duplicated. This is the "many feeders, one node" principle.

**The pie generator is stubbed.** `RollBStubPieGenerator` returns the configured
base weights with one live wire: a single 0–1 `physicality` scalar nudges the
foul slice (then renormalizes). Placeholder to prove the seam carries signal —
not basketball logic.

**Config lives separately.** Roll B's numbers live in the `"RollB"` section of
`config.json` and are loaded by `RollBConfig`. Roll A's flat keys are untouched.
A future cleanup may unify these into a single sectioned config, but that
requires reopening Roll A's signature and is logged as debt rather than done now.

---

## Roll C — Turnover Classification

**Simulates:** the shared turnover node. Every roll that produces a turnover
routes here. Roll C decides *what kind* of turnover it was and ends the
possession. It never knows or cares who fed it — "many feeders, one node."

**Pie shape:** five slices over `TurnoverOutcome` — `BadPassDeadBall`,
`BadPassIntercepted`, `LostBallDeadBall`, `LostBallLiveBall`, `OffensiveFoul`.
(Stub weights today; see below.)

**Five exits — all terminals:**

| Outcome | Result | Ball state |
|---|---|---|
| Bad pass, out of bounds | `Terminal("BadPassDeadBall")` | dead → next possession inbounds |
| Bad pass, intercepted | `Terminal("BadPassIntercepted")` | live → defense in transition |
| Lost ball, out of bounds | `Terminal("LostBallDeadBall")` | dead → next possession inbounds |
| Lost ball, stripped live | `Terminal("LostBallLiveBall")` | live → defense in transition |
| Offensive foul | `Terminal("OffensiveFoul")` | dead → next possession inbounds |

**Why every outcome is a terminal.** A turnover ends the possession by
definition — the ball changes hands the moment it happens. There is no remaining
in-possession variance to resolve, so unlike a foul or jump ball (which classify
and then *continue* to a resolver), Roll C classifies and *terminates*. This
makes Roll C the engine's first terminal-producing roll.

**Why the dead-ball vs. live-ball axis.** The slices are cut along ball state,
not cause, because ball state is what drives the *next* possession. A dead-ball
turnover resumes on an inbound (a future entry roll picks sideline vs. baseline
and adjusts press odds); a live-ball turnover hands the defense the ball in
transition. Carrying this distinction on the result lets a downstream entry roll
and the attribution layer consume it. This is one-directional between-possession
context — Roll C writes the classification, future consumers read it; nothing
feeds back.

**Elapsed time defers.** Unlike the shot-clock violation, a turnover has real
path variance in how long it took, so `ElapsedSeconds` is left null and a future
time roll apportions it. Roll C stamps no time.

**Player attribution is not here.** *Which* offensive player committed the
turnover, and which defender gets the steal on the live-ball slices, is a
counting-stat concern handled by a separate attribution layer that runs over
outcomes — it reads who was involved (the offensive ball-handler is already
selected upstream; the crediting defender is named by matchup) and assigns
credit. It does not gate the possession chain or feed back into resolution, so it
lives entirely outside Roll C, as future stat infrastructure. Roll C answers
"what kind," never "whose." This keeps Roll C a pure terminal and keeps
attribution consistent across offense and defense.

**Integration: like Roll B, not like a stub.** Because Roll C produces a result
rather than a routing string, the resolver *executes* it inside the
`ResolveTurnoverType` case and feeds the returned `Terminal` back through its
loop — exactly the pattern already used for Roll B via `IntoHalfcourtSet`. The
retired `TurnoverTypeResolverStub` is dropped from the resolver's constructor.
The shared-node contract is unchanged: every feeder still emits the same
`ResolveTurnoverType` continuation; only its destination moved from stub to a
real roll. This is the "many feeders, one node" principle paying off — no feeder
reopened.

**The pie generator is stubbed.** `RollCStubPieGenerator` returns the configured
base weights with one live wire: a single 0–1 `pressure` scalar nudges the
`LostBallLiveBall` slice (defensive ball pressure → more live strips), then
renormalizes. Placeholder to prove the seam carries signal — not basketball
logic.

**Config lives separately.** Roll C's numbers live in the `"RollC"` section of
`config.json`, loaded by `RollCConfig`, alongside Roll A's flat keys and Roll B's
`"RollB"` section. The future config unification noted under Roll B would fold
this in too.

---

## Roll D — Non-Shooting Defensive Foul

**Simulates:** the shared foul-type node. Every roll that produces a generic
`Foul` (Roll A's entry foul, Roll B's halfcourt foul) routes here via
`ResolveFoulType`. Many feeders, one node — Roll D never knows who fed it.

**Why it only ever sees non-shooting defensive fouls.** By position in the chain
every foul reaching Roll D is *pre-shot*: no player is selected, no shot is up.
So shooting fouls cannot occur yet (a future post-player-selection roll owns
them), and offensive fouls are already Roll C's (as turnovers). Roll D therefore
never classifies offensive-vs-defensive or shooting-vs-non-shooting — by
construction every foul it sees is a non-shooting defensive foul. There are no
such branches, by settled design.

**What it does — three steps.**

1. **Rolls a flavor** against its pie: `ReachIn` / `Blocking` / `OffBall`. This
   is *descriptive theater only* — logged like turnover-type, it does NOT route.
2. **Increments the fouling team's foul count** for the half. The fouling team is
   the defense this possession (`state.Defense`).
3. **Reads the bonus and routes on it** — a state check, not a roll. Not in
   bonus → `Continue(ResumeInbound)` (the offense keeps the ball, inbounds). In
   bonus → `Continue(ResolveFreeThrows)`.

| Outcome (route) | Result | Meaning |
|---|---|---|
| Not in bonus | `Continue(ResumeInbound)` | offense keeps the ball *(stub)* |
| In bonus | `Continue(ResolveFreeThrows)` | free throws, carrying `BonusType` *(stub)* |

**Flavor is theater; the route is a deterministic state read.** The flavor draw
changes nothing functional — the same draw routes identically. Routing is
entirely the bonus read against the foul count. Because flavor never routes, its
stub generator has *no live signal wire* (unlike B's physicality and C's
pressure): there is nothing functional for a signal to move, and adding one would
falsely imply flavor mattered.

**Continues, not terminals.** Unlike Roll C, a foul does not end the possession —
the offense either retains the ball or goes to the line. So Roll D's exits are
both `Continue`. It integrates like Roll C otherwise: the resolver executes it
inside the `ResolveFoulType` case and feeds the returned `Continue` back through
the loop, which re-routes it to the matching stub. The retired
`FoulTypeResolverStub` is dropped from the constructor, and both Roll A and Roll B
foul feeders light up at once.

**The bonus type is the entire free-throw contract.** Roll D tags its result with
a `BonusType` — `None` / `OneAndOne` / `Double` — that rides on the `Continue` as
*functional payload* (distinct from flavor's theater). This single value is
everything the future free-throw node needs to derive its behavior:

- **OneAndOne** (the 7th–9th team foul): the FT node shoots a front end; a *miss*
  is a live ball → rebound roll. Reboundable.
- **Double** (the 10th team foul onward): two guaranteed attempts; a missed
  *first* is **not** reboundable (dead ball → immediate second attempt), only a
  missed final attempt is live.

Crucially, Roll D and the foul tracker encode *none* of that reboundability
logic — it lives in the FT node, derived from the bonus type. Letting it leak
upstream would couple the foul layer to a node it must not know about.

**Team fouls, not player fouls.** Roll D charges the *team* (which is all the
bonus needs). *Which* defender committed the foul is a counting-stat concern for
the future attribution layer — same as turnover/steal credit. Deferred.

**The FoulTracker.** Team-foul accumulation lives in a dedicated `FoulTracker` on
`GameState`, not as loose ints. It owns both teams' counts, the two thresholds,
and the bonus read — the half-scoped concern as one unit, so the future half-reset
clears one object. It validates `0 < bonus < double` on construction and bands the
post-increment count: `< bonus` = None; `[bonus, double)` = OneAndOne; `>= double`
= Double. It is deliberately ignorant of free throws; it reports bonus state and
stops.

**Roll D takes `GameState`; A/B/C did not.** It is the first roll to mutate
persistent cross-possession state (the team foul). That is inherent to what a
foul *is*, not a contract break — the uniform shape (receive state + pie, roll,
name no successor) holds; the extra argument is simply the state it must touch.

**Config lives separately.** Roll D's numbers — flavor weights and both bonus
thresholds (NCAA classic 7 / 10, tunable) — live in the `"RollD"` section of
`config.json`, loaded by `RollDConfig`.

---

## Roll E — Player Selection

**Simulates:** which of the five on-court offensive players the possession runs
through this time. Roll B's `Proceed` exit lands here. It is the first roll whose
output is *an identity to attribute to* rather than *a branch in the chain* — it
names a player, it does not classify an event.

**Pie shape:** five slices over `SelectionOutcome` — `Slot1`–`Slot5`. Each member
is a slot NUMBER (its declaration position + 1), not a role. **Flat this session:
20% each.**

**What it does — four steps.**
1. Rolls the flat pie to a `SelectionOutcome`.
2. Maps it to a slot number 1–5 and names the real slot on the *offense's* lineup
   via `game.LineupFor(state.Offense).SlotAt(n)`.
3. Stamps the chosen slot onto the possession: `state with { SelectedSlot = slot }`.
4. Returns `Continue(IntoPlayerAction)` carrying that updated state.

**Why the slot lands on `PossessionState`, not on the `Continue`.** The selected
slot is a *durable per-possession fact* that several future rolls (shot creation,
shot quality, make-miss, rebound, shooting foul) and the attribution layer all
read, across multiple chain hops. That is exactly `PossessionState`'s job — the
slot sits beside `Offense`/`Defense` as another per-possession identity reference
(a reference into the game-scoped lineup, never an owned or attribute-bearing
thing). This is the deliberate contrast with Roll D's `BonusType`, which rides on
the `Continue` *because* it is transient input consumed by the very next node and
never persists. Same reasoning about lifetime, opposite conclusion: persistent
fact → state; transient routing input → continuation. Putting the slot on the
continuation would force every downstream roll to fish it out of a transient
envelope instead of reading state.

**`PossessionState` is immutable, so selection is a `with`, not a mutation.**
Stamping the slot produces a *new* record, which is exactly how the resolver
already threads `c.State` forward through its loop. No mutation, no friction.

**Why an enum pie, not an int index.** `Pie<TOutcome>` is constrained to enums, so
a `SelectionOutcome` enum keeps the contract identical to B/C/D with zero
special-casing. The enum↔number map is the trivial `(int)outcome + 1`.

**The flat pie is the whole job; tilting it is not.** What makes one slot more
likely than another — usage, hierarchy, ball-dominance, the filling players'
attributes, coach tendencies — is the deferred player/attribute model. That
arrives later as a smarter *generator* that hands Roll E a non-flat pie over the
same enum; the roll, the resolver, and the slot never change. This is the same
stub-to-real generator swap every other roll already promises, and it is the
specific discipline this roll demanded: selection sits one inch from the player
model, and tilting the pie now would be the premature-crystallization failure mode
the project has twice hit. The flat pie is the honest statement that there is no
signal yet.

**No live-wire signal (unlike B/C).** Roll B's physicality and Roll C's pressure
each nudge a slice to prove the seam carries signal. Roll E has none, because its
first real signal (usage) is part of the deferred attribute model — there is
nothing functional for a wire to move yet, so, like Roll D's flavor generator,
the generator takes no signal argument. Adding one would falsely imply selection
already had a signal.

**Roll E takes `GameState`; so did Roll D, for a different reason.** Roll D takes
it to *mutate* the foul count; Roll E takes it to *read* the offense's lineup and
name a real slot. Either way the uniform shape holds — receive state + pie, roll,
name no successor — and the extra argument is just the state the roll must touch.

**Integration: execute-and-loop, like C and D.** The resolver executes Roll E
inside the `IntoPlayerSelection` case and feeds the returned `Continue` back
through its loop, which re-routes it by its `IntoPlayerAction` kind to the
player-action stub. The retired `PlayerSelectionStub` is dropped; `Proceed` still
emits the same `IntoPlayerSelection` kind — only its destination moved from a stub
to a real roll. The new dead-end is `PlayerActionStub`.

**Why the next kind is `IntoPlayerAction`.** What follows selection is whatever
happens *to* the selected player — a shot attempt, a turnover, a drawn foul — not
only a shot. The kind names the player-centric beat, not one of its outcomes.

**No player fill yet.** Selection points at the *slot*, which Session 7 proved is
nameable on its own. A fill object (the rated player occupying a slot) is only
needed when something reads a player's attributes — not yet. Roll E names; it
reads nothing off the slot.

**Config lives separately.** Roll E's five flat weights live in the `"RollE"`
section of `config.json`, loaded by `RollEConfig` — written as five explicit 0.20
values (not a computed uniform), so the weights are visible and tunable and a
future generator overwrites numbers rather than flipping a mode.

| Roll | Name | Status |
|---|---|---|
| A | Entry — Inbounds (Dead Ball) | Built (stubbed generator + stubbed successors) |
| B | Halfcourt Initiation | Built (stubbed generator + stubbed successors) |
| C | Turnover Classification | Built (stubbed generator; terminal — no successors) |
| D | Non-Shooting Defensive Foul | Built (stubbed flavor generator + stubbed successors) |
| E | Player Selection | Built (flat stubbed generator + stubbed successor) |
| — | Jump ball (arrow node) | Built (50/50 tip placeholder; arrow complete) |

## Known required infrastructure (not yet built)

- **Free-throw node** — consumes the `BonusType` from Roll D. Resolves a 1-and-1
  (front-end miss → live ball → rebound roll) or a double bonus (two guaranteed;
  first miss is dead, only a missed final attempt is live). Currently a stub; the
  reboundability logic lives here, derived from the bonus type — not upstream.
- **Resumed-inbound / possession-continues node** — where a non-shooting foul
  with the opponent not in the bonus lands (offense keeps the ball, inbounds).
  Currently a stub.
- **Per-player foul attribution** — which defender committed the foul, for the
  personal/team foul accumulation. A counting-stat concern like turnover/steal
  credit; Roll D charges only the team. Future.
- **Half-reset of team fouls** — resets/replaces the `FoulTracker` at the half
  (the bonus resets with it). Future; clears the one object.
- **Shooting-foul roll** — a future post-player-selection roll; the only foul
  path Roll D does not cover (Roll D is pre-shot by construction).
- **Time roll** — apportions game-clock seconds for non-invariant outcomes. Every
  terminal except the shot-clock violation defers its time here — including all
  of Roll C's and the jump-ball terminals.
- **Player-selection roll** — BUILT (Session 8, Roll E). Picks which on-court
  offensive slot the possession runs through and stamps it on `PossessionState`.
  Roll B's `Proceed` exit lands here. Flat odds for now; the attribute model tilts
  them later via a smarter generator.
- **Player-action sequence** — where Roll E's selection lands: the shot-creation /
  shot-quality / make-miss / rebound / shooting-foul rolls that resolve what
  happens TO the selected player. Currently the `PlayerActionStub` dead-end; the
  next frontier. Consumes `PossessionState.SelectedSlot`.
- **Player/steal attribution layer** — runs over outcomes whenever a counting
  stat is generated; assigns the offensive turnover and (on live-ball slices) the
  defensive steal to specific players. Orthogonal to the possession chain; reads,
  never gates. Roll C's classification is one of its inputs.
- **Next-possession entry** — the awarded team (after a jump ball) and the team
  inbounding after a dead-ball turnover both need to *start a new possession*.
  Likely a sibling of Roll A.
- **Height-driven tip contest** — replaces the jump-ball node's 50/50 placeholder
  once a player/attribute layer exists (S-curve on centers' height differential).
