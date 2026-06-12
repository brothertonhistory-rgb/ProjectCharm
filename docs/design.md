# Project Charm — Design Document

Why things are built the way they are. This document records rationale, not
task lists (those live in the journal). It is updated every session.

---

## The funnel principle (the project's spine — read first)

The possession engine is a gravity funnel. Imagine dropping balls into the top:
each one falls through pipes and branches until it EXTINGUISHES at the bottom — a
turnover, a missed shot, a made shot, a resolved foul — all into one discard pile.
The work right now is building the pipes and naming them as we go. Where a pipe
isn't built yet, balls park in a HOLDING PEN (a stub) until the next pipe is laid —
exactly as ~80% of possessions parked at the player-selection stub until Roll E
was built.

Two consequences that govern all sequencing:

1. **Build the pipes until every path terminates.** The job is to make every
   possession reach a real ending: turnover, miss, make, foul resolved. A
   possession that dead-ends at a stub isn't finished — it's waiting for its next
   pipe. The roll web is built stub-first, each new roll converting a holding pen
   into either a terminal or another pipe that eventually terminates.

2. **The game layer comes LAST, after the funnel is whole.** The Game Governor
   (the thing that drops the next ball and decides which side it starts on, tracks
   score, declares a winner) does NOT exist yet and should not. There is nothing to
   govern until possessions produce real endings — points, turnovers, misses. "The
   game begins" only once the funnel terminates everywhere. Until then, validation
   is per-pipe (drop balls, check the rates, confirm every ball reaches a bottom),
   exactly as every session has worked so far — no game-level view is needed or
   wanted yet.

Anti-pattern to avoid (it has cost real time): trying to build the Governor against
a half-built funnel, then discovering "this outcome can't say where the next ball
goes." Of course it can't — that pipe isn't built. Finish the pipes; the Governor
becomes trivial once every terminal is real and sitting right there to read.

Current frontier: the chain terminates cleanly for turnovers (Roll C — now fed by A,
B, and F), fouls (Roll D → stubs — also fed by A, B, F), violations and jump balls
(Roll A, B, F). Roll F (player action) is built: it resolves the selected player's
action into a shot attempt, turnover, non-shooting foul, block, or held ball. The
chain now dead-ends at the `IntoShotType` stub for any possession that gets a shot
off, and at the `ResolveBlock` stub for a blocked attempt. The next pipes are the
shot web beyond Roll F (shot type → make/miss, block-recovery, rebound, free
throws). The Governor is built only after those land.

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

## The backcourt / frontcourt division (organizing principle)

The chain splits on a single physical line: **Roll A is the entire BACKCOURT
phase of an offensive possession; everything after Roll A is FRONTCOURT.**

Roll A is not just "the inbound." It owns the whole journey from dead ball to set
offense: inbound the ball, advance it up the floor, get into the halfcourt set.
Every way a possession can die or be interrupted BEFORE it is set up at the
offensive end lives in Roll A — the 5-second inbound failure, the 10-second
backcourt violation, a backcourt turnover (bad pass out of bounds, stepping on the
division line), a backcourt foul, a backcourt jump ball (tie-up bringing it up),
and the shot-clock violation if they never get across. `CleanEntry` is the single
SUCCESS path: "the offense is now set in its halfcourt."

Everything downstream is frontcourt, by construction:
- **Roll B** — halfcourt initiation (the ball is already set; the offense starts working).
- **Rolls E / F / G / H** — a player gets the action, attempts, and it resolves.

