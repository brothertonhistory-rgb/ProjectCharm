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
(Roll A, B, F). Roll F (player action) resolves the selected player's action into a
shot attempt, turnover, non-shooting foul, block, or held ball; Roll G (shot
location) then stamps WHERE a clean attempt comes from (Three / Long / Mid / Short /
Rim). The chain now dead-ends at the `IntoShotResolution` stub for any possession
that gets a shot off (the future Roll H — make/miss), and at the `ResolveBlock` stub
for a blocked attempt. The next pipes are the shot web beyond Roll G (make/miss,
block-recovery, rebound, free throws). The Governor is built only after those land.

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

## Verified routing map (audited from source, post Roll H)

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
| **F** Player action | ShotAttempt / Turnover / NonShootingFoul / Blocked / JumpBall | Roll G (live) / C / D / BlockRecovery stub / jump-ball node |
| **G** Shot location | Three / Long / Mid / Short / Rim (flat-ish 5-way) | Roll H (live, all five) |
| **H** Make/miss | Made / MadeAndFouled / Miss / MissFouled / MissOutOfBoundsLost / MissOutOfBoundsRetained (flat-ish 6-way, location-blind) | **TERMINAL** ×2 (Made, MissOutOfBoundsLost) / ShootingFreeThrows stub ×2 (MadeAndFouled, MissFouled) / Rebound stub (Miss) / SidelineInbound stub (MissOutOfBoundsRetained) |
| **jump-ball node** | arrow read (or `Off` tip coin-flip) | **TERMINAL** (resolves + flips arrow) |

Shared sinks and their feeders (current):
- **Roll C (turnover):** fed by A (Turnover), B (DeadBallTurnover), and F (Turnover).
- **Roll D (foul):** fed by A (Foul), B (Foul), and F (NonShootingFoul).
- **Jump-ball node:** fed by A (JumpBall), B (JumpBall), and F (JumpBall) — all three live.

True terminals today: Roll A's three violation terminals (ShotClockViolation,
FiveSecondInbound, TenSecondBackcourt), all five Roll C slices, the jump-ball
resolution, and now Roll H's two terminals (Made, MissOutOfBoundsLost — a clean
basket and a miss-out-of-bounds-off-offense, the possession's two cleanest ends).
Everything else is a Continue that currently ends at a stub (`ResumeInbound`,
`ResolveFreeThrows`, `ResolveBlock`, `ResolveRebound`, `ResolveShootingFreeThrows`,
`ResolveSidelineInbound`). `IntoShotResolution` is no longer a dead-end stub — it
is now live and runs Roll H.

The live spine A → B → E → F → G → H now resolves the player's action, stamps the
shot's location, AND resolves make/miss — the first time a possession produces a
scored or missed shot. It dead-ends at Roll H's two terminals or at one of three
new continue-stubs: `ResolveRebound` (a live miss — the big dependency), 
`ResolveShootingFreeThrows` (a shooting foul, kept SEPARATE from Roll D's bonus
`ResolveFreeThrows` for now — see Roll H), or `ResolveSidelineInbound` (a miss
deflected out off the defender, offense retains). Roll F remains the third feeder
into C and D and the third feeder into the jump-ball node; Rolls G and H add no new
feeders — like Roll E, each stamps one fact and continues to a single next beat
(H's "next beat" fans into its mixed terminals/stubs, but it is still one roll, one
draw, one stamp).

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
| Shot attempt | `Continue(IntoShotType)` | Roll G — shot location *(live)* |
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
`IntoShotType`), because they have genuinely new downstream work. (`IntoShotType`
now triggers the live Roll G; `ResolveBlock` is still a stub.)

**Takes `(state, pie, rng)` — no `GameState`.** A flat gate reads nothing and
mutates nothing. Unlike Roll D (which charges a team foul) or Roll E (which reads
the lineup), Roll F touches no game state. The jump-ball arrow flip happens in the
jump-ball node; the team-foul charge happens in Roll D. Roll F only classifies the
action and emits a kind. It also stamps NOTHING on `PossessionState` — Roll E's
`SelectedSlot` rides forward untouched; Roll G (live) adds `ShotType`.

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

---

## Roll G — Shot Location

**Simulates:** the beat right after a clean shot attempt gets off (Roll F's
`ShotAttempt`) — WHERE the shot comes from. Stamps one of five zones onto the
possession, then hands off to the future make/miss roll (Roll H).

**Structurally Roll E, not Roll F.** Like Roll E, every outcome stamps a fact and
continues to the SAME next beat; the only thing that differs per outcome is which
zone gets stamped. (Roll E stamped a `SelectedSlot` and all five slots emitted
`IntoPlayerAction`; Roll G stamps a `ShotType` and all five zones emit
`IntoShotResolution`.) It is NOT a gate like Roll F, whose outcomes branch to
different nodes. Unlike Roll E it needs NO `GameState` — a zone is just an enum
value, nothing to look up — so its signature is `(state, pie, rng)`, the Roll F
shape.

**Pie shape:** five slices over `ShotLocation` — `Three`, `Long`, `Mid`, `Short`,
`Rim` (declaration order, walked by the pie). Placeholder weights this session,
roughly real D1 attempt shares: `Three 0.36 / Rim 0.35 / Short 0.11 / Mid 0.10 /
Long 0.08`.

**Five exits, one destination:**

| Outcome (zone) | Result | Routes to |
|---|---|---|
| Three / Long / Mid / Short / Rim | `Continue(IntoShotResolution)` *(zone stamped on state)* | make/miss node *(stub — future Roll H)* |

**The second per-possession fact.** Roll G stamps `ShotType` onto `PossessionState`
(after Roll E's `SelectedSlot`) via a `with`-expression — a durable fact, named
`ShotType` but typed `ShotLocation`. It lands on state, not on the continuation,
for the same reason `SelectedSlot` does: Roll H reads BOTH facts across a chain hop
to resolve the matchup into points. (Contrast Roll D's `Bonus`, which rides the
continuation because it is transient input for the very next node.)

**Location ONLY — quality is NOT here.** No open-vs-contested, no
assisted-vs-unassisted. Shot quality is not its own beat at all: it is folded into
the make/miss PERCENTAGE at Roll H (a great look and a poor look differ only in
conversion odds, never as a stored value). Keeping each zone to one clean meaning
is what gives every bucket a real-world FG% to calibrate against later; a bucket
that smuggled in a second axis would have no clean reference number. This is the
same discipline as Roll F's localized outcomes.

**Why these five buckets (settled, not to re-litigate):**
- **`Long` (long two) stays its own bucket** — the *inefficient* shot. Separating it
  is what lets shot selection matter: lots of long twos should visibly bleed
  efficiency. Never collapsed into `Mid`.
- **`Short` and `Mid` are different populations** — short (floaters, runners, hooks;
  bigs) vs. mid (pull-up jumpers; guards). The split is where slot identity starts
  to express.
- **One `Three` bucket for now** — corner vs. above-the-break is a real efficiency
  gap but a cheap future slice-split; not front-loaded.

**The pie generator is stubbed, with NO live wire (like Roll E and Roll F).** The
only thing that tilts Roll G's pie is the deferred player/attribute model (shot
selection, role, defensive pressure). A placeholder wire would pantomime the exact
signal being deferred. The real generator drops in later through the same seam —
a non-flat pie over the same enum, with no change to Roll G or the resolver.

**Block recovery is deliberately NOT folded in here.** A blocked attempt
(`ResolveBlock`) stays a separate stub — it is loose-ball resolution, entangled
with the not-yet-built rebound system. Building it now risks throwaway work or an
accidental early rebound system; it lands later, next to rebounds.

**Config lives separately.** Roll G's five weights live in the `"RollG"` section of
`config.json`, loaded by `RollGConfig`.

| Roll | Name | Status |
|---|---|---|
| A | Entry — Inbounds (Dead Ball) | Built (stubbed generator + stubbed successors) |
| B | Halfcourt Initiation | Built (stubbed generator; jump-ball slice added S9) |
| C | Turnover Classification | Built (stubbed generator; terminal — no successors) |
| D | Non-Shooting Defensive Foul | Built (stubbed flavor generator + stubbed successors) |
| E | Player Selection | Built (flat stubbed generator; feeds Roll F) |
| F | Player Action | Built (flat-ish stubbed generator, no wire; shot exit now feeds live Roll G, block exit stubbed) |
| G | Shot Location | Built (flat-ish stubbed generator, no wire; stamps ShotType; shot now feeds live Roll H) |
| H | Make/Miss | Built (flat-ish stubbed generator, no wire, location-blind; stamps Result; mixed terminals + 3 new continue-stubs) |
| — | Jump ball (arrow node) | Built (50/50 tip placeholder; fed by A, B, F) |

## Roll H — Make/Miss

Roll H is the beat right after a shot's location is stamped: it resolves the
located shot into one of six outcomes and stamps that outcome as the THIRD durable
per-possession fact (`PossessionState.Result`), after Roll E's `SelectedSlot` and
Roll G's `ShotType`. It is the first roll in the whole engine that turns a
possession into a scored or missed shot — the point of the funnel everything above
has been draining toward.

**Structurally a weld of three earlier rolls.** Nothing about Roll H is novel
architecture; it is the established patterns combined:
- **Roll F's gate skeleton** — a switch over the rolled outcome, one arm per slice.
- **Roll A's mixed ends** — some arms are `Terminal` (the possession ends), some are
  `Continue`. Roll H is the first roll since A to mix the two: Roll C is all-terminal,
  Rolls B/E/F/G are all-continue. Made and MissOutOfBoundsLost end the possession;
  the other four continue.
- **Roll G's stamp-a-fact** — it writes its own outcome onto the carried state via a
  `with`-expression before routing, exactly as Roll G stamped `ShotType`. Both
  terminals carry the stamped state too, so the future Governor reads `Result` +
  `ShotType` off the terminal, not just off the continues.

**The six outcomes (declaration order, settled):** `Made` → TERMINAL; `MadeAndFouled`
(and-1) → `ResolveShootingFreeThrows`; `Miss` → `ResolveRebound`; `MissFouled` →
`ResolveShootingFreeThrows`; `MissOutOfBoundsLost` → TERMINAL; `MissOutOfBoundsRetained`
→ `ResolveSidelineInbound`. Placeholder weights are location-blind (a cross-zone
average, not per-zone FG%): Made .43 / MadeAndFouled .03 / Miss .47 / MissFouled .04 /
MissOutOfBoundsLost .02 / MissOutOfBoundsRetained .01.

**Point value and free-throw count are DOWNSTREAM derivations, not stored.** Roll H
records only WHICH of the six outcomes happened. Whether a make is worth 2 or 3, and
whether a shooting foul yields 1 / 2 / 3 free throws, are derived later from the
`(Result, ShotType)` pair by the scoring and free-throw layers. Roll H computes no
points, charges no fouls, and tracks no stats — the same scope discipline that kept
Roll G from resolving makes. Because of this it reads nothing off `GameState` and
takes no `GameState` (signature `(state, pie, rng)`, like F and G).

**It reads no stamps either.** Roll H does not inspect `SelectedSlot` or `ShotType`;
they ride forward untouched. The intuition that "make/miss should read both stamps"
is correct — but that belongs to Roll H's deferred GENERATOR, which will read the
shooter-vs-defender matchup (and the other-four gravity term, the skill/athleticism
gates, the bounded logistic make-% mapping) to tilt the pie. The roll itself reads
only its pie. This is the same seam every roll since E has had: a smarter generator
drops in later, handing H a non-flat pie over the same enum, with no change to Roll H
or the resolver.

**Shot quality is a percentage, not a slice.** A great look and a poor look differ
only in the make/miss probability (folded into the deferred generator), never as a
stored open/contested or assisted/unassisted value. Keeping quality out of the enum is
what lets the attribute model express purely through the odds.

**Three new continue-stubs, and an open fork.** Roll H's non-terminal arms land at
three new holding-pen stubs: `ReboundStub` (the big dependency — an offensive board
keeps the SAME possession, a defensive board flips it; the rebound system is designed
but unbuilt), `ShootingFreeThrowsStub`, and `SidelineInboundStub`. The shooting-foul
node is deliberately kept SEPARATE from Roll D's bonus `ResolveFreeThrows` node,
because the shot-count rules differ (and-1 vs. fouled-miss vs. bonus). Whether the two
free-throw paths later unify into one FT-resolution node is an OPEN FORK — flagged,
not decided. `SidelineInboundStub` may likewise eventually share a loose-ball / inbound
node with block recovery — also flagged, not merged.

**Config lives separately.** Roll H's six weights live in the `"RollH"` section of
`config.json`, loaded by `RollHConfig`.

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

---

## Block location: why block lives in Roll H, not Roll F (Session 13)

A block is not a property of the player's ACTION; it is a property of the SHOT —
specifically where the shot comes from. Rim attempts get swatted far more than
perimeter jumpers, and threes almost never. So a single flat block rate at the action
beat (where Roll F sits, before any zone exists) is physically the wrong place: it
cannot see the one variable that drives block frequency.

Roll F decides the action BEFORE Roll G assigns the zone. Roll H sits AFTER Roll G,
with the zone already stamped on the shot object. So block belongs in Roll H, where
the variable it depends on already exists. This is the same "signal flows one
direction, consumed where it is available" discipline the funnel is built on — we did
not reach backward to teach Roll F about zones; we moved block forward to where the
zone already rides. Because block left Roll F entirely, block happens exactly once,
with no double-count risk.