This is why Roll A is the busiest node (the backcourt has many failure modes) and
why Roll B is a near-pure gate (surviving to B means the backcourt is already
cleared). It also locates backcourt TIME: the 8/10-second count and the early shot
clock are the same backcourt window, so Roll A is where backcourt time gets
apportioned when the time roll lands (the 10-second violation already stamps its
invariant 10s; a clean entry's advancement time defers to the future time roll).

Consequence for turnovers: a backcourt turnover happens in Roll A's phase, so Roll
A classifies it as a turnover and routes to Roll C exactly as it does today — no
new slice needed for it. The only NEW dedicated Roll A slices are the two
zero-variance VIOLATION terminals (5-second, 10-second); everything else rides
paths Roll A already has.

---

## Verified routing map (audited from source, post Roll F)

The engine is NOT a single chain — it is a spine of action rolls draining into a
small set of SHARED sink nodes. "Many feeders, one node" is the actual wiring, not
a slogan: Rolls A, B, and F all feed the turnover node (C) and the foul node (D),
and A, B, and F all feed the jump-ball node. This table is the authoritative map;
it was reconstructed by reading each roll's outcomes and the resolver's routing
switch.

| Roll | Outcomes (from source) | Routes to |
|---|---|---|
| **A** Entry | CleanEntry / Turnover / ShotClockViolation / FiveSecondInbound / TenSecondBackcourt / Foul / JumpBall | B / C / **TERMINAL** ×3 / D / jump-ball node |
| **B** Halfcourt | Proceed / Foul / DeadBallTurnover / JumpBall | E / D / C / jump-ball node |
| **C** Turnover | 5 slices, all terminal | **TERMINAL** ×5 |
| **D** Foul | (bonus state read) None / OneAndOne / Double | ResumeInbound stub / ResolveFreeThrows stub |
| **E** Selection | one slot (flat 5-way) | Roll F (live) |
| **F** Player action | ShotAttempt / Turnover / NonShootingFoul / Blocked / JumpBall | ShotType stub / C / D / BlockRecovery stub / jump-ball node |
| **jump-ball node** | arrow read (or `Off` tip coin-flip) | **TERMINAL** (resolves + flips arrow) |

Shared sinks and their feeders (current):
- **Roll C (turnover):** fed by A (Turnover), B (DeadBallTurnover), and F (Turnover).
- **Roll D (foul):** fed by A (Foul), B (Foul), and F (NonShootingFoul).
- **Jump-ball node:** fed by A (JumpBall), B (JumpBall), and F (JumpBall) — all three live.

True terminals today: Roll A's three violation terminals (ShotClockViolation,
FiveSecondInbound, TenSecondBackcourt), all five Roll C slices, and the jump-ball
resolution. Everything else is a Continue that currently ends at a stub
(`ResumeInbound`, `ResolveFreeThrows`, `ResolveBlock`, `IntoShotType`).

The live spine A → B → E → F now resolves the player's action and dead-ends at the
`IntoShotType` stub (a shot got off) or the `ResolveBlock` stub (it was blocked).
Roll F is the third feeder into C and D and the third feeder into the jump-ball node.

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

**Feeders (verified from source).** Three live feeders: Roll A's `JumpBall` entry
outcome, Roll B's `JumpBall` slice (held ball while initiating), and Roll F's
`JumpBall` slice (held ball at the player-action beat: trapped handler, gang
rebound). All three emit the same `ResolveJumpBall` kind into the same node — "many
feeders, one node," exactly like C and D. NOT fed from Roll E (selection is not a
physical contest) nor from the shot-resolution rolls (a held ball there is already
a block or a foul). The Roll B and Roll F slivers are small (0.005 each), carved
out of their proceed/shot weights; adding them was the cheap edit the design
predicted — a slice + a switch arm + a config number, no new node.

**The arrow read IS the branch (INTENDED — partially built).** A held ball is
NOT uniformly terminal. The arrow decides both who gets the ball AND which of two
routes fires:

| Arrow holder at the tie-up | Outcome | Route |
|---|---|---|
| **Defense** holds the arrow | defense is awarded the ball | TERMINAL for this possession → the awarded team's NEW possession begins from Roll A |
| **Offense** holds the arrow | offense RETAINS | NOT terminal → a sideline inbound on the offense's side, with different inbound weights than Roll A (a future sideline-inbound node) |

In both cases the arrow still flips after the award (alternating possession). The
opening/OT tip (arrow `Off`) is the only coin-flip; once the arrow is set, the
read is deterministic and so is the branch.

**What is actually built today vs. intended.** The current `ResolveJumpBall` case
collapses BOTH outcomes into a single `Terminal` (resolve arrow, end possession).
That is:
- CORRECT-for-now for the defense-retains case (possession ends; the new-possession
  entry that should follow is the deferred next-possession-entry layer).
- A TEMPORARY oversimplification for the offense-retains case (it terminates
  instead of routing to a sideline inbound, because that node does not exist yet).

Both correct routes depend on infrastructure that is NOT yet built (see below), so
the terminal-collapse stands as the honest placeholder until then. Adding the B/F
slivers does not require fixing this first — they resolve the arrow exactly as A
does today.

**Two deferred nodes this implies (NOT built):**
- **Next-possession entry** — starts the awarded team's possession after a tie-up
  the defense wins (and after any other possession-ending event). Likely a sibling
  of Roll A. The "defense retains → Roll A" route lands here.
- **Sideline-inbound node** — a Roll A variant with its OWN pie (sideline weights
  differ from baseline/dead-ball entry). The "offense retains → sideline inbound"
  route lands here. Until it exists, offense-retains terminates as a placeholder.

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

## Roll A — Entry: the backcourt phase

**Simulates:** the entire backcourt phase of an offensive possession — inbound the
ball, advance it up the floor, get set in the halfcourt. Not just the inbound pass:
everything that can happen before the offense is set at its end lives here. (See
"The backcourt / frontcourt division" above for the organizing principle.)

**Pie shape:** seven slices over `EntryOutcome` — `CleanEntry`, `Turnover`,
`ShotClockViolation`, `FiveSecondInbound`, `TenSecondBackcourt`, `Foul`,
`JumpBall`. (Stub weights today; see below.)

**Seven exits:**

| Outcome | Result | Routes to |
|---|---|---|
| Clean entry | `Continue(IntoHalfcourtSet)` | Roll B |
| Turnover (incl. backcourt bad pass / stepping on line) | `Continue(ResolveTurnoverType)` | Roll C |
| Shot-clock violation | `Terminal("ShotClockViolation")`, elapsed = 30s | possession ends |
| 5-second inbound | `Terminal("FiveSecondInbound")`, elapsed = 0s | possession ends |
| 10-second backcourt | `Terminal("TenSecondBackcourt")`, elapsed = 10s | possession ends |
| Foul | `Continue(ResolveFoulType)` | Roll D |
| Jump ball | `Continue(ResolveJumpBall)` | jump-ball node |

**Three violation terminals, three fixed elapsed times.** All three violations are
zero-variance: the violation simply *is* the outcome, nothing remains to resolve,
so each stamps its own invariant elapsed time with no time roll. They differ only
in how much clock burned:
- Shot-clock (30s) — never got a shot off in the full backcourt+frontcourt window.
- 10-second backcourt (10s) — inbounded, but never cleared the division line; the
  count ran before the whistle.
- 5-second inbound (0s) — the entry pass never came in, so the clock never started.

A backcourt *turnover* (bad pass out of bounds, stepping on the division line) is
NOT a new slice: it rides the existing `Turnover → ResolveTurnoverType` path and
Roll C classifies it by ball-state as it does any other turnover.

**Why foul and jump ball are continues, not terminals.** Both have real variance
in what they become. A foul still needs its type decided (Roll D). A jump ball
needs the possession arrow consulted. Roll A only classifies that the outcome
occurred and hands off; the resolver does the deciding.

**The pie generator is stubbed.** `StubPieGenerator` (in `StubPieGenerator.cs`;
its config is `RollAConfig`, which lives in the misleadingly-named `Config.cs`)
returns the configured base weights with one live wire: a single 0–1 `pressure`
scalar nudges the turnover slice, then renormalizes. Placeholder to prove the seam
carries signal — not basketball logic.

---

## Roll B — Halfcourt Initiation

**Simulates:** the first beat after the offense is cleanly into its halfcourt
set. A pure gate: decides whether the possession advances to player selection or
is interrupted by a foul or dead-ball turnover before any action occurs.

**Pie shape:** four slices over `HalfcourtOutcome` — `Proceed`, `Foul`,
`DeadBallTurnover`, `JumpBall`. (Stub weights today; see below. The `JumpBall`
sliver, 0.005, was carved out of `Proceed` in Session 9.)

**Four exits:**

| Outcome | Result | Routes to |
|---|---|---|
| Proceed | `Continue(IntoPlayerSelection)` | player-selection roll (Roll E, live) |
| Foul | `Continue(ResolveFoulType)` | foul-type resolver (Roll D, live) |
| Dead-ball turnover | `Continue(ResolveTurnoverType)` | turnover-type resolver (Roll C, live) |
| Jump ball | `Continue(ResolveJumpBall)` | jump-ball node (live) |

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
through its loop, which re-routes it by its `IntoPlayerAction` kind to Roll F (live
as of Session 9). The retired `PlayerSelectionStub` is dropped; `Proceed` still
emits the same `IntoPlayerSelection` kind — only its destination moved from a stub
to a real roll.

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

---