The block weight is carved off the top of Roll H's pie per zone (Rim highest, Three
lowest); the six make/miss outcomes keep their relative proportions, scaled by
(1 − b(zone)). This keeps config to one shared six-way shape plus five block numbers,
rather than a 35-number per-zone make/miss table — only the axis that genuinely varies
by zone THIS pass (block) is made zone-aware. Per-zone make/miss percentages are a
deliberately separate future pass; folding them in now would crystallize a per-zone
shooting model before we are ready to calibrate it.

Block routing stays zone-blind even though block weighting is zone-aware: every block,
from any zone, lands at the same block-recovery node. Weighting and routing are
different concerns — how OFTEN a block happens depends on the zone; what happens AFTER
does not.

The block weights are best-guess placeholders. The general philosophy holds: give our
best guess, keep it in editable config (one `BlockWeight(zone)` lookup, read by both
the generator and the harness's blended-rate math), and calibrate later against the
harness's zone-blended readout.

**Forthcoming — the block-recovery roll.** `BlockRecoveryStub` is a parked
placeholder. A future block-recovery roll will replace it (the live-ball scramble:
out of bounds off either team, or recovered by either team) and may at that point feed
the rebound system — its own decision, a later session. It MAY also share a loose-ball
/ inbound node with the sideline-inbound stub — flagged, not merged.

---

## The possession boundary: team-switch ⇒ terminal (Session 14, Roll I)

Roll I (rebound resolution) is the first roll whose job includes handing the ball to
the OTHER team, so it is where the project's possession-boundary rule first becomes
load-bearing: **anything that switches which team has the ball is a TERMINAL.** A
terminal is not just "this roll is done" — it is the possession-end flag, the future
stat-accumulation trigger. When the Governor exists (built last, see "The funnel
principle" and "The Game Governor" notes), it reads each terminal to (a) close the
current possession's accounting and (b) start the next team's possession. So the
terminal/continue distinction is not stylistic; it marks exactly where one
possession's accounting ends and the next begins.

This is why Roll I's four outcomes split the way they do:
- **Defensive rebound** and **loose-ball foul on the offense** flip the ball →
  TERMINALS. The possession is over; its stats will accumulate; a new possession for
  the other team begins (future).
- **Offensive rebound** and **loose-ball foul on the defense** keep the ball with the
  offense → CONTINUES. The SAME possession stays alive — no boundary, no
  possession-count increment. The offensive rebound is the canonical "same team,
  possession continues" case the loop was shaped for from day one (it is what
  preserves the ~67–70 possessions/team anchor: an offensive board does NOT mint a new
  possession).

### Live-vs-dead next-possession entry mapping (what the future spawner consumes)
The two terminals are not interchangeable. They differ on the live-vs-dead axis — the
same axis Roll C's turnover classification splits on — because that axis drives what
the NEXT possession looks like:
- **Defensive rebound → LIVE flip.** The defense secures the ball in live play; its
  next possession enters via the future TRANSITION roll (a fast, live-ball entry), not
  a dead-ball inbound.
- **Loose-ball foul on the offense → DEAD flip.** The whistle stops play; the defense's
  next possession is a DEAD-ball inbound at Roll A.

This mapping is design knowledge recorded HERE for the future next-possession spawner
to consume; Roll I does NOT route it. Roll I only classifies the rebound and names the
terminal; the spawner (when built) reads the terminal's meaning and picks the entry
variant. This is the same discipline as "a roll names its continuation kind, the
Resolver maps it" — the consequence lives where it is generated, the consumer reads it.

### The loose-ball-foul-on-defense arm: reuse, free to diverge
The one continue that touches GameState charges the DEFENSIVE team foul through
`FoulTracker` and reads the bonus exactly as Roll D does — then reuses Roll D's
downstream nodes: `SidelineInboundStub` below the bonus (offense retains, inbounds from
the side) and `ResolveFreeThrowsStub` in the bonus (carrying the `Bonus` payload). This
is deliberate reuse, not fusion: if loose-ball free throws ever need to diverge from
Roll D's non-shooting bonus FTs (different reboundability rules, say), they get a
distinct continuation kind or a context tag at that point — without reopening Roll I.
The offensive-foul terminal, by contrast, charges NOTHING and touches no GameState,
following Roll C's `OffensiveFoul` precedent (personal/offensive-foul accounting is the
deferred attribution layer's concern).

### Forthcoming — the rolls the Roll I stubs hold water for
- **The offensive-rebound roll.** `OffensiveReboundStub` is a parked placeholder. The
  real roll has its own odds and one branch that loops the live possession back to the
  half-court roll → player selection (a put-back, a reset, a kick-out). Its rate is
  also where the possession-count calibration knob lives — too many offensive boards
  inflate possessions per team above the ~67–70 anchor — so the flat 0.29 placeholder
  is explicitly Emmett's to tune against the future full-game readout. A later session.
- **The transition roll.** The defensive-rebound terminal's LIVE meaning enters here.
  No transition roll exists yet; "transition" is the next possession's entry, built
  once the next-possession spawner / Governor exists. A later session.
- **The next-possession spawner / Governor.** Reads Roll I's terminals (and every
  other terminal) to close accounting and start the next possession with the right
  entry variant per the live-vs-dead mapping above. Built last, against a whole funnel.

The block weights' philosophy holds here too: best-guess placeholders, kept in editable
config (the `"RollI"` section), calibrated later against the harness's possession-count
and rebound-rate readouts rather than guessed precisely now.

---

## The thin Governor (Session 15) — the possession-to-possession loop

After Roll I nearly every possession reaches a real terminal, so the funnel terminates
cleanly enough that a second possession is the next thing worth proving. The design doc
had deferred the Governor with one rule — *don't build it against a half-built funnel* —
and that condition has expired. Building it now exercises every cross-possession invariant
(arrow, foul counts, lineup, possession counter) IN SEQUENCE for the first time, surfacing
that bug class one at a time against a small engine instead of all at once later, and it
settles the `EntryType`-vs-start-state reconciliation in a few hundred lines before many
rolls depend on the answer.

### The permanent seam: terminals carry a consequence; the Governor stays dumb

What a terminal MEANS for the next possession — who gets the ball, and how that possession
starts — lives on the terminal as a `PossessionConsequence`, named where the terminal is
generated, NOT parsed from a reason string by the Governor. This is the same discipline as
"a roll names its continuation kind, the resolver maps it": signal flows one direction, and
the consumer stays ignorant of the producer's internals.

The consequence is deliberately MINIMAL — `NextOffense` (a `TeamSide`) and `NextEntry` (the
single reconciled `EntryType`). It carries no points, clock, foul context, or momentum. A
big speculative consequence now would be a bottleneck wearing a scalability costume; those
are clean appends when their consumers exist. It is **required** on `Terminal` (not
nullable): a missing consequence is a compile error at the construction site, which
surfaces omissions loud rather than letting a silent null reach the Governor.

The resolver surfaces the terminal it ended on (`RoutingOutcome.EndedOn`, null on a
stub-park) without disturbing `Destination` / `PossessionEnded`, and owns the top of the
chain via `RunPossession(start)` so the Governor drops a start state and never names a roll.

### One reconciled start-state enum

A possession's entry IS its start-state, so there is ONE enum: `EntryType`, now with
`DeadBallInbound` and `Transition`. There is no parallel "start-state" concept layered
alongside it. New entry kinds append here as they are modelled.

### The live-vs-dead mapping (where each terminal sends the next possession)

| Terminal | Next offense | Next entry |
| --- | --- | --- |
| Roll A violations (shot-clock / 5s / 10s) | the other team | DeadBallInbound |
| Roll C dead turnovers (bad-pass-dead, lost-dead, offensive foul) | the other team | DeadBallInbound |
| Roll C live turnovers (intercepted, stripped live) | the other team | Transition |
| Roll H `Made` | the other team | DeadBallInbound (inbounded under the hoop — a dead ball) |
| Roll H `MissOutOfBoundsLost` | the other team | DeadBallInbound |
| Roll I `DefensiveRebound` | the rebounding (defense) team | Transition |
| Roll I `LooseBallFoulOnOffense` | the other team | DeadBallInbound |
| Jump-ball terminal | the AWARDED team (arrow/tip) | DeadBallInbound |

Every terminal but the jump ball sends the ball to "the other team" (`state.Defense`); the
jump ball is the one exception, set by the arrow/tip.

### TEARDOWN CONTRACT (read before leaning on anything here)

The thin Governor is a temp building. Knowing in one glance what is safe to lean on and
what is slated for demolition:

**PROVISIONAL guts — slated for demolition, do NOT build on these:**
- **Flat clock.** A fixed `SecondsPerPossession` drained per possession, accumulated only
  in the Governor (no clock field exists on `GameState`). Replaced by the future time roll.
- **Zero score.** The Governor writes a literal 0 to the real `GameState` score field. The
  real `(Result, ShotType) → points` derivation replaces the 0 at that same spot.
- **Possession-cap stop.** The game stops after `Governor.PossessionCap` possessions, NOT
  on a real clock. Replaced by real stop conditions (clock expiry, overtime).
- **Temp-route-all-to-Roll-A.** *(Session 16: PARTIALLY discharged.)* A spawned possession
  carrying a `Transition` entry **with a `Rebound` ticket** now enters **Roll J** (the live
  transition-entry gate), not Roll A. Still provisional: a steal-born `Transition` start
  carries no context ticket yet, so it continues to temp-route to Roll A until the
  steal-feeder session lands its ticket and pie. Fully discharged when EVERY `Transition`
  start carries a context ticket (see Session 16 design section below).
- **Parked→default-flip.** Every stub-parked possession (FT, offensive rebound, sideline
  inbound, block recovery, resume inbound) flips to the OTHER team at Roll A on a default
  consequence — because the parked pipe isn't resolved yet. This is deliberately wrong
  basketball (an FT-parked possession should resolve points and decide the next possession
  off the last free throw; an offensive-rebound park should KEEP the same team). When each
  pipe closes, "park → default flip" is replaced AT THE SAME SEAM by "resolve → real
  consequence."

**PERMANENT seam — safe to build on, a future guts-swap hides behind these:**
- **The consequence on the terminal.** `PossessionConsequence(NextOffense, NextEntry)`,
  named at the terminal's generation site. Grows by append (points/clock/etc.) when needed.
- **The resolver surfacing the ended-on terminal AND signalling park-vs-terminal.**
  `RoutingOutcome.EndedOn` (non-null = ended on that terminal; null = parked at a stub).
- **The one reconciled start-state enum** (`EntryType`).
- **The Governor-reads-consequence-and-spawns contract** — INCLUDING the default-consequence
  path that a closed pipe later replaces. The loop shape (run a possession → read its
  consequence, or the default on a park → spawn the next) is what the real game layer swaps
  guts behind without touching the seam.

### Why the loop can't silently leak the possession count

Every possession — terminal OR parked — produces exactly one next possession (until the
cap), and the harness asserts `terminal-ended + parked == cap`. The parked count is the
load-bearing invariant: a dropped park is exactly how the count would silently leak. And
because every stub-park is handled by ONE uniform path (keyed only on "no terminal"), there
is no per-stub branch to forget — the Session-14 bug (handling only one of two bonus-split
landings once the shared game crossed the bonus mid-batch) is structurally impossible here.

### Forthcoming — what closes the provisional guts

- **Roll J (transition entry).** Gives the `Transition` start-state its own entry roll,
  replacing the temp-route-to-Roll-A for live possessions. The next session.
- **The offensive-rebound loop-back.** Replaces that stub's park→default-flip with "same
  team continues, possession count does NOT increment" — the loop is already shaped so this
  is expressible.
- **FT resolution + FT rebounding.** Replaces the FT stubs' park→default-flip with real
  point resolution and a real next-possession decision off the last free throw.
- **The real game layer.** Real clock, real score, real entry variety, real stop conditions
  — a guts-swap behind the permanent seam above.

---

## Session 16 — Roll J (transition-entry gate) & the ticket/station mechanism realized

Session 16 lands the first **live transition entry** and, with it, turns the
ticket/station context-tag idea from a sketch into a working, twice-instantiated
mechanism. Two things were built together because one forces the other: a defensive
rebound now enters a real roll (Roll J) instead of temp-routing to Roll A, and that
roll is the first station to stamp a non-default turnover context on a ticket.

### Two different things that both say "transition"

A naming hazard worth fixing in the record: **transition ENTRY** and the **transition
ROLL** are not the same node, and this session builds only the first.

- **Transition entry (Roll J) — built.** The run-or-not GATE a live possession passes
  through the instant it gains the ball off a defensive rebound. It decides only
  *whether we run*: pull it out and set up (Settle), or go (Push). Grabbing a board
  deep in the backcourt does not mean you run — that decision is exactly what Roll J
  models.
- **The transition roll — NOT built.** What the fast break *produces* once you have
  decided to run: numbers advantage, leak-outs, the transition shot mix. Roll J's Push
  arm parks this at `TransitionStub` via the new `IntoTransition` continuation. A later
  session fills it.

Keeping these separate is what lets Roll J be a small, flat five-way gate instead of a
sprawling fast-break simulator.

### Roll J's shape

Five arms (`TransitionOutcome`), **all continues** — Roll J names no terminal of its
own; its two "ending" flavors resolve at shared downstream nodes already built:

| Arm | Continuation | Lands at |
|---|---|---|
| `Settle` (.65) | `IntoPlayerSelection` | Roll E (halfcourt set) |
| `Push` (.25) | `IntoTransition` | `TransitionStub` (parked) |
| `Turnover` (.06) | `ResolveTurnoverType` + `TurnoverContext.Transition` | Roll C (transition pie) |
| `DefensiveFoul` (.035) | charge defense, bonus-fork | sideline inbound OR free throws |
| `JumpBall` (.005) | `ResolveJumpBall` | shared jump-ball node (consults arrow) |

Signature `(state, pie, game, rng)` — the Roll D / Roll I shape — because the
`DefensiveFoul` arm mutates `GameState` (it charges a team foul). The foul is charged to
`state.Defense`: on a possession spawned off a defensive rebound the new offense is the
rebounding team, so the new defense is exactly the team that lost the board and is
scrambling back — the team fouling on the push. This is the **third feeder** into the
shared charge-and-fork (after Roll D and Roll I): copied, not reinvented.

### The ticket/station mechanism, stated generally

A shared resolution node is reached by multiple feeders. Each feeder **stamps a
contextual ticket** on the object it hands forward; the node **reads the ticket to
select its parameter set** and never queries who fed it. Signal flows one direction;
the node stays blind to its callers. This session instantiates the pattern twice:

1. **Roll C (turnover classification) is a context-consuming node.** Its generator now
   selects between a **Halfcourt** weight set (the legacy `.30/.22/.18/.20/.10`, reached
   by every pre-Roll-J feeder — Roll A, B, F — which stamp nothing and so read as
   Halfcourt by default) and a **Transition** weight set (`.25/.15/.20/.35/.05` — more
   live strips going the other way), reached only when an upstream station stamps
   `TurnoverContext.Transition`. The Halfcourt path is **byte-for-byte unchanged**. The
   context parameter sits LAST with a default, so every existing call site compiles
   untouched; only the resolver passes a context, and only when the ticket carries one.

2. **Roll J is itself a context-consuming node.** The *arriving* transition ticket
   selects Roll J's run-or-not pie. This session one source is live — `Rebound` — so the
   generator builds the rebound pie and fails loud on any other source. The steal pie
   (more Push) is a sibling arm added with the steal-feeder session; no orphan steal
   numbers ship now.

Roll J's Turnover arm is the **forcing case** for the whole mechanism: it is the first
station to stamp a non-default `TurnoverContext`, which is why Roll C had to learn to
read one this session rather than later.

### Carrier choice: a structured growable record, not an enum

The cross-possession ticket is a record — `TransitionContext(TransitionSource Source)`
— **not** a granular `EntryType` enum exploded into `TransitionOffRebound`,
`TransitionOffSteal`, … . An enum would force a teardown every time transition gains a
new origin or a new piece of remembered context; a record grows by **adding a field**
(plug-in, not teardown), exactly the philosophy the rest of the engine follows.
`TransitionSource` has one value this session (`Rebound`); `Steal` is deliberately
undeclared — it arrives with its pie and its routing together, so no half-wired value
sits around. The ticket rides the **cross-possession consequence→entry seam**:
`PossessionConsequence` gained an optional `TransitionContext`, the Governor threads it
onto the spawned `PossessionState`, and the resolver's entry switch reads it.

### Ticket memory (model recorded; only single-hop built)

The general model: a ticket accumulates context as a possession proceeds, and at the
terminal the relevant memory distills into the consequence that seeds the next
possession's entry. This session builds only **single-memory, single-hop** instances —
Roll C reads one `TurnoverContext` off the immediate `Continue`; Roll J reads one
`TransitionSource` off the immediate entry. **Multi-hop accumulation and provenance**
(a steal-born break or an entry-stage turnover pushing the downstream pie harder than a
halfcourt one) is a future clean-append onto the same record, NOT built here.

### Roll J's two deferred modifier seams (documented, independent, NOT built)

The Push/Settle split is where two SEPARATE future inputs will land — and, per the
locked "strategy and matchup modifiers stay independent" rule, they are **never** fused
into one pre-blended weight:

1. **Rebounder tilt (attribute).** WHO grabbed the board nudges push vs. settle — a
   guard pushes more than a center. Lands in the attribute layer, read off the rebounder
   slot once selection/attribution names it.
2. **Coach tempo (strategy).** The team's up-tempo / low-tempo setting nudges push vs.
   settle. Lands in the strategy layer.

Both attach at the GENERATOR (a smarter pie), exactly like the height-driven tip contest
and Roll C's pressure wire; Roll J the roll never changes when they arrive.

### Rebound-first scope, and the one-line flip that finishes it

Only Roll I's `DefensiveRebound` is wired live to Roll J this session, via the new
`PossessionConsequence.TransitionReboundTo(team)` factory (which stamps
`TransitionContext.Rebound`). Steals still emit a plain `TransitionTo` with a null
context, so the resolver's entry switch sends them to Roll A unchanged. When the steal
feeder lands, the change is one line in that switch (every `Transition` start → Roll J)
plus the steal pie — the seam is already shaped for it.

---

## Session 17 — Roll K (offensive-rebound loop-back): the first possession-EXTENDING node

Session 17 lands the offensive-rebound resolution roll (Roll K) and, with it, the first
node that does not move the possession strictly forward. Every roll before this one either
ENDED a possession (a terminal) or HANDED IT FORWARD one step (a continue to the next
roll). Roll K introduces a third shape — a continue that loops the SAME possession back up
the chain — and the architecture absorbs it without a new control structure, because the
resolver was already a `while`-walk and the Governor already counted only ends and parks.

### Two arms that keep the same possession alive

The offense secured its own miss. What happens next splits seven ways, but the load-bearing
distinction is what happens to the BALL:

- **It stays, going back up (`PutBack`).** An immediate go-back-up at the rim. This is a
  CONTINUE into Roll H — the shot-resolution roll already built — with two stamps: the zone
  forced to `Rim`, and a **putback ticket** that tells Roll H's generator to use a distinct
  putback pie. A made putback ends the possession at Roll H's terminal; a missed putback
  re-enters Roll I (the rebound roll), and the cycle can repeat.
- **It stays, kicked back out (`ResetOffense`).** Pull it out and run a fresh play. A
  CONTINUE back to Roll E (player selection) on a blank slate.
- **It stays, via the foul fork (`DefensiveFoul`).** A foul on the defense in the scrum;
  the offense retains. Charges the defensive team foul and forks on the bonus.

Three arms FLIP the ball (`OffensiveFoul`, `DeadBallTurnover`, `LiveBallTurnover` —
terminals), and one ties it up (`JumpBall` — a continue to the shared arrow node). The
seven-way pie's placeholder weights live in `RollKConfig`; the headline calibration knob is
the PutBack/ResetOffense split, which directly sizes how many extra shots and possessions
an offensive board generates.