## Roll F — Player Action

**Simulates:** the beat right after a player (slot) is selected — what the selected
player's action BECOMES. A pure GATE, structurally a clone of Roll B: no terminal,
every outcome a continue, because each one has downstream work.

**Pie shape:** five slices over `PlayerActionOutcome` — `ShotAttempt`, `Turnover`,
`NonShootingFoul`, `Blocked`, `JumpBall`. Placeholder weights this session
(0.82 / 0.09 / 0.05 / 0.035 / 0.005).

**Five exits:**

| Outcome | Result | Routes to |
|---|---|---|
| Shot attempt | `Continue(IntoShotType)` | shot-type node *(stub — future Roll G)* |
| Turnover | `Continue(ResolveTurnoverType)` | turnover-type resolver (Roll C, live) |
| Non-shooting foul | `Continue(ResolveFoulType)` | foul-type resolver (Roll D, live) |
| Blocked | `Continue(ResolveBlock)` | block-recovery node *(stub)* |
| Jump ball | `Continue(ResolveJumpBall)` | jump-ball node (live) |

**No terminal.** Like Roll B, every outcome continues. A shot attempt proceeds
deeper into the shot sequence; the other four route to shared sinks or stubs.

**Three feeders into existing nodes, two new pipes.** Turnover, foul, and jump ball
reuse the exact kinds A and B already emit — Roll F becomes the third feeder into
C, D, and the jump-ball node *for free*. This is the "many feeders, one node"
payoff at its clearest: no new turnover roll, no new foul roll, just a third arrow
into each. Only `Blocked` and `ShotAttempt` open new pipes (`ResolveBlock`,
`IntoShotType`), because they have genuinely new downstream work.

**Takes `(state, pie, rng)` — no `GameState`.** A flat gate reads nothing and
mutates nothing. Unlike Roll D (which charges a team foul) or Roll E (which reads
the lineup), Roll F touches no game state. The jump-ball arrow flip happens in the
jump-ball node; the team-foul charge happens in Roll D. Roll F only classifies the
action and emits a kind. It also stamps NOTHING on `PossessionState` — Roll E's
`SelectedSlot` rides forward untouched; the future Roll G adds `ShotType`.

**Why the shooting foul is NOT here.** `NonShootingFoul` is non-shooting by
construction: no shot is up yet at this beat, so it fits Roll D's existing pre-shot
definition exactly. The shooting foul (fouled in the act, and-1, free throws) is a
deliberately SEPARATE home in the future make/miss roll (Roll H) — kept apart on
purpose, because it resolves against a shot that has already gone up.

**Why the 10-second backcourt violation can't appear here.** A Roll F turnover
routes to Roll C, whose five slices do not include the 10-second/shot-clock
backcourt violations — those are Roll A *terminals*, not Roll C slices. So the
physically nonsensical "backcourt count in the halfcourt" is excluded by routing,
for free, with no suppression logic.

**The pie generator is stubbed, with NO live wire (like Roll E).** The only thing
that tilts Roll F's pie is the deferred player/attribute model (handle, defender
length/hands, rim protection, shot selection), and Roll F sits one inch from it. A
placeholder wire would pantomime the exact signal being deferred. Worse, a signal
like defensive pressure is really a possession-level INPUT Roll F is only one
reader of (it also pushes shot quality on the back end if pressure fails) — wiring
it into F alone would bake in the wrong ownership. So, like Roll D's flavor and
Roll E's selection, the generator takes no signal argument; the real generator
drops in later through the same seam.

**Context-shifted turnover odds (DESIGNED, not built).** A halfcourt turnover
(from Roll F) should classify differently than a backcourt entry turnover (from
Roll A) — more live strips, more offensive fouls. That belongs in **Roll C's
generator**, not in Roll C or Roll F: one classification ROLL, many context-shaped
PIES. The provenance is likely already free on `PossessionState` (`SelectedSlot`
null before Roll E, set after), so no new plumbing is needed when this is built.
Deferred — attribute-model-adjacent.

**Config lives separately.** Roll F's five weights live in the `"RollF"` section of
`config.json`, loaded by `RollFConfig`.