### The loop lives in the resolver, and the count cannot leak

`PutBack` and `ResetOffense` resolve back into the SAME `resolver.Route(...)` call:
`IntoShotResolution` re-enters Roll H, `IntoPlayerSelection` re-enters Roll E, and a missed
putback flows Roll H → Roll I → `ResolveOffensiveRebound` → Roll K again. So a single walk
can now cycle:

```
PutBack → Roll H → Miss → Roll I → OffensiveRebound → Roll K → PutBack → …
ResetOffense → Roll E → Roll F → ShotAttempt → … → Miss → Roll I → OffensiveRebound → Roll K → …
```

This is invisible above the resolver. The Governor's contract is unchanged: it spawns the
next possession only when `Route` RETURNS — at a terminal (`EndedOn` non-null) or a stub
park (`EndedOn` null). A reset or a putback is neither; it is an internal hop. So the
load-bearing invariant `terminal-ended + parked == cap` holds untouched, and the possession
NUMBER never increments inside the loop. The Session-15 design note — "every possession
produces exactly one next possession" — survives intact, because a loop iteration is not a
possession.

### Convergence is a property, so the engine proves it (and guards it)

A node that can re-enter itself raises the obvious question: does it terminate? The answer
is structural, not hopeful. Every cycle requires a chain of independent sub-1.0
probabilities — a putback must be attempted (.40 of boards), MISS (~.42 of putbacks),
produce another offensive board (~.29 of misses), and draw PutBack again — so each
additional cycle is rarer than the last by a large factor. The loop is a decaying geometric
process; it bleeds out.

Two mechanisms make this concrete rather than asserted:

1. **A loud guard.** `Route` carries an `iterations` counter with a 10,000 ceiling. A
   converging possession bleeds out in a handful of cycles, so the ceiling is orders of
   magnitude above any real walk. Reaching it means a genuine non-convergence bug, and the
   resolver THROWS — it does not silently `break` and return a half-resolved possession.
   This is the §2 "fail loud" rule applied to a re-entrant chain.
2. **A depth tally.** `RoutingOutcome.PutbackAttempts` counts the putback shots a walk
   takes. `OffensiveReboundConvergenceCheck` drives 100k possessions into Roll K through the
   REAL resolver, reads the tally, and asserts the survival distribution
   `reachedAtLeast[n]` strictly decreases on its populated levels and the max is comfortably
   bounded. Observed: ≈58% zero putbacks, ≈42% one, ≈1.4% two, a handful three, one four —
   max depth 4, ceiling 20, zero guard hits. The guard never firing IS the convergence
   proof; the tally shows the shape of the bleed-out.

This is the §2a "watch the accumulation across iterations" discipline turned inward: the
shared thing changing across iterations is the possession's own depth.

### The putback as a distinct shot population — ticket/station, third instance

A go-back-up is not a jump shot relocated to the rim; it is a different event with a
different make/miss/foul distribution (point-blank, frequently through contact, higher make
AND higher foul rate, real block risk). Rather than mint a new roll, the putback is
PARAMETERIZED onto the existing shot-resolution roll via the ticket/station mechanism:

- **Stamp.** Roll K's `PutBack` arm sets `Continue.Putback = true` and forces
  `ShotType = Rim`.
- **Read.** `RollHStubPieGenerator.Generate(state, putback)` returns a distinct putback pie
  (its own `Putback*` weights from `RollHConfig`, a flat seven-way placeholder with no
  per-zone block carve — a putback is always at the rim) instead of the located-shot pie.
- **Blind.** The generator never asks who set the ticket; signal flows one direction.