| Roll | Name | Status |
|---|---|---|
| A | Entry — Inbounds (Dead Ball) | Built (stubbed generator + stubbed successors) |
| B | Halfcourt Initiation | Built (stubbed generator; jump-ball slice added S9) |
| C | Turnover Classification | Built (stubbed generator; terminal — no successors) |
| D | Non-Shooting Defensive Foul | Built (stubbed flavor generator + stubbed successors) |
| E | Player Selection | Built (flat stubbed generator; feeds Roll F) |
| F | Player Action | Built (flat-ish stubbed generator, no wire; 2 live nodes + 2 stubs) |
| — | Jump ball (arrow node) | Built (50/50 tip placeholder; fed by A, B, F) |

## The Game Governor — the possession-to-possession layer (DESIGNED, not built)

The engine resolves ONE possession (the Resolver) but has no layer above it: the
ball never changes teams, no second possession ever begins, and the
possessions-as-accounting-unit anchor (~67–70/team, ~1.0 PPP) is therefore not even
measurable. The Game Governor is the layer that turns "resolve a possession" into
"play a game." DESIGNED this session; built LAST — only once the possession engine
terminates on every path (see "The funnel principle" below). The Governor governs a
finished funnel; until every possession reaches a real ending there is nothing to
govern, so it is deliberately deferred behind the rest of the roll web.

**Two routing layers, kept distinct.** WITHIN a possession, routing is unchanged:
roll → continuation kind → Resolver → next roll. BETWEEN possessions is the new
layer: a roll produces a TERMINAL → the Governor records it → the Governor begins
the NEXT possession by handing a fresh `PossessionState` (new offense + start-state)
to the Resolver. The Governor never picks a roll; it picks the next possession's
STARTING CONDITIONS and drops them at the top of the chain. It never reaches inside
a possession.

**Terminals carry their consequence; the Governor stays dumb.** What a terminal
MEANS for the next possession (who gets the ball, dead vs. live start, points
scored) lives where it is generated — the same philosophy as "a roll names its
continuation kind, the Resolver maps it." The Governor reads the stamped
consequence and executes it; it does not inspect reason strings and decide. (The
exact attachment mechanism — stamp every terminal vs. a gap-filling interpreter for
legacy terminals — is the central question for the build session.)

**Default flip, override on the consequence.** Most terminals flip the ball to the
other team. The OFFENSIVE REBOUND is the exception: same team, and it does NOT
increment the possession count — it is a continuation, which is what preserves the
~67–70 anchor. No rebound roll exists yet, so this is a stub branch, but the loop
is SHAPED from day one to allow "same team, possession continues," because
retrofitting that later is painful.

**Owns: loop, whose-ball, clock, score, possession count.** The clock and score are
STUBBED-but-real-shaped at first: the Governor writes score (0 until the make/miss
roll exists) and drains a flat placeholder time per possession toward 40 minutes
(until the real time roll exists). The write paths and fields are real; the values
snap in later without reopening the Governor. The stopping rule starts as a config'd
possession cap, not a real clock.

**Start-state is an enum** (`DeadBallInbound`, `Transition`, …), so the Governor can
eventually route to different entry variants. Roll A is the `DeadBallInbound` entry;
a transition-entry roll is future. (`EntryType` and this enum may be the same
concept — reconcile, don't carry both.) This is also the home for the deferred
jump-ball retain/turnover branch: the jump-ball terminal's consequence carries the
arrow-award result, and once the Governor exists the defense-retains case (new
possession from Roll A) becomes buildable.

---

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
- **Player-action sequence** — where Roll E's selection lands. The gate (Roll F,
  BUILT S9) resolves the action into shot attempt / turnover / non-shooting foul /
  block / held ball. Still ahead: the shot-creation / shot-quality / make-miss /
  rebound / shooting-foul rolls beyond it. The chain now dead-ends at the
  `IntoShotType` and `ResolveBlock` stubs — the next frontier. Roll F consumes
  `PossessionState.SelectedSlot` only indirectly (it rides forward untouched).
- **Player/steal attribution layer** — runs over outcomes whenever a counting
  stat is generated; assigns the offensive turnover and (on live-ball slices) the
  defensive steal to specific players. Orthogonal to the possession chain; reads,
  never gates. Roll C's classification is one of its inputs.
- **Next-possession entry** — the awarded team (after a jump ball) and the team
  inbounding after a dead-ball turnover both need to *start a new possession*.
  Likely a sibling of Roll A.
- **Height-driven tip contest** — replaces the jump-ball node's 50/50 placeholder
  once a player/attribute layer exists (S-curve on centers' height differential).