This is the third live instance of the pattern (Roll C's turnover context, Roll J's
transition context, now Roll H's putback context). The carrier is the leanest that fits: a
single bool, because there is exactly one putback flavor. The variety a putback will
eventually express — a 7-foot center finishing over a guard vs. a point guard who happened
to grab the board flinging up a low-percentage attempt — is NOT more ticket variants; it is
the deferred attribute generator reading the carried slot. The seam is shaped so that
generator drops in WITHOUT Roll K, Roll H, or the resolver changing: the slot already rides
the whole loop untouched.

### Reset re-enters at selection, not initiation

`ResetOffense` wipes the prior shot's facts and re-enters at **Roll E** (player selection),
not Roll B (halfcourt initiation). The reasoning is conservation of hazard: the
offensive-rebound pie ALREADY priced the turnover, foul, and jump-ball risk of the scrum,
so sending the reset back through Roll B — which prices initiation turnovers and fouls
again — would double-charge the same hazards on one possession. Re-entering at E treats the
reset as what it is: the offense already has the ball and settled; it is choosing a new
action, not re-initiating. The wiped slate (`SelectedSlot`, `ShotType`, `Result` all null)
means the fresh play draws the inherent selection odds with no residue from the shot that
missed.

### Two deferred seams on the putback (documented, NOT built)

1. **Putback shot quality (attribute).** The real make/foul/and-1 percentages, tilted by
   the putback-er's size / athleticism / rim rating and the contesting defender. Lands in
   the attribute layer at Roll H's putback-pie generator, reading the carried slot. The
   flat `Putback*` placeholders are the stand-in.
2. **Same-player rebound tilt (attribute).** A missed putback re-enters Roll I, and the
   shooter — especially a big — should be favored to grab his own miss back. Lands in the
   attribution layer once it names the rebounder; the slot rides the loop so it has the
   player to favor.

Both attach at a GENERATOR, exactly like every prior deferred modifier (the height-driven
tip contest, Roll C's pressure wire, Roll J's rebounder/tempo seams); the rolls themselves
never change when they arrive.

### One temp-route still standing

`LiveBallTurnover` emits a plain `TransitionTo(defense)` with no context ticket, so the
resolver temp-routes the spawned possession through Roll A — exactly how steals are handled
today. Its real home is the transition module via the steal feeder; when that feeder lands,
this becomes a one-line routing flip (every `Transition` start → Roll J) plus the steal
pie, the seam already shaped for it in Session 16.

### What Roll K closes, and what it opens

It closes the Session-15 "offensive-rebound stub → default-flip" provisional: an offensive
board now KEEPS the ball with the same team and the possession count does not increment,
replacing the deliberately-wrong park-and-flip. It opens the door to the deferred attribute
work on putback quality and same-player rebounding, and it leaves the broader chain's
remaining stubs (free-throw resolution and rebounding, block recovery, the transition roll)
untouched and on the horizon.

---

## Roll L — Free-Throw Resolution (Session 18)

Roll L is the node every trip to the line resolves at, and the closing of the two
longest-parked FT stubs (`ShootingFreeThrows`, `ResolveFreeThrows`) into one loop. It is the
simplest roll in the engine — and the one place the uniform roll contract is deliberately
relaxed.

### A context-free primitive that returns a bare outcome

Every other roll receives `(state, pie, …)`, classifies its result, and names a
continuation *kind* the resolver maps. Roll L does none of that. A free throw is the shooter
against a flat `Make` / `Miss` pie, and the make probability is **identical** regardless of
how the trip arose — an and-1, a fouled three, and a bonus foul all shoot the same odds.
There is no ticket, no wire, no parameter-set selection of the Roll C halfcourt-vs-transition
kind. Modelling one would invent the very signal the design rejects. So `RollL.Execute(pie,
rng)` reads no state, takes no ticket, and returns a bare `FreeThrowOutcome` (Make / Miss),
not a `RollResult`. The resolver — not Roll L — owns what a make/miss *means*. This is the
clean expression of the principle that probabilities are produced in one place and *consumed*
in another: Roll L produces the bare fact; the conductor consumes it into routing.

### The most direct attribute seam in the engine

Where other rolls map attributes to odds through a skill/athleticism interaction, a free
throw is a **literal 1:1**: a 71-rated shooter makes 71% per spin, full stop. The real
generator is therefore the simplest the engine will have — read the shooter's FT rating,
divide by 100, done. No gravity term, no matchup, no logistic. The stub ships a flat ~.72
placeholder; the real generator reads the carried shooter slot and replaces it without Roll L
or the resolver changing. Two documented seams sit at 0 / deferred: a **road make-penalty**
(a small negative modifier *if* it proves a real statistical effect) and the **bonus-FT
shooter identity** (a shooting-foul trip names the fouled shooter via `SelectedSlot`, correct;
a bonus trip has no shot selected, so its FT shooter is not yet named — the flat stub reads no
slot, so nothing blocks).

### Sequencing is the conductor's, not Roll L's

How many times Roll L spins, and whether the last spin is live, is a structural fact of *how
the foul happened* — plain loop arithmetic the resolver already owns, read at the entry edge,
never a stamp Roll L sees. The two FT resolver edges became two entry points to one
`DriveFreeThrows` loop, differing only in the shot count they derive: the `ShootingFreeThrows`
edge reads the stamped `(Result, ShotType)` — and-1 = 1, fouled two = 2, fouled three = 3;
the `ResolveFreeThrows` edge reads the `Bonus` token — `Double` = 2, `OneAndOne` = a
conditional 1-and-1. This keeps the FT rules in exactly one place (the conductor) and out of
both the upstream foul rolls and Roll L itself.

### The uniform dead-intermediate / live-last rule

Every shot before the last in a fixed set is **dead** regardless of make or miss — the ball
never goes live between shots; it just retriggers the next attempt. Only the last shot
evaluates live/dead: a make ends the possession `DeadBallTo(defense)` (reusing Roll H's `Made`
consequence — the opponent inbounds at Roll A), a miss leaves the ball live and routes to the
FT-rebound node. The 1-and-1 is the one conditional: the front end is *conditionally* the last
shot — a miss forfeits the second and is itself the last shot (live → FT-rebound); a make
brings a now-last second shot under the normal rule. An and-1 is a fixed 1-shot set, so its
single shot is the last shot — a missed and-1 free throw is a live ball (the made basket
already banked its points upstream; only the FT sets the consequence).

This rule has a clean observable signature, which is how the harness proves it without seeing
inside the loop: for any **fixed** n-shot trip, routing depends only on the last shot, so the
made-FT (END) rate equals the per-shot make probability p; for a **1-and-1**, an END requires
both shots to make, so its END rate is p². The split between those two — p versus p² — is the
proof that intermediates are dead and the front-end is conditional.

### A hard bound instead of a convergence guard

Unlike the putback↔rebound loop (which converges probabilistically and needs a loud
10,000-iteration guard), the FT loop is **structurally bounded**: at most 3 spins, at most 2
for a 1-and-1. So it carries a simple assert that the spin count never exceeds 3 — a
shot-count derivation bug surfaces loud rather than silently over-spinning. The harness reads
the exact count via `RoutingOutcome.FreeThrowSpins`, an output observability counter parallel
to `PutbackAttempts` (and, like it, an output seam — never an odds-bearing input ticket;
`RollResult.cs` stays untouched, the count being derived resolver-local from facts already on
the carried state).

### What Roll L closes, and what it opens

It closes the two longest-standing FT parks at once — a trip to the line now *resolves*
rather than dead-ending. It charges no foul and touches no arrow, so it is accumulation-free:
the only mid-batch bonus crossing is upstream, which fixes the `Bonus` token before a trip
ever reaches Roll L. It opens `STUB:FTRebound` — the future FT-rebound roll's holding pen
(offensive / defensive board off a missed FT, plus any foul on that rebound), parked exactly
as prior sessions parked their downstreams. It leaves points accounting (a made FT is 1 point
— the separate deferred attribution pass), the FT-rating generator, the road penalty, the
bonus-FT shooter identity, and end-game deliberate-foul / clock logic all on the horizon.

## Roll M — Free-Throw Rebound Resolution

Roll M resolves what happens to a **missed final free throw**. Roll L parks a live last-shot miss
at the `ResolveFTRebound` edge; Roll M is the roll that edge now executes, closing the
`STUB:FTRebound` holding pen with the same stub→roll swap every prior downstream received.

### Roll I's shape, two tilts and an extra pair

Roll M is deliberately **Roll I with a different population**, not a new structure: a board-battle
gate that mixes terminals and continues and feeds the shared loose-ball foul fork. Two things
differ. First, the board split is **more defensive** — off a free throw the defense holds the inside
box-out positions along the lane and no offensive shooter is crashing in, so the offensive-rebound
share is lower than off a live field-goal miss. Second, a free-throw scramble kicks the ball out of
bounds more than a normal rebound battle, so Roll M carries an **out-of-bounds pair** with no analog
in Roll I.

Seven arms, every one routing to an already-existing node (Roll M opens **no new stub and no new
`ContinuationKind`**):

- **DefensiveRebound** → terminal, a transition start to the defense carrying the `FreeThrowRebound`
  context (Roll J selects its conservative pie).
- **OffensiveRebound** → continue to the offensive-rebound node (Roll K), stamped with the
  `FreeThrow` source.
- **LooseBallFoulOnDefense** → the shared charge-and-fork (the fifth feeder after D / I / J / K):
  charge the defense, then sideline-inbound below the bonus or free throws in it.
- **LooseBallFoulOnOffense** and **OutOfBoundsOffOffense** → terminals, dead ball to the defense at
  Roll A, no foul charged. Same routing, different reason label.
- **OutOfBoundsOffDefense** → continue to the sideline-inbound node, no charge and **no fork**.
- **JumpBall** → the shared arrow node.

### Reuse via context tickets, not duplicated logic

The design rule is *surface variety via parameterization, not new roll types*. Roll M owns no
shooting or transition logic of its own; it hands the ball to **Roll K** (an offensive board) or
**Roll J** (a defensive board pushing the other way), each of which grew a **second weight set
selected by a labeled context ticket** — the same ticket/station pattern as Roll C's turnover
context. A station stamps the tag at write time; the downstream generator reads it to pick a
parameter set and never queries the stamping station back.

- Roll K's `OffensiveReboundSource { LiveBall, FreeThrow }` rides on the offensive-rebound
  continuation. The FT set is more putback / less reset. **A null stamp reads as `LiveBall`**, so
  every legacy field-goal feeder (Roll I) is byte-for-byte unchanged — the new context is purely
  additive.
- Roll J's `TransitionSource` gained `FreeThrowRebound`, selecting a tamer run-or-not pie (more
  Settle, less Push: off a made/missed FT the defense had time to get back).

Both are **labeled tags rather than bools**, so a third source (a tip-in board, a steal) grows by
append. The two inputs stay independent: Roll M decides *which* board, the downstream pie decides
*what happens on it*, and neither fuses into the other.

### The OOB pair and the once-per-trip bound

`OutOfBoundsOffOffense` is `LooseBallFoulOnOffense` minus the whistle — identical routing (dead ball
to the defense), different label, no charge. `OutOfBoundsOffDefense` is the only sideline arm that
**never forks**: with no foul there is no bonus question, so it is always a plain inbound even when
the defense is in the bonus. This is the deliberate asymmetry with the loose-ball-defense arm, which
*does* fork.

Roll M **fires once per free-throw trip**. A missed putback off its own offensive board is a live
field-goal miss and re-enters **Roll I**, not Roll M — so Roll M introduces no new re-entrant loop,
and the possession stays bounded by the existing Roll K putback↔rebound convergence. Roll M charges
a foul only on its loose-ball-defense arm; like Roll I it takes `GameState` for that one arm and
reads nothing off it on the other six.

### Isolating the FT-loop check from a now-live downstream

When Roll M went live, `RollLFreeThrowCheck` — which validates the FT loop's exact spin bands and
its accumulation-free property — would have had its missed-final-FT branch flow downstream into
Roll M, whose foul arm charges and whose offensive-board arm spins Roll K, polluting both the spin
counts and the foul-free invariant. The fix is **unit isolation by pinning**: that check builds its
Roll M with a one-arm pie fixed to the clean `DefensiveRebound` terminal (the same technique
`RollKBonusForkCheck` uses with its `foulOnlyPie`), so a missed final FT terminates cleanly at the
rebound boundary exactly as the old stub did. Roll M's real distribution is `RollMReboundBatchCheck`'s
mandate, not this check's.

## Contextification arc: closing the open stubs as contexts, not new rolls (Session 20)

The possession-flow roll web is complete. From here, **every remaining open stub closes as
a CONTEXT on an existing roll — never a new roll.** A context selects a different pie
(weights may go to zero) but never changes where an outcome routes. Existing rolls are not
renamed or deleted; retired *stubs* are kept in the corner per the established pattern.
This is the spine of a five-session arc.

### The work order (build sequence)

1. **Transition output** — Roll J `Push` → the player-selection chain (Roll E),
   contextualized by a `FastBreak` marker. *(Session 20 — done.)*
2. **Block recovery** — Roll H `Blocked` → Roll M's loose-ball machinery under a `Block`
   context.
3. **Steal feeder** — live turnovers (Roll C intercepted/stripped-live + Roll K live-ball
   TO) → Roll J as a `Steal` source.
4. **Bonus-fork extract** — the charge-and-fork copied in D/I/J/K/M → one shared node.
5a. **Roll C expansion** — seat ALL turnover + violation types in Roll C, context-gated,
   DORMANT (nothing routes to them yet; validate in isolation).
5b. **Roll A reshape** — collapse Roll A to Successful inbound / Turnover / Offensive foul
   before inbound / Defensive foul before inbound / Jump ball; route its Turnover exit into
   the ready Roll C; fold the retained inbounds (ResumeInbound + SidelineInbound) back into
   Roll A.

**Order rationale.** Quiet branches first, the high-traffic inbound spine reshaped last (its
feeders settle first); the bonus-fork extract (#4) collapses five below-bonus landing sites
into one, thinning #5b before it runs.

### #1 — Transition output: the FastBreak marker

Roll J's `Push` (the possession decided to run) used to park at a dead-end stub. It now
routes into **player selection (Roll E)** — the same node `Settle` uses — so a fast break
produces a shot through the shared shot chain, tilted by a transition *context* rather than
a separate transition roll. `IntoTransition` and `TransitionStub` are retired and kept in
the corner.

**Distinguishing Push from Settle.** Both arms enter Roll J off a board, so both carry a
non-null `TransitionContext` — but that ticket records how the possession *started*, and is
non-null on a Settle too. It cannot express "we ran." The decision Roll J made is therefore
a distinct fact: **`bool FastBreak` on `PossessionState`** (default false). `Push` stamps it
true; `Settle` leaves it false. This is the `Continue.Putback` precedent applied to a new
edge — two pies on one `IntoPlayerSelection` edge, a single bit selecting between them —
chosen over a parallel `IntoTransitionSelection` ContinuationKind, which would be the
enum-explosion the engine deliberately avoids.

**State field, not Continue payload.** Unlike `Putback`/`Bonus`/`TurnoverContext` (transient
payloads consumed by the very next node), `FastBreak` must persist across hops because the
deferred Roll G / Roll H transition tilts read it later. So it lives on `PossessionState`
beside `SelectedSlot`/`ShotType`/`Result`. A single bool (one break flavor today); richer
break memory — numbers advantage, leak-out — appends later as a nullable field, no teardown.

**Roll E's context branch.** Roll E's generator reads `FastBreak`: true → the transition
selection pie, false → the flat halfcourt pie — the same context-selects-a-pie pattern as
Roll C/J/K. The transition weights are a non-flat placeholder (30/30/25/10/5) this session,
chosen non-flat *only* so selection is observable in the harness; the real speed/athleticism
favoring is the deferred attribute seam, which replaces the generator without touching Roll
E or the resolver.

**The leak guard.** The only edge that re-enters Roll E for a fresh play is Roll K's
`ResetOffense`. A pushed possession that misses and rebounds would carry `FastBreak=true`
into that reset and wrongly draw the transition pie, so `ResetOffense` clears `FastBreak`
alongside the shot facts — a reset is a fresh halfcourt play. `PutBack` routes to Roll H,
not Roll E, so it draws no wrong pie; the marker riding through a putback is harmless while
G/H are transition-blind, and whether a putback off a break counts as transition is the G/H
follow-up's call, not this session's.
