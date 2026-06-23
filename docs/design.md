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

> ⚠ SUPERSEDED (Contextification #6): Roll A is five-arm — see the correction at the end of this doc.

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

### #2 — Block recovery: the ReboundSource ticket, and Roll I as the field-goal loose-ball resolver

Roll H's `Blocked` arm used to dead-end at `BlockRecoveryStub`. A blocked shot is a
loose-ball scramble — the same event a missed-shot rebound is — so it now resolves through
the rebound machinery. `ContinuationKind.ResolveBlock`, the resolver's `ResolveBlock` case,
and `BlockRecoveryStub` are retired and kept in the corner.

**Home: Roll I, not Roll M.** The work order above says "Roll M's loose-ball machinery," but
Roll M is the *free-throw-board* resolver; a *field-goal* block belongs to the field-goal-side
loose-ball resolver, which is **Roll I**. The session prompt corrected the one-line plan
accordingly. The two resolvers stay parallel (same vocabulary, different board populations),
not merged.

**Reweight became a small arm-add — and why that is the right call.** Roll I had four arms;
Roll M seven. A four-arm pie cannot express a swat going out of bounds or a tie-up, so a pure
"reweight the four" would have nowhere to put a block's real outcomes. Roll I was therefore
grown to Roll M's **seven-arm shape** (`+JumpBall`, `+OutOfBoundsOffOffense`,
`+OutOfBoundsOffDefense`), and — the domain call — those arms are **live for normal misses
too**, not block-only: caroms off the rim go out, rebounders fumble the ball out, tie-ups
happen on any miss. The `Block` context is then a *reweight* of those seven. So #2 is an
arm-add on Roll I plus a context on top, larger than the one-line plan, deliberately.

**Why the OOB pair is distinct from the rebound arms.** A carom out of bounds off the offense
and a clean defensive rebound both give the defense the ball, but they start the defense's
*next* possession differently: the rebound is a **live** transition push (its own weights via
Roll J), the OOB is a **dead-ball** inbound at Roll A (its own weights). Likewise an offensive
rebound (live putback/reset via Roll K) vs. an OOB off the defender (offense restarts **dead**
from the sideline). Folding either OOB into its rebound twin would erase the live-vs-dead
next-possession distinction that is the entire reason to model it. Neither OOB is a turnover
(no possession was established); each only changes how the next possession begins. All seven
arms route to nodes that already exist — **no new stub is opened.**

**The ticket: `ReboundSource { LiveBall, Block }`.** A new optional `ReboundSource?` field on
`Continue` — the `Putback`/`OffensiveReboundSource` precedent. Stamped by Roll H's `Blocked`
arm, read by **Roll I's generator** to select the weight set, never queried back (the
ticket/station rule). A **labeled tag, not a bool**, so a third loose-ball source appends
without a teardown. **Null reads as `LiveBall`**: every legacy feeder (Roll H's `Miss`, a
missed putback re-entering Roll I) stamps nothing, so pie *selection* on the legacy path is
byte-for-byte unchanged. A block reuses the `LiveBall` offensive-rebound pie (Roll I stamps no
source onward to Roll K) and the `Rebound` transition context; distinct block flavors are
deferred (below).

**Edge reuse, not a new ContinuationKind.** `Blocked` emits `Continue(ResolveRebound) {
ReboundSource = Block }` on the existing `ResolveRebound` edge — one edge, a payload selects
the pie, the same shape as #1's `IntoPlayerSelection` carrying `FastBreak`. This is the
enum-explosion the engine avoids (no `IntoBlockRecovery` kind).

**Byte-for-byte deliberately broken at the output.** Because jump-ball and the OOB pair are
live on normal misses, the live-miss outcome *rates* shift slightly from the old four-way
split. This is the rebound model getting more honest, not a regression. Declaration order is
preserved (new members appended last), so the four originals keep their cumulative ranges; the
new slivers are what move the picture. Validation is rate-match against the new seven-arm pie.

**Roll I naming.** The class stays `RollI`; its prose generalized from "rebound resolution" to
"rebound / loose-ball resolution" to cover the blocked-shot and OOB entries. No rename churn.

**Deferred from this session.** (1) A **`TransitionSource.Block`** push rate — a block-and-go
runs differently than a board-and-go — is wired in **#3 (steal feeder)** alongside
`TransitionSource.Steal`; this session a block's defensive recovery reuses the `Rebound`
context. (2) A **distinct block offensive-rebound source** on Roll K (a tipped-in block may
putback differently than a clean board) is a later Roll K context; this session reuses
`LiveBall`. (3) `OutOfBoundsOffDefense`'s **own-side inbound modifiers** belong to the inbound
node and land with the **Roll A reshape (#5b)**.

### #3 — Steal feeder: live turnovers enter Roll J as a `TransitionSource.Steal`

The last placeholder transition feed. Three live-turnover arms — Roll C's `BadPassIntercepted`
and `LostBallLiveBall`, Roll K's `LiveBallTurnover` — emitted `PossessionConsequence.TransitionTo`
with a **null context ticket**, which the resolver temp-routed through Roll A. #3 turns the staged
routing on: those arms now carry a `Steal` context into Roll J, the live transition-entry gate.

**Promote, not add.** Every caller of the placeholder is a steal, so the helper was promoted in
place — `TransitionTo` → **`TransitionStealTo`**, carrying `TransitionContext.Steal`. No bare
null-context helper is retained: a transition with a null context is no longer produced by
anything, so a retained helper would be dead weight, not a retired stub. The three callers are
re-pointed; nothing else changes in those rolls (their pies and the turnover TYPES are untouched —
that expansion is #5a).

**The source append, not an enum-explosion.** `TransitionSource` gains a third value, `Steal`
(parallel to `Rebound` / `FreeThrowRebound`), with a `TransitionContext.Steal` static. The
generator gains a Steal branch returning a third weight set; Roll J's **five arms and their
routing are unchanged** — the Steal pie reweights the same Settle / Push / Turnover / DefensiveFoul
/ JumpBall arms. The resolver's transition-entry guard gains `or TransitionSource.Steal`, so a
steal-born possession enters Roll J via the same `Generate(ctx)` path the other two sources use.
This is the "many feeders, one node" discipline applied to a source: the value, its pie, and its
routing arrive together.

**The pie intent — a steal runs hardest.** Off a live theft the break is already on, so the Steal
pie leans hardest to Push and lowest to Settle of the three transition contexts: **Steal Push >
Rebound Push > FreeThrowRebound Push**. Placeholder weights, spread deliberately wide (Steal 0.50 /
Rebound 0.30 / FreeThrowRebound 0.08) for easier later calibration — larger gaps make the
direction of any tuning nudge obvious. The Rebound and FreeThrowRebound Push values were widened
from their #1/#2 seeds (0.25 → 0.30, 0.12 → 0.08) as part of that spread; all remain tunable in
`config.json` with no engine change. The real speed/athleticism favoring ("who got the steal") is
the deferred attribute seam; Roll J reads no attributes yet.

**The dead-path tripwire.** Once all three live arms carry `Steal`, every transition consequence
stamps a recognized source (`TransitionReboundTo` / `TransitionFreeThrowReboundTo` /
`TransitionStealTo`), so a `Transition` entry can never legitimately reach the resolver's legacy
(Roll A) branch. The else now **throws** if a `Transition` entry arrives without a recognized
source — a loud wiring-bug tripwire rather than a silent halfcourt-route. It costs one line and
fails fast exactly when a future change would otherwise quietly break the "every transition stamps
a source" invariant.

**The `Block` wall (deferred again).** `TransitionSource.Block` is NOT added. A block's defensive
rebound shares Roll I's single `DefensiveRebound` arm with the normal-miss path, so emitting a
Block transition context would force Roll I's **routing** to read the `ReboundSource` — which the
generator consumes, not the roll — crossing the clean generator-eats-source / roll-eats-pie seam #2
just built. Each steal arm statically IS a steal, so it has no such problem. Block tempo gets its
own design conversation later; a block's defensive recovery reuses the `Rebound` context for now.

**What #3 closes.** Every live-ball possession start — defensive rebound, free-throw-board rebound,
and now steal — carries a real context into Roll J. No placeholder transition feed remains. The arc
moves to #4 (collapse the charge-and-fork copied verbatim in Rolls D / I / J / K / M into one
shared node).

### Contextification #4 — Bonus-fork extract: one shared `DefensiveFoulCharge` node (Session 23)

**What was de-duplicated.** The non-shooting-defensive-foul charge-and-fork existed in five copies,
each written as its roll was built ("copied, not reinvented," deliberately, to avoid premature
abstraction): inline in Roll D's `Execute`, and as a private `ResolveFoulOnDefense(state, game)` in
Rolls I, J, K, and M. All five did the same three steps — charge the foul to `state.Defense` via
`FoulTracker.Increment`, read `FoulTracker.BonusFor`, and fork on the bonus. With five live copies
the shape was proven and stable, so #4 collapsed it into one definition. This was a PURE refactor:
byte-for-byte-identical `Continue` at every caller, no rate moved, no route changed.

**The two audited divergences (the reason the careless one-line plan is wrong).** (1) Roll D had no
helper — its fork was inline, so the extract deleted FOUR helpers and replaced ONE inline fork, not
"five private copies." (2) Roll D alone carried a `Flavor` payload on its `Continue` (both arms),
and its below-bonus kind was `ResumeInbound`; I/J/K/M carried `Bonus` only and used
`ResolveSidelineInbound` below bonus. In bonus, all five were identical (`ResolveFreeThrows` +
`Bonus`). A confirmed-on-pull clarification, byte-identical rather than a divergence: all five
already set `Bonus = bonus` on both arms (below-bonus `bonus` is `None`), so the shared node always
sets it.

**The node.** `Core/DefensiveFoulCharge`, a `public static class` with
`RollResult Resolve(PossessionState state, GameState game, ContinuationKind belowBonusKind,
FoulFlavor? flavor = null)`. It is cross-roll infrastructure that reads `GameState.Fouls` and
returns a `Continue`, sitting in `Core/` beside `FoulTracker` and `JumpBall` (the established
static-`Resolve` precedent) rather than parked inside any one roll. Logic: charge `state.Defense`,
read the bonus, fork — in bonus → `ResolveFreeThrows`; below bonus → `belowBonusKind`; stamp
`Bonus = bonus` always and `Flavor = flavor` (null when unsupplied).

**Two knobs stay caller-owned, on purpose.** The below-bonus continuation kind and the flavor are
parameters, never hardcoded — because the five feeders genuinely differ on them and unifying either
would be a behavior change wearing a refactor costume. Roll D passes `ResumeInbound` + its rolled
flavor; I/J/K/M pass `ResolveSidelineInbound` + nothing. The below-bonus kind encodes a real
basketball distinction the role-based engine carries as "which inbound" rather than a court
coordinate: a pre-shot Roll-A/Roll-B foul below bonus resumes the inbound; a live-action foul
(rebound, transition push, FT board) below bonus goes to a sideline throw-in. Both the optional
flavor and the caller-supplied kind are the seams that future work (per-foul-type weighted
descriptors; court-side-aware inbound weighting) plugs into without touching this node.

**Wiring.** Five callers, one `DefensiveFoulCharge.Resolve(...)` call each; four private helpers
deleted; Roll D's inline fork replaced by a tail call that keeps its flavor roll. `ResolveFoulOnDefense`
appears nowhere in engine or harness after the extract.

**Why it is safe.** The five pre-existing per-roll fork checks are unchanged and constitute the
correctness proof — each exercises its caller and asserts the produced `Continue`, so identical
routing through the new node = success. A direct unit check (`DefensiveFoulChargeCheck`) proves the
node itself: both below-bonus kinds, with and without flavor, across the foul-count climb, asserting
charge-to-defense-only, the below/in-bonus split, the `Bonus` payload on both arms, and the flavor
pass-through. The node is now the single place fouls cross the bonus, so the Governor accumulation
check (§2a) is the end-to-end guarantee.

### Contextification #5a — Roll C expansion: every no-shot loss seated, context-gated and DORMANT (Session 24)

**What it establishes.** Roll C becomes the single canonical home for EVERY way a possession is lost
without a shot — all turnover types and all violation types. #5a SEATS the full set, gated by
context, but DORMANT: declared and resolvable, zero weight in every live context, nothing routing to
them. Proven in isolation. #5b reshapes Roll A and wires its loss exit in, turning them live. The
split keeps the expansion behavior-neutral and independently provable before anything depends on it.

**One enum, one pie per context.** A possession is lost exactly one way, so a single draw over one
expanded `TurnoverOutcome` picks the single loss type. Ten members are APPENDED after `OffensiveFoul`
— append order is load-bearing: a zero-weight slice does not advance `Pie`'s cumulative walk, so the
same draw maps to the same outcome it did before (a same-seed 5-vs-15-member parity trace confirmed
per-draw identity for the five legacy types). Seven new turnover types (`Travel`, `DoubleDribble`,
`Carry`, `ThreeSecondViolation`, `FiveSecondCloselyGuarded`, `OffensiveGoaltending`,
`BackcourtViolation`) are dead-ball with deferred (null) elapsed. Three violation types
(`ShotClockViolation`, `FiveSecondInbound`, `TenSecondBackcourt`) are dead-ball but stamp INVARIANT
elapsed (30 / 0 / 10) — the only timed arms in Roll C. Defensive goaltending is deliberately excluded
(it awards the basket → a Roll H make/miss variant, deferred).

**The Pie-forces-zeros consequence.** `Pie` walks every enum member and throws on any omission, and
validates sum-to-1. So "dormant" cannot mean "absent": every new member must appear at `0.0` in the
Halfcourt and Transition dicts, and `RollCConfig` (and the `"RollC"` config.json section) must carry
a backing field for each — in all three contexts — so #5b turns weights on by editing config alone.
The Halfcourt pie stays 30/22/18/20/10 and Transition stays 25/15/20/35/05, byte-for-byte.

**Invariant elapsed inside Roll C (the new wrinkle).** Every existing Roll C arm sets no
`ElapsedSeconds` (a turnover's duration has real variance, deferred to the future time roll). The
three violation arms are the exception: their elapsed is invariant and known here, mirroring Roll A's
violation terminals. `RollCConfig` gains `ShotClockViolationElapsedSeconds` (30),
`FiveSecondInboundElapsedSeconds` (0), `TenSecondBackcourtElapsedSeconds` (10) — dormant copies of
Roll A's values until #5b consolidates Roll A's terminals into Roll C and removes the duplication.
`RollC.Execute` gains an OPTIONAL `RollCConfig? config = null` parameter (mirroring the generator's
optional `context` default) so every legacy call site is unchanged; the violation arms read elapsed
through it and fail loud if reached without one. They are never reached on the live path (dormant), so
the resolver's existing `RollC.Execute(..., _rng)` call (config defaulting to null) is safe.

**The court-phase context scheme.** A third `TurnoverContext`, `EntryBackcourt`, seats the
post-made-basket / backcourt-start phase. Court phase gates which losses are reachable: Halfcourt is
the settled set (travel, over-and-back, 3-second, carry, closely-guarded, offensive goaltending,
frontcourt shot-clock — turned on in #5b); Transition is the outlet/push (unchanged); EntryBackcourt
is the bring-it-up phase (5-second inbound, 10-second backcourt, backcourt shot-clock, plus a bad pass
/ lost ball on the way up). Over-and-back lives in Halfcourt, not EntryBackcourt — it is only possible
once the frontcourt is established. The origin-dependent gating that selects the context per inbound
(made basket → 10-second + backcourt shot-clock reachable, over-and-back not; foul past halfcourt →
frontcourt start, no 10-second, over-and-back possible) is SEATED ready here and IMPLEMENTED in #5b.

**The dormancy / isolation discipline.** Every new type is `0.0` in both live contexts this session;
real (placeholder) weight lives only in EntryBackcourt and is exercised solely by `RollCExpansionCheck`.
That check proves the seated set in two parts: (1) drive `EntryBackcourt` directly — its weighted
members reachable at configured rates, its zero members unreachable; (2) a directly-built uniform pie
over all fifteen types lights up every arm (including the halfcourt-natural types zeroed in every live
context this session), asserting each is a clean terminal with the right consequence (dead-ball to
defense; steal only on the two pre-existing live arms) and the right elapsed (violations 30/0/10,
turnovers null), and that no new type leaks a steal. The three existing Roll C checks are untouched
and read identical (modulo additive `0.000` rows where a check iterates `Pie.Slices`, since `Pie`
stores a slice per member — every existing rate and pass/fail signal is unchanged).

**Known divergence and deferral (flagged per §0/§6c).** "All three regression checks byte-for-byte"
is literally true only for the two that reference the five existing types by name;
`RollCBatchCheck` (and `ShowSamples`' pie print) iterate all slices and gain additive zero-rows —
accepted, not a behavior change. Separately, `Pie.Roll`'s overflow fallback returns the last slice,
now a zero-weight appended type, so a draw within ~1e-16 of 1.0 in a live context could fall through
to it (≈ 1e-11 expected over a 100k batch — will not fire). Fixing it is a pie-mechanism change, out
of scope; logged as a deferral.

---

## Contextification #6 — Roll A reshaped, the halfcourt loss set live, and the chain closed

#6 completes the arc #5a seated. #5a made Roll C the canonical home of every no-shot loss, seated but
dormant. #6 turns the halfcourt set live, reshapes Roll A to its real outcomes, wires Roll A's loss
exit into Roll C by court phase, and closes the possession loop. (Forecast as "#5b" in the #5a notes;
shipped as #6 — the plan bent to the code, CONVENTIONS §6a.)

**Roll A's five outcomes.** `EntryOutcome` is now `CleanEntry`, `Turnover`, `OffensiveFoul`,
`DefensiveFoul`, `JumpBall`. The three former violation terminals are GONE from Roll A — a backcourt
violation is a way the possession is lost, and every no-shot loss belongs in Roll C, so they resolve
there via the Turnover exit's `EntryBackcourt` context. The old single foul slice split offensive vs.
defensive. Base weights (placeholders, sum 1): clean 0.88, turnover 0.08 (absorbing the old violation
mass, which now surfaces as TYPES inside Roll C's EntryBackcourt pie), offensive foul 0.0045, defensive
foul 0.0255, jump ball 0.01. Roll A no longer reads its config (the violation terminals were its only
readers); `cfg` is retained on `Execute`'s signature for call-site parity.

**The court-state marker.** `PossessionState.Frontcourt` (a single `bool`, default false) records court
phase: false = backcourt (bringing it up — 10-second count, backcourt shot-clock, 5-second inbound all
live), true = frontcourt (across, into the set — those backcourt-only losses unreachable). It latches
true the instant `CleanEntry` hands to Roll B and never flips back within a possession (the role-based
model has no spatial "return to backcourt"; over-and-back is a Halfcourt loss, not a court-state flip).
A re-inbound carries the current phase. This is the origin signal that selects Roll A's loss context.
A single bit suffices today, exactly like `FastBreak`; the finer spot-flip (the other team starting in
the frontcourt after a backcourt turnover) appends later without teardown.

**Context selection on the loss exit.** Roll A's Turnover arm stamps
`state.Frontcourt ? TurnoverContext.Halfcourt : TurnoverContext.EntryBackcourt`. A backcourt bring-up
routes to the EntryBackcourt pie (5-second inbound, 10-second backcourt, backcourt shot-clock, plus a
bad pass / lost ball on the way up); a frontcourt re-inbound routes to the Halfcourt pie (where those
backcourt-only violations are 0.0 and cannot happen). The Halfcourt pie is now the live 13-way
breakdown — 24/18/16/14 mains, 9 offensive foul, then travel 8, over-and-back 2, shot-clock 2.5,
3-second 2.5, double-dribble/carry 1.5 each, closely-guarded/offensive-goaltending 0.5 each. This one
pie governs EVERY halfcourt turnover (Roll A frontcourt re-inbound, Roll B, Roll F): a travel is a
travel whoever caused it.

**Invariant elapsed wired through the resolver.** With Halfcourt `ShotClockViolation` and the
EntryBackcourt violation arms now live, the resolver MUST pass `RollCConfig` to `RollC.Execute` so those
arms can stamp their invariant elapsed (30/0/10) — they fail loud without it. The resolver gained a
`_rollCConfig` field/param and passes it; all eight harness resolver constructions pass it too. This is
the consolidation #5a forecast: Roll A's violation-elapsed fields were removed and Roll C's
`*ElapsedSeconds` are now the sole source.

**Offensive foul: a deterministic loss terminal.** A new `ContinuationKind.ResolveOffensiveFoul` maps
in the resolver, with no pie, to `Terminal("OffensiveFoul", state, DeadBallTo(defense))` — identical
reason and consequence to Roll C's offensive-foul arm. A player-control foul yields no free throws and
no bonus charge. Keeping it a continuation kind (not a Roll A terminal) preserves "one node names the
loss" and gives the future offensive-foul flavor tag (charge / off-arm / illegal screen) a single home.
That tag needs a flavor field on the loss TERMINAL — terminals carry none today — so it is a separate
task; it matters because those flavors attribute to different players (the handler vs. the screener).

**The closed chain.** The two keep-the-ball inbound edges no longer park. `ResumeInbound` (Roll D
below bonus) and `ResolveSidelineInbound` (OOB-retained, and the I/J/K/M below-bonus loose-ball-defense
/ OOB-off-defense edges) RE-RUN Roll A carrying the current court-state, feeding the resolver loop
exactly like `IntoHalfcourtSet`. The resolver no longer stores the two inbound stub objects (fields and
assignments removed; ctor params retained only to keep construction sites stable; the harness builds
its own stub instances for direct fact-echo). With the violation terminals moved into Roll C and these
two edges re-entrant, the live chain parks NOWHERE — every possession resolves to a terminal. The
re-entry is convergent: `CleanEntry`'s dominant weight makes the inbound loop geometric (mean ≈ 1.03
hops), and a shared game's accumulating fouls cross the bonus, converting a re-inbound into a
free-throw trip that terminates — both landings handled, the iteration ceiling never threatened (§2a).

**Deferrals seated by #6.** Offensive-foul flavor (needs a terminal flavor tag); the backcourt-turnover
spot-flip; "easier + pressure-driven" re-inbound weights (the marker carries the distinction now, the
weights tilt later in the real generator); and the attribute-driven generators that replace every stub
pie.

---

## Session 27 — Offensive-foul flavor tag + backcourt dead-ball spot-flip

Two surface refinements deferred by #6. Neither changes any rate or opens any stub; both are additive
appends to existing structure.

**Offensive-foul flavor.** `OffensiveFoulFlavor { Charge, PushOff, IllegalScreen }` is theater
backfilled onto every `OffensiveFoul` terminal at the resolver's single chokepoint — the one `case
Terminal t:` where all three emitters (Roll C, Roll K, `ResolveOffensiveFoul`) converge. Two mixes,
selected by `t.State.Frontcourt`: frontcourt (30/20/50 — illegal screens dominate halfcourt set plays)
and backcourt (40/50/10 — screens don't happen before the ball crosses). `Terminal` gained
`public OffensiveFoulFlavor? Flavor { get; init; }` mirroring `Continue.Flavor`; null on every
non-offensive-foul terminal. The stamping is a `t = t with { Flavor = flavor }` mutation at the single
return site; Roll C and Roll K signatures are unchanged. Config: `OffensiveFoulFlavor` section in
`config.json` (front/back weight sets). Unblocks correct per-player attribution later: a charge
attributes to the ball-handler; an illegal screen attributes to the screener.

**Backcourt dead-ball spot-flip.** The rule: on a dead-ball turnover, the new offense inbounds from
wherever the ball already was. Lost it before crossing (Frontcourt==false) → the other team starts
already across, skip Roll A's bring-up → `BallAdvanced` entry → Roll B. Lost it after crossing
(Frontcourt==true) → normal dead-ball restart → `DeadBallInbound` → Roll A. `EntryType.BallAdvanced`
is the new enum value; `PossessionConsequence.BallAdvancedTo(team)` is the parallel static helper to
`DeadBallTo`. All 13 dead-ball arms in Roll C now use the conditional; `ResolveOffensiveFoul` in the
resolver likewise. `RunPossession` has a new `BallAdvanced` branch that drops straight into Roll B,
between the Transition branch and the legacy Roll A branch. Over-and-back self-handles: it is
Halfcourt-only (EntryBackcourt weight 0.0), so it always reads Frontcourt==true and never flips.
The Governor is unchanged — it already threads `Entry: consequence.NextEntry` onto the spawned
possession. Later: Roll B's pie odds can reflect the easier inbound situation (no full-court press
possible on a BallAdvanced inbound).

---

## Session 28 — Scoring: the resolver tallies points, the Governor accumulates the score

The first real numbers on the board. The score-write seam already existed (the Governor wrote `+= 0`
every possession); this session fills it with a real tally. Stub pies still drive everything, so the
scorelines are un-basketball-like — the machinery is proven, calibration waits for attribute generators.

**Where points are tallied: the walk, not the terminal.** Points have three sources, and only the
resolver's walk sees all three: a clean made field goal (a `Made` terminal, 2/3 by zone); an and-1
basket (a `MadeAndFouled` shot — a `Continue` into the shooting-FT node, NOT a terminal); and made free
throws (1 point each). The Governor cannot derive the full total from the final terminal alone — the
and-1 basket and the intermediate FT makes are invisible there — so the tally rides out on
`RoutingOutcome.Points`, the same reasoning that put `FreeThrowSpins` there. `Points` is a third walk
tally of the exact `PutbackAttempts` / `FreeThrowSpins` shape: init-only `int`, 0 default, pure append.

**The 2/3 rule lives in one place.** `Core/Scoring.FieldGoalPoints(ShotLocation)` → 3 for `Three`, 2
for every other zone (Long is a long *two*). A made free throw is always 1 point and is counted in the
FT driver, never here. The single-home discipline matches `JumpBall` and `DefensiveFoulCharge`.

**The three banking sites.**
- *Clean FG* — at the `case Terminal t:` return, `t.Reason == "Made"` banks `FieldGoalPoints(zone)`.
  `ShotType` is non-null (Roll G stamped it before Roll H could resolve a make).
- *And-1 FG* — at the `ResolveShootingFreeThrows` edge, `Result == MadeAndFouled` banks the basket's
  2/3. The edge is hit exactly once per shooting foul; `Result` distinguishes the and-1 (basket counts)
  from a fouled miss (no FG).
- *Free throws* — `DriveFreeThrows` counts every `Make` in its `Spin()` local (intermediate or last,
  each worth 1) and returns the count via a new `out int ftPoints`; both call sites (the bonus fork and
  the shooting-foul edge) add it to the walk's `points`.

**Accumulation and the offense-only invariant.** The Governor reads `outcome.Points` into
`pointsThisPossession` (replacing the literal 0) and credits it to the offense via the unchanged
Home/Away split — all points credit `state.Offense`, never the defense. `PossessionRecord` carries a
trailing `int Points` so the harness can verify per-possession. The harness proves the offense-only rule
*for free*: it accumulates `homePoints` / `awayPoints` keyed on `r.Offense` and asserts
`HomeScore == homePoints && AwayScore == awayPoints` — a point credited to the wrong side lands in the
wrong accumulator and fails the match. It also asserts the total matches and that points actually flow
(`> 0`), plus a deterministic FG-rule check.

**What stays out.** No clock (flat placeholders persist). No config knobs — points are *derived*
(2/3 + FT makes), so nothing was added to `config.json`. No new `ContinuationKind`, enum, or roll
signature; rolls C/H/K/L and every generator are untouched. The only engine files that changed are
`Resolver.cs`, `Governor.cs`, and the new `Core/Scoring.cs`. Realistic scorelines and per-player point
attribution are both deferred — the machinery reports whatever the pies produce and credits the team.

---

## Session 29 — The game clock: possessions draw elapsed per shot-clock period; Governor counts down two halves

**The clock model.** A possession's elapsed time is the sum of its **shot-clock periods**. The first
period runs the full 30-second clock; each offensive rebound that keeps the possession alive resets the
clock to 20 and starts a new period. Each period's duration is an **outcome-blind truncated-normal draw**
— centered by pace (a config stub today), bounded by `[floor, ceiling)`, where the exclusive ceiling is
what produces the natural skew (a fast center keeps its tail toward the cap; a slow center gets its tail
clipped and leans short). Two things override the draw: a terminal that already carries an **invariant
`ElapsedSeconds`** (shot-clock violation = 30s, backcourt = 10s, five-second inbound = 0s), and
**free throws, which draw 0** (the FT sequence is a clock-stopper; the live period up to the foul
already drew its time). The Governor counts each possession's elapsed **down** from `HalfSeconds`
(1200), runs two halves, and ends a half when the time is spent — the last possession's contribution
is **capped** at the time remaining so each half sums exactly to 1200.

**The architecture: draw lives in the Governor, count lives in the walk.**
- The resolver's `Route` walk **counts** shot-clock periods: `var shotClockPeriods = 1;` at the top
  (period 1 = the fresh 30s clock), incremented at every `case ContinuationKind.ResolveOffensiveRebound:`
  (one per offensive rebound, hit exactly once each). The count rides out on the new
  `RoutingOutcome.ShotClockPeriods` (fourth walk tally, same init-only `int`/0-default shape as `Points`).
- The Governor owns the actual draw via `DrawPossessionSeconds(periods)`: period 1 from `(Center, StdDev,
  Floor, FullClockSeconds)`, each reset period from the same distribution scaled by
  `ResetClockSeconds / FullClockSeconds` (≈ 0.667). `RollClockConfig` and `IRng` are injected into the
  Governor; the resolver's 8 construction sites are untouched.

**Why the split keeps the resolver clean.** Injecting a clock config + rng into the resolver would
touch all 8 harness constructor sites and create a new dependency for something the resolver does not
need (it routes; it does not accumulate time). The Governor already owns the stop rule and the
accumulation; the clock draw belongs there. The period count is the only piece the walk must supply
because the Governor cannot see offensive rebounds from outside.

**`Core/ClockDraw.cs` — the engine's first continuous roll.** A static helper implementing a truncated
normal via Box-Muller + reject-resample. The ceiling is exclusive (`x < ceiling`, not `x <= ceiling`) so
the upper tail thins smoothly and nothing piles at exactly the shot clock — the exact-clock case is the
shot-clock-violation terminal (invariant 30s), not a draw. An attempt guard after 100 rejections falls
back to the clamped center so no config can spin forever.

**`Config/RollClockConfig.cs` — every tempo number in one editable place.**
- `Center = 17.0` — the stub average for all teams; a future coach pace (1–10) attribute shifts this
  per team, pushing the draw distribution left or right.
- `StdDev = 4.5` — spread; controls how fat the tails are.
- `Floor = 4.0` — minimum possession length (can't shoot in under a few seconds).
- `FullClockSeconds = 30.0` — fresh shot-clock ceiling (exclusive).
- `ResetClockSeconds = 20.0` — offensive-rebound reset ceiling.

**`GovernorConfig` retired `SecondsPerPossession`; gained `Halves = 2`, `HalfSeconds = 1200.0`.**
`PossessionCap` repurposed as a safety ceiling (default bumped to 400); the clock is the real stop rule.

**`PossessionRecord` gains `double Elapsed` and `int Half`.** The harness uses these for the per-half
drain check, realized APL, and the tempo histogram.

**The tempo histogram is the tuning instrument.** The harness samples `ClockDraw` 100k times and prints
5-second bin counts, min, and max. Emmett reads this to see the bell shape, verify both tails are
present, tune `Center`/`StdDev`, and confirm the ceiling-exclusive contract (nothing at or past 30).

**Future seams seated by this session.**
- The coach pace (1–10) attribute plugs in at `RollClockConfig.Center` — no engine change, only the
  center value shifts.
- Per-outcome time shaping (turnovers shorter than makes) would go in `DrawPossessionSeconds` as a
  context parameter, but is explicitly deferred (creep risk).
- End-of-half hold-for-last-shot is the next clock session: a `halfRemaining < 30` branch in the
  Governor that probabilistically drains and then resolves (or misses the shot) rather than just capping.

---

## Session 30 — End-of-half intent: the Governor's clock-management branch

The possession engine now models what the offense does when it has less than a full shot clock left in
the half. This is a Governor-side decision, not a resolver or roll decision — the resolver resolves one
possession and knows nothing about the clock or the half. The Governor, which already owns the clock
countdown, is the only component that has `halfRemaining` in scope.

### The three intents and what each does

When `halfRemaining < EndOfHalfConfig.HoldThresholdSeconds` at the start of a possession, the Governor
draws from a flat `Pie<EndOfHalfIntent>` built once in the ctor from config weights:

| Intent | Weight | What the Governor does | Points | Elapsed |
|---|---|---|---|---|
| `HoldShootLast` | 70% (starting knob) | Resolver runs normally; elapsed **forced** to `halfRemaining` | real | `halfRemaining` |
| `ShootEarly` | 20% | Resolver runs normally; elapsed drawn the S29 way and capped at `halfRemaining` | real | S29 cap |
| `NoShot` | 10% | Resolver **not called**; possession synthesized | 0 | `halfRemaining` |

On all other possessions (`halfRemaining >= HoldThresholdSeconds`) the intent is null and the S29
base-clock path runs byte-for-byte.

### Why this is in the Governor, not the resolver or a new roll

The end-of-half intent is a **clock-management** decision, not a possession-resolution decision. The
resolver resolves one possession and knows nothing about the clock or the half; the Governor already owns
`halfRemaining` and the stop rule. Putting the intent draw in the resolver would violate the seam that
kept the resolver's 8 construction sites untouched in S29. Putting it in a new roll would create a roll
that gates on a single clock check and produces no new ball-state — a resolution node that resolves
nothing. The Governor is the right home for the same reason S29's time draw is in the Governor.

### NoShot is a third possession class (§2a discipline)

A `NoShot` possession is not a terminal (no resolver, no EndedOn) and not a stub park. This was the
load-bearing §2a check: any assertion of the form `terminalEnded + parked == records.Count` is wrong the
moment a NoShot can fire. The count assertion is now three-class:
`terminalEnded + parked + noShotCount == records.Count`, where `noShotCount` is counted from the records
directly via the new `PossessionRecord.EndOfHalfIntent` field. The §2b sweep confirmed this was the only
site of the old shape.

### The single-construction-site discipline preserved

NoShot synthesizes its variables (zero points, `applied = halfRemaining`, synthesized consequence and
label) and then **converges to the same `records.Add(new PossessionRecord(...))` call** every other path
uses. There is no second construction site. The prior `grep -n "new PossessionRecord("` returning exactly
one hit is still true after this session.

### The drain invariant (the load-bearing harness gate)

Both `HoldShootLast` and `NoShot` force `applied = halfRemaining`, driving `halfRemaining` to 0 and
always tripping the half boundary. `ShootEarly` and normal possessions cap at `halfRemaining` as before.
So the S29 guarantee holds for all intent values: each half sums to exactly `HalfSeconds`. The existing
`drainOk` check (per-half sum within 0.01) is unchanged and is the load-bearing gate — if the intent
ever leaked into the drain, this is where it would fail.

### Score-blind and tempo-blind by design; future seam is explicit

The flat pie knows nothing about the score margin, the score itself, or which team is ahead. The intent
depends only on the clock. Score-aware late-game tactics (leading team milks, trailing team races;
intentional fouling) are a future session where the intent becomes a **context-selected** pie — a
generator reading the margin and the time — and this session's flat weights become the context-neutral
fallback. The architecture is already shaped for that: a generator replaces the ctor's direct `Pie`
construction without touching the Governor loop, the resolver, or the rolls.

---

## Session 31 — The player object and Roster seam

### The player model arc (overview)

Phase 1 of a six-phase arc that replaces all stub pie generators with real, attribute-driven ones.
The arc is documented in full in `player-model-roadmap.md`. Phase 1 delivers the player object and
the slot-to-player seam; Phases 2–4 wire attributes into generators; Phase 5 builds the
shot-creation classifier roll; Phase 6 calibrates.

### The author-vs-derived line

Every attribute is one of four kinds (from `attributes.md`): authored individual (a raw 0–99 integer
typed per player), derived (computed by the engine, never authored), team-aggregate (authored or
derived per player, aggregated across the five on the floor), or modifier/amplifier (scales a family
of other attributes). Phase 1 implements all authored individual attributes and the four derived
attributes. Team-aggregates and modifier effects are deferred.

The derived values computed on `Player` (never stored, always recomputed on read):
- `Athleticism` — mean of Strength, Speed, Quickness, FirstStep, Vertical. The locked ceiling
  principle: athleticism caps how far skill can express against a given competition level.
- `Transition` — mean of Athleticism and Finishing. Derived-from-derived; dependency order is
  explicit so nothing cycles.
- `GravityContribution` — mean of Close, Mid, Outside, Finishing. Per-player input to the future
  team-aggregate gravity value. Dormant-pending-module.
- `SpacingContribution` — Outside rating. Per-player input to the future team-aggregate spacing
  value. Dormant-pending-module.

All four placeholder formulas are flat means. Phase 6 tunes weights and formula shapes.

### The Roster object (the almanac bridge)

`Roster` is a separate object — not inline on `Lineup` or `GameState` — because it is the thing that
persists into the dynasty/almanac layer. The historical archive holds rosters, rosters point at
players, players accumulate career stat lines. The sports-reference shape: player page → season logs
→ game logs, navigable because the roster is the bridge.

`GameState` holds `HomeRoster` and `AwayRoster` (constructed internally; no ctor signature change;
all 24 existing `new GameState(fouls)` sites unaffected). `RosterFor(side)` mirrors `LineupFor(side)`.
The seam the attribute generator will walk: `game.RosterFor(side).PlayerAt(slot)`.

The substitution model: slot is a stable seat for the whole game. A substitution appends a
`SubstitutionEntry(Slot, Player, AtPossession)` to the roster's log. Starters are logged at
AtPossession = 1. The current occupant is the last log entry for that slot. A player can occupy
different slots at different times (a returning sub goes to whatever slot is open); the log is the
source of truth for per-player attribution across any possession window.

### The config JSON authoring path

The `"Rosters"` section of `config.json` is the embryo of the dynasty save format. Every future
layer that writes or reads a starting lineup — the coach screen, the save file, the almanac — points
at the same JSON contract. `PlayerConfig` (the DTO) and `RosterConfig` (the loader) follow the
nested `GetProperty` pattern established by `RollHConfig`.

### Rating scale

0–99 integer. 99 is the ceiling; 100 is impossible. The 1:1 free-throw calibration note in
`attributes.md` (a 72-rated shooter makes ~72%) is a rough Phase-2 anchor for the bounded logistic
make-rate mapping. Phase 6 tunes the actual mapping.

### Phase 1 wall

No generator reads a `Player`. The seam exists and resolves end to end; nothing on the roll side
touches it. Rolls A–M, the Governor, and every stub generator are byte-for-byte unchanged by this
session. Phase 2 wires the first generator (own-attribute → own-pie, no matchup effects).

---

## Session 32 — Phase 2: The bounded logistic make-rate model (Roll H real generator)

Phase 2 wires the first real generator into the possession engine. The seam is
`game.RosterFor(side).PlayerAt(slot)` → `player.{ZoneAttr}` → `cfg.MakeProbability(zone,
rating)` → `Made` weight in Roll H's pie. Everything else in the pie (block carve, foul slices,
OOB pair, putback path) is unchanged.

### Zone → attribute mapping

`ShotLocation` names WHERE the shot comes from; the player attribute names the SKILL needed to
convert it. These are two different axes and naming both is what makes the mapping legible:

| Zone | Player attribute | Why |
|---|---|---|
| Three | `Outside` | Perimeter shooting skill |
| Long | `Outside` | Same — a long two draws from the same skill pool as a three |
| Mid | `Mid` | Mid-range skill |
| Short | `Close` | Floaters, runners, hooks — close-range conversion inside the paint |
| Rim | `Finishing` | Converting rim attempts; distinct from `Close` |

`Short` reads `Close`, not `Finishing`. The doc comment on `Player.Close` says "Converting
close-range looks (inside the paint, not at the rim)"; `Player.Finishing` says "Converting rim
attempts." The naming split is intentional and preserved.

### The logistic formula

`makePct = floor + (ceiling − floor) / (1 + exp(−k × (rating − midpoint)))`

The curve is **inflection-above-50**: slow crawl from rating 1 to 50, steeper gains through the
elite range, flattening near the ceiling. This intentional asymmetry means average-rated players
(50) produce make rates near the middle of the floor-to-ceiling range but slightly below midpoint,
elite players (85+) gain steeply, and the ceiling acts as an honest cap.

Parameters live in `RollHConfig` — nothing hardcoded in the generator — so every knob is editable
without touching any C# code. The `MakeProbability(ShotLocation zone, double rating)` method lives
on config (not on the generator) so the generator and the harness validation checks always call
the same formula.

### Make weight substitution (not a full-pie rebuild)

The logistic result replaces the `Made` weight only. The five non-Made, non-Blocked base weights
are **rescaled proportionally** to fill `(1 − block − makePct)`:

```
nonMadeBase  = BaseMadeAndFouled + BaseMiss + BaseMissFouled + BaseMissOutOfBoundsLost + BaseMissOutOfBoundsRetained
nonMadeShare = 1.0 − block − makePct
scale        = nonMadeShare / nonMadeBase
```

Each of the five weights is multiplied by `scale`. The seven-way pie always sums to 1 for any
`(zone, rating)` combination where `makePct ∈ [0, 1 − block]`, which the logistic guarantees when
parameters are well-formed. The `Pie` constructor's sum-to-one validation remains the loud guard.

The SHAPE of the non-Made outcomes (their relative proportions) is unchanged by Phase 2 — a high
make rate shrinks the non-made share, but MadeAndFouled still slightly dominates MissFouled, etc.
Changing those relative proportions is a future basketball call, not a Phase 2 concern.

### The interface pattern

`IRollHPieGenerator` with one method: `Pie<ShotResult> Generate(PossessionState state, bool putback
= false)`. The Resolver field is typed to the interface; both the stub and the real generator
implement it. This is the permanent pattern for all future real generators — stub implements the
interface, real generator implements the interface, Resolver holds the interface. No teardown when
the next phase builds another real generator; only the construction site changes.

The 12 harness sites that construct `RollHStubPieGenerator` directly for isolated checks are typed
to the concrete class and are unaffected — they never go through the Resolver's field.

### Fallback discipline

A null `SelectedSlot` is a wiring bug (throws). A null player (unpopulated roster) is a harness
convenience (falls back to stub pie). This asymmetry is intentional: a missing slot means something
upstream failed to run the selection roll, which is always a bug; a missing player means the harness
didn't call `SetStarter`, which is legitimate for the 12 isolated checks that existed before Phase 2.

The fallback ensures the isolated checks produce flat stub rates and their existing assertions pass
unchanged — no harness regression from the generator swap.

### Phase 2 wall (what is deliberately NOT here)

- No defender attribute. No matchup effects.
- No gravity, no spacing, no team aggregates. Phase 4.
- No axis math. Phase 3.
- No tendencies. Phase 5.
- No putback tilt by size/athleticism. Putback path stays flat. Phase 4.
- No other generator. Roll H only.

## Session 34 — Post-Phase-3 design capture: the matchup deep dive (2026-06-14)

**A design session, not a build (CONVENTIONS §4) — no code, no harness.** This records the design
locked *verbally* in the long conversation after Phase 3 (Session 33) shipped, so the next prompt is
built on the written record, not chat memory. The Session 33 **skill correction** lives in `axes.md`
(it amends that doc directly); everything else is captured here. All of it is concept, each item
gated to a later pass; nothing is wired.

### The skill correction (full text in axes.md)

Session 33 called skilled "the baseline, NOT a tilt." Corrected: skill is the baseline the shift
starts from **and** an active **skill-vs-skill contest**. Make/miss is the shooter's shooting against
the defender's defending; the effective-rating shift has **two sources** (physical matchup + skill
matchup), neither a separate roll. There is **no simulated "did he get open" step** — openness is the
*physical input* to the shift, not an event. Skill becomes the **whole signal** when the physical
battle is even (most D1 games). The counter-attribute **pairing map** (which offensive skill contests
which defensive one) is Phase 4. See `axes.md` → "Skilled — a baseline AND an active contest."

### The per-possession ceiling and floor (emergent, no new mechanism)

- Each pie spin tops out around college reality (~75–80% on the best looks); a single shot is never
  99%, so a possession's scoring odds are bounded above by the **curve's own asymptote** — no cap is
  imposed.
- **Offensive rebounds legitimately push a *possession* above the per-shot ceiling.** Each ORB is a
  fresh, distinct event, so they **compound** — the 7-footer who keeps tipping it back and scoring is
  correct, good compounding of distinct events, not a ceiling violation.
- **Floor:** the worst mismatch (a D3 team vs a champion) still scores something — ~20–25% / ~40 pts
  baked in — from busted coverage, run-outs, and lucky bounces the engine cannot simulate. Not an
  imposed floor; it is the **residue of events we do not model.** This same floor doubles as the
  **cold-streak hard bottom** — nobody goes to literally 0%.
- Both ceiling and floor are **emergent** from the curve plus distinct-event compounding. No new
  mechanism.

### Double-count discipline and the two-defender drive

- **Spend a gap ONCE per shot.** A single physical edge must not be charged into both the location
  pie and the make% as if it were two separate edges.
- **"Different faucets"** keeps it honest: different *matchups* source different doors.
- **The two-defender drive** makes it concrete: the **on-ball defender gates location** (beaten off
  the dribble → rim access), while a rotating help **big gates the finish** (make% / block / foul at
  the rim). Two defenders, two doors — not one gap double-charged.
- **Defender-ID resolves BEFORE location**, so the matchup is known in time to shape both doors.

### The expressed game is relative (opportunity widens attempts; skill governs results)

- A player's *attempted* game expands and contracts with the competition. Expressed game = latent
  skill × whether the gap lets him use it. A D1 role-player dropped to D2 starts launching threes;
  prime Shaq on a D2 team would handle the ball.
- **Opportunity widens *attempts*; skill still governs *results*** — which preserves player identity
  (he is not a different player, he just gets more rope).
- Same lever as the usage-choke, opposite direction: the athletic mismatch *chokes* the out-classed
  player's touches; the talent mismatch *widens* the dominant player's.
- Consequence: **roles are earned against the competition, not assigned.** Tendencies therefore
  cannot be fixed per-player numbers — they must emerge from the matchup. (A constraint on the future
  tendencies pass.)

### The competency floor (a player-generation constraint, later)

- College is a *selected* population: everyone can shoot OR defend OR pass OR has size/athleticism —
  the recruiting bar enforces a floor of competence.
- So a wide-open college guard hits ~30%+ from three at minimum. A bad rating shows up as **how far
  below the floor he drops as the look tightens**, not as "can't make an open one."
- This constrains the future **player-generation** layer (the curve's floor + how ratings
  distribute), recorded here so generation honors it.

### Combination — how the matchups become a game

Two halves, both resolved:

- **Between teams = two one-way battles.** A's offense vs B's defense, and B's offense vs A's
  defense — separate. Each resolves ~130 trips; the sum is the game. The axes are **never weighted
  against each other by a formula** — the **scoreboard is the common currency.** The "round robin"
  is **sparse**: each attack meets its natural counter (this attacker vs his defender), not an
  all-vs-all cross product.
- **Within a team = coverage.** Strength = how many fronts you hold serve on; weakness = a front you
  lose badly. **"No weakness beats one peak"** falls directly out of this (a team with no losing
  front beats a team with one elite front and three soft ones).
- **Pace/tempo is NOT a separate dial.** It lives in **turnovers + defensive rebounds:** a fast team
  feeds its break through steals and run-outs; a strong, ball-secure team starves the break by not
  turning it over and by ending possessions on the defensive glass → forcing halfcourt. Tempo
  *emerges*; it is not set.
- **Roster composition sets VARIANCE, not just average.** A volume-three team is a coin-flip / upset
  machine (high variance); a rim-attack team is a steady favorite (low variance). The same average
  can carry very different variance — which is where upsets and bad-games-for-good-teams live.

### Defender identification — a weighted roll (Phase 6 build)

- Who guards whom at the shot is a **weighted roll**, not a fixed slot map. Weights encode scheme
  (man / zone) and switch variability; base weights do the matching (perimeter D on perimeter, bigs
  on bigs) without rigid slot-vs-slot lockstep.
- **v1 premise = positional pairing** (the engine already has on-court slots). Cross-slot
  **mismatch-hunting** (deliberately attacking the weak defender) is a later build-pass elaboration,
  not v1.
- **Sequencing is load-bearing and must not be reordered:** Phase 4 designs the **gap function**
  (defender ratings → modifier); the **picker** (who the defender is) is built later in Phase 6. The
  gap function assumes a defender exists; the picker supplies him. Function first, picker second.

### Variance and streaks — hot/cold meters, governed by usage

- The independent-rolls model under-produces real-life variance (a flaw surfaced earlier). The fix
  revisits the hot/cold idea once talked out of — now *justified* by that flaw.
- **Per-player hot/cold meters** aggregating to a team feel; only **real runs (3+)** trigger — normal
  scatter is ignored, so it does not become noise.
- **Usage is the self-correcting thermostat.** Hot → fed more → more attention → exposed → cools
  (+ turnover risk). Cold → fed less → cleaner looks for others → warms. This converts a dangerous
  snowball into negative feedback.
- **CAUTION (load-bearing):** the self-correction is a **tuned** relationship, not automatic. The
  cold side is more dangerous than the hot side and needs a **hard floor** (the per-possession floor
  above). The whole governor **depends on the deferred usage layer** — at flat-usage v1 the streak is
  an *unregulated snowball*. **Do not ship the streak mechanic before its usage governor exists.**
- **Alternative / complement:** a per-game **"shooting weather"** draw carries variance with **no
  feedback loop** — safer. Streak for *feel*, weather for the *variance dial*. Likely both.

### Usage ↔ efficiency, and the two-pie strategic game

- **Usage and efficiency are inverse** — the load-bearing regulator. Pile volume on a player and his
  efficiency falls (more attention, harder shots); *how fast* it falls is the player's signature.
- **The two-pie game:** the offense allocates a **usage pie** (who shoots); the defense allocates an
  **attention pie of 100** across the five offensive players. They collide through the make-odds.
- **KEY UNIFICATION — attention is the source of "openness."** Openness = a player's gravity minus
  the attention spent on him. This ties the previously-parked **gravity/spacing** dial into the same
  machinery: attention is *not* a new make% input, it is the team-level half of the openness input
  the skill section already named.
- **Superstar, defined mechanically:** a **flat usage-efficiency curve** (stays efficient at high
  volume) **+ outsized gravity** (draws over-allocation). You can pile both usage and attention on
  him and he still rolls well — and the attention he vacuums off the other four feeds *their* looks.
  Multiple stars force the defense to either flatten attention (pure matchups, conceding the stars)
  or overload (gambling the lightly-guarded others get cold rolls).
- **CAUTION:** this depth exists only with **intelligent, adaptive** allocation; static dumb AI gets
  exploited. The usage-efficiency curve is THE regulator and must stay calibrated.

### Coaching AI and the tactics rating

- **Imperfect adaptivity is partly a FEATURE.** Coaches have rigid identities, recruit-to-style, and
  brute-force their strengths; the relative model lets a real edge **leak through a counter**
  (dampened, not erased), so a coach can impose his game without perfectly reacting. Rigid coaches
  struggle vs certain team types and **top out** at a level — realistic content, not a bug.
- **Dependency:** "rigid coach struggles vs certain teams" is only good *content* if the user can
  **see** the rigidity (scouting / legibility) and build or play to exploit it. The payoff rides on
  scouting being real.
- **The tactics rating drives ALLOCATION quality, NOT a flat make-boost.** Two traps, both rejected:
  (a) computing the *optimal* allocation per game/lineup (the swamp — uncomputable for every dynamic
  matchup); (b) a **flat make-success boost** (an *absolute* "good coach = your shots fall more" dial,
  the back-door difficulty knob the engine forbids). The threadable middle: run a **simple adjustment
  heuristic** — attention follows threat and drifts toward whoever's hot; usage flows to where
  attention isn't — and let the **tactics rating scale how *sharp and prompt* that heuristic runs**
  (high = quick, pointed; low = slow, blunt). The difference shows up **through the pies** (the
  relative model resolves it), never as a free bonus. Tactics = adjustment quality, working through
  the model, not around it.

### Fatigue / endurance (its own future session)

- An **endurance attribute** sets each player's **stamina drain rate**; falling stamina reduces
  effectiveness. The engine must accommodate **iron-men** who play 36–37 minutes (they exist), and
  stamina costs more for **bigger, heavier** players.
- **Design hook to bank:** fatigue should drain the **physical / athletic axis first**, not nuke
  everything flat — a tired great shooter keeps his touch but his legs go (his three flattens); a
  tired defender cannot stay in front. Run the sag **through the existing matchup machinery** (tired →
  athletic axis dips → beaten more → worse looks) rather than a separate flat penalty. "Bigs tire
  faster" and "iron-men exist" both fall out of per-player drain rates. Same discipline as the
  tactics rating: through the model, not around it.
- A **large, standalone session.** It also gives the roster its center of gravity (depth, the bench,
  minutes as a resource) and feeds late-game variance.

### Game-state awareness (a future modifier layer)

- The engine does not yet know the **score or clock** in its possession behavior. Wanted: state-aware
  modifiers — down big late → more threes, faster pace, more pressure, end-game fouling; up big late
  → milk clock, avoid fouls, take the safe two. This is where comebacks and white-knuckle finishes
  live.
- **Discipline:** game-state is **not a new system** — it **bends the pies already present** (shot-mix
  toward threes, the possession/pace count, foul tendency in the hack zone). Score and clock are just
  another set of inputs reweighting existing levers. The **end-of-half intent** branch (Session 30) is
  the existing seam this extends.

### Closed question: shot-quality degradation (recorded so it is not re-raised)

Considered and **rejected as a separate mechanism.** "A good defense forces a *worse* shot" is
already fully covered by the two existing doors: it is the **shot-mix** pie reweighting toward worse
zones, and/or the **make%-tilt** lowering the odds on the shot taken. A separate within-possession
"clock winds down, option 1 taken away, then 2, then a heave" sequence would be exactly the
event-by-event simulation the architecture rejects; its *outcome* (more desperation shots vs elite
defense) already emerges from the mix reweight. **No new lever — scratched.**

### Deferred seams (captured open, deliberately NOT resolved)

Resolving these now would design layers ahead of the current one — the cascade trap that killed the
Python builds. Logged for their proper pass:

- **Attention × matchup combination** — how the team-level attention contribution and the
  individual-matchup contribution combine into the one make% shift (attention is a deferred layer;
  combining them now is premature).
- **The streak governor** — the usage layer the hot/cold mechanic depends on; streak cannot ship
  before it.
- **Generation floors** — the competency floor and the relative-expressed-game constraints land on
  the future **player-generation** pass.
- **Phase 4 scope — RESOLVED at prompt-draft time (the split is decided):** the matchup math is
  split along the one-duel / whole-roster seam. **Phase 4 = the individual-matchup tilt** (laddering
  attributes → axes + counter-attribute pairings + the per-matchup gap function — everything to turn
  one attacker-vs-defender into pie tilts). **Phase 5 = the roster strength-read** (the non-linear
  coverage math alone: no-weakness-beats-one-peak, diminishing returns, credible-beats-elite).
  **Phase 6 = the build** (defender-ID picker + wiring matchup into the generators). The 4th-axis
  (experience/cohesion) neutral stub seam rides along in Phase 4. Canonical phase ladder as of this
  session: 3 fingerprints (done) → 4 individual tilt → 5 roster read → 6 build. (This supersedes the
  older "Phase 5 = tendencies" forward-reference in the Session 32 wall, which is left as-is per the
  append-only rule; the newest entry is canonical.)

## Session 35 — Phase 4: the individual-matchup tilt (2026-06-14)

**A design session, not a build (CONVENTIONS §4) — no code, no harness.** Phase 4 turns **one
attacker against one defender** into a set of pie tilts: the attribute → axis laddering, the
counter-attribute pairing map, and the per-matchup gap function. The spec lands in `docs/axes.md`
(new "Phase 4" section, completing its Dependencies block; Settled/Open lists updated); this entry is
the conversational deep-dive behind it. Everything is structure and direction — **no magnitudes** —
and still unwired (the picker and the generators are Phase 6). The locked two-layer spine from the
post-Phase-3 conversation (the Session 34 capture) is the input; this session resolved the open
structure on top of it.

### The two-layer shape, confirmed and made concrete

The make door — and every door — is **baseline → physical nudge → defensive (skill) nudge**, all
effective-rating shifts that **sum** on the one shared curve (`MakeProbability`, never touched; its
output % never multiplied). Emmett's framing of the flow: **athletic/size sets the scene first** (how
open is everyone if the game were resolved on physics alone — equal → near-flat), the coaching layer
leans on that scene (deferred), and **skill goes to war inside it.** "Physical first" is intuition; the
arithmetic is a single additive sum. Zero gap = **average** defense = flat (not an open shot).

**The double-count line, settled.** Offensive skill enters **once** — as the baseline (the shooter's
own touch, where he starts on the curve). The defender's defense enters **once** — as a nudge anchored
at average defensive stature (a great defender slides him down, a weak one up). The physical openness
gap slides him along that scale. No attribute is charged twice: touch is the scale, the defensive
rating is the nudge, the physical gap is the slide.

### Laddering — the trait assignments (the anti-double-count crux)

The doc had a live contradiction: `axes.md` equated the **athletic axis** with *derived athleticism*
(strength + speed + quickness + first step + vertical), while the resolved horizontal/vertical
principle wanted **vertical** and **strength** expressing through **big.** Resolved:

- **Athletic axis = horizontal separation only:** speed, quickness, first step.
- **Big / size axis = vertical reach + mass:** height, wingspan, weight, **strength, vertical.**
  Emmett's organizing read: *does the player's size and the heights he can reach disrupt the
  opponent's skill advantage?*
- **Derived athleticism stays the FULL composite as the locked ceiling** — a separate object from the
  athletic matchup axis, so the axis never re-counts the composite's parts. (Same name, two objects:
  the axis is the separation read; the composite is the skill-expression ceiling.)
- **The defensive-side overlaps resolved by the same principle as the offense:** a named defensive
  rating (perimeter D, post D, rim protection, defensive rebounding, steals) is the **skill layer
  only** — technique, timing, discipline, anticipation. Its physical underpinning (quickness for
  steals, length for rim protection, size / strength for the glass) lives in the **physical axes**,
  read off the raw physicals — never the same trait twice. So perimeter D does not secretly re-count
  the quickness the athletic axis already used.
- **Modifiers ride duels, not axes:** hustle → the effort family (glass, steals); IQ → the decision
  family (creation, playmaking). **Discipline → the foul pies** directly (not an axis member). **Free
  throw is matchup-immune** (Roll L's flat 1:1).

### The counter-attribute pairing map (the per-door duels)

One offensive-skill baseline vs a specific defensive counter, over a physical scene, on each door:

- **Make %** — touch (by zone) ↔ a **defensive gradient** that slides with distance: Outside ↔
  perimeter D · Mid ↔ **perimeter D + post D** · Close ↔ **post D + rim protection** · Rim / Finishing
  ↔ rim protection · Post moves ↔ post D. The mid-range pairing was Emmett's call: a 3/4-type mid-range
  scorer punishes a small defender in the post *or* drags a post defender out to challenge his foot
  speed, so a switchy (perimeter+post) defender is the counter. ("Help" dropped out of the duel once
  post carried the mid-range — help is a team aggregate anyway.)
- **Shot location / mix** — self-creation + ball-handling (control) ↔ perimeter / on-ball D; physical =
  athletic separation, big (strength → inside). **Self-creation gates shot *type*:** Emmett's framing —
  a great-touch player with no self-creation is limited to catch-and-shoot and entry-to-position looks
  (shots needing little beyond getting to the spot); self-creation is the craft to manufacture his own
  look off the dribble and to fight on-ball pressure. Consequence: a low-self-creation scorer leans on
  team scaffolding (gravity / spacing / passing) to get open; a self-creator is self-sufficient — which
  is exactly the job those team aggregates do (a forward hook, parked).
- **Turnovers** — ball-handling (security) + passing ↔ steals; physical = athletic (hands / quickness).
- **The glass** — offensive rebounding ↔ defensive rebounding; physical = big primary, athletic
  secondary (motor / second jump).
- **Blocks** — carved off the top before make resolves; finishing / shooter size ↔ rim protection;
  physical = big (length).
- **The tip** — wingspan / reach; physical = big (the `JumpBall` seam).
- **Transition** — athletic owns efficiency; frequency is coaching (deferred).

Parked as team-aggregate / interaction, not duels: passing's creation make-bump, gravity, spacing,
help D, off-ball D, screening, playmaking's create-for-others, off-ball movement's real counter.

### The gap function — additive, convex, lean on the curve

- **One convex primitive.** Accelerating gap → shift (power / exponential on the signed gap):
  near-flat at a marginal gap, steepening, cartoonish at a mismatch. No imposed cap — only the curve's
  asymptote / 100%. Reused everywhere; per-axis and per-pairing weights parameterize it. Steepness
  knob, **steeper for physical than skill** (the asymmetry in the shape).
- **Two outputs:** curve pies → an effective-rating delta into `MakeProbability`; categorical pies → a
  slice reweight of the same signed magnitude.
- **The gap split:** physical gap **per-axis** (athletic, big, compounding); skill gap **per-pairing**,
  anchored at average defense.
- **Composition is additive, with NO separate recovery / clawback term.** This was the session's
  sharpest resolution. Emmett pressed the floor case: a 5'6" poor athlete who can shoot, against a
  6'6", 6'10"-wingspan, stronger / quicker / bigger athlete — his shooting is "virtually obsolete, he
  won't get open," yet over a long game he still hits a few (he squirts free, the defender falls);
  athleticism "can't extinguish that." And the converse: when athleticism + size are insurmountable
  they can "wipe out all skill gaps." The clean structure delivers **both residuals without a bolted-on
  term:**
  - **Skill lives in the baseline** (always present, scales with skill) — pure athleticism never zeroes
    it; a monster physical nudge drags a high baseline down hard but it is still *his* high baseline.
  - **The make-curve's own floor + flattening:** a monster physical edge crushes a player toward the
    floor, where make% never hits 0 *and* the make%-distance between him and a scrub **compresses** —
    athleticism "wipes out the skill gap" in the box score while the skilled player still edges the
    scrub and hits a few.
  - **The convex physical nudge:** at an extreme gap it swamps even a high baseline (**size
    insurmountable**); at a small gap it is ~0 so the skill duel is the whole signal (**skill decides
    the even game**). Tuned steep enough, an extreme gap outruns the rating scale — "size can't be
    completely defeated" is the *shape*, not a cap.
  - Under all of it: the **per-possession floor** (the unmodeled-events residue — squirts-free /
    defender-falls), skill-agnostic. A residual-floor knob can exist for calibration headroom; it is
    not structurally required.
  - **Additive beats multiplicative `gap × (1 − recovery)`:** one mechanism, double-count-free, and
    "skill decides the even game" falls out for free (physical = 0 → net = skill); the multiplicative
    form needs a second additive skill term for the even case.
- **The 4th-axis stub:** experience / cohesion enters as a **hard-zero-gap term** — the signature slot
  exists, contributes nothing until the persistence layer feeds it.

### Worked check — 5 elite athletes (low skill) vs 5 elite skill (low athletes)

Falls straight out of the model, each side with its benefits and sacrifices:
- The **athletes** live at the rim with high finishes and own the glass (the athletic + big physical
  nudges), and shoot poorly from range (low touch baseline, no physical help out there) — so they hunt
  the rim, where physics helps most.
- The **skill** team cuts turnovers (handling baseline beats the steal pressure), lifts each other's
  looks through the passing bump (team aggregate), and brings real shooting to every roll — paying for
  it on the glass and at the rim.
- The **zero-athlete elite shooter still shows up in the box score** — his high baseline + the curve
  floor + the passing bump keep him scoring, exactly as he would in real life. The engine cannot
  extinguish skill from athleticism alone.

### Scope walls held

No roster coverage / strength-read (Phase 5). No defender-ID picker, no wiring (Phase 6). No team
aggregates, no strategy / tendencies / usage / fatigue / game-state. The out-athleted player's
**volume choke** ("never shoots 20 times") is the **usage / cascade** lever — deferred; Phase 4 owns
only the per-shot tilt. No magnitudes. `MakeProbability` untouched.

## Session 36 — Phase 5: the roster strength-read (2026-06-14)

**A design session, not a build (CONVENTIONS §4) — no code, no harness.** Phase 5 is how the **five
individual matchups** across a roster (Phase 4's per-attacker-vs-defender outputs) combine into team
strength: the non-linear coverage math alone — *no weakness beats one peak*, *credible beats elite*,
*composition sets variance*. The spec lands in `docs/axes.md` (new "Phase 5" section; the Open "non-linear
strength read" item moved to Settled); this entry is the conversational deep-dive behind it. Everything
is structure and direction — **no magnitudes** — and still unwired (picker + generators are Phase 6).

### The crux that framed the whole session

Phase 4's gap → tilt response is **convex and explicitly does NOT saturate** (axes.md ~270): a bigger
edge on one front keeps paying. So "no weakness beats one peak" and "credible beats elite" **cannot**
come from the gap function flattening — it doesn't. The saturation that reverses it lives **elsewhere,
and it is already in hand:** the gap → tilt feeds the **make-curve, which is a bounded logistic and
does** saturate. The per-possession *payoff* of an edge caps even while the rating edge climbs. That one
observation is the spine of Phase 5 — everything else is its consequences.

### Q1 — where the non-linearity lives: it EMERGES, never computed

Resolved: the non-linearity lives in the **sparse five-matchup sum on the scoreboard**, not in a profile
aggregation the engine consults and not in a coverage formula. Worked in plain terms and confirmed:

- A game is **five one-on-one battles, each resolved ~26 times, added up.**
- **A won front is capped** (an open shot is not 110%; frequencies cap at 100%). **A lost front bleeds
  every trip.** Asymmetric ends.
- A one-peak roster **wins one capped front and loses four bleeding ones** — four leaks drown one
  fountain. The balanced roster that holds serve everywhere wins the four trades.

Consequence for the **no-scalar wall** (the easiest locked principle to violate): there is no
team-strength number *to* violate it — it is **never built.** The radar / team-axis profile is a
**derived descriptor** for intuition and legibility, never an input the resolver reads. Tightest possible
satisfaction of the wall.

**Emmett's counterweight call.** Emmett immediately named the force that pushes back: a superstar good
enough to **bend the defense** draws so much attention (gravity) that his four teammates get easier looks
than their own matchups would earn — "less than full man-to-man." Correct and true to life — and
correctly **out of scope**: that lift is the **gravity / attention / team-aggregate layer** (one player's
pull changing *other* players' looks), hard-gated on the deferred attention machinery. Kept in a separate
drawer so coverage and the gravity lift can push against each other naturally instead of being
pre-blended. Flagged loudly in the spec; not folded in.

### Q2 — diminishing returns: two kinds wearing one name, only one ours

The HARD CONSTRAINT's "diminishing returns *and/or* coverage" resolved as **two distinct ideas**, not one
mechanism viewed twice:

1. **Over-investing in a front you already win** (a better star, or a second star on the same matchup)
   yields less and less — the **same coin as the cap.** Comes free here, no extra knob.
2. **Spreading elite talent across positions** (does the fifth great athlete add as much as the first?)
   — in the pure five-matchup picture he *does* (he wins a fifth front the team would otherwise lose).
   The reason he adds less in reality (five rim-attackers crowding the paint, five shooters past the help
   point, one ball) is **spacing / gravity / usage crowding** — the **parked** team-aggregate / usage
   layer.

So Phase 5 needs **no separate diminishing-returns mechanism.** Confirmed.

### Q3 — "credible" and "front held": the curve's middle, not a threshold

Not a line we draw — a region of the **make-curve already in use.** The top **caps** (elite → dominant is
wasted surplus). The bottom has a **soft floor and flattens** (the Phase-4 residual — broken plays, the
defender slips, garbage looks — so a badly-beaten man still hits a few, and the make%-gap to a scrub
compresses; losing a lost front *worse* barely moves the number, but you pay it every trip). The
**responsive action is the middle**, where most matchups live. "**Credible**" = still in that middle, not
pinned to the floor; "**front held**" = not shoved to the bottom. We never pick a credibility magnitude —
it is the **natural bend of the curve**, reused. This is *why* credible-everywhere beats elite-in-one-spot.

### Q4 — composition → spread: the shot diet, refined twice by Emmett

Confirmed: the spread rides the **shot diet the matchups already produce**, expressed through the existing
pies — **not** a variance dial, **not** the deferred streak governor. Two Emmett refinements sharpened it:

- **"Not swingy — low odds that sometimes hit."** A three-heavy diet is genuinely **lower-odds**, not the
  same average with more bounce. The rim team is both more efficient *and* steadier. The three team
  competes on **volume** and by **winning the other battles** (rebounds, turnovers → extra possessions →
  more cracks, independent of the jumper falling) — which are just more of the five-matchup picture, and
  the categorical fronts swing whole possessions.
- **"It's what they try to do to win — and it's a *good* strategy with the personnel."** Not two fixed
  identities (steady team vs swingy team). The real variable is **how much a team must lean on the
  lower-odds shot with nothing underneath.** A team with real shooters that *also* wins the glass and the
  defensive fronts is **covered** — cold night, still in the game; low spread, high quality (a sound,
  even dominant, build). The genuine **upset machine** is the *three-or-nothing* team that has no other
  way to score. This is the coverage picture again, applied to variance: *do you hold the other fronts
  when the shots aren't falling?*

Parked here: the **strategic choice** of how many threes to take (strategy layer); the
**usage-concentration** kind of variance (leaning on one or two scorers — usage layer); the **streak**
mechanic (wall).

### Q5 — the Phase-6 seam

Phase 5 is the coverage math over a **given** sparse round-robin — it sums whatever five matchups it is
handed. **Who guards whom** (the defender-ID picker, including later mismatch-hunting) is **Phase 6**, the
mirror of Phase 4's "assumes a defender is given." Nothing in the coverage read secretly needs the picker.
Function first, picker second. Confirmed.

### Q6 — legibility: read off results, never a meter

No team-strength number to show, so the read **telegraphs through outcomes:** no-weakness-beats-one-peak
shows as a star's huge line *in a loss* with the other four outplayed by their counterparts ("carried in
one spot, bled everywhere else"); the balanced team as nobody's line jumping off the page but everyone
holding serve in a grind-out win; the upset machine across a **season** as blowout wins / ugly losses /
wide scoring range (and runs inside a game); the steady favorite as tight, consistent margins. The user
infers strength the way a real coach does — from results.

### Scope walls held

No team-strength scalar, no coverage formula (it emerges). No defender-ID picker, no wiring (Phase 6). No
team aggregates — the **gravity lift** and the **across-position diminishing returns** stay parked in their
own layer. No strategy / tendencies / usage / fatigue / game-state; the **streak governor is deferred** and
the **usage-concentration** variance with it — the spread here is the static shot-diet kind only. No
magnitudes. The **make-curve and the Phase-4 gap function are untouched** — the cap and floor that do the
coverage work are already inside them.

## Session 37 — Phase 6: the matchup wiring (make-door vertical slice) (2026-06-15)

The first build of Phase 6, and the first time two players' attributes meet on the court. Phases 4–5
*designed* the matchup (the attribute → axis laddering, the pairing map, the convex gap, the coverage
math); this session wires **one door** — the make door — end to end, leaving every other door
matchup-blind. It is deliberately a vertical slice: prove the seam (picker → gap → effective rating →
make-curve) on the single highest-value door, with a named, swappable picker and a pure matchup
primitive, so the remaining doors and the mismatch-hunting picker drop in without re-architecting.

### The pieces

**DefenderPicker (v1, slot-guards-slot).** The contesting defender is the player in the same slot number
on the defense side (the selection roll stamps an offense-side `SelectedSlot`; the matched defender is
that number on `Defense`). The pick is deterministic and currently has a single consumer (the make door),
so it is **derived at generate-time, not carried on `PossessionState`** — the leanest seam that still
isolates the concept. It is a distinct, named unit precisely so the eventual mismatch-hunting picker is a
drop-in replacement. **Flagged promotion:** the moment a second door consumes the defender, or the pick
becomes non-deterministic, it must be promoted to a `PossessionState.DefenderSlot` stamped once after
Roll E so every door in a possession shares one coherent pick.

**Matchup (the primitive).** Pure and static. Four pieces:
- `OffenseRating(zone, player)` — the zone→skill map (Three/Long → Outside, Mid → Mid, Short → Close,
  Rim → Finishing). This is now the **single source** of that pairing: RollHGenerator's old private
  `RatingFor` was deleted and the generator's baseline read delegates here.
- `DefenseRating(zone, defender, cfg)` — the **CONF-1 per-zone defensive blend**: a weighted read of the
  defender's three defensive attributes (PerimeterDefense / PostDefense / RimProtection) that slides
  perimeter → interior across the five zones. A blend, not one attribute per zone, so a two-way defender
  is rewarded everywhere and a rim-protector-only big gives up his weak perimeter share at Mid/Long.
  Weights are config data.
- `GapFn(gap, steepness, exponent, scale)` — DEC-5 (below).
- `EffectiveRating(zone, attacker, defender, cfg)` — DEC-2 composition: `baseline + skillShift +
  physicalShift`, where the skill shift runs the (baseline − blended-defense) gap through `GapFn` with the
  skill steepness/exponent and the physical shift runs the (attacker − defender Athleticism) gap through
  `GapFn` with the physical steepness/exponent. The shifts are summed onto the baseline and the result is
  fed to the **unchanged** make-curve. A contest is a shooter sliding up or down the shared scale — never
  a reshaping of the curve.

### DEC-5 — the gap function is a signed power law

The one design act of the session. axes.md's Phase 4 gave the *properties* of the gap response but never
an equation; pinning the functional form was a design decision, not a build choice, and was settled
conversationally before any code. The form:

> `shift = steepness · sign(gap) · (|gap| / scale)^exponent`,  exponent > 1.

It is the only simple family that satisfies every property axes.md demands **simultaneously**:
- **Odd / signed.** An even matchup yields exactly zero shift; a skill or size disadvantage shifts down by
  the same law it shifts up. The *asymmetry* of real basketball (a beaten man still scores; the floor)
  lives in the **make-curve**, not here — so the gap function itself stays clean and symmetric.
- **Flat-bottomed.** exponent > 1 forces zero slope at the origin, so a marginal edge is imperceptible and
  the effect only becomes real as the gap opens. This is the property that **rules out** the obvious
  alternatives: `exp(|g|) − 1` and any linear form both have a non-zero slope at the origin (a one-point
  edge would already register), and a concave / ≤ 1 exponent would make small edges *over*-matter.
- **Convex / accelerating, and uncapped.** The effect grows faster than the gap. There is **no asymptote
  in the gap function** — the make-curve's logistic ceiling/floor is the *only* bound on the payoff, so
  the saturation lives in exactly one place (the curve), consistent with the Phase-5 coverage math.
- **Physical steeper than skill.** Implemented as `physicalExponent > skillExponent`. Steepness in the
  *tail* (not a larger constant) is what encodes "**size is insurmountable**" — a large athletic gap runs
  away faster than a large skill gap — while the make-curve's floor independently guarantees "**skill is
  never extinguished**" (the baseline carries the skill, and the floor keeps even a badly-beaten skilled
  shooter above a scrub).

**`referenceScale` is a unit, not a magnitude.** It is the gap (in rating points) at which a shift equals
its steepness — a fixed, legible denominator that keeps the steepness parameters identifiable (move it
rarely; tune steepness/exponent in calibration). Placeholder defaults: skill exponent 2.0, physical
exponent 2.7, both steepnesses 6.0, scale 25.0. These are best-guess scaffolding; the calibration pass
owns the real numbers, and `MatchupConfig.Load` enforces only the **invariant** (exponent > 1, scale > 0),
not any particular magnitude — a deliberate, small deviation from `RollHConfig.Load`'s no-validation
pattern, justified because an exponent ≤ 1 silently breaks the convex/flat-bottom contract.

### DEC-6 — two distinct fallbacks, not one

The make door has **two** independent "no real matchup" paths, and they must not be conflated:
1. **Unpopulated roster** (no shooter in the selected slot) — the pre-existing path: the generator
   short-circuits to the stub pie before any matchup logic. Unchanged this session.
2. **Shooter present, defending slot empty** (the defender lookup returns null) — the **new** guard: the
   make% reads the **raw own-rating** via `OffenseRating` with no matchup term, exactly reproducing
   pre-Phase-6 behaviour. This is the only fallback Phase 6 added.

### Why the make-curve is untouched

The bounded logistic from Phase 2 already maps a single effective rating to a make%. Phase 6 changes *what
rating* is fed in (own → matchup-adjusted), never the curve. This is the load-bearing reuse: the cap and
the floor that do the Phase-5 coverage work (a won front caps; a lost front bleeds but never zeroes) are
already inside the curve, so the matchup layer only has to produce the right effective rating and let the
curve do the saturation.

### Validation

A new `Phase6MatchupWiringCheck` provides the §4 calibration evidence. The sweeps read make%
analytically (`MakeProbability` over `EffectiveRating` — exactly the weight `BuildRealPie` assigns to
Made), so they are deterministic; the DEC-6 fallback is exercised through the **real generator** (batched)
so the picker, the null-defender guard, and the pie build are covered end to end. Every assertion matched
an independent Monte-Carlo pre-check to the decimal: the defender sweep monotone down with even ==
baseline and the edge compressing toward the floor, not zero; the Mid blend's two sub-attributes equal and
swap-symmetric; a rim specialist beatable on the perimeter but strong at the rim; the shooter sweep rising
and flattening under the ceiling; physical steeper than skill at equal gap; and the empty slot reading raw
rating while a strong defender lowers the sampled make rate. The full 100k chain and Phase 2 still pass —
Phase 2's high/low shooter gap actually *widens* under wiring (the convex skill gap amplifies the edge).

### Process note

The session opened on a near-complete interrupted build sitting uncommitted on disk: the three new files,
the RollHGenerator edit, and the config edit were all present and correct, but `Program.cs` referenced a
`Phase6MatchupWiringCheck` that had never been written (the interruption fell between adding the call and
writing the body — the harness would not have compiled). The fix was to author that one method against the
confirmed-current tree, re-verify (brace balance, defined-once / called-once, no stale `RatingFor`
references anywhere, all generator sites 3-arg), and only then deliver and — after the harness went green —
write these docs. The build was validated before a word of documentation was written (CONVENTIONS §3).

### Scope walls held / deferred

Make door only — location, turnovers, glass, blocks, and the tip remain matchup-blind. No
`PossessionState.DefenderSlot` (the picker is derived at generate-time; promotion is flagged). No
athletic/big axis split — one physical gap on the full Athleticism composite (the Phase-4 axis split is
deferred). No team aggregates, no gravity lift. No magnitude hunt — placeholders throughout, calibration
owns the numbers. One placeholder explicitly needs a basketball call: the Rim blend split
(Post 0.35 / RimProtection 0.65), flagged in both the config and the code.

---

## Phase 7 — The block door (Session 38, 2026-06-15)

The second matchup door. Phase 6 made the **make %** matchup-aware by sliding the shooter along the
existing per-zone make-curve. Phase 7 does the analogous thing for **block weight**: the configured
per-zone baseline (Rim 12%, Short 6%, Mid 3%, Long 2%, Three 1%) bends toward a per-zone ceiling when
the defender has the edge and toward a per-zone floor when the shooter does. The make curve, the
gap-function primitives, and the defender picker are all reused untouched — Phase 7 only adds new
weights, a new physical composite, and one new method.

### What the block contest is, conceptually

A block is not the negation of a make. It depends on **different physical attributes** than the make
door. Height, wingspan, and vertical leap are what put a hand on the ball; quickness and strength matter
for a contest but don't matter for *whether the shot is blocked at all*. So Phase 7 introduces a
block-specific **length** composite (Height + Wingspan + Vertical, equal weights by default) that is
separate from the make door's full Athleticism composite (Strength + Speed + Quickness + FirstStep +
Vertical). The asymmetry is intentional and *not* to be unified — the two doors read different physical
signals.

The skill side of the block contest, however, reuses the make door's reads exactly: the same per-zone
offense attribute (Three/Long → Outside, Mid → Mid, Short → Close, Rim → Finishing) against the same
CONF-1 per-zone defensive blend (Three: perimeter only; Mid: 50/50 perimeter/post; Rim: 35/65
post/rim protection; etc.). A rim protector blocks more rim shots; a perimeter defender contests more
threes; the blend table is shared.

### The bend formula

Two contributions, additively composed and then per-zone weighted:

```
skillShift  = GapFn(defense_blend − offense_skill, skillSteepness, skillExponent, scale)
lengthShift = GapFn(defender_length − shooter_length, physicalSteepness, physicalExponent, scale)
totalShift  = skillWeight·skillShift + lengthWeight·lengthShift   // per zone
```

Both shifts use the same `GapFn` from Phase 6 — same steepnesses, same exponents, same reference scale.
The block contest does **not** introduce a new gap function. What it does introduce is **per-zone
contest weighting**: the relative importance of skill vs length depends on where the shot is from.

- **Rim:** 40% skill / 60% length. Length is the physical anchor — a wingspan advantage matters more
  here than a rim-protection rating delta.
- **Three:** 40% skill / 60% length (Emmett's anchor). The contest is dominated by reach; the perimeter
  defender's positional skill is real but secondary.
- **Mid, Short, Long:** interpolated between (0.50/0.50, 0.45/0.55, 0.42/0.58 respectively).

Each pair sums to 1.0 (enforced in `Load`). These are placeholders — the magnitudes are calibration work.

### Saturating toward floor and ceiling

The total shift then bends the configured per-zone block weight via a tanh saturation:

```
bw      = baseBlockWeight[zone]
span    = (totalShift ≥ 0) ? (ceiling − bw) : (bw − floor)
bend    = span · tanh(totalShift / BlockReferenceShift)
result  = bw + bend
```

`tanh` is the key: it's odd (zero gap → zero bend, even matchup → exact baseline), bounded in (−1, +1)
so the result *asymptotes* toward ceiling or floor but **never crosses**, and smooth so there's no
discontinuity at any shift magnitude. `BlockReferenceShift` is the saturation knob — a net total shift
of that many rating points gets you to tanh(1) ≈ 76% of the way from baseline toward the asymptote.
Default 20.0; calibrate later.

`span` is asymmetric by direction: defender-edge bends use the headroom toward the ceiling; shooter-edge
bends use the headroom toward the floor. This is what lets the **same formula** produce both directions
without crossing baseline at zero shift.

**Floors are non-zero.** Even a peak shooter against a stiff is occasionally blocked — Rim floor 4%,
Short 2%, Mid 1%, Long 0.5%, Three 0.3%. Ceilings encode "what an elite defender can credibly do":
Rim 30% (Emmett's anchor), Three 4%. The whole table is placeholder; the shape is the spec.

### The DEC-6 fallback (carried from Phase 6)

The block door inherits the same two-tier fallback the make door uses:

1. **Unpopulated roster** (no shooter) → `BuildStubPie` reads `_cfg.BlockWeight(zone)` directly, no
   matchup logic invoked. Unchanged from pre-Phase-7.
2. **Shooter present, defending slot empty** → the new path. The caller checks for a null defender; if
   null, the configured baseline is used (no call to `Matchup.BlockWeight`). The block-rate read
   collapses to pre-Phase-7 behaviour. This is enforced as an exact-equality test in harness check (d).

### The one design call: the tanh sign

The session's design prompt specified the bend as `result = bw + (shift ≥ 0 ? bend : -bend)`. The
intent was symmetric — defender edge bends up, shooter edge bends down. But because `tanh` is already
odd, `bend` is *already* negative when `totalShift` is negative; the `-bend` in the spec's negative
branch re-flipped it back to positive, producing the wrong direction. The fix is to drop the conditional
entirely: `return baseBlockWeight + bend`. The asymmetric **span** selection still happens (different
headroom toward ceiling vs floor); `tanh` handles the directional sign on its own.

This was caught at the Monte-Carlo pre-check stage on check (e), the shooter-edge symmetry test, before
any code was delivered. Per CONVENTIONS §0, the spec contradiction was surfaced to Emmett rather than
silently overridden. The build then went green on the first harness run.

### Why this doesn't reshape the make door

The make door reads `makePct × (1 − block)` (Session 37.5's carve-then-convert). When Phase 7 varies
`block` per matchup, the make-curve's output is still the **conversion rate given the shot is not
blocked** — semantically unchanged. The observed make rate shifts slightly because the non-block
headroom `(1 − block)` now varies per shot, but the relationship `observed-FG% ≈ curve × (1 − block)`
is the same one the calibration pass will fit. Harness check (f) confirms an elite finisher vs a weak
rim protector still produces a valid pie and an 88.1% make rate — relationships intact.

### Validation

Six sub-checks in `Phase7BlockDoorCheck`, all deterministic (`Matchup.BlockWeight` is pure, no batching
needed for the analytical checks):

- **(a)** Rim defender sweep monotone rising; bounded by `[floor, ceiling]`.
- **(b)** Three defender sweep monotone rising; *spread* much smaller than the Rim sweep's spread
  (proves per-zone weighting is registering — Three is closer to baseline because both its ceiling and
  its block rate are smaller).
- **(c)** Skill-only delta vs length-only delta, at both Rim and Three. Length delta exceeds skill
  delta at both zones (the 60/40 split, observed: Rim 9.7% vs 4.2%; Three 1.6% vs 1.4%).
- **(d)** DEC-6 fallback: empty slot == configured baseline; even matchup == baseline (exact
  arithmetic, zero shift); strong defender > baseline + 0.005.
- **(e)** Shooter-edge symmetry at Rim: block rate falls monotonically as shooter improves, stays above
  the floor. The check that exposed the spec's sign bug.
- **(f)** Regression — Phase 6's make door still produces valid pies with the new block weight
  threading through `BuildRealPie`'s carve.

### Scope walls held / deferred

Block door only — OOB rates, shooting-foul rates, and the rebound pie remain matchup-blind. Putback
contests still use the flat config block weight (Phase 4 work — no defender slot is carried into the
putback path yet). `PossessionState.DefenderSlot` is **not** promoted — Phase 7 is the second
matchup-aware door but reuses the same picker, single consumer, derived at generate-time; the
promotion bar is "a second *independent* consumer," not satisfied yet. The Athleticism/Length composite
asymmetry is intentional, flagged in the code comments so the watchdog and future sessions don't try to
"unify" them.

## Phase 8 — The foul door (Session 39, 2026-06-15)

Phase 8 makes the shooting-foul rate matchup-aware (the third Roll H matchup door, after make% in Phase 6
and block weight in Phase 7) and simultaneously fixes the zone-flat foul-rate problem: a 7% foul rate
regardless of zone was basketball-wrong (three-point shooters almost never draw shooting fouls; rim
attacks draw them constantly).

### The new attribute: FoulDrawing

`FoulDrawing` (0–99) is the skill of generating shooting fouls — going up strong, initiating contact,
selling the call. Emmett's basketball call: **low FoulDrawing is NOT a skill**; it is absence of
opportunity. A catch-and-shoot wing who doesn't draw fouls isn't "bad at it" — he's just not in
foul-drawing situations. The model encodes this asymmetry via per-zone floor/ceiling positions (narrow
downward range, wide upward range), not via any special-casing of the attribute itself.

### The contest — offense-dominant, asymmetric weights

Two attributes participate in the foul contest:
- **Shooter: `FoulDrawing`** — the dominant signal (weight 0.80).
- **Defender: `Discipline`** — a light tap (weight 0.20).

Both are expressed as deviations from an `AttributeMidpoint` (default 50.0), so an average player
contributes zero:

```
contestValue = OffenseFoulWeight × (shooter.FoulDrawing − midpoint)
             − DefenseFoulWeight × (defender.Discipline  − midpoint)
```

One global weight pair governs all five zones. Per-zone variation in foul *impact* lives entirely in
the per-zone floors and ceilings — not in the weights. This is Emmett's explicit call against per-zone
weight pairs.

**No physical anchor.** Unlike Phase 6 (Athleticism) and Phase 7 (Length), the foul contest has no
physical term. The correlation between size/strength and foul-drawing lives in attribute *generation*
(a strong post player earns a high `FoulDrawing` rating because of how he plays), not in the contest
itself. Strength and similar attributes were explicitly rejected from this formula.

### The math — same GapFn, same tanh, different inputs

```csharp
var shift   = GapFn(contestValue, cfg.SkillSteepness, cfg.SkillExponent, cfg.ReferenceScale);
var ceiling = cfg.FoulCeiling(zone);
var floor   = cfg.FoulFloor(zone);
var span    = shift >= 0.0 ? (ceiling − baseFoulRate) : (baseFoulRate − floor);
var bend    = span × Math.Tanh(shift / cfg.FoulReferenceShift);
result      = baseFoulRate + bend;   // plain addition — tanh supplies the sign
```

Foul-drawing IS a skill contest, so the call reuses `GapFn` with the skill steepness and exponent —
the same parameters the make door uses. The tanh saturation shape mirrors Phase 7 (plain addition, same
Session 38 lesson: tanh is already odd and supplies the sign; a conditional flip would reverse the
direction). `FoulReferenceShift` (default 20.0, same as `BlockReferenceShift`) is the saturation knob.

### Per-zone baselines, floors, and ceilings

The asymmetry — **narrow downward range, wide upward range** — encodes Emmett's basketball insight:

| Zone  | Floor | Baseline | Ceiling |
|-------|-------|----------|---------|
| Rim   | 0.17  | 0.20     | 0.35    |
| Short | 0.075 | 0.10     | 0.18    |
| Mid   | 0.035 | 0.05     | 0.10    |
| Long  | 0.02  | 0.03     | 0.06    |
| Three | 0.008 | 0.015    | 0.04    |

An elite foul-drawer can push the rim rate from 20% to 35% (15pp upward range). An elite disciplined
defender can only push it from 20% to 17% (3pp downward range). The harness asymmetry check confirmed
up_bend (6.8%) > 3× down_bend (0.09%) at Rim.

### The and-1 split (MafFraction) — per-zone, not matchup-aware

When a foul is drawn, a per-zone `MafFraction` splits it into `MadeAndFouled` (and-1, shot went in)
and `MissFouled` (two-shot trip). This split is **not matchup-aware** — Emmett's call. It is a pure
configuration:

| Zone  | MafFraction |
|-------|-------------|
| Rim   | 0.35        |
| Short | 0.28        |
| Mid   | 0.18        |
| Long  | 0.12        |
| Three | 0.10        |

Rim fouls become and-1s often (a layup finishing through contact). Three fouls rarely do (the shot is
disrupted). All placeholders; calibration owns the magnitudes.

### The carve math — extending Phase 7's carve-then-convert

Phase 7 carved block off the top. Phase 8 carves block AND foul:

```
block           = matchup-aware block weight  (Phase 7)
foul            = matchup-aware foul rate     (Phase 8)
nonBlockNonFoul = 1 − block − foul

made            = makePct × nonBlockNonFoul                  // clean unblocked-unfouled conversions
maf             = foul × MafFraction(zone)                   // MadeAndFouled
missFouled      = foul × (1 − MafFraction(zone))             // MissFouled
nonMadeShare    = nonBlockNonFoul − made
scale           = nonMadeShare / (BaseMiss + BaseMissOOBLost + BaseMissOOBRetained)
```

`Miss`, `MissOutOfBoundsLost`, and `MissOutOfBoundsRetained` scale to fill the remainder. The pie sums
to 1 by construction for any (makePct, block, foul) triple where block + foul < 1. Max possible at Rim
(block ceiling 30% + foul ceiling 35% = 65%) is well below 1; an overflow guard throws explicitly if
`nonBlockNonFoul ≤ 0` (defense in depth).

`BaseMadeAndFouled` and `BaseMissFouled` were retired from `RollHConfig` — their jobs are now done by
`FoulRate(zone) × MafFraction(zone)` and `FoulRate(zone) × (1 − MafFraction(zone))`.

### Stub and putback paths

`BuildStubPie` (unpopulated roster) and `BuildPutbackPie` (offensive rebound putback) apply the same
carve-then-convert math using the configured per-zone `FoulRate` and `MafFraction` directly — no matchup
term, because these paths have no defender data. For putback (always Rim), `FoulRate(Rim)` and
`MafFraction(Rim)` are used.

The preexisting `RollKPutbackPieCheck` in the harness needed updating: it previously compared the putback
pie's slices against the old flat `Putback*` config values; after Phase 8 those slices are computed by
the carve formula. The expected values in the check were updated to match.

### Scope walls held / deferred

Shooting fouls only. OOB rates, block weight, make %, shot location, rebounding all remain matchup-blind.
Individual player foul tracking ("foul trouble") requires a personal foul ledger that doesn't exist yet
(only team fouls via `FoulTracker` are tracked). Per-zone foul contest weights were explicitly rejected
by Emmett — one global offense-dominant pair; zone impact lives in floors/ceilings. Physical anchor
(Strength) was explicitly rejected — correlation with size lives in attribute generation. `PossessionState.
DefenderSlot` not promoted (three matchup-aware doors but all reuse the same single-consumer picker).

## Phase 9 — The shot-location door (Session 40, 2026-06-15)

Phase 9 makes Roll G matchup-aware: the shot-zone distribution a player attacks reflects both
**who he is** (his authored per-zone tendencies) and **what the defense looks like** (the
defending team's collective per-zone resistance). Before this session every shooter received
the same flat zone mix. After, Cory Baptiste (high `ThreeTendency`) attacks the arc more than
Javon Okafor (high `RimTendency`) even against the same defense, and both adjust toward the
zones where the specific defense they're facing is weakest.

### Why shot location reads the whole defense, not the slot-matched defender

The make, block, and foul doors are duel-shaped: one shooter vs his matched defender. Shot
location is different. The shooter is reading the *entire defense's deployment* before he
decides where to attack — he goes to the rim because the rim protector is already committed,
or he pulls up for a mid-range because the defense is packing the lane. A great rim protector
suppresses rim attempts even when the ball-handler is matched up against a wing defender, because
he will rotate. So Phase 9's resistance read is a **top-3 defender blend** across all five
on the floor, not just the slot-matched player.

### The five new per-player authored attributes

`RimTendency`, `ShortTendency`, `MidTendency`, `LongTendency`, `ThreeTendency` (0–99, authored
per player, validated on load). These represent **how much this player wants to shoot from each
zone**, separate from how well he converts. A player with `Outside=80` and `ThreeTendency=80`
is a high-volume three-point shooter; a player with `Outside=80` and `ThreeTendency=30` is a
skilled shooter who rarely takes them (the Klay Thompson shape). Skill and tendency are
independently authored. Tendency governs the shot MIX; the existing skill attributes govern
CONVERSION. The new tendency-sum rule in `Player.Validate()` catches configs with all five
tendency fields omitted or zero — those would produce an invalid pie at runtime.

### The coaching seam (identity in v1)

`CoachingPull.Apply(shooter, coach, malleability)` routes tendencies through the coaching layer
before the matchup math runs. In v1 this is the identity function. The coaching session adds
fields to the `CoachProfile` placeholder type and replaces `CoachingPull.Apply`'s body; every
call site in `RollGGenerator` is unchanged because the signature already uses the real type.
Emmett's basketball call on the future coaching session: every player is at least somewhat
malleable — coaches pull tendencies toward the team's system, weighted by a per-player
malleability attribute. Coaching pull can be against the player's best interest (a coach running
three-heavy with a post-up big makes the big shoot threes he can't make — a basketball feature,
not a bug).

### The top-3 blend and what it models

For each zone: rank all five defenders by their `DefenseRating` at that zone (the existing
CONF-1 blended per-zone defensive read), take the top three, and blend with weights 0.55 / 0.30
/ 0.15 (sum to 1). The weights encode: the best zone defender carries the most weight because
he'll rotate over, but help is not instantaneous so the second and third also matter. Fourth
and fifth are too far from the action.

Per-zone weights are **global for v1** — the same 0.55/0.30/0.15 for every zone. A per-zone
variant (rim specialist-driven vs perimeter coverage) is a calibration call deferred.

### The ratio-form multiplier (v2 fix)

For each zone, the per-zone gap (offensive capability minus defensive resistance) runs through
the existing `GapFn` → tanh saturation into a **ratio-form multiplier**:

```
multiplier = exp(log(LocationMaxMultiplier) × tanh(shift / LocationReferenceShift))
```

This form is bounded in `(1/LocationMaxMultiplier, LocationMaxMultiplier)`, exactly 1.0 at
zero gap, and **can never go negative or zero**. An earlier draft used an additive form
(`1 + tanh(x) × Range`) with Range=1.5, which could produce a lower asymptote of −0.5 and
blow up the pie. The ratio form fixes this structurally — no clamp, no guard, just math that
cannot produce a negative weight.

With `LocationMaxMultiplier = 2.5`: multipliers range between 0.4 and 2.5. At zero gap the
multiplier is exactly 1.0. At extreme gaps (fin=99 vs rimP=1) the harness measured 2.491;
at the opposite extreme (fin=1 vs rimP=99) it measured 0.401.

### The renormalization and why it is mathematically honest

After computing five bent tendencies (each tendency × its zone's multiplier), renormalize to
sum to 1. The relative magnitudes of the multipliers are what redistributes the shot mix; the
absolute magnitudes don't matter. This produces one critical behavior: **uniform gaps cancel**.
If every zone has the same gap → every multiplier is the same constant → renormalization erases
the shift → the player shoots at his raw tendency ratios. D3 vs D3 and D1 vs D1 produce very
similar mixes; the weird stuff only shows up when you mix levels AND the defensive shape is
uneven. A D1 finisher vs a D3 team with weak rim protection but average perimeter D will
shift toward the rim — because the rim gap is large and the perimeter gap is small. The engine
encodes per-zone gap inequality, not average level difference.

### DEC-6 fallbacks (two tiers)

1. **No shooter** (roster not populated): flat stub pie from `RollGConfig`, byte-for-byte
   identical to `RollGStubPieGenerator`. Every isolated regression check in the harness hits
   this path.
2. **Shooter present, zero populated defenders**: normalized player tendencies through the
   coaching seam, NO matchup multiplier. Player identity preserved; only the matchup term is
   absent.
3. **1–2 populated defenders**: `Matchup.DefensiveResistance` renormalizes the blend weights
   to the available N defenders, then proceeds normally.

### Roll G's architecture after Phase 9

- `IRollGPieGenerator` — new interface. Both stub and real generator implement it; Resolver
  holds the interface (`_rollGGenerator` field typed to the interface).
- `RollGStubPieGenerator` — unchanged in behavior; now `: IRollGPieGenerator`. Used by all 14
  isolated harness check constructions (13 stub-only + 1 inside `RollGLocationBatchCheck` for
  baseline regression).
- `RollGGenerator` — matchup-aware real generator. Ctor takes `(RollGConfig, MatchupConfig,
  GameState)`. Mirrors `RollHGenerator`'s pattern.
- **Roll G itself unchanged.** `RollG.Execute` still takes `(state, pie, rng)`. Only the
  generator reads `GameState`.

### SeatStartersFromConfig (the v2 fix that makes Phase 9 actually run)

Main's `game` object has empty rosters by default. `RollGGenerator` and `RollHGenerator` are
both tied to this `game`; without seating, every `PlayerAt()` returns null and both generators
fall back to stub pies. Phase 9 would be "wired" but never actually exercised in the live chain
— bizarre and undetectable from a passing harness run.

`SeatStartersFromConfig(game, configPath)` is called in Main after `GameState` construction and
before the generator constructions. It mirrors the seating loop in `Phase1RosterCheck`. Called
in `RunGame` also (generators there remain stubs but the seating future-proofs that path).

### Phase 9 check results (all sub-checks, all green)

- Zero-gap neutral matchup: exact raw-tendency normalization confirmed to 4 decimal places.
- All-five-weak-rim defense: rim share 24.993% > 20% uniform baseline.
- Top-3 blend: Config B (1 elite + 3 solid help) resists more than Config A (1 elite + 4 weak),
  rim share B 17.6% < A 19.9%.
- D1 finisher vs D3 weak-rim: rim 65.9% vs tendency baseline 50.0% — rises significantly.
- Same-level matched control: within 4.6pp of tendency baseline.
- Uniform D1 vs D3 (everyone 75 vs 45): diff = 0.000% — uniform gaps cancel exactly.
- DEC-6 zero defenders: exact 20.0% for uniform tendencies.
- DEC-6 one elite rim defender: rim 14.2% < 20% — strong suppression.
- DEC-6 one elite + two normals: rim 17.9% > 14.2% — elite diluted by help, less suppression
  (counterintuitive but correct: one defender alone gets 100% blend weight, max suppression;
  two normal partners dilute and reduce it).
- Coaching seam identity: confirmed exact pass-through.
- Roll H regression valid after Phase 9 roster seating.
- Negative-multiplier guard: both extremes confirmed strictly within `(0.4, 2.5)`.

### The RollHResolutionBatchCheck fix (post-harness run #1)

The first harness run revealed an unexpected failure: `RollHResolutionBatchCheck` produced
a Made rate of 48.4% vs the expected 36.5%. Root cause: `SeatStartersFromConfig` now populates
Main's `game` (a necessary change for Phase 9 to work), and `rollHGenerator` is constructed
against that same `game` — so the check's `rollHGenerator.Generate(state)` now reads actual
player attributes (Marcus Webb, Outside=71, etc.) and produces matchup-adjusted make rates
instead of the neutral/stub rates the expected values were calibrated against.

Fix: `RollHResolutionBatchCheck` now constructs its own isolated `RollHGenerator` bound to its
own local empty `game`. PlayerAt returns null everywhere in that game → the generator falls back
to stub/neutral rates → the calibrated expected values remain valid. The Phase 6/7/8 matchup
checks were unaffected (they always constructed their own isolated setups). This is the correct
architectural resolution: the batch check is a **baseline calibration** check (configured rates),
not a matchup-validity check (that's Phase 6/7/8's job).


---

## Phase 10 — Matchup-aware rebounding (Roll I, "the glass")

### The settled model: two touchpoints, binary mass reweight

A missed or blocked shot becomes a loose ball. Roll I's seven-way pie resolves it.
Phase 10 makes **exactly one thing** matchup-sensitive: the `DefensiveRebound` vs
`OffensiveRebound` split. The five flat slivers (fouls, OOB, jump-ball) are untouched.

The bend uses **two size touchpoints that do not collide:**

**Touchpoint 1 — Pre-staging size check (external, team-vs-team).**
Compare the mean `ReboundPhysical` composite (height + strength, config-weighted) across
both teams' populated players. Bigger mean → positive size shift → off-share bends up.
This is a *relative* comparison: a 7-foot stiff helps against a small lineup and hurts against
giants. Works example: a stiff vs. a 5-out team of guards wins the size check AND his rating
× 1.2 beats a guard's × 0.8 — he vacuums the boards. The same stiff vs. a lineup of athletes
loses the size check AND his ratings may not overcome theirs.

**Touchpoint 2 — Positional-weighted skill shift (internal, intra-team).**
Within each lineup, each player gets a `PositionalWeight` =
`1.0 + swing × tanh((Postness_i − lineupMean) / scale)`. The "post-ness" composite
(height + post defense + strength, config-weighted) sorts who within a lineup is positioned
to rebound. Posts above 1.0, guards below 1.0, exactly 1.0 at the lineup mean. Even a
positionless 5-out lineup forces someone to be the relative post.

Each team's weighted mean `OffensiveRebounding` (or `DefensiveRebounding`) × positional
weight is computed, then the gap goes through `GapFn` → skill shift. On the offensive side,
the shooter's contribution is nerfed on `Three/Long/Mid` (the shooter is outside and can't
crash as easily); no nerf on `Rim/Short`.

**Composition (BlockWeight shape):**
`totalShift = ReboundSizeWeight × sizeShift + ReboundSkillWeight × skillShift`
`span = (ceiling − baseOffShare) if totalShift ≥ 0 else (baseOffShare − floor)`
`bend = span × tanh(totalShift / ReboundReferenceShift)`
`finalOffShare = baseOffShare + bend`  ← plain addition; tanh is odd and supplies the sign.

**Why not a double-count.** Both touchpoints share the underlying traits (height, strength)
but do different jobs. Touchpoint 1 compares teams *across* teams (external). Touchpoint 2
sorts who within a team is the big (internal). A 7-footer plays both roles simultaneously,
but the two comparisons answer different questions and can produce orthogonal results.

### The binary mass reweight (Divergence 3 from the Phase 9 template)

Roll G renormalizes all five location slices. Roll I only moves `DefensiveRebound` and
`OffensiveRebound` within their combined `Def+Off` mass. The five flat slivers are left
at their baseline config values. The pie still sums to 1 because:
`newDef + newOff = mass × (1 − finalOffShare) + mass × finalOffShare = mass`
and `mass + five_slivers = 1` (the original pie summed to 1).

### Two source baselines (Divergence 1)

Live-miss: `Def 0.66 + Off 0.27 = 0.93` mass; off-share ≈ 0.290.
Block: `Def 0.50 + Off 0.32 = 0.82` mass; off-share ≈ 0.390.

The bend operates from each source's own baseline. At a neutral matchup (all-50 teams)
each returns exactly its own baseline, confirmed in `Phase10ReboundDoorCheck` sub-check (a)
and (g).

### The interface signature (Divergence 1 continued)

`IRollIPieGenerator.Generate(PossessionState state, ReboundSource source)` takes both
parameters because the real generator needs both: `source` to select the baseline, `state`
for the rosters, shooter slot, and shot zone. The stub ignores `state`. This is the one
structural difference from `IRollGPieGenerator` (single `state` param) and
`IRollHPieGenerator` (state + bool).

### The fallback guard (Divergence vs Roll G)

`RollGGenerator` fails loud on a null `SelectedSlot` — a slot is always stamped when Roll G
runs. `RollIGenerator` cannot make the same assumption because the four harness batch checks
call the resolver with real rosters but bare possession states (no shooter stamped). The
fallback fires whenever either team has zero populated players OR `SelectedSlot` is null.
Both conditions return the flat baseline pie. A real in-game possession always has populated
rosters and a stamped slot; the fallback never fires on the live path.

### The cross-config constructor guard

At construction, for both source baselines, `RollIGenerator` computes `baseOff /
(baseDef + baseOff)` and throws if it falls outside `[ReboundOffShareFloor,
ReboundOffShareCeiling]`. The baseline lives in `RollIConfig`; the band lives in
`MatchupConfig`. A future config edit that pushes a baseline outside the band would silently
invert the tanh bend direction — caught loud at startup rather than producing wrong
(but not obviously broken) output.

### Parked items

- **Roll M (free-throw-board rebounds):** same seven-arm vocabulary, different baseline
  (more defensive, lower off-share, no shooter nerf), no shooter position to read. Fast-follow
  next session; these exact rails carry it.
- **Per-player rebound attribution:** which slot grabbed the board is the deferred attribution
  pass. Roll I decides only which team.
- **Coaching sliders** (crash-glass vs. get-back / crash-vs-break-out): the `finalOffShare`
  returned by `OffensiveReboundShare` is the insertion point; v1 is matchup-only; the seam is
  documented in `RollIGenerator.Generate`.
- **Per-zone rebounding** (long misses off threes favoring guards): the zone gate (shooter
  nerf on/off) is the only zone read. No per-zone rebound table.
- **`Hustle`:** confirmed on `Player`, natural amplifier to fold in later; not used in v1.
- **Athletic/big axis split:** `ReboundPhysical` and `Postness` are dedicated composites;
  `Player.Athleticism` is not read; no entanglement with the deferred horizontal/vertical
  axis refactor.

### Phase 10 calibration knobs (all placeholders)

`ReboundStrengthWeight` / `ReboundHeightWeight` (size composite); `PostnessHeight` /
`PostnessPostDefense` / `PostnessStrength` (postness composite); `ReboundPositionalSwing`
(~0.8/1.2 range of positional weights); `ReboundPositionalScale` (spread at which one unit
of swing is reached); `ReboundSizeWeight` / `ReboundSkillWeight` (sum to 1; relative pull of
each touchpoint); `ReboundShooterNerf` (multiplier on shooter's offensive contribution on
Three/Long/Mid); `ReboundOffShareFloor` / `ReboundOffShareCeiling` (tanh asymptotes on the
off-share); `ReboundReferenceShift` (tanh saturation speed). All in the `Matchup` section of
`config.json`. None are calibrated values — Emmett tunes against the harness.

---

## Phase 11 — Matchup-aware free-throw rebounding (Roll M, "the FT glass")

### The settled model: the Phase 10 twin, more defensive, no crashing shooter

Roll M is Roll I's two-touchpoint model applied to the FT glass. The machinery is identical:
`Matchup.OffensiveReboundShare` (pre-staging size check + positional-weighted skill shift + tanh
saturation); `Matchup.ReboundPhysical`, `Matchup.Postness`, `Matchup.PositionalWeight` reused
verbatim; the binary mass reweight of `DefensiveRebound` and `OffensiveRebound` within the
`Def+Off` mass, five flat slivers untouched. No new config, no new player attributes.

Two basketball facts make the FT glass different from the field-goal glass:

1. **The shooter is behind the line.** Off a field-goal miss the shooter may be near the rim and
   crash in. Off a free throw the shooter is behind the line by rule; everyone else lines up along
   the lane in assigned box-out spots. The defense holds the better positions and no one crashes
   from the shooter's spot.

2. **The defense is more organized.** Box-out assignments are explicit off a free throw in a way
   they aren't off a live shot. This produces a lower natural offensive-rebound share.

The model expresses both facts through Roll M's config baseline (`Def 0.735 / Off 0.18`, natural
off-share ≈ **0.197** vs Roll I's live-miss ≈ 0.290) and by passing `shooterIdx = -1` to
`OffensiveReboundShare` — the shooter nerf is structurally off, not tuned off. The FT baseline
sits inside the same `[ReboundOffShareFloor, ReboundOffShareCeiling]` band as Roll I's baselines;
the same bend applies from this lower starting point.

### The four divergences from the Roll I template

All four are structural necessities, not design preferences:

**Divergence 1 — No source selector; one-arg interface.**
Roll I has two baselines (live-miss, block); its generator takes `(state, source)`. Roll M has
exactly ONE source (a missed final FT). `IRollMPieGenerator.Generate(state)` takes only `state`.
One cross-config baseline guard at construction; one flat-baseline fallback helper (no source
enum, no source switch).

**Divergence 2 — No shooter, no nerf.**
`OffensiveReboundShare` is called with `shooterIdx = -1` and `zone = ShotLocation.Rim` (a
constant, not derived). The nerf gate `i == shooterIdx` is never true at -1; every offensive
rebounder contributes un-nerfed. The generator does not read `state.SelectedSlot` or
`state.ShotType` for the matchup math.

**Divergence 3 — Fallback: empty-roster only.**
The single fallback condition is zero populated players on either team. No `SelectedSlot` check,
no `ShotType` check. Two kinds of state reach Roll M: a bonus FT trip (slot null, zone null) and
a shooting-foul FT trip (slot and zone stamped). Roll M must accept both without branching on
slot nullness — a null slot is expected, not a fallback trigger. The empty-roster path returns
the flat baseline pie (byte-for-byte the stub).

**Divergence 4 — Resolver field was typed to the concrete stub.**
`Resolver._rollMGenerator` was `RollMStubPieGenerator` (the only generator field that hadn't
been promoted to an interface yet). Retyped to `IRollMPieGenerator`; ctor param likewise. The
dispatch site updated: `_rollMGenerator.Generate(c.State)` (was `Generate()` with no args).

### Architecture after Phase 11

- `IRollMPieGenerator` — new interface. One-arg `Generate(PossessionState state)`. Both stub and
  real generator implement it; Resolver holds the interface.
- `RollMStubPieGenerator` — now `: IRollMPieGenerator`. Accepts `state` (ignored); returns flat
  config baseline. Used by `RollMReboundBatchCheck` (directly, via the stub constructor at the
  call site) and all 8 Resolver-construction sites in isolated harness checks.
- `RollMGenerator` — new matchup-aware generator. Ctor `(RollMConfig, MatchupConfig, GameState)`
  with null guards + cross-config baseline guard. `Generate(state)`: empty-roster fallback only;
  populated path calls `Matchup.OffensiveReboundShare(..., shooterIdx: -1, zone: Rim, ...)`;
  splits the `Def+Off` mass; five slivers untouched. Coaching seam documented at identity.
- **Roll M itself unchanged.** `RollM.Execute` still takes `(state, pie, game, rng)`. Only the
  generator reads `GameState`.

### The no-shooter invariance (the positive proof)

`Phase11FreeThrowReboundDoorCheck` sub-check (e) constructs two identical all-50 neutral matchups
— one with `SelectedSlot = null, ShotType = null` (the bonus trip path) and one with a stamped
slot and nerf-eligible zone (`Three`) — and asserts the off-shares are **byte-identical**. With
an all-50 lineup `Matchup.OffensiveReboundShare` produces the baseline exactly in both cases
because `shooterIdx = -1` means the nerf never fires regardless of zone or slot. This confirms
Divergences 2 and 3 are correctly implemented: Roll M is slot-blind and zone-blind by design.

### The FT-vs-field-goal baseline comparison

At neutral (all-50 teams), Roll M's off-share is ≈ 0.197 vs Roll I's live-miss ≈ 0.290 and
block ≈ 0.390. The FT glass is the most defensive of the three, exactly as basketball dictates.
All three baselines lie inside the same `[0.08, 0.55]` band; the shared tanh saturation knobs
apply to all three. Calibrating the FT glass is a matter of tuning Roll M's config weights
(`DefensiveRebound` / `OffensiveRebound`); the matchup machinery reacts automatically.

### Parked items

- **Per-player rebound attribution:** Roll M decides only which TEAM. The attribution pass is
  separate and deferred.
- **Coaching sliders** (crash-glass / get-back): the insertion point is after
  `OffensiveReboundShare` returns and before the mass split; v1 is matchup-only; the seam is
  documented in `RollMGenerator.Generate`.
- **Per-zone FT rebounding:** Roll M passes `zone = Rim` as a constant because a bonus FT trip
  carries no `ShotType`. The nerf gate never fires at `shooterIdx = -1` regardless of zone, so
  the zone constant is arbitrary (Rim is the clean choice). No per-zone FT rebound table.
- **`Hustle`, athletic/big axis split:** same parks as Phase 10.


---

## Phase 12 — The pressure / disruption door (Roll F)

### Overview

Phase 12 makes Roll F's generator pressure-and-matchup-aware. The selected handler's chance of
turning the ball over and of drawing a non-shooting foul now reflects the defending team's pressure
setting and the one-on-one handling-vs-steals contest. Roll F itself is untouched.

This is the defensive-disruption twin of the block door: rim protection disrupts the shot; ball
pressure disrupts the possession before the shot gets off.

### Two faces of pressure — this session builds only the disruption face

Pressure has two faces in the model:

- **Disruption face (this session):** pressure raises the `Turnover` slice and the
  `NonShootingFoul` slice. High pressure = more reach-ins and ball-strips.
- **Shot-quality face (deferred):** beating high pressure yields scrambled-defense rim busts;
  backed-off low pressure packs the paint and concedes the perimeter. The offense side of pressure.

The two faces are deliberately separate. One dial bending four things in opposing directions is
exactly the interacting-variable trap that sank two prior Python attempts. Build one face,
calibrate it, then the other.

### The matchup model

**Single attribute pair: `handler.BallHandling` vs. `defender.Steals`.** Emmett settled on these
two alone. Broader composites (adding Passing, BasketballIQ, PerimeterDefense, Quickness) were
considered and rejected — they over-build the door and exceed the settled design.

**Pressure is the master dial.** It does two jobs on the steal/turnover slice:

1. **Flat, skill-independent lift.** Even a neutral or unfavorable matchup produces a lift when
   pressure is above neutral. Backing off suppresses disruption regardless of how good the
   hands are.
2. **Pressure gates how much the matchup matters.** At low pressure the handling-vs-steals
   gap is muted (`pressureGate ≈ 0`). At high pressure the gate opens and the gap drives the
   outcome. The same lever captures both "high-steals defender climbs faster" and "big gap climbs
   faster" — one term, not two.

**Foul slice: pressure only.** Reach-in fouls track aggression. The handling-vs-steals matchup
does NOT steepen the foul climb — the `foulShift = pressureLift` only. No matchup term.

### The math (settled)

```
pUnit        = (pressure − PressureNeutral) / PressureScale
pressureLift = pUnit
pressureGate = max(0, pUnit)

stealGap        = defender.Steals − handler.BallHandling
matchupShift    = GapFn(stealGap, SkillSteepness, SkillExponent, ReferenceScale)
disruptionShift = pressureLift + pressureGate × matchupShift

// Turnover share (share of actionMass)
toSpan     = (disruptionShift ≥ 0) ? (TurnoverCeiling − baseTurnoverShare)
                                    : (baseTurnoverShare − TurnoverFloor)
toBend     = toSpan × tanh(disruptionShift / PressureReferenceShift)
finalToShare = baseTurnoverShare + toBend       // plain addition; tanh supplies the sign

// Foul share (share of actionMass)
foulShift   = pressureLift                      // NO matchup term
foulSpan    = (foulShift ≥ 0) ? (FoulPressureCeiling − baseFoulShare)
                               : (baseFoulShare − FoulPressureFloor)
foulBend    = foulSpan × tanh(foulShift / PressureReferenceShift)
finalFoulShare = baseFoulShare + foulBend

// Three-way mass split; JumpBall flat
actionMass        = BaseShotAttempt + BaseTurnover + BaseNonShootingFoul
baseTurnoverShare = BaseTurnover       / actionMass
baseFoulShare     = BaseNonShootingFoul / actionMass

finalShotShare = 1 − finalToShare − finalFoulShare
newShot  = actionMass × finalShotShare
newTO    = actionMass × finalToShare
newFoul  = actionMass × finalFoulShare
newJump  = BaseJumpBall    // EXACTLY flat — never touched
```

**Plain addition throughout.** tanh is odd and already negative when the shift is negative — the
same Session 38 lesson as `BlockWeight` and `OffensiveReboundShare`. Do NOT write
`bend if shift ≥ 0 else -bend`.

**Low ceiling, high reference shift = gradual climb.** `TurnoverCeiling` is deliberately low
(0.18 = 18% of action-mass possessions end in a Roll F strip). `PressureReferenceShift` is
deliberately high relative to the pUnit range (1.2, vs pUnit max ≈ 1.25 at pressure 10), so the
climb is gentle and saturates well short of absurd. Nobody gets 5 steals a game even in the
fastest system.

### Changed calibration anchor

Every prior matchup door held the invariant: even matchup = config baseline. Phase 12 breaks that
sub-invariant. Here the anchor is:

**(neutral pressure + even matchup) = today's flat Roll F rates.**

This is Emmett's basketball call. Pressure is the new axis that moves the rates; the matchup is
gated underneath pressure. At non-neutral pressure, even an even matchup produces a different
outcome rate. Flag this in calibration — it is not a bug.

### Pressure home — v1 config scalar, CoachProfile migration path

Pressure is a per-team defensive setting, not a player attribute and not a per-possession fact.
For v1, it lives as `MatchupConfig.HomePressure` / `MatchupConfig.AwayPressure` (both default 5.0
= neutral). A `PressureFor(TeamSide)` helper on `MatchupConfig` is the read seam the generator
calls.

`CoachProfile` (stubbed in Phase 9) is the eventual owner. Migration path: when the coach-settings
layer arrives, move `HomePressure`/`AwayPressure` to per-team `CoachProfile` fields and update
`PressureFor(...)` to read from there. Only that one method changes; `RollFGenerator.Generate` is
untouched. Do NOT half-build a coach-settings layer in this session.

### DefenderPicker fork — second consumer, promotion still deferred

`DefenderPicker.cs`'s own doc-comment says: "The moment a second door consumes the defender, or
the pick becomes non-deterministic (mismatch-hunting), the defender must be promoted to a carried
`PossessionState.DefenderSlot`."

Phase 12 is the **second door** to consume the slot-matched defender (Roll H's generator being the
first). This technically meets the second-consumer trigger.

**Decision: promotion still deferred.** The pick is still **pure and deterministic** —
`new Slot(state.Defense, selectedSlot.Number)` — so two doors deriving it independently produce
the **same defender** with zero divergence risk. The hazard the comment guards against (two doors
picking different defenders, or a non-deterministic pick) does not exist while the pick is this
pure slot-match.

Roll F derives its defender **locally** — same slot-match logic as `DefenderPicker.Pick`, without
routing through it. The first door that needs a non-deterministic or mismatch-hunting pick is what
forces the promotion. That moment has not arrived.

Record: Phase 12 is the second consumer. The promotion bar has technically been met on the
"second consumer" clause, but NOT yet on the "or the pick becomes non-deterministic" clause. The
deferred status is correctly classified as "because the pick is still pure, not because we haven't
noticed."

### Architecture after Phase 12

- `IRollFPieGenerator` — new interface. One-arg `Generate(PossessionState state)`. Both stub and
  real generator implement it; Resolver holds the interface.
- `RollFStubPieGenerator` — now `: IRollFPieGenerator`. Used by `RollFActionBatchCheck` (fresh
  inline stub) and all Resolver-construction sites in isolated harness checks.
- `RollFGenerator` — new matchup-aware real generator. Ctor `(RollFConfig, MatchupConfig,
  GameState)`. Fallbacks: null slot → flat baseline; absent handler player → flat baseline (DEC-6);
  absent defender player → flat baseline (DEC-6). Calls `Matchup.DisruptionShares`; three-way mass
  split; JumpBall pinned flat. Overflow guard (throws if finalToShare + finalFoulShare ≥ 1).
- `Matchup.DisruptionShares` — new pure static method. The disruption-face math, mirrors `FoulRate`
  structure. The foul slice has no matchup term.
- `MatchupConfig` — new Phase 12 block: 9 new properties, `PressureFor(TeamSide)` helper, all
  Load invariants.
- **Roll F itself unchanged.** `RollF.Execute` still takes `(state, pie, rng)`.

### Parked items

- **Shot-quality face of pressure.** Deferred. No hooks, no stubs. The seam is the next session.
- **Roll B, Roll A.** The same handling-vs-steals matchup applies one and two steps earlier in
  the chain. Their own fast-follow sessions.
- **Roll C turnover classification.** Pressure moves the turnover RATE at Roll F. The type mix
  (Roll C) is unchanged. "How often" vs. "what kind" are correctly separated.
- **Player/steal attribution.** Which defender gets credit for the steal. The deferred attribution
  pass.
- **Broader coach-settings layer.** Tempo, help-defense rules, etc. beyond this single dial.
  `CoachProfile` is the eventual home; the rest arrives piece by piece.
- **`PossessionState.DefenderSlot` promotion.** See the fork section above.
- **Broader composites.** Passing, BasketballIQ, PerimeterDefense, Quickness in the matchup.
  Emmett settled on handling vs. steals alone for Phase 12.

## Phase 13 — The pressure / disruption door (Roll B, halfcourt initiation)

### The reframing — why Roll B is not a trivial copy of Phase 12

The Phase 12 plan described Roll B as "the same matchup one step earlier in the chain."
This was stale. Roll B runs **before** player selection (Roll E). `PossessionState.SelectedSlot`
is null at Roll B — Roll E has not run. The Phase 12 one-on-one handling-vs-steals contest
requires a handler (for `BallHandling`) and a slot-matched defender (for `Steals`). Neither
exists at Roll B. This is a structural constraint, not a design limitation.

### Two resolved design questions

**DQ1 — Is there a per-player matchup at Roll B, or pressure-only?**

Emmett settled on **Option B: two-sided team aggregate**. No individual player is selected
yet, so the matchup uses slot-weighted aggregate scores: the five offensive players' `BallHandling`
values vs. the five defensive players' `Steals` values, both with the same guard-heavy weights.

Basketball logic: a pressing defense with good thieves earns turnovers even before the specific
ball-handler is known. Which player eventually coughs it up is Roll C's attribution concern —
Roll B decides the rate. Guard-slot players dominate because they have far more opportunities
to handle and pick pockets at halfcourt initiation. A center with great `Steals` contributes
but at lower weight (8%) because the game creates fewer opportunities for them.

Rejected options: pressure-only (Option A, simpler but doesn't capture Emmett's intent that
steals ratings matter); team aggregate only for defense (one-sided, excluded offensive
ball-handling); composite attributes (adding Passing, BasketballIQ, etc. — over-built).

**DQ2 — What happens to the existing physicality wire?**

Emmett settled on **keep physicality dormant** (Option ii). The `physicality` parameter stays
in the interface and generator, applied after the pressure bend as a secondary nudge on the
Foul slice. It is fed 0.0 at both live dispatch sites. Physicality and pressure are distinct
concepts — how rough/chippy the play is vs. how aggressively the defense hounds the ball.
When the physicality dial becomes live in a future session, the wire already exists.

### The pressure-only model for the foul slice

The foul slice is pressure-only (no matchup term) at Roll B — same as the foul side of Phase
12's Roll F. Reach-in fouls at halfcourt initiation track defensive aggression, not skill. You
can reach in against anyone if you're playing aggressively enough. `foulShift = pressureLift`
with no matchup term.

### The math

```
// Pressure normalization (shared with Phase 12)
pUnit        = (pressure − PressureNeutral) / PressureScale
pressureLift = pUnit
pressureGate = max(0, pUnit)

// Team aggregate scores (slot-weighted, normalized over non-null slots)
offHandling = Σ weight[i] × off_player[i].BallHandling   (i = slots 1–5)
defSteals   = Σ weight[i] × def_player[i].Steals         (same weights)

// Team gap → matchup shift (same GapFn as all other doors)
teamGap        = defSteals − offHandling
matchupShift   = GapFn(teamGap, SkillSteepness, SkillExponent, ReferenceScale)
disruptionShift = pressureLift + pressureGate × matchupShift

// Turnover share (share of actionMass) — team-aggregate matchup + pressure
toSpan       = (disruptionShift ≥ 0) ? (RollBTurnoverCeiling − baseToShare)
                                      : (baseToShare − RollBTurnoverFloor)
toBend       = toSpan × tanh(disruptionShift / PressureReferenceShift)
finalToShare = baseToShare + toBend           // plain addition

// Foul share (share of actionMass) — pressure only, NO matchup term
foulShift    = pressureLift
foulSpan     = (foulShift ≥ 0) ? (RollBFoulPressureCeiling − baseFoulShare)
                                : (baseFoulShare − RollBFoulPressureFloor)
foulBend     = foulSpan × tanh(foulShift / PressureReferenceShift)
finalFoulShare = baseFoulShare + foulBend     // plain addition

// Three-way mass split; JumpBall held exactly flat
actionMass        = BaseProceed + BaseFoul + BaseDeadBallTurnover  // = 0.995
baseToShare       = BaseDeadBallTurnover / actionMass
baseFoulShare     = BaseFoul             / actionMass
finalProceedShare = 1 − finalToShare − finalFoulShare
newProceed  = actionMass × finalProceedShare
newTO       = actionMass × finalToShare
newFoul     = actionMass × finalFoulShare    // THEN physicality nudge + renormalize
newJump     = BaseJumpBall                   // EXACTLY flat
```

**Plain addition throughout** (Session 38 lesson). `Math.Tanh` is odd and already negative
when the shift is negative. Do NOT flip the sign.

### Slot weights

`[0.35, 0.25, 0.20, 0.12, 0.08]` for slots 1–5. Same weights for offense (`BallHandling`)
and defense (`Steals`). Stored in `MatchupConfig` as `SlotWeight1`–`SlotWeight5` with a
`SlotWeights` convenience array. `Load` enforces: each ≥ 0, sum = 1.0. Calibration
placeholders — tunable in `config.json`.

Reasoning: guards dominate at halfcourt initiation on both sides of the ball. The slot-5
center still contributes (8%) but at far lower weight because the game creates fewer
opportunities. An elite center's Steals rating still shows up in the aggregate — it just
doesn't dominate it.

### Roll-B-specific ceilings/floors

Roll B's baseline foul share (≈12% of action mass) is far higher than Roll F's (≈5%), and
its baseline TO share (≈3%) is lower. The Phase 12 `TurnoverCeiling`/`FoulPressureCeiling`
keys are wrong for Roll B. New keys in `MatchupConfig`:

- `RollBTurnoverCeiling` = 0.10 — max TO share of action mass at max disruption
- `RollBTurnoverFloor` = 0.01 — min TO share at min disruption
- `RollBFoulPressureCeiling` = 0.22 — max Foul share at max pressure
- `RollBFoulPressureFloor` = 0.06 — min Foul share at min pressure

The shared `PressureNeutral`/`PressureScale`/`PressureReferenceShift` are reused — they
describe the pressure dial's normalization, which is a global property of the pressure scale.

### Changed calibration anchor

Same as Phase 12: (neutral pressure 5.0 + even aggregate) = today's flat Roll B rates.
Non-neutral pressure or a team mismatch bends the rates. Not a bug.

### Physicality wire (DQ2 resolution)

The interface is two-arg `Generate(PossessionState state, double physicality)`. After the
pressure bend produces the three-way mass split, the physicality nudge is applied to the raw
Foul weight and the whole dict renormalizes. At `physicality = 0.0` (both dispatch sites)
this is a no-op. `PhysicalityFoulNudge` stays in `RollBConfig` and `config.json`. Future
session: wire a real value to activate.

### Fallback

No null-slot fallback, no absent-player fallback. Roll B reads no `SelectedSlot` and no
individual player. The only fallback triggers when either roster has zero populated players
(isolated test calls). Partial rosters proceed with weights renormalized over populated slots.
The generator's doc-comment states this explicitly.

### Architecture after Phase 13

- `IRollBPieGenerator` — new interface. Two-arg `Generate(state, physicality)`. Both stub
  and real generator implement it; Resolver holds the interface.
- `RollBStubPieGenerator` — now `: IRollBPieGenerator`. Used by `BatchCheck` and
  `PhysicalitySignalCheck` (fresh inline stubs) and all 8 Resolver-construction sites in
  isolated harness checks.
- `RollBGenerator` — new real generator. Ctor `(RollBConfig, MatchupConfig, GameState)`.
  Fallback: zero-population only. Calls `Matchup.TeamDisruptionShares`; three-way mass split;
  JumpBall flat; overflow guard; physicality nudge (dormant).
- `Matchup.TeamDisruptionShares` — new pure static method alongside `DisruptionShares`.
  Takes pre-computed aggregate scores, not `Player` objects. Uses Roll-B-specific ceilings/floors.
- `MatchupConfig` — new Phase 13 block: 5 slot-weight properties, `SlotWeights` array,
  4 Roll-B-specific ceiling/floor properties, all `Load` invariants.
- **Roll B itself unchanged.** `RollB.Execute` still takes `(state, pie, rng)`.

### Parked items

- **Shot-quality face of pressure.** Deferred. No hooks, no stubs.
- **Roll A.** Its own session with its own wrinkles (backcourt phase, violation terminals,
  different turnover-type context). NOT a trivial copy of Phase 13.
- **Roll C turnover classification / type mix.** Pressure moves the turnover RATE at Roll B.
  The type mix (which player gets the steal credit, weighted toward guards and Steals rating)
  stays Roll C's domain.
- **CoachProfile migration.** `MatchupConfig.PressureFor(TeamSide)` is the only read site
  that changes when the coach-settings layer arrives.
- **`PossessionState.DefenderSlot` promotion.** Still deferred. Roll B reads no defender.
- **Player/steal attribution.** Which defender gets the steal credit. Roll C, with weight
  toward position and Steals rating per Emmett's settled design.
- **Physicality as a live dial.** Dormant at 0.0 from both dispatch sites. A future session
  wires a real value when the concept is ready.
- **Slot-weight tuning.** The 0.35/0.25/0.20/0.12/0.08 split is a calibration placeholder.

---

## Roll A — correction: five-arm pie (Contextification #6 supersedes the "seven slices" section above)

The Roll A section above (the "Pie shape: seven slices" block) describes the pre-Contextification-#6 shape and is stale. A banner on that section marks it superseded. The Contextification #6 entry later in this document documents the correct shape in full. This correction restates the key facts plainly for any reader landing on the stale section first.

**Roll A's pie is five-arm.** `EntryOutcome` is: `CleanEntry`, `Turnover`, `OffensiveFoul`, `DefensiveFoul`, `JumpBall`. The three former violation terminals (`ShotClockViolation`, `FiveSecondInbound`, `TenSecondBackcourt`) are gone from Roll A's pie.

**Where the violations went.** A backcourt violation is a way the possession is lost — it belongs in Roll C, the canonical home of every no-shot loss. Roll A's Turnover arm stamps `TurnoverContext.EntryBackcourt` on a backcourt bring-up; Roll C's EntryBackcourt pie resolves the type, which includes `FiveSecondInbound`, `TenSecondBackcourt`, and the backcourt shot-clock. A frontcourt re-inbound stamps `TurnoverContext.Halfcourt` instead, where those backcourt-only losses are 0.0. The invariant elapsed times formerly in `RollAConfig` now live in `RollCConfig`.

**The foul split.** The old single `Foul` slice became two: `OffensiveFoul` (charge / illegal screen → offensive-foul resolution: deterministic dead-ball loss to the defense, no free throws, no bonus) and `DefensiveFoul` (reach-in / bump on the ball-handler → Roll D, which charges the team foul and forks on the bonus).

See the Contextification #6 design entry for full rationale and wiring details.

---

## Phase 14 — Full-court press disruption door (Roll A, backcourt entry)

### What this phase is and is not

Phase 14 wires the **disruption face** of full-court press on Roll A — the moment the offense is still bringing the ball up the floor. The press bends Roll A's pie toward more turnovers and more fouls. It is the third sibling of the disruption door family: Roll F (Phase 12, individual player vs. defender, halfcourt) and Roll B (Phase 13, team aggregate, halfcourt) are the existing siblings.

**What it is not.** The press-break → transition face — when the offense beats the press and gets a high-quality transition look the other way — is the reward side of pressing. It lives in the transition machinery (`FastBreak` / transition path) and is deferred to its own next session. No hooks, no stubs for that face exist in this phase.

### The confirmed design

**Two independent dials.** Roll B and Roll F read `HomePressure` / `AwayPressure` — how hard the defense guards in the halfcourt. Roll A reads `HomeFullCourtPress` / `AwayFullCourtPress` — the distinct, independent tactical decision to press the full court. A team can press full-court then fall back into a halfcourt zone: the two dials are independent. `FullCourtPressFor(state.Defense)` is the only Roll A read site; migrating to per-team `CoachProfile` fields changes only that one call.

**The turnover slice — three gaps, additively composed.** Like `EffectiveRating` (Phase 6), the disruption effect composes additively. A baseline set by how hard the team presses, plus three weighted gap terms:

1. **Skill gap**: slot-weighted `Steals` (defense) − `BallHandling` (offense). `GapFn` with SKILL steepness/exponent. Same axis as Roll B.
2. **Athleticism gap**: slot-weighted `Athleticism` composite (defense) − offense. `GapFn` with PHYSICAL steepness/exponent.
3. **Size gap**: slot-weighted `LengthRating` composite (defense) − offense. `GapFn` with PHYSICAL steepness/exponent. Weight is smallest of the three.

Formula: `disruptionShift = pressureLift + pressureGate × (skillWeight·skillShift + athWeight·athShift + sizeWeight·sizeShift)`. The gate is `max(0, pUnit)`, so backed off, all three gaps stop mattering and the TO rate sits at the press-only baseline.

Basketball intent: a big, skilled, athletic backcourt against a small, slow, low-steal press drives turnovers toward the floor (the press gets burned). A long, quick, ball-hawking press against shaky guards drives them toward the ceiling. Size matters, but least of the three, and it is symmetric (a rangy press forces more; a big backcourt coughs up fewer).

**Both foul slices are press-only.** `DefensiveFoul` (reach-ins) and `OffensiveFoul` (charges / player-control fouls) track defensive aggression, not who is on the floor. No gap terms. `OffensiveFoul` ceiling is set low (≈15% of DefFoul ceiling): backcourt player-control fouls are rare. Both use `FullCourtPressReferenceShift` for tanh saturation.

**Separate saturation constant.** `EntryDisruptionShares` uses `cfg.FullCourtPressReferenceShift` — NOT `cfg.PressureReferenceShift` (halfcourt). This is a named architectural choice: the two dials are fully independent, so neither the normalization (same `PressureNeutral` / `PressureScale`, since both are 1–10 dials) nor the saturation speed of full-court press is coupled to halfcourt. A future calibration pass can tune them independently.

**Action-mass normalization.** `actionMass = BaseClean + BaseTurnover + BaseOffensiveFoul + BaseDefensiveFoul = 0.99`. Shares are normalized over `actionMass` before entering `EntryDisruptionShares`, then multiplied back out. `JumpBall` is pinned outside the action mass at `BaseJumpBall` exactly — it is NOT subject to the bend, NOT renormalized, and does NOT shrink when disruption rises. `CleanEntry` absorbs the complement of the three bent shares within the action mass.

**Court-state gating.** Roll A fires at three moments:
- Initial dead-ball entry: `Frontcourt = false` → full press + matchup computation runs.
- `ResumeInbound`: carries current `Frontcourt`; may be either.
- `SidelineInbound` post-crossing: always `Frontcourt = true` → `FlatBaseline()` returned immediately.

When `Frontcourt = true`, the offense has already crossed half — the full-court press is irrelevant. The generator returns the flat config baseline without reading the dial or the rosters. This is the reason court-state is checked first, before the roster read.

**Why team aggregate, not per-player.** Roll A runs before player selection (Roll E). `PossessionState.SelectedSlot` is null — no individual handler or defender is known. The slot-weighted aggregate (same guard-heavy weights [0.35, 0.25, 0.20, 0.12, 0.08] as Roll B) is the DQ2 Option B resolution.

**Six slot-weighted aggregates.** The generator computes: offense `BallHandling`, defense `Steals`, offense `Athleticism`, defense `Athleticism`, offense `LengthRating`, defense `LengthRating`. The athleticism and length selectors read the existing composites already on `Player` (`Athleticism` is a computed property; `LengthRating` calls `Matchup.LengthRating(p, _matchup)`).

**Gap weights — calibration placeholders.** `RollASkillWeight = 0.50`, `RollAAthleticismWeight = 0.35`, `RollASizeWeight = 0.15`. These need not sum to 1; they are tunable independently. Size is smallest. Harness check (k) confirms the ordering holds at the code level.

### Architecture after Phase 14

- `IRollAPieGenerator` — `Generate(PossessionState state, double pressure)` interface. The `pressure` parameter is validated `[0,1]` then discarded (`_ = pressure`); the real dial is read from `MatchupConfig.FullCourtPressFor(state.Defense)`. Documented as dormant — allowing the parameter to influence the press math would create a second accidental pressure input.
- `StubPieGenerator` — still `: IRollAPieGenerator`. Used by `BatchCheck` and all 8 isolated-check `Resolver` construction sites. Returns the flat baseline-with-dormant-nudge the batch checks rely on; not affected by rosters or the full-court press dial.
- `RollAGenerator` — real generator, `(RollAConfig, MatchupConfig, GameState)` ctor. Court-state gate first; empty-roster fallback second; six aggregates; `Matchup.EntryDisruptionShares`; overflow guard; four-way mass split + pinned JumpBall. `FlatBaseline()` helper for gate and fallback paths.
- `Matchup.EntryDisruptionShares` — new pure static method alongside `DisruptionShares` (Phase 12) and `TeamDisruptionShares` (Phase 13). Takes six pre-computed aggregates. Three gap terms compose into the TO shift; two press-only bends for DefFoul and OffFoul. Uses `FullCourtPressReferenceShift` (separate from `PressureReferenceShift`). Roll-A-specific ceilings/floors from `MatchupConfig`.
- `MatchupConfig` — new Phase 14 block: `HomeFullCourtPress` / `AwayFullCourtPress` / `FullCourtPressFor(TeamSide)` (existing), `FullCourtPressReferenceShift`, `RollASkillWeight`, `RollAAthleticismWeight`, `RollASizeWeight`, and the existing Roll-A ceilings/floors. All Load invariants added.
- `Resolver.cs` — `_rollAGenerator` field and ctor param retyped from `StubPieGenerator` to `IRollAPieGenerator`. Three Roll A dispatch sites (`RunPossession`, `ResumeInbound`, `ResolveSidelineInbound`) pass `pressure: 0.0` unchanged.
- **`RollA.cs` unchanged.** `RollA.Execute` still takes `(state, pie, rng, cfg)`.

### Parked items

- **Press-break → transition face.** The reward side of pressing. Lives in the transition machinery. Deferred to its own next session. No hooks, no stubs.
- **Steal attribution at Roll A turnovers.** Which defender gets credit. Deferred.
- **Changed turnover-type mix in Roll C under full-court press.** A full-court press turns more backcourt turnovers into steals vs. violations. Deferred.
- **Fatigue effects.** Deferred.
- **`CoachProfile` migration.** `MatchupConfig.FullCourtPressFor(TeamSide)` is the only read site that changes. Swap to per-team `CoachProfile` fields when that layer arrives.
- **Gap weight tuning.** The 0.50 / 0.35 / 0.15 split is a calibration placeholder.
- **`FullCourtPressReferenceShift` tuning.** Default 1.2 (same as `PressureReferenceShift`). Independent knob — calibration pass tunes separately from halfcourt.

## Phase 15 — Press frequency + Standard mode (Roll A reframe of Phase 14)

### What this phase is and is not

Phase 15 substantially revises Phase 14's intensity model. The full-court press dial stops being an intensity knob (how hard) and becomes a frequency dial (how often). A per-possession yes/no press decision fires once per dead-ball possession in the Resolver, and when it fires, the possession runs in **Standard** mode — a fixed disruption profile. The three-gap matchup model (skill + athleticism + size) from Phase 14 is re-pointed into this Standard profile, not rebuilt.

**What it is not — Phase 16 (the back-end break).** When the offense beats a Standard press it gets a genuine fast break the other way, mitigated by the defense's back-line rim protection. That is a separate session. No stub, no hook, no break arm was carved here. **Desperate mode** is similarly deferred: declared in the enum, reserved, the generator throws if it ever receives it.

### The confirmed design

**The dial is frequency.** Each team has `HomePressFrequency` / `AwayPressFrequency` (1–10). The mapping to a per-possession press probability is a simple linear interpolation via `PressProbabilityFor(side)`:

```
prob = PressProbabilityAtOne + (freq − 1) / 9 × (PressProbabilityAtTen − PressProbabilityAtOne)
```

Default frequency is **1.0** (LOW) — most teams do not full-court press as a base strategy. Defaults: `PressProbabilityAtOne = 0.05`, `PressProbabilityAtTen = 0.80`.

**The press/no-press roll lives in the Resolver, above the generator.** The generator is a pure pie builder (no RNG, read-only `GameState`). The Resolver draws one RNG value per dead-ball possession against `PressProbabilityFor(start.Defense)` and stamps the result as `PossessionState.PressMode` before calling `Generate`:

```
var probability = _matchup.PressProbabilityFor(start.Defense);
var mode        = _rng.NextUnitInterval() < probability ? PressMode.Standard : PressMode.None;
start           = start with { PressMode = mode };
var pieA = _rollAGenerator.Generate(start, pressure: 0.0);
```

The stamp is written BEFORE `Generate` so the generator reads a finished decision.

**PressMode enum.** `None` → flat baseline; `Standard` → Standard press pie; `Desperate` → reserved (throws). Default on `PossessionState` is `None`; safe because `RunPossession` is called exactly once per possession and stamps before any generator call. The field survives every `with` in the possession chain — `ResumeInbound` and `ResolveSidelineInbound` both use `c.State`, which carries `PressMode` through all continuation derivations.

**Standard mode — fixed lift + the three-gap matchup.** When `PressMode == Standard`, the generator reads the roster aggregates and calls `Matchup.EntryDisruptionShares`:

- **Turnover:** `disruptionShift = cfg.StandardLift + cfg.StandardGate × (skillWeight·skillShift + athWeight·athShift + sizeWeight·sizeShift)`. The tanh saturation still uses `FullCourtPressReferenceShift`.
- **DefFoul:** `cfg.StandardLift` only (no gap terms). Ceiling / floor from `StandardDefFoulCeiling / Floor`.
- **OffFoul:** `cfg.StandardLift` only (no gap terms, ceiling ≈ 15% of DefFoul ceiling). Ceiling / floor from `StandardOffFoulCeiling / Floor`.
- **JumpBall:** pinned flat. **CleanEntry:** absorbs complement.

`StandardLift` and `StandardGate` are **fixed config constants**, not functions of the frequency dial. The dial is consumed entirely by the upstream press-roll; it never enters the pie math.

**What changed from Phase 14's formula.** Phase 14 used `pressureLift = pUnit` and `pressureGate = max(0, pUnit)` derived from the dial value via `PressureNeutral / PressureScale`. Phase 15 replaces that with `cfg.StandardLift` (fixed) and `cfg.StandardGate` (fixed). There is no `pUnit`, no neutral point, no `PressureNeutral/Scale` normalization in Roll A math anymore. The halfcourt normalization stays confined to Roll B/F.

**Key behavioral difference vs Phase 14.** In Phase 14, every possession saw a blend of press and no-press (dial = 5 → average). In Phase 15, possessions are binary: some are fully pressed Standard, the rest are pure baseline. The continuum lives in the *frequency* of pressed possessions, not in the *magnitude* of each one.

**Court-state gate survives.** `Frontcourt == true` fires first — before the PressMode switch, before any roster read. The press is irrelevant once the offense has crossed half; `FlatBaseline()` is returned immediately.

**PressMode.None → FlatBaseline immediately** — before any roster read. Non-pressed possessions are byte-identical to today's pre-Phase-14 baseline.

### Architecture after Phase 15

- **`PressMode.cs` (new)** — `enum PressMode { None, Standard, Desperate }`. `None` is the default on `PossessionState`.
- **`PossessionState`** — trailing positional param `PressMode PressMode = PressMode.None` added. All 30 existing construction sites use named args; trailing default is safe. Survives every `with` in the chain.
- **`Resolver.cs`** — adds `MatchupConfig _matchup` field and ctor param (between `offensiveFoulGenerator` and `game`). The Roll A else-branch adds the press roll: one `_rng.NextUnitInterval()` draw, one `with` stamp, then `Generate`. All 9 `new Resolver(...)` sites updated.
- **`RollAGenerator.cs`** — switch on `state.PressMode`: `None` → `FlatBaseline()` (before roster read); `Standard` → break (proceed to six-aggregate computation); `Desperate` → `throw InvalidOperationException`; `default` → `throw ArgumentOutOfRangeException`. The `FullCourtPressFor` call is gone; the `pressureLift` / `pressureGate` pUnit derivation is gone.
- **`Matchup.EntryDisruptionShares`** — signature drops `double fullCourtPress`. Method reads `cfg.StandardLift` and `cfg.StandardGate` instead of `pressureLift` / `pressureGate`. All `cfg.RollA*` references renamed to `cfg.Standard*`.
- **`MatchupConfig.cs`** — Phase 14 block replaced with Phase 15 block: `HomePressFrequency` / `AwayPressFrequency` (default 1.0); `PressProbabilityFor(TeamSide)` (linear interpolation); `PressProbabilityAtOne=0.05` / `PressProbabilityAtTen=0.80`; `StandardLift=0.5` / `StandardGate=0.5`; `Standard*Ceiling/Floor` properties; `FullCourtPressReferenceShift` retained unchanged; `Standard*Weight` properties. All Load invariants updated.
- **`IRollAPieGenerator.cs`** — doc comment updated to describe `PossessionState.PressMode` stamp rather than `MatchupConfig.FullCourtPressFor`.
- **`StubPieGenerator`** — unchanged; ignores `PressMode` and returns flat baseline. Used by all 8 isolated-check Resolver construction sites.
- **`RollA.cs`, `Governor.cs`** — unchanged.
- **`config.json`** — Phase 14 key names replaced with Phase 15 key names; two new keys added (`PressProbabilityAtOne`, `PressProbabilityAtTen`, `StandardLift`, `StandardGate`).

### Parked items

- **Phase 16 — back-end break.** When the offense beats a press it gets a genuine fast break the other way, mitigated by back-line rim protection. No carve, no stub. Own session.
- **Desperate mode.** Situational end-game press (down late, before intentional fouling). Needs score-and-clock-aware module that doesn't exist. Declared in enum only.
- **`CoachProfile` migration.** `PressProbabilityFor(side)` reads `HomePressFrequency` / `AwayPressFrequency`; the method is the read seam. Swap to per-team `CoachProfile` fields when that layer arrives.
- **StandardLift / StandardGate calibration.** Defaults 0.5 / 0.5 are calibration placeholders.
- **Steal attribution, changed Roll C mix under press, fatigue.** Deferred.

---

## Phase 16 — Press-break fast break (Session 47)

**Problem being solved.** Phase 15 stamped `PressMode.Standard` on possessions where the press fires and wired the tighter matchup into Roll A's generation. But there was no "and" — when the offense beats the press, nothing different happened. Phase 16 closes that loop: a clean entry against a live press triggers a genuine fast break rather than a normal halfcourt possession.

**The gate.** `IntoHalfcourtSet` is the only site. When `c.State.PressMode == PressMode.Standard`, the gate fires: `breakState = c.State with { FastBreak = true, PressMode = PressMode.None }`. Roll B is skipped; Roll E is called directly from `IntoHalfcourtSet` with `breakState`. The roll E generator reads `FastBreak=true` and can select the transition selection pie instead of the halfcourt pie (implemented in `RollEStubPieGenerator` Phase 15; Phase 16 just ensures the stamp arrives). `PressMode.None` is stamped simultaneously so later re-inbounds in the same possession cannot re-trigger the gate.

**`IRollEPieGenerator` interface.** Pre-build audit found the Resolver held `_rollEGenerator` as `RollEStubPieGenerator` (concrete), blocking spy injection. Created `Generators/IRollEPieGenerator.cs` (single method: `Pie<SelectionOutcome> Generate(PossessionState state)`), added `: IRollEPieGenerator` to `RollEStubPieGenerator`, retyped the Resolver field and ctor param. Same pattern as `IRollBPieGenerator`. Zero call-site changes required (the stub still satisfies the interface).

**Dead-ball state hygiene.** Two re-inbound cases needed explicit rules:
- `ResolveSidelineInbound`: unconditionally clears both `FastBreak=false` and `PressMode=None`. Any sideline dead ball ends the break context and the press. `FastBreak=true` from a prior break must not leak into the next halfcourt set (Phase 16 makes Roll G read `FastBreak`, so leaking gives the wrong location pie).
- `ResumeInbound`: conditional. `Frontcourt=true` clears both (dead ball in the frontcourt — press can't survive this). `Frontcourt=false` (backcourt foul) preserves state — the press is still live and can still be beaten on the next Roll A.

**Phase 15 test 7c corrected.** The previous assertion (`ResolveSidelineInbound(Standard)` → spy sees `PressMode.Standard`) was wrong: `ResolveSidelineInbound` now clears `PressMode` to `None` before calling `Generate`. The test and label were updated to assert `None`.

**Roll G fast-break pie.** `RollGConfig` gains five `FastBreak*` properties (default weights summing to 1.0: Rim=0.70, Short=0.10, Mid=0.10, Long=0.05, Three=0.05 — calibration placeholders). `RollGConfig.Load` validates non-negative weights and sum==1.0. `RollGGenerator.Generate` checks `state.FastBreak` after the `SelectedSlot` null-guard and before any shooter/defender read; returns `BuildFastBreakPie()` immediately. The stub path (`RollGStubPieGenerator`) is untouched — it remains FastBreak-blind by design.

**`PressMode.Standard` semantic (refined).** "Press live, not yet beaten." The stamp survives backcourt dead balls (the press is still on at the re-inbound) and is consumed at the first `CleanEntry` (press beaten, break fires). Frontcourt dead balls clear it because the press decision cannot logically persist past halfcourt.

**Deferred.** Roll H foul-drawing tilt for transition and back-line help-rim-protector selection remain out of scope. `RollGStubPieGenerator` stays FastBreak-blind (stub path).


---

## Observation Run v1 — Macro sentinel harness (Session 48)

**Purpose.** A repeatable flight recorder: N full games against a frozen scenario corpus, one self-describing sentinel block out. The block is an archive of what the engine currently produces — recorded, not judged.

**The frozen-corpus-v1 contract.**
A scenario is seed + rosters + lineups + strategy + game context. Frozen so runs are reproducible and comparable across code changes:
- Corpus id: `"frozen-corpus-v1"` (string constant in the harness)
- Seed set: seeds 1..N (N=1000 in v1); `new SystemRng(seed)` for the Resolver, `new SystemRng(seed+1)` for the Governor
- Rosters/lineups: existing `config.json` starters (Webb/Pryor/Holloway/Okafor/Baptiste vs Shaw/Monroe/Dupree/Eze/Thornton)
- Strategy/context: whatever `config.json` currently sets

**Important caveat (baked into every archive entry header).** A frozen corpus stabilizes aggregate distributions across runs. It does NOT freeze individual possession outcomes when a code change alters RNG draw topology — same corpus, comparable means, not event-for-event pairing. Do not chase a "bug" that is a downstream draw shift.

**Corpus versioning rule.** When the corpus changes, bump the id (`frozen-corpus-v2`). Never silently overwrite a prior benchmark.

**Config hash.** Every sentinel block carries the SHA-256 hash of the loaded `config.json` bytes. This ties every recorded number to the exact configuration that produced it.

**Generator selection.** `ObservationRunV1` mirrors `Main`'s construction: real generators for A, B, F, G, H, I, M; stub generators for C, D, E, J, K, L, OffensiveFoul (concrete stub type, no real alternative yet). A fresh `GameState` + fresh generators + fresh RNGs are constructed per game so fouls, score, and arrow do not bleed across games.

**Mechanical checks (the only pass/fail in the method):**
1. Scoring reconciles per game: `game.HomeScore == sum(Points where Offense==Home)`, likewise Away
2. Count invariant: `records.Count == TerminalEnded + Parked + NoShotCount`
3. Zero parks: `Parked == 0` across all games — engine chain is complete; a park is a genuine finding
4. No throws: all N games complete; the Governor safety guard never hits
5. Loose sanity: PPP ∈ (0,3), pace ∈ (0,200) — catches NaN/÷0, not realism

**Deferred sentinels.** Shooting splits (FG%/3P%/FT%), shot mix, ORB%/DRB%, FTr, press rate — need counter-plumbing at the resolver's existing scoring sites. Separate session.

**First reading.** Run 1 (2026-06-16) is preserved in `docs/observations.md`. Key numbers: pace ~133 total / ~67 per team is realistic (real D1 ~65–72/team); combined PPP ~1.19 is somewhat above real D1 (~1.0–1.1) and is the more notable calibration target.

---

## Counter Plumbing v1 — Per-possession shot and rebound counters (Session 49)

**What this session adds.** Ten integer counters ride alongside the existing `Points` / `FreeThrowSpins` / `PutbackAttempts` / `ShotClockPeriods` fields: `Fga`, `Fgm`, `ThreePa`, `ThreePm`, `ShotResolutions`, `MissFouled`, `Fta`, `Ftm`, `OrbChances`, `OrbWon`. They accumulate in the resolver's `Route` walk and surface on `RoutingOutcome` as init-only fields (0 defaults, pure append). The Governor threads them through to `PossessionRecord`. The observation harness reduces them per game and reports four new sentinel sections. No probability weight moved.

**The FGA definition (box-score).**
Six of the seven `ShotResult` values count as a field-goal attempt: `Made`, `MadeAndFouled`, `Miss`, `MissOutOfBoundsLost`, `MissOutOfBoundsRetained`, `Blocked`. The one exclusion is `MissFouled` — a shooting foul on a missed shot sends the shooter to the free-throw line with no FGA charged. Charging both a missed FGA and the resulting free throws would double-count the trip. This matches NCAA/NBA box-score convention.

**The single Roll H chokepoint.**
Every field-goal attempt passes through `IntoShotResolution` in the resolver (the case that calls `RollH.Execute`). The FGA/FGM/3PA/3PM/ShotResolutions/MissFouled tally happens immediately after `RollH.Execute` at that single site, reading the stamped `ShotResult` and `ShotLocation` off `result.State`. This is the design discipline from CONVENTIONS §2a: one counter, one site, not spread across four arms. The prompt identified this chokepoint explicitly: "Do NOT try to tally FGA at the Made terminal or the ResolveShootingFreeThrows case individually — that splits one count across four sites and is exactly the multi-site-drop bug class."

**The denominator guard.**
`ShotResolutions` counts all seven Roll H outcomes (FGA + MissFouled = ShotResolutions). This guard exists because the points reconciliation check (`Points == 2*(FGM−3PM) + 3*3PM + FTM`) is blind to the attempts denominator — `MissFouled` scores zero, so a wrong FGA definition cannot be caught by the reconciliation alone. If FGA ever drifts to include a fouled miss, `FGA + MissFouled` overshoots `ShotResolutions` and the denominator guard fails loud.

**FTA/FTM at two sites.**
Free-throw attempts and makes are tallied at the two `DriveFreeThrows` call sites: `ResolveFreeThrows` (bonus/non-shooting FT trips) and `ResolveShootingFreeThrows` (shooting foul trips). In both cases `ftPoints == ftMakes` inside `DriveFreeThrows` (confirmed in source), so the spin count is FTA and the returned `ftPoints` is FTM — no new out-parameter needed.

**ORB% — combined-only.**
The ORB counters tally at the two rebound-resolution sites: `ResolveRebound` (Roll I, after `RollI.Execute`) and `ResolveFTRebound` (Roll M, after `RollM.Execute`). At each site:
- `Terminal("DefensiveRebound")` → `orbChances++` (defense won)
- `Continue(ResolveOffensiveRebound)` → `orbChances++; orbWon++` (offense won)
- All other arms (fouls, OOB, jump-ball) → not counted (not a secured board)

Emmett's call: the print section reports the combined headline rate only (`OrbWon / OrbChances` summed across all sources). No per-source breakdown (FG-miss / block / FT) is printed. The counters accumulate a single pair — not three separate pairs — since the breakdown lines were dropped. The combined rate is the box-score-comparable figure: standard ORB% already includes rebounds off missed free throws, so the combined number is the one to aim at calibration.

**Governor pass-through.**
The Governor does not add the counters to `GovernorRunResult`'s aggregate fields. The observation harness sums them across `records` itself (matching how every existing sentinel is computed). `PossessionRecord` gains 10 new positional parameters with default values of 0; the `NoShot` synthesized possession (no resolver call) stays at 0 naturally.

**The reconciliation identity.**
`Points == 2*(FGM − 3PM) + 3*3PM + FTM` holds because:
- A clean made basket: FGM=1, points=2 or 3 depending on zone
- An and-1 (`MadeAndFouled`): FGM=1, FTM=0 or 1, points= basket + FT
- A `MissFouled`: FGA=0 (excluded), FTM=k, points=k
- Every other FGA (Miss, Blocked, OOB): FGM=0, FTM=0, points=0
- Bonus FT trip (ResolveFreeThrows): FGA=0, FTM=k, points=k

The identity was validated by Python Monte Carlo (23/23 cases) before any C# was written.


---

## Per-Zone Shooting Counters (Session 50)

**What this session adds.** Eight integer counters extend the v1 set: `RimFga`/`RimFgm`, `ShortFga`/`ShortFgm`, `MidFga`/`MidFgm`, `LongFga`/`LongFgm`. The Three zone reuses the existing `ThreePa`/`ThreePm` pair rather than adding a redundant ninth/tenth, so the five zones are covered by four new pairs plus Three. Same shape as the v1 counters: init-only fields on `RoutingOutcome`, threaded through `PossessionRecord`, reduced in the harness. No weight or routing moved.

**One chokepoint, binned by zone.** Every field-goal attempt already passes through `IntoShotResolution` (the case that calls `RollH.Execute`), where the v1 FGA/FGM tally lives. The per-zone bin happens at that same single site: a switch on the stamped `ShotLocation` routes the attempt (and the make, if any) into exactly one of the five zone counters. No second site, no new field on any outcome type — the zone is already on `result.State`. This is the single-chokepoint discipline from the v1 FGA work, extended.

**The bin-integrity guard.** Two mechanical checks pin the zone split to the totals: `RimFga + ShortFga + MidFga + LongFga + ThreePa == FGA`, and the same for makes against FGM. This is the per-zone analog of the v1 denominator guard. If a shot ever failed to bin (e.g. a null `ShotLocation`), the sums fall short of FGA/FGM and the check fails loud rather than silently distorting a zone's FG%. Validated by Python Monte Carlo (9/9 bin cases) before delivery.

**What it exposed.** The combined 57.8% FG% decomposes as Rim 67.9% / Short 64.5% / Mid 49.3% / Long 48.5% / Three 49.7%, with attempt shares Rim 32% / Short 16% / Mid 16% / Long 10% / Three 25%. The five reconstruct the combined figure exactly. The reading is decisive: the high FG% is make-rate, not shot selection (the mid-heavy mix, if anything, suppresses FG%). This is the data the calibration plan below is built on.

---

## Shooting-Curve Calibration Plan (Session 50)

**Status: EXECUTED in-session (Session 50) — see "Shooting-Curve Calibration — Executed" at the end of this doc for the final parameters and the validated result.** The per-zone counters above exposed the shooting numbers; this section records the calibration decisions reached conversationally. (The re-fit was originally scoped for a separate session; Emmett chose to execute it in the same conversation once the design was settled.)

**Where the make rate comes from.** Roll H's make% is a per-zone bounded logistic owned by `RollHConfig`: `make = Floor + (Ceiling − Floor) / (1 + exp(−K·(rating − Midpoint)))`, with its own Floor/Ceiling/K/Midpoint per zone (five sets, living in the class defaults — `config.json` does not override them). The rating fed in is the shooter's zone-relevant attribute (Three/Long→Outside, Mid→Mid, Short→Close, Rim→Finishing), slid by the matchup (`Matchup.EffectiveRating` = own rating + skill-gap shift + athletic-gap shift, both odd and zero at an even matchup). These five logistic parameter sets are the calibration dials.

**Why the game FG% is high (the decisive trace).** At an even (rating-50) matchup the current curve already returns ~the real targets: Three 34.3%, Rim 61.4%, Long 37.2%, Mid 41.2%, Short 49.4%. Three/Rim/Long are essentially on target at 50. The game reads ~50% threes / ~68% rim only because the test rosters are rated ~64–67 in their shooting skills — they sit on the upper part of the curve. Feeding the actual roster ratings through the logistic reproduces the observed per-zone FG% almost to the decimal (the rim gap is blocked shots). The matchup shift is minor (a 10–20 point rating gap moves effective rating ~1–4 points). So the FG% problem is not a 13-point miscalibration of the curve — it is above-average rosters read against a curve that is correct at the average.

**Decision — 50 is absolute average.** On the 1–99 scale, 50 is dead-on average (25 = below average, 75 = above). At equal context a 50-rated shooter vs a 50-rated defender cancels to the zone's average make rate (e.g. 34% from three). The engine already implements exactly this — the two gap-shift terms are zero at 50-vs-50, so the effective rating stays 50. This is the level-flat principle made concrete: one curve for all divisions; D2/D3/JUCO differ by where their players' ratings fall, not by a separate curve.

**Decision — the curves are centered right but too steep.** At an even matchup the current curve gives a rating-99 three-shooter ~62% and a rating-1 shooter ~6% — roughly 2–3× the real spread (real elite ~44%, real floor ~27%). An over-steep curve means dominance is partly *imposed by the curve* rather than emerging from attributes, which cuts against the project's thesis. Calibration = flatten all five curves: floors up, ceilings down, the 50-anchor held at the targets. The Short zone (49.4% at rating 50 vs ~43% target) is the one mid-anchor that also comes down.

**The agreed anchors** (rough, 2015–2025 D1 style; even matchup unless noted):

| Zone | rating 1 | rating 50 | rating 99 even | rating 99 maxed |
|------|----------|-----------|----------------|-----------------|
| Rim   | ~48% | 61% | ~73% | ~80% |
| Short | ~30% | 43% | ~55% | ~63% |
| Mid   | ~26% | 39% | ~51% | ~59% |
| Long  | ~24% | 36% | ~49% | ~57% |
| Three | ~22% | 34% | ~50% | ~60% |

Emmett set the Three endpoints explicitly: a 99 three-shooter tops at ~50% at an even matchup and ~60% with every advantage (the matchup shift owns the 50→60 band; the logistic itself tops near 50 at even). The rating-1 column is the worst shooter in the file at even; max disadvantage pushes a bit below. The 50-row is the locked target set. The gentle middle is the point: a 16-point rating gap (e.g. 46 vs 62, both a B/C grade) maps to ~5% make difference — inside the season-to-season noise band (a ~100–150-attempt season carries a ±~4% binomial SE; you need a true gap of >~10% to distinguish two shooters), so nearby ratings are correctly indistinguishable.

**Principle — era lives in the shot mix, not these curves.** Per-zone make rates are ~era-invariant (a mid-range jumper hit ~39% in 1995 and ~39% in 2020; what changed is how often it was taken). "Modern (minimal mid-range) vs 1990s (mid-range-heavy)" is a Roll G location-weight profile swapped later, on top of fixed make curves. Calibrate the curves once now; the era selector drops in without redoing them.

**Principle — the real at-scale calibration target is a healthy strategy space.** Matching D1 aggregate FG% is the small-scale check. The harder, truer test arrives only when 350 teams of varying talent play full conference/non-conference schedules: no style should be bizarre-dominant, and none utterly non-viable. Because elite ratings will be rare in the player population (a rating-distribution decision that lands in the player-model layer), the exact curve endpoints barely move league aggregates — the 40–70 middle and the 50-anchor carry the numbers. So the endpoints are set roughly and not over-tuned; getting the two-team case "in the right neighborhood" is sufficient before moving on.

**Next step.** A fresh calibration session re-fits the five logistic curves to the three anchors per zone (editing the `RollHConfig` Floor/Ceiling/K/Midpoint defaults), validates with Python that each curve hits its 1/50/99 anchors and that a rating-50 roster reproduces the targets, then Emmett's harness confirms the aggregate. Hard dependency: this session's per-zone counters must be committed first — the SHOOTING BY ZONE readout is the verification surface.


## Shooting-Curve Calibration — Executed (Session 50)

The plan above was executed in the same conversation. The five per-zone logistic make curves in `RollHConfig` were re-fit to the agreed anchors, validated in Python, and confirmed green on the harness.

**Final curve parameters** (`RollHConfig` class defaults; `config.json` does not override them, so these defaults are the live calibration):

| Zone | Floor | Ceiling | K | Midpoint |
|------|-------|---------|------|----------|
| Three | 0.1608 | 0.6328 | 0.029646 | 65.8067 |
| Long  | 0.1934 | 0.6034 | 0.034190 | 59.5793 |
| Mid   | 0.1042 | 0.6447 | 0.021592 | 42.3369 |
| Short | 0.1316 | 0.7045 | 0.021592 | 42.3369 |
| Rim   | 0.3582 | 0.9527 | 0.024666 | 43.9840 |

**The carve correction (why the anchors are not the raw make targets).** The logistic returns the *clean* make rate — the conversion given the shot is neither blocked nor fouled. The harness (and a box score) report *observed* FG% = made / attempts, computed AFTER blocks and shooting fouls are carved out of the pie. The two differ by an affine map per zone: `observed = slope·makePct + intercept`, where `slope = (1 − block − foul)/FGA`, `intercept = (foul·mafFraction)/FGA`, and `FGA = 1 − foul·(1 − mafFraction)`. The agreed anchors are *observed* targets, so each was inverted through its zone's block/foul/maf rates to get the makePct the logistic must output. The effect is negligible on the perimeter (three carve ≈ 1%) and large at the rim (block 0.12, foul 0.20, maf 0.35 → slope 0.78, intercept 0.08), which is why the rim make ceiling sits at ~0.95 to net ~73% observed. **Coupling flag:** this ties the rim/short make anchors to the Roll H block/foul baselines; if those move, the rim/short make curves must be re-derived. Noted in `RollHConfig.cs`.

**Long ≥ Three.** Long's rating-99 even anchor was nudged 49→51% (above Three's 50%) so a long two stays at or above a three at every rating; otherwise the curves crossed at the elite end.

**Phase 6 (f) threshold.** The harness check that a strong defender lowers the generator's make rate demanded a >5-point drop — a value calibrated to the retired steep curve. On the flattened curve a *skill-only* strong defender (PerimD 90 vs a 50 shooter, even athleticism) lowers the three by ~4.9 points, correct by design: a skilled-but-not-more-athletic defender should only nudge a shooter. The floor was relaxed to 0.03. The direction (strong defender lowers make) is the invariant; the magnitude is a design quantity, not an invariant.

**Athletic vs skill (confirmation, not a change).** The engine already encodes "athletic separation suppresses a shooter harder than a skill gap" via DEC-5 (physical gap exponent 2.7 > skill 2.0). The two axes cross at a 25-point gap (= ReferenceScale): below it skill is marginally steeper, above it athletic runs away. Worked example, good shooter (Outside 78) at Three: vs an elite-skill/even-athlete defender 46→43% (barely dented), vs a much-more-athletic defender 46→39% (dragged down), vs both 46→36%. If a future pass wants athletic to dominate at *moderate* gaps too, the lever is a smaller physical ReferenceScale or a higher PhysicalSteepness — left unchanged for now.

**Validated result (frozen-corpus-v1, 1000 games; Run 4 in observations.md):**
- Combined FG% 57.8% → 50.4%. A rating-50 roster nets 45.1% (real D1 average); the test rosters read ~50% because their shooting ratings average ~64–67 (above average) — the intended consequence of "50 = average," not a hot curve.
- Per-zone FG% (was → now): Rim 67.9→64.5, Short 64.5→48.3, Mid 49.3→42.3, Long 48.5→41.9, Three 49.7→41.7 — a real efficiency gradient Three < Long < Mid < Short < Rim.
- Combined PPP 1.19 → 1.08. FT%, shot mix, ORB%, FTr unchanged (the calibration touched only the make curves).


## Roll E: Attribute-Driven Usage Selection (Session 51)

**What this is.** Roll E selection share = who takes the shot this possession, weighted by scoring ability and self-creation. Not who handles the ball — a pass-first guard who rarely shoots is correctly low here. The seam was already built; this session replaced the flat 20% placeholder with a real attribute-driven generator without touching Roll E, its interface, or the resolver.

**Formula.** Per-player usage score:
```
score = 0.35 * SelfCreation
      + 0.30 * (Close + PostMoves) / 2
      + 0.35 * (Outside + Mid + Finishing) / 3
```
Clamped to `max(score, MinUsageScore)` before the sharpening exponent. The (Close+PostMoves)/2 term ensures a high-post-moves, high-close center earns meaningful usage even with modest SelfCreation — Emmett's explicit basketball call. Passing, Playmaking, BallHandling, BasketballIQ intentionally excluded (Roll E is who *takes* the shot).

**Sharpening exponent.** Raw scores → `score^UsageExponent` → normalize. At the default (2.0) a realistic D1 alpha lands ~34% and a Rodman-type is held by the floor.

**Hard constraints — constrained redistribution (water-filling).** Floor and rail are not soft nudges:
- `UsageFloor` (default 0.09): minimum share for any populated slot. Anchored to the Rodman-era NBA floor (~8–9%), which compresses toward ~9–10% in college.
- `UsageRail` (default 0.52): hard cap on any single slot. Only reachable by absurd talent gaps; realistic rosters never approach it (the floor hands teammates guaranteed shots, suppressing the alpha's raw ceiling).
- Both enforced by iterative constrained redistribution. Naive double-renormalize was rejected: clamping a railed slot reduces the pie, which can push a floored slot back under the floor on renormalization.

**Rail feasibility standdown.** When `populatedCount × UsageRail < 1.0` (only possible with thin test rosters), the rail is skipped. Normal five-man game: 5 × 0.52 = 2.6 ≥ 1.0 — always active.

**Coupling.** `UsageExponent` and `UsageFloor` are coupled: raising the floor lowers the alpha's realistic ceiling at any fixed exponent, because floors hand teammates guaranteed shots. Calibrate them together, not independently.

**FastBreak passthrough.** When `state.FastBreak == true`, the generator returns the existing `cfg.TransitionSlot1..5` pie byte-for-byte. The transition selection attribute model is deferred.

**Null-slot handling.** Null/unpopulated slots receive 0.0 and can never be selected. Floor and rail apply only to populated slots. Zero-populated fallback returns the flat Base* pie (test-only; a real game always has five offense slots filled).

**Roll E share ≠ box-score USG%.** Roll E fires only when a possession reaches player selection. Turnovers and violations before Roll E peel possessions off, and USG% folds in FTA and weights turnovers differently. Reconciling Roll E selection share with true USG% is a calibration job deferred to when both Roll E and the usage→efficiency curve are tuned together.

**Config fields added (all calibration placeholders):**
- `UsageExponent`: tilt strength. Invariant: > 0. Default: 2.0.
- `UsageFloor`: guaranteed minimum share. Invariant: ≥ 0, and 5 × floor < 1.0. Default: 0.09.
- `UsageRail`: hard cap per slot. Invariant: > UsageFloor and ≤ 1.0. Default: 0.52.
- `MinUsageScore`: positivity guard before the exponent. Invariant: > 0. Default: 1.0.

**Harness check shape.** `RollESelectionBatchCheck` now: seats a known five-man test roster in a local game, asserts empirical convergence to the generator's own pie (not a hardcoded 20%), asserts Alpha slot > 2× Rodman slot (the regression guard for "a star separates"), asserts FastBreak pie exactly equals cfg.Transition* weights. Call site takes `cfgD` to construct the local game.

**Observation delta.** Aggregate stats unchanged from Session 50. Correct: Roll E changes *who* gets the ball, not *what happens* to them. The efficiency coupling arrives with the usage→efficiency curve (next build).


## Phase 17: Usage → Efficiency Curve (Session 52)

**What this is.** The thesis of Roll E is that shooting share concentration should cost efficiency — a specialist forced into heavy use should shoot worse than a versatile player carrying the same load. Phase 17 delivers that coupling: volume pressure flows from Roll E through Roll G (where it bends the shot diet) into Roll H (where it penalizes the make rate).

**Two new per-possession facts.**
- `UsagePressure` (stamped by Roll E): `max(0, finalShare − equalShare)`, where `equalShare = 1.0 / populated`. Zero below the equal share (no penalty for carrying a normal load). Null until Roll E runs; null on FastBreak (no volume load on a transition possession). Cleared to null by Roll K's `ResetOffense`.
- `UsageResidualPressure` (stamped by Roll G): the load Roll G could NOT absorb into a wider shot diet. Zero when fully absorbed (versatile shooter, ordinary defense). Positive for a forced specialist. Cleared to null by Roll K's `ResetOffense`.

**The two-stage mechanism.**
1. **Roll G — diet shift (what the player does under load):** `requestedShift = pressure × PressureShiftScale`. The player's authored tendencies define his *intrinsic capacity* to diversify (spread = large capacity; one-zone specialist = near zero). The defense's matchup-bent profile defines how much dominant-zone mass is actually available to move. `absorbed = min(requestedShift, intrinsicCapacity, bentDomMass × PressureShiftCapFraction)`. What cannot be absorbed becomes residual. The result: a versatile player under load actually diversifies his shot diet; a forced specialist cannot and carries the load forward to Roll H.
2. **Roll H — efficiency penalties (what the cost is):** Two terms applied after the matchup logistic, before `BuildRealPie`: (b) volume-tax `makePct *= (1 − pressure × PressureVolumeTaxScale)` — small, hits everyone carrying load; (c) residual penalty `makePct -= residual × PressureResidualPenaltyScale` — large, hits only the specialist. Attribution is derivable from the two separate scalars in the harness output.

**Why these are separate (not merged into one penalty).** The volume-tax is real even for a versatile player: volume load is taxing regardless of shot variety. The residual penalty is specifically the efficiency loss of *being unable to vary*. Merging them would hide the specialist vs versatile distinction — the exact thesis the system is supposed to demonstrate. Keeping them named also means the harness output shows the split (Phase 17 (b): `vol-tax=2.5pts  residual-penalty=12.0pts`).

**Zero-pressure branch-skip.** When both scalars are 0.0 (or null), the Phase 17 block in `RollHGenerator` is a complete branch-skip. The zero-pressure case is numerically identical to pre-build behavior — confirmed by Phase 17 (a). This is the regression guard.

**The specialist/versatile split (design targets, validated):**
- Specialist at rail (~0.32 pressure): ~10–15pt drop, almost entirely from residual (intrinsicCapacity ≈ 0.10, residual ≈ 0.06). Harness: **11.7pts** (2.5 vol-tax + 12.0 residual). ✓
- Versatile player at same rail: ~2–4pt drop, almost entirely from vol-tax (residual ≈ 0). Harness: **1.5pts**. ✓
- Ordering invariant: specialist drop >> versatile drop at the same pressure. ✓

**FastBreak exemption.** `UsagePressure` is stamped as 0.0 on FastBreak at Roll E (no volume concentration on a transition possession). Roll G returns residual 0.0 on FastBreak unconditionally. Roll H sees 0.0/0.0 and branch-skips. Confirmed by Phase 17 (f).

**Four calibration dials (all in config.json — no recompile needed):**
| Field | Config file | Effect | Default |
|---|---|---|---|
| `PressureShiftScale` | RollGConfig | Diet-shift magnitude (0 = ablate diet shift) | 0.5 |
| `PressureShiftCapFraction` | RollGConfig | Max fraction of dominant zone movable | 0.8 |
| `PressureVolumeTaxScale` | RollHConfig | Vol-tax multiplier | 0.12 |
| `PressureResidualPenaltyScale` | RollHConfig | Residual-penalty multiplier | 2.0 |

`PressureShiftScale = 0` ablates the diet shift (residual always 0; only vol-tax fires). All four at 0 fully ablates Phase 17 while leaving plumbing intact — useful for calibration comparison.

**Interface widening (engineering note).** `_rollEGenerator` and `_rollGGenerator` in `Resolver` were widened from the base pie interfaces to derived `IRollEGenerationProvider` / `IRollGGenerationProvider`. Both stubs implement the new interfaces (returning zero pressures/residual), so all 20 harness Resolver construction sites compile unchanged. The base interfaces remain for callers that only need the pie.

**Deferred.** The four config dials are calibration placeholders. The correct values emerge once rosters have real star/role distinctions — a realistic alpha commands 35–40% share, producing measurable pressure. On the current test roster (near-equal shares) the effect is present but small at game scale. Calibration of these dials is the natural follow-on once player-model depth exists.

**Observation run.** FG% and PPP unchanged from Session 51 (test-roster shares are near-equal; pressures are small). The mechanism is wired, proven, and waiting for realistic usage concentration.


## Phase 18: Roll L Real Generator — FreeThrow Attribute Wired (Session 53)

**What this is.** Roll L has always been the engine's purest primitive — one free throw, one pie, make or miss. Phase 18 makes it the first roll to read the shooter's own attribute directly as the make probability. No logistic, no matchup, no context modifier: `makeProbability = player.FreeThrow / 100.0`. This is the cleanest 1:1 in the entire model.

**FreeThrow is absolute, not relative.** Every other attribute is on a 50 = average relative scale. FreeThrow is literal: a 72-rated shooter makes exactly 72% of free throws. The value is a real-world percentage — 72 = a typical D1 average, 85 = a very good shooter, 55 = a poor one. No calibration pass is needed; the attribute IS the rate.

**The generator is the simplest real generator in the engine.** `RollLGenerator.Generate(state)`:
1. If `state.SelectedSlot` is null → use `config.MakeProbability` (fallback).
2. If `state.SelectedSlot` is non-null, walk `game.RosterFor(state.Offense).PlayerAt(slot)`. If the player is null (unpopulated slot) → use `config.MakeProbability` (fallback).
3. Otherwise: `makeProbability = player.FreeThrow / 100.0`.
4. `Math.Clamp(makeProbability, 0.0, 1.0)` as a safety net.
5. Build and return `Pie<FreeThrowOutcome>` with Make = makeProbability, Miss = 1.0 − makeProbability.

**`RoadMakePenalty` is dormant.** The `RollLConfig.RoadMakePenalty` field (0.0) is a documented seam for a future home/road FT effect. The real generator does NOT read it — not even conditionally. The principle: do not introduce any contextual modifier in the same build that establishes FreeThrow as an absolute attribute. The seam is named; the build that wires it is its own session.

**The null-slot fallback is a named loose end, not a bug.** Not all free throw attempts are shooter-attributed. Bonus foul trips that arrive before Roll E has selected a shooter carry `SelectedSlot = null`. The generator falls back to `config.MakeProbability` (72%) for these. The reported FT% is therefore a blend of player-attributed ratings and the 72% flat fallback. This is visible in the observation run and stress test output via the printed note. The fix (passing shooter identity through the bonus foul path) is a future task, not a Phase 18 scope item.

**Interface pattern.** `IRollLPieGenerator` follows the same pattern as `IRollMPieGenerator`: single one-arg `Generate(PossessionState state)` method; both the stub and the real generator implement it; the Resolver field is typed to the interface. The stub ignores its state parameter; the real generator reads `SelectedSlot` off it.

**Four real-game construction sites swapped; 18 stub sites retained.** The four sites that use real-roster games (Main, RunGame, ObservationRunV1, StressTestArchetypeRosters) now use `RollLGenerator`. The 18 sites that construct empty-roster isolation check games keep the stub — the stub is the correct tool for tests that have no rosters.

**Main construction order fix.** The original stub was declared before `game` was constructed (line 43, before line 48). `RollLGenerator` requires `game`. The fix: remove the early stub declaration, add `var rollLGenerator = new RollLGenerator(cfgL, game)` after `SeatStartersFromConfig` alongside the other real generators (G, H, I, M, F, B, A, E). Same move Phase 11 made for `rollMGenerator`.

**Harness check extended.** `RollLFreeThrowCheck` now has two parts: (a) the existing stub-driven raw make rate and per-trip routing checks (unchanged), and (b) a new real-generator check proving: p72 → 0.720000 within epsilon; p85 → 0.850000 within epsilon; null-slot fallback → config.MakeProbability; empty-slot fallback → config.MakeProbability. Part (b) uses `Pie.Slices.First(s => s.Outcome == FreeThrowOutcome.Make).Weight` to read the make weight (there is no `WeightOf` method on `Pie<T>`).

**Stress test FT% readings (validated):**
- Elite team vs Weak team: ~68% vs ~47% — strong separation driven by authored ratings
- Average vs Average: ~53% — average archetypes have FreeThrow in the ~50s range
- Elite vs Elite: ~64–65% — elite archetypes have higher FreeThrow ratings
- The attribute differentiates meaningfully across all 8 archetype buckets; mirror gap 1.4% (tight)


## Phase 19: Roll E Live in ObservationRunV1 + StressTest (Session 54)

**What this is.** Phase 19 is not a new generator or a math change — it is the activation of a dormant subsystem. The Phase 17 usage→efficiency chain (Roll E stamps pressure → Roll G bends shot diet → Roll H penalizes make rate) was fully wired and proven in isolation but had never fired at game scale. The reason: `RollEStubPieGenerator.GenerateWithPressure` returns an all-zeros pressures array, and both game-scale runs (ObservationRunV1 and StressTestArchetypeRosters) were still using the stub. Swapping those two sites to `RollEGenerator` turns on the entire Phase 17 chain at game scale for the first time.

**The observation discipline.** Because two competing effects fight each other — usage concentration routes attempts to a better scorer (raises efficiency) and the efficiency penalty taxes concentrated load (lowers it) — the net direction was genuinely uncertain before the run. The session deliberately did not predict direction. It reported what the run produced. This is the correct pattern for any session that activates a dormant subsystem: measure first, grade later.

**StarVsBalanced — first live evidence the chain works end-to-end.** The star team (Team A) wins 60.4% with PPP 1.041 vs 0.987 for the balanced team. More importantly, the shot mix shifted in the expected direction: star team Rim 36% / Three 23% vs balanced Rim 29% / Three 26%. The alpha is commanding more rim attempts, which is what the usage-weighted selection pie predicts for a player whose scoring attributes favor the rim. The efficiency penalty did not cancel the star's advantage — it modulated it. This is the first game-scale confirmation that the thesis holds directionally.

**FreeThrow authoring principle (established by this run).** Phase 19's most significant non-engine finding: FreeThrow is the one attribute in the model that carries **zero correlation with tier or athletic ability**. The archetype generator currently scales FreeThrow down with talent tier, producing unrealistically low FT% for average and weak teams (~53% and ~48% vs real D1 average of ~69%). The correct authoring rule: FreeThrow is drawn from its own independent distribution regardless of tier. The only permitted relationship is a weak positive skew toward high-Outside players (shared shooting mechanics). No other attribute in the model shares this property — athleticism, finishing, defense, rebounding, even passing all have meaningful tier correlation. FreeThrow does not. The fix belongs to the archetype builder, not the engine.

**Sites that stayed stub — why each is correct.** Isolation checks (e.g. `RollFActionBatchCheck`) construct a Resolver with a stub Roll E because they assert rates against a flat, known selection pie. A real, roster-dependent Roll E would make the selection share depend on whichever players happen to be seated, breaking fixed expectations. The stub is the right instrument for an isolation test — same principle that kept 18 stub sites in Phase 18. `RunGame` stays stub because its fidelity question (consistently all-stub vs consistently production-like) is a design call deferred to Emmett.

**What remains after Phase 19.** The usage→efficiency chain is live and directionally validated. The calibration question — whether the current scalar defaults produce realistic per-player efficiency curves at actual D1 usage loads — is entirely open. The FreeThrow authoring fix is a named follow-on. The `RunGame` fidelity question is parked for Emmett's decision.


## Phase 21: Per-Slot FGA Usage Readout (Session 55)

**What this is.** Phase 21 makes the Phase 17 usage→efficiency chain directly observable: instead of inferring slot concentration from PPP and FG% shifts, the harness now prints exactly what fraction of each team's FGA each slot took. The instrumentation is purely additive — no engine math changes.

**Attribution chokepoint.** The slot-binning switch lives at the single `IntoShotResolution` case in `Route()` — the only path every field-goal attempt passes through, including putbacks. Placement here gives completeness automatically: every FGA that the engine counts must increment exactly one slot counter (or the unattributed counter).

**Normal-shot identity chain.** Roll E stamps `SelectedSlot` when the shooter is selected. No intermediate routing case (IntoHalfcourtSet, IntoPlayerAction, IntoShotType) clears it. `SelectedSlot` is non-null and correct at IntoShotResolution for normal possessions.

**Putback identity (Roll K carry-through).** On a putback, Roll K's PutBack arm uses `state with { ShotType = ShotLocation.Rim }` — only `ShotType` is modified. `SelectedSlot` is not in the `with` expression and carries through unchanged. Contrast: the `ResetOffense` arm explicitly wipes `SelectedSlot = null` — preserve-vs-wipe is intentional and symmetric. Putback FGAs are credited to the original Roll E shooter's slot.

**The `SlotUnattributedFga` counter.** The slot-binning switch has a `default: slotUnattributedFga++; break;` arm that fires when `SelectedSlot` is null. This scenario is exclusively the bonus-free-throw putback path: a pre-shot foul sends the team to the line before Roll E runs → last FT missed → Roll M offensive rebound → Roll K PutBack → IntoShotResolution with null SelectedSlot. Roll K correctly preserves null; the unattributed counter captures it. The completeness invariant holds: `Slot1Fga+…+Slot5Fga+SlotUnattributedFga == Fga` for every possession. In practice, ~0.2% of FGAs are unattributed.

**Plumbing layers.**
- `RoutingOutcome` (Resolver.cs): 6 new init-only fields (`Slot1Fga`–`Slot5Fga`, `SlotUnattributedFga`). All default 0 — same pure-append pattern as every prior observability field. One new local per counter in `Route()`; extended return statement.
- `PossessionRecord` (Governor.cs): 6 new defaulted params threaded from `RoutingOutcome` via the existing `possessionX` local pattern. No positional deconstruction exists anywhere in the codebase, so trailing defaulted params are backward-compatible.
- `ObservationRunV1` (harness): 12 running total accumulators (6 slot buckets × 2 sides); per-game slot-bin integrity check asserts `recS1+…+recS5+recSU == recFga`; USAGE section prints slot percentages and Unattr.
- `StressTestArchetypeRosters` (harness): `VariantStats` gains 12 fields; game-loop accumulates all 12; per-bucket reconciliation check asserts `slotSum+unattr == aggFga` and feeds `failures` on mismatch; usage output lines.

**Not per-player — a named seam.** The slot counters are correct under fixed lineups. When substitutions arrive, a slot may be occupied by different players across a game and the counter will combine them. A separate player-ID attribution layer is required at that point. The per-slot counters are the designed foundation for that future layer.

**Bucket 5 (StarVsBalanced) as calibration reference.** Team A Slot 1 carries 29% of FGA vs ~16–23% for the balanced roster slots — the star's usage concentration is now directly readable. This is the input the Phase 17 efficiency penalty was designed to respond to.


## Phase 22: Per-Slot FGM Readout + Tier-Decoupled FreeThrow Authoring (Session 56)

### Item 1: Per-Slot FGM — the efficiency half of per-slot FGA

**What this is.** Phase 21 made slot concentration visible (FGA share per slot). Phase 22 adds the other half: FGM per slot, so `per-slot FG% = SlotNFgm / SlotNFga` falls out directly without any new rate-tracking machinery. The architecture is identical to Phase 21 — one new counter family threaded `RoutingOutcome → PossessionRecord → harness`, with two invariants and an unattributed bucket.

**Placement discipline.** The per-slot FGM switch lives INSIDE the `Made or MadeAndFouled` block in `Route()`'s IntoShotResolution case — makes only. The Phase 21 FGA switch stays OUTSIDE that block, in the non-MissFouled else scope — all official FGAs. This placement is load-bearing: it is what makes the subset invariant (FGM ≤ FGA per slot) a structural truth rather than a sanity bound.

**Two invariants, both asserted.**
- *Make completeness:* `Slot1Fgm+…+Slot5Fgm+SlotUnattributedFgm == Fgm`. Asserted per-game in ObsRun and per-bucket in StressTest.
- *Subset invariant:* per-slot `SlotNFgm ≤ SlotNFga` and `SlotUnattributedFgm ≤ SlotUnattributedFga`. This is the diagnostic completeness alone misses: a mismatch that nets to the correct global FGM (one slot over-credited, another under) would pass completeness but fail the subset check. Asserted as a hard failure in both harness sections.

**`SlotUnattributedFgm`** is the exact analog of Phase 21's `SlotUnattributedFga` — it fires when a bonus-FT putback (the only null-SelectedSlot path) makes its shot. Without the `default` arm the make completeness invariant fails, identical to the Phase 21 lesson. The arm is present.

**Ceiling note.** Per-slot FG% is the honest ceiling of what this session can deliver. A real box score is per-*player* (points/reb/ast tied to a named person across a whole game, surviving substitutions). Per-slot counters blend all players who occupied a slot — a limitation only the future per-player identity layer resolves.

### Item 2: Tier-Decoupled FreeThrow Authoring

**The problem (established in Phase 19).** The archetype generator's original FreeThrow assignments used `Clamp(AtBaseline())` or `Clamp(AtStrength())` — both of which read the tier-scaled distribution center. The result was an artificially strong tier gradient: Weak teams shot ~47–48% at the line; Elite teams ~64–65%. Real-world top-50 team FT% shows D1, D2, and D3 leaders clustered in the same 75–81% band (2025–26 NCAA data), which does not support a large direct tier coupling.

**The model.** `DrawFreeThrow(outsideRating, heightRating, rng)` in the harness:
- **Base draw:** mean of 3 independent uniform draws on `[center±half]`, giving a bell-shaped distribution centered at `center`. `half=30` → SD≈10 → clear peak near 70 with the distribution reaching both bounds [45,95].
- **Center nudges (fixed-pivot, not tier-pivot):** `outsideNudge = ((Outside−50)/49) × 4.0` (up) and `heightNudge = −((Height−50)/49) × 3.0` (down). Both measured against the fixed constant 50, NOT the tier distribution center. This removes the direct read of `tp` (tier params) from the FreeThrow draw.
- **Clamp:** `Math.Max(45, Math.Min(95, (int)Math.Round(draw)))`. Creates small point-mass piles at exactly 45 and exactly 95 (confirmed small, ≤2.1% for the shapes tested).

**Why "tier-decoupled" not "tier-independent."** Because Outside and Height are themselves tier-scaled (they read `tp` for their own draws), a faint residual tier correlation survives through the nudge inputs. Measured before build: corr(FT, tier-rank) ≈ 0.016, Elite→Weak mean spread ≈ 0.4pt. This passes the stop condition (spread ≤2pt, |corr|≤0.10) and matches the basketball principle Emmett stated: the best free-throw shooter in the nation could be a D3 point guard. DATA PROVENANCE: 2025-26 NCAA top-50 team FT% (D1/D2/D3) supplied by Emmett; used only to justify removing strong direct tier coupling; full-population quantiles for calibrating any residual division effect are a future calibration-pass input.

**Option A hoisting — rng-stream implication.** `Player` is a `sealed class` with `{get; init;}` properties — not a record, so `with` expressions don't compile and `init` properties can't be reassigned post-construction. Passing `p.Outside` or `p.Height` inside the same `new Player(name) { … }` initializer is also illegal (properties are not yet assigned). Solution: hoist the two attribute draws to named locals at the top of each case, before `new Player(name)`. The locals are used both at their property assignment positions in the initializer and as arguments to `DrawFreeThrow`. Side-effect: the rng stream shifts for every attribute in every archetype (Outside and Height draws now happen first, before the initializer's other draws). This is accepted because the frozen corpus uses the hardcoded named roster (unaffected) and the stress test asserts on distributional aggregates (no per-player golden-output dependency).

**Six calibration placeholders.** Center (70), min (45), max (95), Outside nudge max (±4), Height nudge max (±3), and `half` (30) are all first-pass values. The harness histograms and team-level FT% distribution (particularly whether the top-team FT% band reaches 75–81% as in real data) will inform tuning. One named finding from this run: Elite tier's mean FT% runs ~66–68%, below the average-tier ~70–71%. The mechanism is correct — Elite bigs have high AtStrength Height values (75–88 range), producing larger downward Height nudges — but whether the real FT% distribution should show this gradient requires full-population data.

---

## Phase 23: Named Player Attribution Under Fixed Lineups

### The attribution model — exact vs. probabilistic

Attribution runs as a post-game pass over the completed `GovernorRunResult`. It divides into two families:

**Exact attribution** (no draws): stats the engine already records to a specific slot — FGA, FGM, 3PA, 3PM, FTA, FTM. The engine knows which slot shot and which slot was fouled. Phase 23 adds four new `SlotGroup` fields to `RoutingOutcome` (and threads them through `PossessionRecord`) to carry this information. Zero new `IRng` calls.

**Probabilistic credit** (weighted draws): stats where the engine does not track a specific actor — defensive rebounder, stealer, blocker, turnover on pre-Roll-E possessions. The harness draws from a weighted distribution after the game completes. Weights are attribute composites (e.g. DReb weight = Height + Strength + Wingspan + DefensiveRebounding). These are credit assignments, not proof of who produced the event. The journal and box-score header document this distinction explicitly.

**TO is hybrid:** if Roll E had already selected a player when the turnover fired, `TurnoverOffSlot` carries that slot number and the credit is exact. If the turnover fired before Roll E (backcourt violation, entry turnover), `TurnoverOffSlot` is null and the harness draws by `BallHandling` weight.

### Attribution RNG isolation

The attribution `Random(seed + 2)` is constructed fresh per game inside `AttributeGame`, after `governor.Run()` returns. It is never used during gameplay. Because it is seeded deterministically and called in a fixed order (same possession loop, same event sequence within each possession), `AttributeGame(result, game, seed)` is deterministic: the same inputs always produce the same `PlayerBoxTotals`. The same-seed reproducibility check (§5g) proves this mechanically: two calls on identical inputs produce `AllEqual` output.

The three RNGs per game are:
- `SystemRng(seed)` → Resolver (gameplay rolls)
- `SystemRng(seed + 1)` → Governor (end-of-half intent, clock draws)
- `Random(seed + 2)` → Attribution (post-run weighted draws only)

### PlayerId assignment — class vs. record

`Player` is a `sealed class`, not a record. `with` expressions and post-construction `init` reassignment both fail to compile. The correct pattern for stamping `PlayerId` is to construct the player with the ID already set, before the first `Roster.SetStarter` call. `StampPlayerId(Player p, int id)` is the harness helper that copies all 37 authored attributes into a new `Player(p.Name) { PlayerId = id, Close = p.Close, … }` initializer. The inline seating loop in `ObservationRunV1` calls `StampPlayerId(configs[i].ToPlayer(), newId)` and passes the result directly to `SetStarter` — no re-seating needed.

This pattern generalizes to substitutions: whoever constructs the incoming player is responsible for assigning their ID. `SeatStartersFromConfig` itself stays ID-agnostic (it is a config reader used in multiple contexts that do not need attribution); only the Phase 23 seating path in `ObservationRunV1` stamps IDs.

### SlotGroup

A `readonly record struct` carrying six counters: S1–S5 (the five on-court slots) plus `Unattr` (the unattributed bucket for null-SelectedSlot paths, e.g. bonus-FT putbacks where Roll E never ran). `Total` sums all six. The indexer `this[int slot]` maps 1–5 to S1–S5 and anything else to `Unattr`. `WithSlot(int slot, int delta)` returns a new `SlotGroup` with one bucket incremented — immutable accumulation pattern matching how the existing Phase 21/22 scalar counters work in `Route()`.

### The completeness invariant

For each exact-attribution family, the harness verifies:

```
Σ named-player counts == total − unattributed
```

This is an integer identity: every event either lands in a named slot or the unattributed bucket, with no rounding. Any deviation is a coding bug. The per-slot subset checks add a second layer: for each slot index (including Unattr), `3PM ≤ 3PA`, `3PA ≤ FGA` (possession-level), `FTM ≤ FTA`. These catch slot-level corruptions that aggregate completeness misses — the same lesson as Phase 22's subset invariant.

For weighted-credit families, the check is simpler but exact: every draw fires exactly one credit, so the sum of per-player credits must equal the engine event count (OReb won, DReb possessions, BlkCount, live-TO possessions, TO possessions). Any mismatch means a credit was silently dropped or doubled.

### Upward vs. downward validation

The BLK check proves downward (every `BlkCount` credit was distributed to a player) but not upward (the Resolver captured every blocked shot in `blkCount`). The upward invariant is protected by code placement — `blkCount++` sits in the `ShotResult.Blocked` arm of the `IntoShotResolution` block, which is the single Roll H chokepoint every field-goal attempt passes through. There is no independent block-event total in the current record from which upward validation could be computed mechanically; this limitation is documented in the journal and in the harness comment.

---

## Phase 24 — Attribution sanity check: controlled roster validation

### What this session validates and what it does not

The Phase 23 attribution machinery assigns probabilistic credit for weighted stats (rebounds, steals, blocks, pre-Roll-E turnovers) via `WeightedDraw` — a single-event lottery whose outcome depends on the weight function applied to each slot's player. Phase 23 proved the arithmetic is correct: counts are conserved, named-player totals reconcile with event totals, same-seed inputs produce identical outputs. What it did not prove is that the weight functions are pulling the right attributes.

Phase 24 answers that question with a controlled experiment rather than a proof of any individual event. No causal player exists for a defensive rebound — the engine has no rebound resolver that picks a named player mid-possession. `WeightedDraw` runs after the game, against the game's possession log. The sanity check proves that when extreme attribute contrasts are authored, extreme box-score contrasts appear in the expected direction. That is the strongest claim available given the architecture.

### Controlled roster design

Two symmetric teams (Rim Anchor in Slot 1, four perimeter role players in Slots 2–5) remove team-level confounds. Only within-team attribute spread drives the box-score contrast. The key weight function inputs:

- **DReb** (`Height + Strength + Wingspan + DefensiveRebounding`): Anchor = 367, Role = 105, ratio 3.50×
- **OReb** (`Height + Strength + Wingspan + OffensiveRebounding`): Anchor = 362, Role = 105, ratio 3.45×
- **BLK** (`RimProtection + Height + Wingspan + Vertical`): Anchor = 324, Role = 110, ratio 2.95×

Pre-validated at prompt-drafting time; re-confirmed against live source at build time. All three ratios exceed their thresholds by a comfortable margin, making the directional assertions conservative.

The 3PA assertion is not a pure attribution check — it is an integrated test of Roll E usage selection (who gets the possession), Roll G shot-type selection (what kind of shot), and exact 3PA slot attribution. A role player with ThreeTendency=60 and the anchor with ThreeTendency=1 produce a 39× ratio in 3PA, which validates that all three layers are connected and pulling the authored attributes.

### FT% as exact attribution verification

FreeThrow is the cleanest 1:1 in the model (`makeProbability = player.FreeThrow / 100.0` in RollLGenerator.cs). FTA/FTM are exact per-slot stats: the engine knows which slot was at the line from the possession record. The FT% directional assertion (Anchor FT% < 65% on FreeThrow=55; Role combined FT% > 72% on FreeThrow=78) therefore tests whether the exact slot attribution is correctly threaded end-to-end from Roll L through the possession record through `AttributeGame`. Any wiring break — wrong slot index, off-by-one, incorrect PlayerId — would deflect FT% toward 72% (the config fallback) and fail the assertion.

FoulDrawing is set asymmetrically (Anchor=30, Role=65) to ensure both sample gates are reachable in 200 games: anchor FTA ≈372 >> gate of 50; role FTA ≈3,228 >> gate of 200. Insufficient sample is a hard failure, not a silent pass.

### Duplication note

The invariant checks inside `AttributionSanityCheck` duplicate logic from `ObservationRunV1`. This is accepted for Phase 24 scope — the two call sites can diverge over time as rosters evolve. A future `ValidateAttributionTotals` shared-helper extraction is logged as a candidate refactor.


---

## Phase 25 — Shooting Foul Attribution

### What this session adds and what it does not

Phase 25 wires shooting-foul events from the resolver walk to the attribution pass and draws a weighted-credit fouling defender for each event. It does NOT introduce per-player foul tracking (individual foul ledger, foul-trouble logic), does NOT cover non-shooting fouls (team fouls, offensive fouls, loose-ball fouls), and does NOT calibrate the formula parameters.

### ShootingFoulEvent — the record

`ShootingFoulEvent(ShotLocation Zone, int ShooterSlot)` is a `readonly record struct` appended to the walk's `shootingFouls` list each time the resolver hits `ContinuationKind.ResolveShootingFreeThrows`. One event per `MadeAndFouled` / `MissFouled` resolution.

`ShooterSlot` is 1–5 when Roll E ran (the normal path) and 0 on the rare bonus-FT putback path where Roll E never fired (`PossessionState.SelectedSlot` was null at the edge). The 0 value is the "no matched man" sentinel; `DrawFoulingDefender` routes it to a flat fallback.

### Why a possession can carry more than one event

Roll K's PutBack arm routes back to `ContinuationKind.IntoShotResolution` with zone forced to `ShotLocation.Rim`. A putback can itself be fouled, generating a second `ResolveShootingFreeThrows` hit in the same resolver walk. The `List<ShootingFoulEvent>` in the walk accumulator (snapshotted to an array at the Terminal return) handles this naturally; a scalar count would not.

### The `?? 0` / `!` asymmetry at the edge

At `ResolveShootingFreeThrows`:

```csharp
shootingFouls.Add(new ShootingFoulEvent(
    c.State.ShotType!.Value,          // non-null: Roll G always stamps zone
    c.State.SelectedSlot?.Number ?? 0 // may be null: bonus-FT putback
));
```

`ShotType` is asserted non-null (`!`) because Roll G stamps the zone before Roll H fires the foul — this is always true on this edge. `SelectedSlot` is null-safe (`?? 0`) because the bonus-FT putback path (Roll K PutBack → `IntoShotResolution`, where Roll E never ran) reaches this edge with `SelectedSlot` null. This is a legitimate game path, not a bug; 0 is the sentinel, not a throw.

This pattern matches the pre-existing Phase 23 FTA/FTM slot reads in the same block.

### DrawFoulingDefender — the formula

```
interior(p) = p.Height + p.Strength + p.PostDefense   (unweighted; no MatchupConfig dependency)
meanInt = mean interior across all populated defending slots
```

**Matched-man weight** (slot index equals shooter's slot index):

| Zone | matchedShare | signedK |
|---|---|---|
| Rim | 0.50 | +0.50 |
| Short | 0.65 | +0.25 |
| Mid | 0.70 | 0.00 |
| Long | 0.80 | −0.25 |
| Three | 0.80 | −0.50 |

**Residual weight** (all other populated slots):

```
raw_i = exp(signedK × (interior_i − meanInt) / SCALE)
weight_i = (1 − matchedShare) × raw_i / Σraw
```

SCALE = 40.0. All zone values and SCALE are calibration placeholders.

**Direction:** positive `signedK` favors interior defenders on the residual (rim fouls — the help big rotates late); negative `signedK` favors perimeter defenders (three-point fouls — the closeout or switching guard is most likely to foul). Mid is zone-neutral (K=0, flat exponential, equal residual weight).

**Fallbacks (flat distribution over all populated defenders):**
- `shooterSlot == 0` (bonus-FT putback, no matched man)
- Matched slot is unpopulated

**Cumulative draw:** same shape as `WeightedDraw` (the existing TO/STL/DReb/OReb/BLK draws), using the separate `seed+3` RNG.

### RNG isolation: seed+3

The existing `AttributeGame` attribution draws all consume `Random(seed+2)` in a fixed order: TO → STL → DReb → OReb loop → BLK loop. Adding shooting-foul draws to that stream would shift every prior draw number. Phase 25 introduces `var foulRng = new Random(seed+3)` for the foul draws only, leaving the `seed+2` stream bit-for-bit unchanged. The reproducibility contract (`AllEqual` on two calls with identical inputs) is extended to include `ShFoul`.

### Completeness invariants

Two checks in `Phase25ShootingFoulAttributionCheck`:

**Side-specific reconciliation (exact integer identity):**
```
Σ ShFoul[Home player indices] == totalHomeDefEvents
Σ ShFoul[Away player indices] == totalAwayDefEvents
```
A side-reversal bug would pass a global check but fail a side check. Both sides are verified independently.

**Global completeness:**
```
Σ ShFoul[0..9] == Σ r.ShootingFouls.Count over all possessions
```

**No-zero defender:** every player must accrue > 0 SFL across 200 games. Zero fouls for a populated player indicates a dead draw path or wiring break.

### Calibration targets (first session)

All five matched-share values, all five signedK values, and SCALE=40.0 are calibration placeholders. With the Phase 24 controlled roster (Anchor interior=230, Perim interior=115, meanInt=138), SCALE=40 gives the interior big ~58% of the rim-shot residual probability. This is strong — the Anchor almost always gets blamed when a guard drives the rim without his matched defender available. Calibration should probably raise SCALE (weakening the tilt) once realistic rosters are in use. The first calibration target is to match real shot-chart fouling tendencies by zone.

The draft prompt estimated ~37% of the rim residual going to the big (computed at SCALE≈100). The chosen SCALE=40 is deliberate — erring toward a visible, measurable effect that will show up in box scores and can be tuned down. Invisible effects are harder to calibrate.


---

## Phase 26 — Slot/Archetype Cohort Box Scores + Slasher Shooting Floor

### What this session adds and what it does not

Phase 26 makes archetype-level stat distributions readable in the stress test by adding per-slot/archetype cohort box scores to every bucket. It also fixes the Slasher archetype's shooting floor so it functions as a below-average-but-functional perimeter threat. No engine changes. All changes are in `src/Charm.Harness/Program.cs`.

### Slasher shooting floor fix

Two changes to the `case PlayerArchetype.Slasher:` block in `MakePlayer`:
- `int slOutside = Clamp(AtBaseline())` (was `AtWeakness()`) — raises Outside from ~30 mean to ~50 mean
- `ThreeTendency = TStr(25, 40)` (was `TStr(5, 10)`) — raises ThreeTendency mean from ~7.5 to ~32.5

`FreeThrow = DrawFreeThrow(slOutside, slHeight, rng)` is unchanged; it automatically reads the higher `slOutside`. The design intent is "hits the open one" — below-average shooting skill but enough tendency to be a genuine floor spacing threat.

Blast radius: Buckets 5 (Slasher Elite star in StarVsBalanced), 6/7/8 (AthleticRoster has two Slashers). Buckets 1–4 unaffected.

### PlayerId stamping by logical team

**The problem.** The stress test alternates physical home/away every other game (`gameIndex % 2 == 1`). Before Phase 26, no PlayerId was ever assigned in the stress-test path — all players had PlayerId=0. `AttributeGame` guards `if (oi < 0 || oi >= 10) continue`, so PlayerId=0 → oi=-1 → every player is silently skipped → all-zero box score, no crash.

**The fix.** After the bucket→roster dispatch switch (once per variant, before any game is played), Team A players are stamped with PlayerId 1–5 and Team B players with PlayerId 6–10, using the existing `StampPlayerId` helper. The stamped `Player[]` arrays are what `SeatRoster` seats into the `GameState` every game. Because the ID is on the `Player` object (not derived from physical side), the same logical player lands in the same `AttributeGame` accumulator index regardless of which physical side they drew that game.

**Convention.** In all stress-test contexts: Team A = IDs 1–5, Team B = IDs 6–10. This is per-bucket; Buckets 7 and 8 are independent (in Bucket 8, Skill is Team A and gets IDs 1–5, Athletic is Team B and gets IDs 6–10 — the reverse of Bucket 7).

### Per-variant PlayerId contract validation

Immediately after stamping, before the first game of the variant, the code validates:
- Exactly 10 unique IDs across both arrays
- Min=1, max=10
- Team A array holds exactly {1, 2, 3, 4, 5}
- Team B array holds exactly {6, 7, 8, 9, 10}

A violation is a hard failure into `failures` (the variant is excluded). This catch is per-variant by design: a bucket-level no-zero check cannot detect a single mis-stamped variant hidden among nine good ones.

### Cohort accumulator scoping

Per-bucket cohort accumulators (`long[10]` arrays for all 12 stat families: FGA, FGM, 3PA, 3PM, FTA, FTM, OReb, DReb, Blk, Stl, To, ShFoul) are declared as locals inside the bucket loop, before the variant loop. They reset naturally when the bucket loop moves to the next bucket. They are NOT fields on `VariantStats` — cohort data is bucket-level reporting state, not per-variant team-performance state.

A per-variant `variantFga[10]` (declared inside the variant loop) resets each variant and serves as the primary per-slot corruption check after the variant's games complete.

### Atomic acceptance boundary

Within the game loop, the attribution and accumulation block sits between the mechanical checks and `vs.ValidGames++`:

```
count invariant check → continue on fail
AttributeGame() → continue on throw (game excluded from everything)
variantFga accumulation
cohort accumulation        ← no possible continue between here and ValidGames++
vs.ValidGames++
team-level accumulation (unchanged)
```

A game that fails attribution is excluded from both the cohort numerator and the `ValidGames` denominator. This ensures every denominator (team-level and cohort) is the same accepted-game set.

### What "cohort box score" means

Each bucket generates 10 different rosters from different seeds. A cohort row pools all 10 variants: it is the per-game average for one logical slot/archetype across 500 combined games (10 variants × 50 games). This is genuinely useful — it shows how an archetype performs in a matchup — but it is not an individual player's line (each variant generated a different player for that slot). Every cohort table carries a mandatory caveat header stating this.

True per-player box scores (one persistent player's line) await a single-roster context or the future persistent-universe layer.

### Cohort table format

Mirrors ObservationRunV1's per-player box score column set:
`PTS / FGA / FGM / FG% / 3PA / 3PM / 3P% / FTA / FTM / FT% / ORB / DRB / REB / STL / BLK / TO / SFL`

Rows labeled `[A] Slot1 — Slasher`, `[B] Slot2 — AthleticBig`, etc. The archetype name comes from the bucket's fixed composition array (not from any single player's generated Name). Bucket 5 prints a note that Slot 1 is the Elite-tier star.

Denominator: `totalValid` (total attribution-accepted games across all 10 variants), the same denominator used for team-level stats.

## Phase 27 Session 1: Defensive Attention Pie + Gravity/Spacing Rework + Roll H Make% (Session 61, 2026-06-18)

### What this is

The first half of the team-aggregate ("gravity") layer — the first session that gives the offensive five a collective identity beyond the sum of individual matchups. Builds the defensive attention pie (a 100-point allocation across the five offensive players), reworks gravity and spacing into real bounded formulas, computes an asymmetric team-openness interaction, and feeds these into Roll H (make/miss) three ways. Session 2 threads the same attention signal into selection (Roll E) and shot location (Roll G), and makes passing live.

### The two-pie model

The offense allocates a **usage pie** (Roll E — who shoots). The defense allocates an **attention pie** — 100 points across the five offensive players. They collide through the make-odds.

Attention is allocated by two drivers: **gravity** (rim pressure, which creates help/collapse attention) and **usage** (focal-point role — the defense keys on whoever the offense runs through, regardless of his gravity). Both are required: gravity-only allocation cannot represent a defense keying on a low-gravity focal shooter. The fixed-100-point pie produces the focal-point relativity (same player, opposite outcomes depending on what surrounds him) for free.

Attention is **relative allocation** — where the defense spends its 100 points. Absolute offensive danger lives in the team gravity/spacing aggregates and the openness interaction, not in the attention shares.

### Gravity formula (Player.cs, reworked)

`GravityContribution` replaced the wrong `(Close + Mid + Outside + Finishing) / 4` (which gave gravity weight to perimeter shooting) with a bounded [0,100] rim-pressure composite:

```
PerimeterAccess = avg(FirstStep, SelfCreation, Speed)
PostAccess      = avg(PostMoves, Strength)
Access          = max(PerimeterAccess, PostAccess) + 0.10 × min(PerimeterAccess, PostAccess)
GravityContribution = 0.35×Finishing + 0.25×Close + 0.30×Access + 0.10×Mid
```

Finishing and Close carry the highest weight (converting near the basket IS the threat). Access uses `max` of two routes (one route suffices) plus a small bounded versatility bonus. Mid has moderate weight. Outside near-zero — perimeter shooting is spacing, not gravity.

### Spacing formula (Player.cs, reworked)

`SpacingContribution` replaced the pure `Outside` with:

```
SpacingContribution = 0.85×Outside + 0.15×Mid
```

No artificial floor. A big who can step to 16 feet has real spacing value. The D1 competency floor is a player-generation constraint, not a formula clamp. Gravity and spacing are independent per-player values — one player can be high on both.

### Team-openness interaction (AttentionGenerator.cs)

**Asymmetric form: gravity enables spacing.** The key basketball truth is that spacing only helps when gravity forces the defense to collapse first. Without a dominant rim threat, the defense can wall up a perimeter lineup without paying a price.

```
GravitySource = sigmoid((top − 60)/15) + 0.12 × sigmoid((second − 60)/15)   → [0,1]
SpacingField  = mean spacing of the four non-primary-gravity players / 99     → [0,1]
BaseOpenness  = GravitySource × (GravityAloneYield + SpacingMultiplier × SpacingField)
              = GravitySource × (0.25 + 0.75 × SpacingField)   [placeholders]
```

The sigmoid gate is centered at 60: a player needs genuine rim presence to activate the defense-scrambling effect. Moderate gravity (~37) barely registers; elite gravity (~81) nearly maxes it. `SecondGravityFraction = 0.12` captures diminishing returns on additional gravity sources (the rim absorbs only so much).

**Required five-case ordering (verified by Python before any C# was written):**

| Lineup | Openness | Label |
|---|---|---|
| 5-Korver (pure spacing, no gravity) | 0.162 | LOW |
| 5-Evans (pure gravity, no spacing) | 0.329 | LOW |
| 4-Evans+1-Korver (gravity source, limited spacing) | 0.428 | LIMITED |
| 4-Korver+1-Evans (one gravity source + four spacers) | 0.664 | HIGHER |
| 5-Durant (elite on both axes) | 0.736 | HIGHEST |

The product-of-averages form cannot produce this ordering (5-Korver and 4-Korver+1-Evans would be indistinguishable). The asymmetric product form achieves it with realistic moderate inputs.

**Stamped team levels:** Roll H needs the signed imbalance, not just openness, to dock the correct zone under C2. Three separate scalars are stamped on `PossessionState`: `TeamBaseOpenness`, `TeamGravityLevel`, `TeamSpacingLevel`.

**Passing seam (neutral-pinned):** The formula includes `× PassingAmp` (currently pinned at 1). Session 2 computes PassingAmp from Passing/Playmaking/Vision — no formula reshape needed.

### Attention allocation (AttentionGenerator.cs)

Scores each player by `GravityBlendWeight × (gravity/100) + UsageBlendWeight × FinalShares[i]`. Normalization to [0,1] before blending is required — a naive weighted sum lets gravity (~100 scale) overwhelm usage (~1 scale) ~100× and defeats the focal-point correction while still compiling. After scoring, normalized to a pie and floor-constrained (floor pass of `ApplyFloorAndRail`, copied from `RollEGenerator` per A2).

The seam: `AttentionGenerator.Generate` is called at both Roll E dispatch sites in the Resolver, immediately after `GenerateWithPressure`. Results are threaded through the extended `RollE.Execute` signature and stamped atomically alongside `SelectedSlot` and `UsagePressure`.

### Roll H make% consequences (RollHGenerator.cs)

Three separable adjustments, all labeled for harness-attributable six-value output:

**C1 — bonus-only openness nudge (≥ 0).** Between the matchup logistic and the Phase 17 block:
```
AttentionRelief = max(0, 0.20 − a)   // a = ShooterAttentionShare
ShooterOpenness = clamp(TeamBaseOpenness × AttentionRelief × C1ReliefScale, 0, 1)
makePct += ShooterOpenness
```
The prior draft used `max(0, g − a)` (gravity minus attention share) — wrong because `g` is an absolute trait and `a` is a relative share. A star with g=0.85, a=0.35 would get a large bonus despite being heavily attended. Corrected: C1 measures attention relative to the equal-share neutral point only. Gravity has already done its work upstream.

**C2 — zone-specific imbalance penalty.** Also halfcourt-only (FastBreak guard):
```
imbalance = TeamSpacingLevel − TeamGravityLevel
Three/Long: penalty = max(0, imbalance) × C2ImbalanceScale
Rim/Short:  penalty = max(0, −imbalance) × C2ImbalanceScale
makePct -= penalty
```
Spacing-heavy lineup (five Korvers): contested threes. Gravity-heavy lineup (five Evanses): packed paint.

**C3 — amplifier on the Phase 17 usage penalty.** Inside the Phase 17 block:
```
AttentionPressure = max(0, a − 0.20)
c3Amplifier       = 1 + AttentionPressure × C3AttentionAmplifier
makePct *= (1 − usagePressure × volTaxScale × c3Amplifier)
makePct -= usageResidual × residualScale × c3Amplifier
```
C3 amplifies both Phase 17 terms. A forced specialist under defensive attention takes the largest hit — residual is already larger, and C3 amplifies it further. Equal-share attention → amplifier ×1 → Phase 17 unchanged (regression anchor). Zero usage pressure → zero penalty regardless of attention.

**Exemptions (A5):** Putback short-circuit precedes all new code (existing architectural guarantee). FastBreak guard added for C1 and C2 explicitly.

**Leak guard:** `ResetOffense` in Roll K clears all four new `PossessionState` fields alongside `UsagePressure` and `UsageResidualPressure`.

### What is Session 2 (not this session)

- Attention tilting selection (Roll E): low attention → more attempts
- Attention tilting shot location (Roll G): low attention → preferred zones; high attention → improvise to rim
- Passing made live — `PassingAmp` computed from Passing/Playmaking/Vision

**Session 2 feedback-loop guard (recorded):** Attention is computed once from Roll E's pre-attention `FinalShares`. Session 2 may use that fixed attention array to bend the final selection pie but must not recompute attention recursively from the attention-adjusted shares. One-pass dependency only.

## Phase 27 Session 2: Selection Tilt + Passing Converter (Session 62, 2026-06-18)

### What this is

The second half of the gravity/spacing/attention layer — closes Phase 27. Session 1 built the attention pie and wired its consequences into Roll H make% (C1/C2/C3). Session 2 threads the same attention signal into selection (Roll E) and makes passing live as a separate make% consequence.

Shot location tilt (Roll G) remains deferred; it was always listed as a separate future task, not a Phase 27 deliverable.

### Selection tilt (BendByAttention)

The attention pie now bends the usage pie before the slot is rolled. A slot the defense under-attends relative to its usage intent gets more attempts; a slot the defense over-attends gets fewer.

**Formula (bounded multiplier, same primitive as LocationMultiplier and BlockWeight):**
```
gap[i]        = FinalShares[i] − attentionShares[i]
multiplier[i] = exp(log(MaxTiltMultiplier) × tanh(gap[i] / TiltReferenceShift))
```
Strictly bounded in `(1/MaxTiltMultiplier, MaxTiltMultiplier)`; exactly 1.0 at zero gap (neutral anchor: usage == attention reproduces the pre-tilt pie). After applying multipliers and normalizing, `ApplyFloorAndRail` is called with the TILTED shares as both input and redistribution basis — using the original `expScores` would pull mass back toward pre-tilt proportions and partially undo the tilt.

**Wiring:** `BendByAttention` is added to `IRollEGenerationProvider`, implemented in `RollEGenerator`, and stub-passthroughed in `RollEStubPieGenerator` and `RollESpyGenerator`. The Resolver calls it at the halfcourt `IntoPlayerSelection` site only. The FastBreak site (`IntoHalfcourtSet`) passes the transition pie directly — untilted, as before.

**Pre-tilt pressures unchanged.** The tilt changes which slot is rolled; it does not change the volume load each slot was carrying. Pressures (fed to C3) are computed pre-tilt by `GenerateWithPressure` and passed to `RollE.Execute` unchanged.

**One-pass dependency.** Attention is computed once from the pre-tilt `FinalShares`. The tilt may use that fixed array to bend the final pie; it does not recompute attention from the tilted result. The feedback-loop guard recorded at the end of Session 1's design section is confirmed closed here.

### PassingAmp removal (bug fix)

`TeamBaseOpenness` reverts to the pure gravity×spacing value. `PassingAmp` (the neutral-pinned `const double = 1.0`) is removed from `AttentionGenerator`. The bug it would have caused: folding passing into the openness field caused the conversion bonus to vanish whenever the defense played evenly (C1 relief = 0 at equal-share attention = 0.20). Fix: passing has its own separate consequence (C4), fully independent of attention allocation.

### Passing converter (C4 in RollHGenerator)

Passing **converts** the gravity/spacing advantage — it does not generate it. A lineup of elite passers behind a dominant rim threat with four shooters converts more of their open looks into makes. A lineup of elite passers with no gravity source gets a modest floor-level lift from the direct term only.

**TeamConversionQuality (stamped on PossessionState at Roll E time):**

Computed in `AttentionGenerator.Generate` after the attention and openness blocks:
```
IQ-adjusted effective playmaking per player:
  effPlaymaking[i] = (Playmaking[i]/100) × lerp(IqMin, IqMax, BasketballIQ[i]/100)

Collapse route (flat, level-agnostic this session):
  perimeterRoute[i] = avg(Quickness[i], FirstStep[i]) / 100
  postRoute[i]      = GravityContribution[i] / 100

PlaymakingActivation per player (higher collapse route wins):
  activation[i] = effPlaymaking[i] × max(perimeterRoute[i], postRoute[i])

Top-down geometric decay (redundancy saturation):
  PlaymakingActivation = Σ activation[sorted high→low, i] × PlaymakingDecay^i

PassingCompound = mean(Passing[i]/100) across five players

conversionQuality = ConversionFloor
                  + DirectPassingScale × PassingCompound
                  + ActivationScale × PlaymakingActivation × PassingCompound
```
Clamped to [0,1]. The direct term lifts make% modestly even when `PlaymakingActivation ≈ 0` (good passers who lack a collapse route). Without it, Passing would only matter inside the activation gate — erasing its value for lineups that pass well but don't generate gravity or quickness.

**Passing bonus in Roll H (C4):**
```
opportunityGate = PassingOpportunityFloor + (1 − PassingOpportunityFloor) × TeamBaseOpenness
passingBonus    = MaxPassingBonus × conversionQuality × opportunityGate
makePct        += max(0, passingBonus)
```
Bonus-only; halfcourt and non-putback only (putback short-circuit precedes; explicit FastBreak guard). Reads `state.TeamConversionQuality ?? 0.0`.

**Leak guard:** `TeamConversionQuality = null` added to Roll K's `ResetOffense` blank-slate `with`, alongside all other per-possession attention fields.

### Engineering calls

**Tilt knobs in RollEConfig** (not AttentionConfig). Selection-shaping decisions all live in one config: `UsageFloor`, `UsageRail`, `UsageExponent`, `MaxTiltMultiplier`, `TiltReferenceShift`. AttentionConfig stays focused on defensive attention allocation.

**Single `TeamConversionQuality` field** (not the pair `PlaymakingActivation`/`PassingCompound`). Roll H needs the scalar only. The components live in the generator. A future attribution session can surface them if needed without tearing down the field.

### Calibration placeholders

All magnitudes are provisional and consistent with the "wire the form, tune later" mandate:
- `MaxTiltMultiplier = 1.5`, `TiltReferenceShift = 0.08`
- `ConversionFloor = 0.05`, `DirectPassingScale = 0.10`, `ActivationScale = 0.20`, `PlaymakingDecay = 0.80`, `OpportunityFloor = 0.10`, `MaxPassingBonus = 0.08` (AttentionConfig)
- `PassingOpportunityFloor = 0.10`, `MaxPassingBonus = 0.08` (RollHConfig)

### What is not this session

- Shot location tilt (Roll G): deferred, never part of Phase 27
- Matchup-relative collapse route in the converter (defender quickness vs. attacker): deferred — DefenderPicker.Pick throws pre-selection, and the flat authored route is the correct placeholder
- Calibration passes: correctness before calibration is the standing sequencing principle

## Phase 28 — Attention-Location Tilt + Steal-Origin Split + Roll J Real Generator

### Overview

Three scopes in one session. All three close open calibration seams rather than building new game structure.

### Scope 1: Attention-location tilt (Roll G)

**Problem:** The existing `ApplyDietShift` already handles volume-driven location shift (`UsagePressure → requestedShift → absorbed → residual`). But it had no signal from defensive attention: a star getting double-teamed shifted his shot mix the same amount as an unguarded role player at the same usage pressure. The attention tilt fixes this.

**Formula (inside `ApplyDietShift`, after `requestedShift` is computed, before the `intrinsicCapacity` cap):**
```
EqualShare = 0.20   (same neutral point as Roll H C1/C3)
attnPressure     = max(0, ShooterAttentionShare − EqualShare)
attnAmplifier    = 1 + attnPressure × AttentionShiftAmplifier
requestedShift  *= attnAmplifier
```

**Why before the cap:** placing the amplifier before `min(requestedShift, intrinsicCapacity, availableMass)` means the amplified request hits the same ceiling. For a one-trick player (`intrinsicCapacity ≈ 0`), the amplified request is still capped — `absorbed` stays constant — but `residual = requestedShift − absorbed` grows. Location barely moves; make% falls. This is A4 behavior: defensive attention does not move a player off his comfort zone, but it makes him less efficient when he stays there.

**Bonus-only:** `attnPressure = max(0, ...)` — below-equal-share attention never reduces the shift. At exactly equal-share attention, `amplifier = 1.0`, reproducing Phase 17 output byte-for-byte (regression anchor).

**Harness confirmation:** Phase 17 check (b) specialist residual = 0.0600 = exact `intrinsicCapacity`. Residual-penalty attribution = 12.0pts vs vol-tax = 2.5pts — residual is the dominant efficiency cost for a specialist under high attention.

**Config knob:** `AttentionShiftAmplifier` in `RollGConfig` (default 1.0, invariant `>= 0`). Zero = ablation mode (attention has no location effect). Calibration placeholder pending the full attribute calibration pass.

### Scope 2: Steal-origin split (`TransitionContext` extension)

**Problem:** All live-ball steals arrived at Roll J on a single `Steal` context, selecting the same run-or-not pie regardless of where on the court the steal happened. A half-court turnover strip is not the same fast-break opportunity as an intercepted outlet pass.

**Role-flip wire:** `PossessionState.Frontcourt` belongs to the VICTIM (the team that lost the ball). The run odds belong to the THIEF (the new offense), and the mapping is inverted:

- `Frontcourt == false` (victim still in backcourt) → thief already near his scoring basket → `BackcourtVictim` (high run)
- `Frontcourt == true` (victim in halfcourt set) → thief must go the full court → `FrontcourtVictim` (low run)

**Three steal sites:**
- `RollC.BadPassIntercepted` → ternary from `state.Frontcourt`
- `RollC.LostBallLiveBall` → same ternary
- `RollK.LiveBallTurnover` → fixed `FrontcourtVictim` (not a ternary; proven by source: the offense had crossed halfcourt, shot, and rebounded before losing the ball live — `Frontcourt == true` on every Roll K path)

**`TransitionContext` extension:** Two optional fields appended to the record (the "grows by append" design):
- `StealOrigin? Origin` — the steal-origin discriminator
- `TeamSide? OffenseSide` — the new offense team identity, stamped by all three transition helpers (`TransitionStealTo`, `TransitionReboundTo`, `TransitionFreeThrowReboundTo`) where the new offense is already known

`OffenseSide` is the architectural answer to a generator constraint: `IRollJPieGenerator.Generate(TransitionContext)` must remain the per-call interface (no `TeamSide` parameter added). Stamping `OffenseSide` on the ticket lets the real generator compute the directional athleticism gap — "the ticket carries what the node needs."

**Config:** `BackcourtVictimPush = 0.55`, `FrontcourtVictimPush = 0.35` (both ≥ `Push = 0.30` Rebound baseline; old `StealPush = 0.50` retained as null-origin fallback). Load-time invariants enforce direction.

### Scope 3: Roll J real generator

**`IRollJPieGenerator` interface:** Created following the codebase pattern (all real generators implement interfaces). `Generate(TransitionContext)` is the single method. Both `RollJStubPieGenerator` (unchanged behavior) and `RollJGenerator` (new) implement it. Resolver and all harness construction sites hold the interface.

**`RollJGenerator` — base weight selection:**

| Source | Origin | Weight set |
|---|---|---|
| Rebound | — | `Push`, `Settle`, … |
| FreeThrowRebound | — | `FreeThrowPush`, `FreeThrowSettle`, … |
| Steal | `BackcourtVictim` | `BackcourtVictimPush`, `BackcourtVictimSettle`, … |
| Steal | `FrontcourtVictim` | `FrontcourtVictimPush`, `FrontcourtVictimSettle`, … |
| Steal | null | `StealPush`, `StealSettle`, … (fallback) |

**Two independent modifiers — never pre-fused:**

1. **Coach pace (config-only seam):** `PaceLift = TeamPaceBias × PaceScale`. Neutral at `TeamPaceBias = 0.0`. Positive = up-tempo (more Push); negative = slow. A future coaching session replaces this config-only knob with a real team/coach source; harness scenarios vary pace by constructing `RollJConfig` variants.

2. **Team athleticism-gap (relative, directional):** `AthlLift = (offenseFiveAthl − defenseFiveAthl) × AthleticismGapScale`. `offenseFiveAthl` = mean derived `Player.Athleticism` of the active five for `ctx.OffenseSide`. Null `OffenseSide` → gap = 0, regression anchor (isolated harness tests that do not seat full rosters).

**Modifier application:**
```
modifiedPush   = max(0, basePush   + PaceLift + AthlLift)
modifiedSettle = max(0, baseSettle − PaceLift − AthlLift)
```
Turnover, DefensiveFoul, JumpBall are fixed to their base values.

**Regression anchor:** At neutral pace (`TeamPaceBias = 0.0`) and neutral athleticism gap (0 — empty lineups in harness checks), Rebound and FreeThrowRebound pies reproduce configured weights exactly. Confirmed by `RollMContextSelectionCheck`.

**Harness observation:** Athleticism-gap modifier visible in stress test. AthleticVsSkill bucket: Athletic Tr%=39.9% vs Skill Tr%=33.4%. SkillVsAthletic (mirrored): Athletic Tr%=39.7% vs Skill Tr%=33.9%. Mirror gap = 0.2% — directional and side-neutral. Pace knob at neutral (0.0); all signal is from athleticism gap.

### Calibration placeholders

All magnitudes provisional (wire-the-form mandate):
- `AttentionShiftAmplifier = 1.0`
- `BackcourtVictimPush = 0.55`, `FrontcourtVictimPush = 0.35`
- `TeamPaceBias = 0.0`, `PaceScale = 0.15`, `AthleticismGapScale = 0.001`

### What is not this session

- Coaching/offensive-hierarchy layer: `TeamPaceBias` is the seam; wiring it to a real `CoachProfile` field is a future coaching session
- Defender picker architecture: athleticism gap uses team-level means (active five), not individual matchup gaps — the per-slot defender attribution layer is a deferred chapter
- Calibration passes: correctness before calibration is the standing sequencing principle
- Deferred roll faces (C, D, J putback-loop, K): unbuilt chapters, not cleanup debt

## Phase 29 Session 1: Player Hierarchy + Heliocentric Bias in Roll E (Session 64, 2026-06-18)

### What this is

The first coaching layer. Introduces two authored values: `Player.HierarchyRank` (1–10, default 5) and `CoachProfile.HeliocentricBias` (1.0–10.0, default 5.0). `RollEGenerator` uses these to blend a coaching-intent signal into the usage pie alongside the existing attribute-based usage scores.

### The semantics that matter

`HeliocentricBias = 5.0` is **not** hierarchy-off — it is standard authored-hierarchy expression. Rank 10 gets 2× the weight of rank 5; rank 1 gets 0.2×. `HeliocentricBias = 1.0` is hierarchy-off / egalitarian: the exponent collapses to 0 and all weights become 1.0 regardless of authored rank, so attributes drive usage entirely.

The regression anchor (frozen corpus output identical to Phase 28) comes from all existing players defaulting to `HierarchyRank = 5`, which produces weight 1.0 at any exponent — not from the bias value.

### Hierarchy blend formula

After the `MinUsageScore` floor and before the `UsageExponent` sharpening pass in `GenerateWithPressure`:

```
// Derive hierarchyExponent from coach bias (piecewise-linear, monotone, continuous at bias=5)
if bias <= 5.0:
    hierarchyExponent = HierarchyExponentNeutral × (bias − 1.0) / 4.0
else:
    hierarchyExponent = HierarchyExponentNeutral
                      + (HierarchyExponentMax − HierarchyExponentNeutral) × (bias − 5.0) / 5.0

// Multiply each populated slot's raw score by the hierarchy weight
weight[i] = (HierarchyRank[i] / 5.0) ^ hierarchyExponent
rawScores[i] *= weight[i]
```

**Key properties:**
- At rank 5: weight = 1.0 for any exponent → regression anchor.
- At exponent 0 (bias 1.0): weight = 1.0 for all ranks → attributes only.
- Placement: after `MinUsageScore` floor, before `UsageExponent` sharpening. The hierarchy gap is further amplified by `UsageExponent` — this is intentional (stacked exponents produce realistic star-vs-role differentiation), but it means both exponents calibrate together.
- Low-ranked player's score may be pushed below its post-MinUsageScore value. The floor/rail machinery (not MinUsageScore) is the participation protection. `MinUsageScore` is NOT reapplied after the multiply.

### Hierarchy feeds attention (intentional cascade)

`BendByAttention` reads `gen.FinalShares`, which already have hierarchy baked in. A coach feeding the star more possessions will draw more defensive attention to that player — hierarchy → higher FinalShare → higher AttentionShare → tilt and pressure interactions respond. Correct basketball behavior.

### CoachProfile in GameState

`HomeCoach` and `AwayCoach` are initialized to `new CoachProfile()` in the constructor body (not as constructor parameters), mirroring the `HomeRoster`/`AwayRoster` pattern. All 59 existing `new GameState(...)` construction sites compile unchanged. `SetCoach(TeamSide, CoachProfile)` is the mutator; `CoachFor(TeamSide)` is the reader — both mirror the existing `SetPossessionArrow` / `RosterFor` pattern.

### `StampPlayerId` gap (required fix)

`StampPlayerId` in the harness manually copies every init property when stamping a `Player` with a new `PlayerId`. Without explicitly copying `HierarchyRank`, all stamped players received rank 0, which would have triggered the `InvalidOperationException` guard in `RollEGenerator` on the first possession. Adding `HierarchyRank = p.HierarchyRank` to the copy was a required companion to adding the property.

### Calibration note

At `HierarchyExponentMax = 2.0` and bias 9.0, a high-attribute rank-10 player in a diverse lineup can approach the usage rail (0.52). The rail is doing its job. `HierarchyExponentMax = 2.0` is the conservative starting point — reduce it if rank-10 players consistently rail in standard-roster calibration runs. Both `HierarchyExponentNeutral` and `HierarchyExponentMax` calibrate alongside `UsageExponent`; the stacked-exponent amplification means they cannot be tuned independently.

### What is not this session

- `ShotSelectionBias`, `FreelanceDial`, `PaceBias` on `CoachProfile` — deferred seams, no code
- Max-3-per-number authoring enforcement — belongs to the future roster authoring layer
- Coaching/offensive-hierarchy layer beyond usage bias (freelance, shot selection, pace) — separate sessions
- Calibration passes: correctness before calibration is the standing sequencing principle

## Phase 30 — ShotSelectionBias + PaceBias (Coaching Layer 2)

### ShotSelectionBias nudge math

`CoachingPull.Apply` now holds real nudge math. The formula:

```
nudge = (ShotSelectionBias − 5.0) / 5.0   // [1,10] → [−0.8, +1.0]
adjRim   = rim   × (1 − nudge × 0.40)
adjShort = short × (1 − nudge × 0.40)
adjMid   = mid                              // neutral zone — unchanged
adjLong  = long  × (1 + nudge × 0.40)
adjThree = three × (1 + nudge × 0.40)
// floor clamp: max(1.0, adj) per zone
```

Inside coach (bias 1, nudge = −0.8): Rim/Short boosted ×1.32, Long/Three suppressed ×0.68.
Outside coach (bias 10, nudge = +1.0): Long/Three boosted ×1.40, Rim/Short suppressed ×0.60.
Neutral (bias 5, nudge = 0.0): all five returned unchanged — identity.

The formula is intentionally asymmetric: the inside ceiling (−0.8) is slightly weaker than the outside ceiling (+1.0). This was flagged and accepted; 5.0 is the neutral point regardless. A symmetric mapping would require a different formula.

**Mid is neutral by design.** Redistributing to/from mid would require a separate bias axis and risks the five-zone coherence problem (deferred). Inside/outside coaches can only move volume between the rim/short cluster and the long/three cluster.

**Player identity preserved.** Floor clamp (1.0) ensures no zone is fully suppressed. Shaq test (Rim=80, Three=10, bias=10): rim 48.0 > three 14.0. Korver test (Three=80, Rim=10, bias=1): three 54.4 > rim 13.2. Dominant zone stays dominant at every bias value.

**No normalization in `Apply`.** Returns raw adjusted tendencies. `RollGGenerator` owns normalization, which it already does after the matchup multipliers. Normalizing inside `Apply` would interfere with those multipliers.

**Null-coach fallback = 5.0 = identity.** Any call site that passes `coach: null` (or a coach not yet authored) produces the same output as the v1 identity stub. The Phase 9 harness check (identity sub-case) still passes unchanged.

### RollGGenerator wiring

One surgical change: `CoachingPull.Apply(shooter, coach: null, ...)` replaced by `CoachingPull.Apply(shooter, _game.CoachFor(state.Offense), ...)`. One new local `offCoach`. Everything downstream (matchup multipliers, diet shift, pie construction) is unchanged.

### PaceBias in Roll J

`rawPaceBias` reads `_game.CoachFor(ctx.OffenseSide.Value).PaceBias` when the offense side is stamped on the ticket; falls back to `_cfg.TeamPaceBias + 5.0` (→ neutral) when null. Mapped pace `= (rawPaceBias − 5.0) / 5.0`. `paceLift = mappedPace × PaceScale`. The two modifiers (pace, athleticism gap) remain independent inputs to the same Push/Settle adjustment — never pre-fused, per the standing rule.

**`_cfg.TeamPaceBias` role change.** Now a signed fallback knob used only when `OffenseSide` is null. Not removed — still the regression anchor for isolated harness checks. `PaceScale` is unchanged.

### Roll J clamp-asymmetry bug fix

The pre-existing modifier-application block:
```csharp
// OLD (buggy):
var modifiedPush   = Math.Max(0.0, basePush   + paceLift + athlLift);
var modifiedSettle = Math.Max(0.0, baseSettle - paceLift - athlLift);
```
When `FreeThrowPush=0.08` minus a slow-pace lift of 0.09 went negative and clamped to 0, `modifiedSettle` still received the full +0.09 boost. Pie summed to 1.01, throwing `PieValidationException`. Fix:
```csharp
// NEW (correct):
var rawPush      = basePush + paceLift + athlLift;
var modifiedPush = Math.Max(0.0, rawPush);
var actualDelta  = modifiedPush - basePush;         // what Push actually changed
var modifiedSettle = Math.Max(0.0, baseSettle - actualDelta);
```
`actualDelta` mirrors only the delta that Push actually moved, so the pie always sums to 1.0 regardless of which source and which bias. Affects the FreeThrow source at slow pace (bias ≤ ~3 with PaceScale=0.15). The bug was latent before Phase 30 because `TeamPaceBias = 0.0` never produced a lift large enough to floor Push.

### PaceBias in Governor

`DrawPossessionSeconds` gains a `TeamSide offense` parameter. Center shift:

```
paceAdj = (5.0 − PaceBias) / 5.0 × PaceCenterScale
center  = max(Floor + 1.0, Center + paceAdj)
```

Fast coach (bias > 5): paceAdj negative → shorter possessions. Slow coach (bias < 5): paceAdj positive → longer possessions. Neutral (5.0): paceAdj = 0, center unchanged. Floor guard prevents center from dropping below Floor + 1.0. At PaceCenterScale=1.5: bias 10 → center 15.5s; bias 1 → center 18.2s; bias 5 → center 17.0s (unchanged).

`PaceCenterScale` lives in `RollClockConfig` (not `RollJConfig`) because it controls the Governor's draw, not Roll J's pie weights. [CALIBRATION PLACEHOLDER — default 1.5]

### APL timing gap (deferred)

`ElapsedSeconds` is stamped on exactly three terminal outcomes in Roll C: `ShotClockViolation`, `FiveSecondInbound`, `TenSecondBackcourt`. Every other terminal returns `null`, causing the Governor to draw from the full shot-clock distribution (center ~17s). Early-exit possessions — turnovers on Roll A/B, press-break fouls, live-ball turnovers — receive a full-clock time draw instead of the short one they actually consumed (~4–8s). This is the primary reason APL runs ~18s vs the real D1 average of ~14–15s.

This is not a wiring bug. The seam is correct and the machinery works. The fix requires per-terminal-type elapsed distributions (short-TO draw, foul draw, etc.) or a possession-phase model that conditions the time draw on where in the shot clock the terminating event typically fires. Deferred to after all rolls are wired — designing and tuning the time-draw model once against a stable, fully-wired possession chain is the right sequencing.

### What is not this session

- Per-player `Malleability` attribute — deferred; `malleability: null` treated as 1.0; documented in `CoachingPull` for the session that adds it
- `FreelanceDial` — not yet designed
- System-identity constraint (five zones as a coherent system) — future design
- `HierarchyRank` calibration — separate pass
- Roll G location tilt from attention — already deferred from Phase 27/28
- APL timing gap (early-exit possessions) — deferred to post-wiring calibration pass

## Phase 31 — Offensive Rebounder Picker (Session 66, 2026-06-19)

### What this is

The first in-possession picker in the engine. Phase 31 stamps WHICH offensive player secured the offensive rebound onto `PossessionState.ReboundSlot`, conditional on Roll I having already awarded the board to the offense. It does not re-adjudicate the offense-vs-defense split; that remains Roll I's settled math. The picker is purely additive: Roll I awards the board, the picker identifies who grabbed it, Roll K resolves what happens next.

### The two-layer rebounding model (post-Phase-31)

Rebounding now has two distinct layers with a clean handoff:

1. **Team layer (Roll I / Roll M):** decides WHICH TEAM gets the board, using the matchup-aware two-touchpoint model (pre-staging size check + positional-weighted skill shift + tanh saturation). This is the probability mass that drives possession outcomes and ORB%.
2. **Attribution layer (OffensiveRebounderPicker):** given that the offense won the board, decides WHICH OFFENSIVE PLAYER gets credit. This is a within-side weighted draw over the five offensive slots; it does not touch the team-layer math.

These two layers are kept strictly separate. The picker runs downstream of Roll I's team verdict; it never re-adjudicates the team split.

### The picker weight formula

```
weight[i] = max(1, OffensiveRebounding[i] × PositionalWeight(Postness[i]) × shooterNerf[i])
```

Where:
- `OffensiveRebounding[i]` is the authored player attribute (0–99)
- `PositionalWeight(Postness[i])` is the existing `Matchup.PositionalWeight` method — weights relative to the lineup's mean Postness, bigs above 1.0 and guards below, exactly 1.0 at the lineup mean. The same method Roll I's matchup math uses.
- `shooterNerf[i] = ReboundShooterNerf (0.35, from MatchupConfig)` when candidate slot matches the shooter (`state.SelectedSlot?.Number == i`) AND `state.ShotType` is `Three`, `Long`, or `Mid`; 1.0 (no nerf) on `Rim`/`Short` and when `ShotType` is null (bonus FT boards where Roll E never ran)
- Floor of 1 ensures every populated offensive slot has a positive weight — no zero-weight slots

The same `Matchup.Postness` and `Matchup.PositionalWeight` methods used by Roll I are reused directly. No new matchup math; only a new consumer.

### Conditional-within-side (Option A) architecture — decision and known limitation

**Why Option A, not Option B (unified 10-player contest).** A unified 10-player contest (Option B) would compute each player's probability of grabbing the board in a single draw across all ten on-court players. This is the structurally correct model — it naturally produces realistic per-player ORB% without any inflation. However, implementing Option B requires replacing Roll I's working two-touchpoint team-math with a per-player attribution model, which is a large rearchitecture. Option A adds the picker strictly downstream of Roll I's verdict: Roll I decides the team; the picker decides the individual within the offense. The architecture is additive and preserves all existing machinery.

**Known limitation: within-side share inflation.** Because the picker distributes probability across 5 offensive players rather than all 10, a weak rebounder's realized share from the picker (~3%) exceeds what his true OR% contribution would be in a full 10-player contest (~1%). The picker correctly ranks players within the offense but inflates their absolute shares relative to the full-game picture. This is a named calibration artifact, not a structural bug. Option B is the named future fix; it is deferred as a calibration-phase task after correctness is confirmed for all existing chains.

### `ReboundSlot` field and lifecycle

`PossessionState.ReboundSlot` (nullable `Slot?`, default null) is appended as the last positional parameter, separate from `SelectedSlot`. Two distinct per-possession facts must persist independently:

- `SelectedSlot`: which offensive player shot the ball (stamped by Roll E)
- `ReboundSlot`: which offensive player secured the rebound (stamped by Phase 31 picker at `ResolveOffensiveRebound`)

Both are nullable because not every possession involves both a shot and an offensive rebound. Both are cleared to null by Roll K's `ResetOffense` wipe (Phase 32 reads `ReboundSlot` before Roll K clears it, so the tilt lands on the correct possession boundary).

### `OrbBySlot` invariant

`RoutingOutcome` carries `OrbBySlot` (a `SlotGroup`) that accumulates one credit per `ResolveOffensiveRebound` case hit. The terminal return includes `OrbBySlot`. The harness asserts `OrbBySlot.Total == OrbWon` as an invariant in a controlled governor run — every offensive rebound credits exactly one player. This mirrors the Phase 23/24 exact-attribution invariants for FGA/FGM/FTA/FTM, extended to the picker's output.

### SeedMinimalRoster harness pattern (architectural note)

Phase 31 is the first resolver-walk code that reads from the game's roster. Previously, bare `GameState` objects (no players seated) could be passed to the full resolver in harness checks without issue. Once the picker is live, any possession that reaches `ResolveOffensiveRebound` will call `OffensiveRebounderPicker.Pick`, which calls `game.RosterFor(side).PlayerAt(slot)`. An empty roster returns null and the picker throws.

**The fix:** `SeedMinimalRoster(GameState g)` — a private static helper in the harness seating 5 identical all-50 players per side — is called immediately after any bare `GameState` construction in checks that route through the full resolver.

**Four exception categories (must NOT receive SeedMinimalRoster):**
1. `RollHResolutionBatchCheck` — explicitly bound to a bare game so `PlayerAt()` returns null and `RollHGenerator` falls back to stub/baseline rates. Seeding shifts make% outside the check's calibrated tolerances.
2. `AttributionSanityCheck`, `ObservationRun` per-game loop, `Phase25ShootingFoulAttributionCheck` loop — these call `SetStarter` themselves. Adding `SeedMinimalRoster` produces "Slot already has starter" exceptions.
3. `StressTestArchetypeRosters` per-game loop — uses `SeatRoster()` which wraps `SetStarter`; same conflict.
4. Phase 31 invariant sub-check — uses variable name `govGame` (not `game`) to avoid global-replace collision during harness build.

**Standing rule going forward:** Any new harness check that creates a bare `GameState` and passes it to a full resolver must add `SeedMinimalRoster(game)` immediately after construction.

### Phase 31 calibration note

The interior anchor at `SCALE=40` captures ~58% of rim residual probability (the share of non-matched-man probability tilted toward interior defenders on rim attempts). The prompt draft estimated ~37% using `SCALE≈100`. Smaller `SCALE` produces a stronger interior tilt. This is a calibration item — the formula shape is correct; the magnitude is a tune-later task, consistent with the wire-the-form mandate.

### What is not this session

- Roll K putback tilt toward the rebounding player — Phase 32, the first consumer of `ReboundSlot`
- Defensive rebounder picker — no in-possession consumer exists yet; DReb credit remains a post-hoc `WeightedDraw` in the harness
- Option B (unified 10-player contest) — the named future fix for within-side inflation; deferred to the calibration phase
- Per-player foul ledger, coaching/offensive-hierarchy layer, deferred roll faces (C, D, J, K) — unbuilt chapters, not cleanup debt
- Calibration of `SCALE`, `ReboundShooterNerf`, or picker weight magnitudes — correctness before calibration is the standing sequencing principle

---

## Phase 32 — Roll K Real Generator: Putback Attempt Rate

### Scope

Replaces `RollKStubPieGenerator` with `RollKGenerator`, the first consumer of `PossessionState.ReboundSlot` (stamped by Phase 31). The generator tilts the `PutBack`/`ResetOffense` mass split using the rebounder's physical profile, the defensive team's interior deterrence composite, and a per-zone modifier. The five minority arms stay flat at config. Follows the standard interface + retype pattern introduced in Phase 9.

### The formula

```
offScore  = PutbackOffStrengthWeight    × rebounder.Strength
          + PutbackOffHeightWeight      × rebounder.Height
          + PutbackOffAthleticismWeight × rebounder.Athleticism   // computed: (Str+Spd+Qck+FS+Vert)/5
          + PutbackOffFinishingWeight   × rebounder.Finishing

interiorScore[i] = PutbackDefRimProtectionWeight × defender.RimProtection
                 + PutbackDefHeightWeight         × defender.Height
                 + PutbackDefStrengthWeight       × defender.Strength

defScore  = Σ(interiorScore[i]²) / Σ(interiorScore[i])   // self-weighted mean

gap       = offScore − defScore
shift     = Matchup.GapFn(gap, SkillSteepness, SkillExponent, ReferenceScale)

basePutback = source == FreeThrow ? cfg.FreeThrowPutBack : cfg.PutBack
span        = shift >= 0 ? (PutbackCeiling − basePutback)
                         : (basePutback − PutbackFloor)
bend        = span × tanh(shift / PutbackReferenceShift)

adjustedPutback = basePutback + bend
finalPutback    = Clamp(adjustedPutback × zoneMod, PutbackFloor, PutbackCeiling)
```

### Two separate rate parameters

`ReferenceScale` (25.0, shared across all matchup doors) is the GapFn rating-point unit: a gap of 25 rating points reaches `GapFn = SkillSteepness`. `PutbackReferenceShift` (20.0, Roll K only) is the tanh saturation denominator: a net shift of 20.0 rating points reaches `tanh(1) ≈ 76%` of span. The two parameters are independent and serve different purposes; conflating them is the named failure mode documented in the adversarial preamble. This mirrors the existing block/foul pattern in `Matchup.cs`.

### Self-weighted defense composite

The formula `Σ(score²) / Σ(score)` ensures that a single elite interior defender disproportionately raises the team score. Five identical all-50 defenders produce a team score of 50 (the same as each individual); one elite (interior=85) among four weak (interior=20) produces a score of ~55, well above the arithmetic mean of 33. This reflects the basketball reality that one dominant rim protector changes the putback calculus for the entire defense.

### Zone modifier

| ShotType | Modifier |
|---|---|
| Three | 0.50 |
| Long | 0.70 |
| Mid | 0.85 |
| Short | 1.00 |
| Rim | 1.10 |
| null (FT board) | 1.00 |

The modifier applies after the tanh bend and before the floor/ceiling clamp. The Short modifier is 1.0 (no zone effect) so sub-checks that want to isolate the matchup component use Short. The Rim modifier exceeds 1.0 — even a neutral matchup generates a slight putback boost on a rim miss because the board is right there.

### Null-rebounder fallback

If `PossessionState.ReboundSlot` is null (Phase 31 not yet run, or the slot is unpopulated), the generator returns the flat config pie for the given source — identical to `RollKStubPieGenerator`. This is the DEC-6 equivalent for Roll K: correctness without crashing on uninitialized state.

### Config additions

**`MatchupConfig` (13 new properties):** `PutbackOffStrengthWeight` (0.40), `PutbackOffHeightWeight` (0.40), `PutbackOffAthleticismWeight` (0.15), `PutbackOffFinishingWeight` (0.05), `PutbackDefRimProtectionWeight` (0.55), `PutbackDefHeightWeight` (0.25), `PutbackDefStrengthWeight` (0.20), `PutbackReferenceShift` (20.0), `PutbackZoneModifierThree` (0.50), `PutbackZoneModifierLong` (0.70), `PutbackZoneModifierMid` (0.85), `PutbackZoneModifierShort` (1.00), `PutbackZoneModifierRim` (1.10). Load invariants: `PutbackReferenceShift > 0`; all five zone modifiers `> 0`.

**`RollKConfig` (2 new properties):** `PutbackFloor` (0.15), `PutbackCeiling` (0.70). Load invariants: floor ≥ 0; ceiling ≤ 1.0; floor < ceiling. Startup overflow guards: `PutbackCeiling + flatArmSum < 1.0` enforced at Load time for both LiveBall and FreeThrow source modes (in addition to the runtime overflow guard in the generator).

### What is not this session

- Putback conversion rate tilt in Roll H — Roll H already handles the make/miss/foul resolution of a putback attempt; a putback-specific attribute tilt there is a future session
- Defensive rebounder picker — no in-possession consumer; DReb credit remains a post-hoc `WeightedDraw`
- Separate `PutbackCeiling`/`PutbackFloor` per source (FreeThrow vs LiveBall) — single pair for now; calibration can split later
- Calibration of all weights, zone modifiers, reference shift — placeholders only; direction is what Phase 32 validates
- Deferred roll faces (Rolls C, D, J, K minority arms) — unbuilt chapters, not cleanup debt

---

## Phase 33 — Turnover Committer Picker (Roll C Session 1)

### Scope

Promotes the pre-selection turnover-committer attribution from a post-hoc harness `WeightedDraw` into an engine-side `TurnoverCommitterPicker`, stamped onto `RoutingOutcome.TurnoverOffSlot` during the possession walk. This is the exact move Phase 31 made for offensive rebounds: an attribution that lived in the harness becomes part of the engine, computed once, on the walk.

The picker decides which offensive player committed a turnover on the **pre-selection** paths (Roll A entry/bring-up, Roll B halfcourt-initiation) — the paths where Roll E had not yet named a shooter. The **post-selection** path (Roll F, `SelectedSlot` non-null) is unchanged and untouched.

This session is the committer (WHO), not the type tilt (WHAT). The type-mix tilt — the committer's Passing/BallHandling/IQ bending which kind of turnover it was — is Roll C Session 2.

### The picker

`TurnoverCommitterPicker` is a static class (not an `I...Generator` interface) mirroring `OffensiveRebounderPicker` exactly in structure. Weight per populated offensive slot:

```
raw  = tanh((postness[i] − lineupMeanPostness) / TurnoverCommitterPostnessScale)
mult = TurnoverCommitterPostFloor + (1 − PostFloor) × (1 − (raw + 1) / 2)
weight[i] = max(1.0, BallHandling × mult)
```

The perimeter multiplier maps postness the **opposite direction** from `Matchup.PositionalWeight`. `PositionalWeight` gives posts weight above 1.0 (rebounding: posts dominate). Here the direction inverts: a guard (raw < 0) gets mult near 1.0; a post (raw > 0) gets mult toward `PostFloor`. The same `Matchup.Postness` coefficients are reused — no new postness math, only a new consumer.

The floor of 1 ensures every populated slot has a positive draw probability even for a BH=0 player or a maximally-suppressed post. The three-stage structure is identical to `OffensiveRebounderPicker`: populate loop → mean → one RNG draw cumulative walk with last-populated fallback.

### Resolver seam

```csharp
turnoverOffSlot = t.State.SelectedSlot?.Number
    ?? TurnoverCommitterPicker.Pick(t.State, _game, _matchup, _rng).Number;
```

The null-coalescing pattern is the correct seam between the two paths:

- **Post-selection (Roll F):** `SelectedSlot` is non-null. The `??` short-circuits — no draw consumed, stream unchanged for those possessions.
- **Pre-selection (Roll A / Roll B):** `SelectedSlot` is null. One `_rng.NextUnitInterval()` draw is consumed.

The draw is placed inside the turnover-reason branch only. Non-turnover possessions are unaffected.

### `TurnoverOffSlot` as universal post-condition

After Phase 33, every turnover possession exits the engine with a non-null `TurnoverOffSlot`. The harness fallback is retired and replaced with a loud `InvalidOperationException` throw — making a null slot a detectable wiring break rather than a silent fallback. This mirrors the Phase 31 `OrbBySlot.Total == OrbWon` invariant: both promote a "null means unknown" state to "null is impossible, throw if reached."

The invariant is asserted in a controlled governor run as a harness sub-check.

### RNG stream shift (documented consequence)

The draw is consumed from the engine's `_rng`, not from the harness's separate `new Random(seed + 2)` (which handles post-hoc STL/DReb/BLK draws). Moving the pick on-walk shifts every downstream engine draw on pre-selection turnover possessions. This is the identical stream shift Phase 31 documented when `OffensiveRebounderPicker` moved on-walk. The corpus hash changes; same-seed reproducibility within Phase 33 holds.

### Config additions

**`MatchupConfig` (2 new properties):** `TurnoverCommitterPostFloor` (0.10) — the positional-weight floor a maximally post player reaches; must be in (0, 1]. `TurnoverCommitterPostnessScale` (40.0) — the postness spread (rating points, relative to lineup mean) over which the multiplier slides; must be > 0. Both are calibration placeholders — direction (posts suppressed, handling tilts within perimeter) is what Phase 33 validates.

### Picker family

Three pickers now exist as static attribution helpers:

| Picker | Attributes | Direction |
|---|---|---|
| `DefenderPicker` | Steals/Speed/... | perimeter favored |
| `OffensiveRebounderPicker` | OffensiveRebounding × PositionalWeight | posts favored |
| `TurnoverCommitterPicker` | BallHandling × perimeterMult | perimeter favored, posts suppressed |

All three share the same cumulative-walk structure and one-draw-per-possession contract.

### What is not this session

- **Type-mix tilt (WHAT kind of turnover)** — committer's Passing/BallHandling/IQ bending the per-context type pie. Roll C Session 2, the direct sequel.
- **Illegal-screen → screener redirect** — per-player screener attribution. Deferred to the Roll D foul-attribution session.
- **Reason-aware committer mapping** — every pre-selection turnover is attributed to the picked ball-handler-ish slot; no per-reason differentiation this session.
- **Defensive rebounder picker** — DReb credit remains a post-hoc `WeightedDraw`; no in-possession consumer yet.
- **Calibration of `PostFloor`, `PostnessScale`, or weight magnitudes** — correctness before calibration is the standing sequencing principle.

---

## Phase 34 — Turnover Attribution Completion (2026-06-19)

### Type-aware committer dispatch

Phase 33 attributed every turnover uniformly via `TurnoverCommitterPicker` (handling-weighted, perimeter-gated). Phase 34 replaces that single call with a three-branch type dispatch in `Resolver.Route()`:

| Branch | Reasons (3) | Picker | TurnoverOffSlot |
|---|---|---|---|
| Team violations | FiveSecondInbound, TenSecondBackcourt, ShotClockViolation | — | null |
| Interior violations + offensive foul | ThreeSecondViolation, OffensiveGoaltending, OffensiveFoul | TurnoverInteriorPicker | non-null |
| Ball-handler violations | BadPassDeadBall, BadPassIntercepted, LostBallDeadBall, LostBallLiveBall, Travel, DoubleDribble, Carry, FiveSecondCloselyGuarded, BackcourtViolation | TurnoverCommitterPicker | non-null |

`IsTurnoverPossession` is unchanged — all 15 reasons still return true. Team violations increment aggregate TO counts but carry no per-player attribution.

### TurnoverInteriorPicker

Post-weighted attribution helper for interior violations and offensive fouls. Same 3-stage structure as `TurnoverCommitterPicker`; the only formula difference is `(raw + 1) / 2` instead of `1 − (raw + 1) / 2`:

```
raw  = tanh((postness[i] − lineupMean) / TurnoverInteriorPostnessScale)
mult = TurnoverInteriorGuardFloor + (1 − GuardFloor) × ((raw + 1) / 2)   // NOT inverted
weight[i] = max(1, Strength × mult)
```

A post (raw > 0) gets `(raw+1)/2 > 0.5` → mult high. A guard (raw < 0) gets mult near `TurnoverInteriorGuardFloor`. Config keys: `TurnoverInteriorGuardFloor` (0.10), `TurnoverInteriorPostnessScale` (40.0).

### StealerPicker

Guard-favored attribution helper for live-ball turnovers (BadPassIntercepted, LostBallLiveBall). Reads the **defensive** lineup — the first picker to operate on the defense side. Formula is identical to `TurnoverCommitterPicker` (perimeter-favored); only the side, base attribute, and config keys differ:

```
raw  = tanh((defPostness[i] − defLineupMean) / StealerPostnessScale)
mult = StealerPostFloor + (1 − PostFloor) × (1 − (raw + 1) / 2)
weight[i] = max(1, Steals × mult)
```

Config keys: `StealerPostFloor` (0.10), `StealerPostnessScale` (40.0).

### StealerSlot threading

`int? stealerSlot = null` is declared alongside `turnoverOffSlot` in `Resolver.Route()`. It is set only inside the ball-handler branch when `turnoverWasLiveBall = true`, immediately after the `turnoverWasLiveBall` assignment:

```csharp
if (turnoverWasLiveBall)
    stealerSlot = StealerPicker.Pick(t.State, _game, _matchup, _rng).Number;
```

`RoutingOutcome.StealerSlot` (int?, init) appends after `OrbBySlot`. `PossessionRecord.StealerSlot` (int?, null default) appends as the last positional parameter. Harness reads it as exact attribution with a throw on null for live-ball possessions.

### RNG stream

`StealerPicker` consumes one `_rng.NextUnitInterval()` draw per live-ball turnover possession. Every downstream draw on those possessions shifts — the same documented consequence as Phase 31 (OffensiveRebounderPicker) and Phase 33 (TurnoverCommitterPicker). Only DReb and BLK remain as harness `WeightedDraw` calls.

### Picker family (updated)

| Picker | Side | Attributes | Direction |
|---|---|---|---|
| `DefenderPicker` | Defense | Steals/Speed/... | perimeter favored |
| `OffensiveRebounderPicker` | Offense | OffensiveRebounding × PositionalWeight | posts favored |
| `TurnoverCommitterPicker` | Offense | BallHandling × perimeterMult | perimeter favored |
| `TurnoverInteriorPicker` | Offense | Strength × interiorMult | posts favored |
| `StealerPicker` | Defense | Steals × perimeterMult | perimeter favored |

### What is not this session

- **Offensive foul sub-type** (illegal screen vs. charge) — committer is now attributed; distinguishing the flavor is a future session.
- **Team rebounds** — ball out of bounds off a miss; no individual defensive rebounder. Deferred.
- **Defensive rebounder picker** — DReb credit remains a post-hoc `WeightedDraw`.
- **Calibration** of any weight, floor, or scale — correctness before calibration is the standing sequencing principle.

---

## Phase 35 — Rebounding: Wingspan in Battle + Attribution, Defensive Rebound Attribution On-Walk (2026-06-19)

### Wingspan in the team battle (`ReboundPhysical`)

The team-size composite used in Roll I's Stage 1 size shift is extended:

```
ReboundPhysical(p) = ReboundStrengthWeight * p.Strength
                   + ReboundHeightWeight   * p.Height
                   + ReboundWingspanWeight * p.Wingspan
```

`ReboundWingspanWeight` (default 0.5, matching the equal-ish convention of the existing weights) lives in `MatchupConfig` alongside the other rebound-battle knobs. Because `ReboundPhysical` is called only inside `OffensiveReboundShare`'s Stage 1 (team-vs-team mean comparison) and nowhere else, adding wingspan there changes the battle outcome without touching any attribution picker. A longer-armed team earns more boards at the team level; the defense benefits from long arms equally.

### Shared wingspan attribution helper (`ReboundWingspanMultiplier`)

A second, distinct wingspan term operates at the attribution (within-team) level:

```csharp
public static double ReboundWingspanMultiplier(
    double playerWingspan,
    double lineupMeanWingspan,
    MatchupConfig cfg)
    => 1.0 + cfg.ReboundWingspanSwing
           * Math.Tanh((playerWingspan - lineupMeanWingspan) / cfg.ReboundWingspanScale);
```

This is a multiplier centered at 1.0, bounded by the tanh asymptote to the range `(1 − Swing, 1 + Swing)`. At the defaults (Swing = 0.10, Scale = 15.0) the maximum tilt is ±10% — wingspan is a gentle secondary factor, not a dominant one. The helper lives on `Matchup` (parallel to `Postness` and `PositionalWeight`) so both pickers call the same math. The design invariant: **wingspan is rebounding-specific**. It is not folded into `Postness`, which would silently change turnover pickers and steals.

### Offensive picker extended (`OffensiveRebounderPicker`)

Weight formula after Phase 35:

```
max(1, OffensiveRebounding × PositionalWeight(postness) × ReboundWingspanMultiplier × shooterNerf)
```

Stage 1 now collects wingspan alongside postness and computes the lineup-mean wingspan. The multiplier is applied in Stage 2 after positional weight and before (or alongside) the shooter nerf. All existing behavior — the nerf on Three/Long/Mid, the floor of 1, the cumulative walk — is unchanged.

### Defensive rebounder picker (`DefensiveRebounderPicker`)

New static class on the Phase 34 `StealerPicker` pattern. Reads `state.Defense`. Weight:

```
max(1, DefensiveRebounding × PositionalWeight(postness) × ReboundWingspanMultiplier)
```

This is the offensive picker's formula with two changes: `DefensiveRebounding` replaces `OffensiveRebounding`, and `shooterNerf` is absent (the defense has no shooter). The same `Matchup.PositionalWeight` and `Matchup.ReboundWingspanMultiplier` calls are made with the defensive lineup's means. Throws on empty defense lineup. One `IRng.NextUnitInterval()` draw consumed per DReb possession.

### Threading (`DefensiveRebounderSlot`)

Mirrors the Phase 34 `StealerSlot` pattern exactly:

- `Resolver.Route()`: `int? defensiveRebounderSlot = null` declared; stamped in `case Terminal t:` when `t.Reason == "DefensiveRebound"`; appended to `RoutingOutcome` as `DefensiveRebounderSlot`.
- `RoutingOutcome`: `public int? DefensiveRebounderSlot { get; init; }` field appended after `StealerSlot`.
- `Governor.cs`: `int? DefensiveRebounderSlot = null` appended as last parameter of `PossessionRecord`; local + threading + `records.Add(...)` updated.
- Harness: retired DReb `WeightedDraw`; reads `r.DefensiveRebounderSlot` with throw on null for DReb possessions; Phase35 governor invariants assert 1:1 count and null-on-non-DReb.

### RNG stream

`DefensiveRebounderPicker` consumes one engine `_rng` draw per `DefensiveRebound` terminal (from both Roll I and Roll M feeders). Every downstream engine draw on those possessions shifts — the same documented consequence as Phase 31 (OffReb picker), Phase 33 (TO committer), and Phase 34 (stealer). Only BLK remains as a harness `WeightedDraw`. Config hash `50cd44d7...`; same-seed reproducibility within Phase 35 holds.

### Stale comment cleanup (documentation only)

`RoutingOutcome` XML comments updated: removed "metadata for harness draws" framing of the TO-attribution fields (Phase 33/34 moved those engine-side); updated `TurnoverOffSlot` comment to remove "harness draws from BallHandling"; updated `TurnoverWasLiveBall` comment to remove "harness issues exactly one STL" (Phase 34 moved STL engine-side via `StealerSlot`); updated `BlkCount` comment to note BLK is the one remaining harness draw.

### Config additions

**`MatchupConfig` (3 new properties):**
- `ReboundWingspanWeight` (0.5) — weight of Wingspan in `ReboundPhysical` (battle). Non-negative.
- `ReboundWingspanSwing` (0.10) — half-amplitude of the within-team wingspan multiplier. Must be in [0, 1).
- `ReboundWingspanScale` (15.0) — tanh saturation knob for the multiplier, in rating points. Must be > 0. Mirrors `ReboundPositionalScale`.

All three are calibration placeholders. Direction and shape are what Phase 35 validates.

### Picker family (updated)

| Picker | Side | Base attribute | Positional tilt | Wingspan tilt | Shooter nerf |
|---|---|---|---|---|---|
| `DefenderPicker` | Defense | Steals/Speed/... | — | — | — |
| `OffensiveRebounderPicker` | Offense | OffensiveRebounding | PositionalWeight | ReboundWingspanMultiplier | ✓ (Three/Long/Mid) |
| `DefensiveRebounderPicker` | Defense | DefensiveRebounding | PositionalWeight | ReboundWingspanMultiplier | — |
| `TurnoverCommitterPicker` | Offense | BallHandling | perimeterMult (suppressed posts) | — | — |
| `TurnoverInteriorPicker` | Offense | Strength | interiorMult (favored posts) | — | — |
| `StealerPicker` | Defense | Steals | perimeterMult (suppressed posts) | — | — |

### What is not this session

- **Blocks (BLK).** Still a post-hoc `WeightedDraw`. Its own future pass; a natural piece of the attribute-coverage audit.
- **Wingspan in steals / shot contests / perimeter defense.** Part of the attribute-coverage audit, after remaining generators (Roll C type-mix, Roll D fouls) are real.
- **Unifying the battle's two stages into one shared composite.** The two-stage structure stays; this session only adds wingspan to Stage 1.
- **Team rebounds (ball out of bounds off a miss).** No individual rebounder credited; deferred.
- **Calibration** of any rebound weight, floor, swing, or scale. Correctness before calibration is the standing sequencing principle.

---

## Phase 36 — BlockerPicker: BLK Attribution On-Walk (Session 71, 2026-06-19)

### Problem

BLK attribution was the last remaining post-hoc harness `WeightedDraw`. After a possession resolved, the harness looped `for (var i = 0; i < r.BlkCount; i++)` and drew a slot from a flat attribute-sum (`RimProtection + Height + Wingspan + Vertical`) with no zone awareness — the same weight formula regardless of whether the block came on a Rim put-back or a Three-point closeout. This was Phase 23 scaffolding: correct enough to validate plumbing, wrong for anything resembling real basketball.

### Design

**Zone-aware weight formula.** Each defensive player's weight is `max(1, BlockerWeight(zone, player, cfg))` where `BlockerWeight` is a straight weighted sum of six attributes with zone-specific coefficients:

```
BlockerWeight = BlkRimProtection(zone) * p.RimProtection
              + BlkPerimeterDefense(zone) * p.PerimeterDefense
              + BlkPostDefense(zone)      * p.PostDefense
              + BlkHeight(zone)           * p.Height
              + BlkWingspan(zone)         * p.Wingspan
              + BlkVertical(zone)         * p.Vertical
```

Direction: Rim/Short blocks favor help-side bigs — RimProtection and Height dominate, PostDefense contributes. Three/Long blocks favor perimeter defenders — PerimeterDefense leads, Wingspan remains high (the reach that contests). Wingspan is meaningful at every zone; it is the one attribute that bridges rim protection and perimeter contests. Vertical contributes everywhere for timing. Mid is between the two extremes.

No tanh, no gap function. `Matchup.BlockerWeight` (Phase 36, attribution) is intentionally distinct from `Matchup.BlockWeight` (Phase 7, shot-block door) — the former asks "given a block occurred, who gets credit?" while the latter asks "does this matchup produce a block?" Conflating them would be incorrect.

**All five defenders eligible.** Unlike some attribution pickers that narrow the draw (e.g. steals favor guards), every defensive slot is in the pool on every block. A help-side big can block a three-point attempt by rotating in time; a rangy wing can block a post shot by rotating baseline. The zone weighting handles the probability differential without hard exclusions.

**Floor of 1.** The floor is applied by `BlockerPicker`, not by `BlockerWeight`. This follows the existing picker convention (same as `OffensiveRebounderPicker`, `DefensiveRebounderPicker`) and keeps `BlockerWeight` a pure computation.

**`BlkBySlot` is a `SlotGroup`, not `int?`.** BLK can fire multiple times per possession. A possession can contain a primary shot, an ORB, a putback attempt, another ORB, another putback — each of which can be blocked. `StealerSlot` and `DefensiveRebounderSlot` are `int?` because those events fire at most once per possession. `BlkBySlot` mirrors `OrbBySlot` by accumulating via `blkBySlot.WithSlot(slot, 1)` on each block. `BlkCount` is retained as the reconciliation target: `BlkBySlot.Total == BlkCount` is asserted on every possession.

**`SelectedSlot` is irrelevant.** BLK attribution is a defensive question; `SelectedSlot` is offense-side shooter attribution stamped by Roll E. The zone (`ShotType`) from `shotSt` — not the offensive slot — determines which defensive attributes are weighted. Note: the prompt's claim that Roll K's PutBack arm nulls `SelectedSlot` was incorrect per live source. PutBack does `state with { ShotType = ShotLocation.Rim }`; it is the ResetOffense arm that nulls `SelectedSlot`. The design conclusion is unchanged (BlockerPicker does not use `SelectedSlot`), but the reasoning differs.

**Null `ShotType` fallback → Rim.** Roll G stamps `ShotType` on all non-putback paths; Roll K's PutBack arm forces `ShotLocation.Rim`. In current routing, `ShotType` is never null at a block site. The fallback is a defensive guard for future routing changes. Rim is the correct fallback because all putbacks are Rim zone by forcing.

### Structural location

`BlockerPicker.cs` lives in `src/Charm.Engine/Core/` alongside `DefensiveRebounderPicker.cs`, `StealerPicker.cs`, `TurnoverCommitterPicker.cs`, and `TurnoverInteriorPicker.cs`. `Matchup.BlockerWeight` is appended to `Matchup.cs` as a public static helper, following the existing pattern (`Matchup.BlockWeight`, `Matchup.FoulRate`, `Matchup.ReboundWingspanMultiplier`).

### Config additions

**`MatchupConfig` (30 new properties):** Six attributes × five zones = 30 `Blk{Attr}{Zone}` properties (e.g. `BlkRimProtectionRim`, `BlkWingspanThree`). Six switch helpers (`BlkRimProtection(zone)`, etc.) follow the `BlockContestWeights`/`BlockFloor`/`BlockCeiling` pattern. All 30 coefficients must be `>= 0` (enforced in Load). No sum-to-one constraint — these are weighted reads, not distributions. All defaults are calibration placeholders.

### Attribution family: complete

All six on-walk pickers are now wired. Every post-hoc harness `WeightedDraw` has been retired:

| Phase | Picker | Attribute basis | Moved from |
|---|---|---|---|
| 31 | `OffensiveRebounderPicker` | OffensiveRebounding × PositionalWeight × WingspanMultiplier | harness WeightedDraw |
| 33 | `TurnoverCommitterPicker` | BallHandling × perimeterMult | harness WeightedDraw |
| 34 | `TurnoverInteriorPicker` | Strength × interiorMult | harness dispatch |
| 34 | `StealerPicker` | Steals × perimeterMult | harness WeightedDraw (seed+1) |
| 35 | `DefensiveRebounderPicker` | DefensiveRebounding × PositionalWeight × WingspanMultiplier | harness WeightedDraw (seed+2 DReb) |
| 36 | `BlockerPicker` | Zone-weighted sum of 6 blocking attributes | harness WeightedDraw (seed+2 BLK) |

`foulRng` (seed+3, shooting fouls via `DrawFoulingDefender`) is the only remaining harness-side attribution draw; it is intentionally harness-side because shooting-foul attribution is not yet a first-class resolved event in the engine.

### What is not this session

- **Calibration** of any blocker weight. Block rates in the frozen corpus are low (per-player BLK ~0.5–0.9/game) and will need tuning once all generators are real.
- **Roll C real generator** (turnover type-mix). Next session.
- **Roll D real generator** (defensive foul flavor). Future session.
- **Mismatch-hunting / help-defense model for DefenderPicker.** Noted in `DefenderPicker.cs` itself; a future session.
- **Team rebounds** (ball out of bounds off a miss). No individual credited; deferred.

---

## Phase 37 — Roll C Real Generator: Flat Context-Driven Type-Mix (2026-06-19)

### What Roll C models

Roll C answers one question: given that a possession ended in a turnover, what *kind* was it? The answer depends on the game situation — where the ball was and what the offense was trying to do — not on individual player attributes. A sure-handed guard who rarely turns it over turns it over the same *way* as a turnover-prone one; it's just rarer.

### Three flat pies, one per context

Roll C selects one of three fixed weight sets based on the `TurnoverContext` ticket stamped on the incoming `Continue`:

- **Halfcourt** — the default. Every turnover that doesn't arrive with a context ticket (Roll B's loss, Roll F's player-action turnovers, Roll A's frontcourt re-inbound) lands here. Bad passes, lost balls, travels, carries, violations — the full menu of halfcourt turnovers. Backcourt-only types (FiveSecondInbound, TenSecondBackcourt) are zero.
- **Transition** — stamped by Roll J's Turnover arm. More live strips (LostBallLiveBall 35%) than Halfcourt (16%); offensive fouls nearly vanish (5%). The live/dead split is higher because transition turnovers are more often contested breaks, not set-play mistakes.
- **EntryBackcourt** — stamped by Roll A when `state.Frontcourt == false`. The backcourt bring-up context, now live. Only types that can happen before crossing halfcourt: bad passes, lost balls, and the three backcourt-only violations (FiveSecondInbound, TenSecondBackcourt, ShotClockViolation on the way up). Travels, carries, 3-second violations, offensive fouls, and over-and-back are zero — you haven't crossed halfcourt yet.

### No pressure parameter

The stub's pressure wire (`PressureLostBallLiveBallNudge`) was a seam-test placeholder — it proved the generator→roll seam could carry signal, not that pressure should influence the type-mix. Pressure changes how often a team turns it over (Roll A/B/F), not what kind of turnover results when they do. The parameter was always called at 0.0 in every call site; its removal is a mathematical no-op, confirmed by the corpus hash being unchanged.

### No player-attribute tilt

Turnover type is context-driven, not player-driven. This is a deliberate design call: the type depends on the phase of play (bring-up, halfcourt set, transition break), not on who has the ball. Player attributes tilt how often a turnover happens and who commits it (Roll A/B/F and `TurnoverCommitterPicker`), not what form it takes once it happens.

Calibration of all three pies is explicitly deferred until all generators are wired. Current weights are shape-appropriate placeholders.

### What is not this session

- **Calibration** of any Roll C weight. Live/dead split calibration is deferred.
- **Roll D real generator** (defensive foul flavor). Future session.
- **Transition expanded types** (Travel, DoubleDribble in Transition). Currently zero; a calibration-session question.

## Phase 38 — Wire RollKGenerator Into All Full-Engine Simulations (2026-06-19)

### What changed and why

`RollKGenerator` shipped in Phase 32 and has been exercised by its own dedicated checks (`RollKReboundBatchCheck`, `RollKBonusForkCheck`) ever since. Phase 38 is a pure activation step: it replaces the flat stub with the real generator at every harness site that builds a full-engine resolver, so the observation corpus and all attribution checks now run with matchup-aware putback tilt.

### The stub's surviving role

`RollKStubPieGenerator` remains the test double at 20 sites where flat Roll K behavior is required by the test's design:

- **`RollMContextSelectionCheck`** asserts the pie equals the flat config weights exactly. The real generator tilts PutBack by matchup, so it correctly produces a different answer — which would break the assertion. The stub is the only way to verify the flat-pie assertion without restructuring the check.
- **Handoff and isolation checks** (`RollFHandoffCheck`, `RollGHandoffCheck`, `RollHHandoffCheck`, `RollIBlockContextSelectionCheck`, `GovernorLoopCheck`, `RollLFreeThrowCheck`, `OffensiveReboundConvergenceCheck`, `RunGame`) keep other rolls flat to measure only the targeted behavior. Injecting attribute-driven Roll K would contaminate the signal they exist to produce.
- **Press checks** (`Phase15`, `Phase16`) keep Roll K flat for the same isolation reason.
- **`Phase30CoachingLayer2Check`** — same logic.

The stub file is not deleted and `IRollKPieGenerator` is not collapsed. Both remain until the isolation checks are refactored, which is not a goal.

### Classifier: two variable-name patterns

All nine FLIP sites were verified against the live source before editing. The two patterns are:

- **Pattern A** — four sites (`ObservationRunV1`, `StressTestArchetypeRosters`, `AttributionSanityCheck`, `Phase25ShootingFoulAttributionCheck`): config is pre-loaded into named locals; the swap is `new RollKStubPieGenerator(cfgK)` → `new RollKGenerator(cfgK, cfgMatchup, game)`.
- **Pattern B** — five sites (`Phase31`–`Phase36` attribution checks): config is loaded inline; the swap is `new RollKStubPieGenerator(RollKConfig.Load(configPath))` → `new RollKGenerator(RollKConfig.Load(configPath), matchupCfg, govGame)`.

A global find-replace on the constructor name alone would have compiled the wrong variable names at Pattern B sites (confusing `cfgMatchup` with `matchupCfg`, or missing `govGame`). The per-site read was load-bearing.

### Corpus effect

The putback formula tilts PutBack mass by (rebounder offensive composite) minus (self-weighted defensive team interior composite), run through GapFn and clamped to `[PutbackFloor, PutbackCeiling]` with a zone modifier. The Python pre-check and harness confirm the expected pattern:

- **EliteVsWeak**: dominant-big rebounder vs weak interior → putback rate well above stub flat (0.40). Stress-test ORB%: Elite 48.8% vs Weak 12.9% — the gap is now properly encoded in the engine.
- **AverageVsAverage**: near-zero gap → near-zero shift → putback rate barely above flat. Stress-test ORB%: 28.9% vs 29.1% — essentially unchanged.
- **Config hash unchanged** (`e48085ff...`) — no config edit, only a generator swap.


## Phase 39 — Assist Attribution Core: AssistPicker, Zone-Based Rate, Lineup-Passing Factor (2026-06-20)

### What assist attribution models

An assist credit is not a tracked pass — it is a probabilistic draw on a made field goal. Same philosophy as STL/BLK/DRB: given a trigger event (a made bucket), who on the offense gets credit? This keeps the engine free of per-event state and consistent with every prior attribution picker.

Two questions to answer per eligible made FG: (1) was the bucket assisted? (2) if yes, which teammate gets credit?

### Eligibility

- **Putbacks are ineligible.** A putback follows an offensive rebound — no pass occurred. Gated by `!c.Putback` in the Resolver, independently of `SelectedSlot` state. The null-`SelectedSlot` guard (`shotSt.SelectedSlot is not null`) is a separate, independent safety for the bonus-FT putback edge (Roll E never ran on that path). These two guards are not equivalent and must remain separate.
- **Both `Made` and `MadeAndFouled` are eligible.** The and-1 basket is a real made field goal; the assist is credited at the time of the make, before the foul shot.
- **Free throws are never assisted** — the roll lives in the made-FG branch; FT makes enter via a completely separate path (`FtmBySlot`) and never reach this site. No guard needed; stated so the build does not add one in the wrong place.

### Was it assisted? — Zone × lineup-passing factor

```
assistProb = clamp( zoneBase × LineupPassingFactor, AssistRateFloor, AssistRateCeiling )
```

**Zone base rates** come from real hoop-math data (assisted-FG% by zone), blended to five engine zones:

| Zone | Base rate | Data anchor |
|---|---|---|
| Three | 0.88 | Blend of corner (~95%) and above-the-break (~80–81%) |
| Long | 0.62 | Baseline two, wing two range |
| Rim | 0.54 | Low paint |
| Mid | 0.50 | Straight-up mid-range |
| Short | 0.43 | High paint / self-created floaters |

Three highest: almost every three is created by a pass. Long above rim: baseline twos and wing twos are largely catch-and-shoot. Rim above short: lay-ups are usually pass-initiated; short (floaters, hooks) is where self-creation peaks.

`AssistRateCeiling = 0.95` sits above the Three base (0.88) so strong-passing lineups can approach the corner-three reality. `AssistRateFloor = 0.25` prevents the rare case of very weak passing lineups from producing implausibly low rates on catch-and-shoot zones. The ceiling is not a stylistic cap — it is a floating-point safety and a calibration headroom mechanism. It should always sit above the highest zone base rate.

**`AssistedRateThree` and `AssistRateCeiling` corrected post-green.** The v1 prompt set both at 0.83/0.80 using the above-the-break three figure as the *ceiling* when it is actually the *floor*. Corrected once the harness confirmed the error: threes were saturating at 0.80 for every lineup regardless of passing quality. The fix (`AssistedRateThree = 0.88`, `AssistRateCeiling = 0.95`) allows the lineup-passing factor to meaningfully differentiate team three-point assist rates.

**LineupPassingFactor** (deterministic, no RNG):

```
meanAssistWeight = mean of AssistWeight(p) over the five populated offensive players
                   (shooter included — this is a team property)

LineupPassingFactor = 1.0 + AssistPassSwing
                          × tanh( (meanAssistWeight − AssistPassMidpoint) / AssistPassScale )
```

Defaults: `AssistPassMidpoint = 50.0`, `AssistPassScale = 20.0`, `AssistPassSwing = 0.25`. Factor range: (0.75, 1.25). A league-average lineup (mean attribute ≈ 50) produces factor ≈ 1.0. A broad high-passing lineup lifts all zone rates proportionally; a scorer-heavy lineup suppresses them.

**Why the multiplier is necessary.** Without it, passing attributes only decide *who* gets credited, never the team rate. Every team would post the same zone-averaged assist rate (varying only by shot mix), which cannot reproduce the real 37%→71% spread. The multiplier at the lineup grain is the mechanism that makes a ball-movement roster post a high team rate and a scorer-heavy roster a low one.

### Who gets credit? — AssistPicker

Per-player assist weight:

```
AssistWeight(p) = AssistPassingWeight    × p.Passing       (default 0.50)
                + AssistPlaymakingWeight × p.Playmaking     (default 0.35)
                + AssistIqWeight         × p.BasketballIQ   (default 0.15)
```

**Coefficient sum-to-one is correct and intentional.** This deviates from `BlockerWeight` and the rebound positional weights, which do NOT sum to one (the picker normalizes among players, so absolute scale is irrelevant there). The sum-to-one constraint keeps `AssistWeight` on the 0–100 attribute scale, making `AssistPassMidpoint = 50` the correct league-average reference for `LineupPassingFactor`. Conflating these conventions would be wrong; the documented rationale survives in both the class XML doc and `MatchupConfig`.

Pick mechanics: the shooter's slot gets weight 0 (excluded); every other populated offensive player gets `max(1, AssistWeight(p, cfg))`; one RNG draw, cumulative walk, last eligible slot as floating-point fallback. Throws `InvalidOperationException` on empty non-shooter lineup — loud, unreachable in valid play.

`AssistWeight` is private to `AssistPicker` because nothing else consumes it. This differs from `Matchup.BlockerWeight` (shared with the team shot-block math) and the rebound helpers (shared with team rebound share). No `Matchup` static is needed or added.

### The W Illinois problem — deferred

W Illinois posted a 37% team assist rate despite having passing guards — because those guards didn't carry the offensive load. Good passers who *don't take the shots* can't generate assists on shots they never take. Phase 39 captures the lineup-collective grain: a roster of good passers produces a higher rate than a roster of poor passers. It does not yet capture load concentration: a good passer who takes 5% of shots contributes less than a good passer who takes 30%. That is the iso/motion concentration slider's job — a deferred feature that Phase 39 must stand alone without.

### Structural location

`AssistPicker.cs` lives in `src/Charm.Engine/Core/` alongside every other picker. `AstBySlot` threads as a `SlotGroup` init-only property through `RoutingOutcome → PossessionRecord`, mirroring `BlkBySlot` exactly. `AstBySlot.Total ≤ Fgm` on every possession (harness-asserted). `totalAstBySlot` is accumulated per-game in the main run loop; `bsAst.Sum() == totalAstBySlot` is the reconciliation invariant, mirroring the BLK reconciliation.

### Config additions (`MatchupConfig`, 14 new properties)

Three coefficient props (`AssistPassingWeight`, `AssistPlaymakingWeight`, `AssistIqWeight`) — load invariant: each `>= 0`, sum `== 1.0 ± epsilon`. Three factor props (`AssistPassMidpoint`, `AssistPassScale`, `AssistPassSwing`). Five zone rate props (`AssistedRateThree`, `AssistedRateLong`, `AssistedRateMid`, `AssistedRateShort`, `AssistedRateRim`) — load invariant: each in `[0,1]`. Two bound props (`AssistRateFloor`, `AssistRateCeiling`) — load invariant: both in `[0,1]`, `Floor < Ceiling`. Switch accessor `AssistedRate(ShotLocation zone)`. All defaults are calibration placeholders.

### Attribution family: complete (all six pickers)

| Phase | Picker | Attribute basis | Event |
|---|---|---|---|
| 31 | `OffensiveRebounderPicker` | OffensiveRebounding × PositionalWeight × WingspanMultiplier | Offensive rebound |
| 33 | `TurnoverCommitterPicker` | BallHandling × perimeterMult | Pre-selection turnover |
| 34 | `TurnoverInteriorPicker` | Strength × interiorMult | Interior/post turnover |
| 34 | `StealerPicker` | Steals × perimeterMult | Live-ball steal |
| 35 | `DefensiveRebounderPicker` | DefensiveRebounding × PositionalWeight × WingspanMultiplier | Defensive rebound |
| 36 | `BlockerPicker` | Zone-weighted sum of 6 blocking attributes | Block |
| 39 | `AssistPicker` | Passing × 0.50 + Playmaking × 0.35 + BasketballIQ × 0.15 | Assist on made FG |

### What is not this session

- **Calibration** of any assist rate, coefficient, or factor. All values are shape-appropriate placeholders. Calibration waits until all generators are wired.
- **Iso/motion concentration slider.** The W Illinois problem — good passers who don't carry the load. Deferred; Phase 39 must stand alone if the slider is never built.
- **Per-event assist record.** `AstBySlot` (per-possession `SlotGroup` totals) is all v1 needs. A per-event `(shooterSlot, assisterSlot)` log is a noted future option.
- **Transition-specific assist rates.** Zone rate is the anchor for now.

## Phase 40 — Retire Last Two Flavor Stubs: RollDGenerator, RollOffensiveFoulGenerator

### Why both stay flat

Roll D foul flavor and offensive foul flavor are the last two generators that carried the "stub" label. Both are now renamed to real generators with honest doc comments. Neither received new math. This is identical in nature to the Roll C rename in Phase 37.

**Roll D stays flat for an architectural reason.** `ResolveFoulType` is emitted by Roll A, Roll B, and Roll F — all three fire before Roll G runs. Roll G is the step that stamps `ShotType` onto `PossessionState`. So `state.ShotType` is null at every Roll D call site; zone context is architecturally unavailable without restructuring the possession chain. `SelectedSlot` can be non-null on the Roll F feeder path (Roll E may have already run), but flavor is non-routing theater with no downstream consumer. Adding player-attribute context here would imply the flavor field matters to the simulation. It does not.

**Offensive foul flavor stays flat for the same reason.** The Frontcourt context split already ships — it correctly captures the dominant real-world pattern (illegal screens in halfcourt sets vs. charges/push-offs on backcourt bring-ups). Adding further player-attribute tilt would imply the flavor field routes or scores. It does not.

**The standing rule for both:** future sessions must not add attribute-driven logic to either generator without first establishing a downstream consumer that reads the flavor field for routing or scoring purposes.

### No interfaces for either generator

`IRollDPieGenerator` and `IRollOffensiveFoulPieGenerator` do not exist and were not created. Both generators are used as concrete types directly everywhere in `Resolver.cs` and `Program.cs`. This is the correct pattern for flat theater-only generators where interface polymorphism adds no value.

### Rename counts (global find-replace)

| Old name | New name | Occurrences replaced |
|---|---|---|
| `RollDStubPieGenerator` | `RollDGenerator` | 32 (Program.cs) + 4 (Resolver.cs) |
| `RollOffensiveFoulStubPieGenerator` | `RollOffensiveFoulGenerator` | 29 (Program.cs) + 4 (Resolver.cs) |

### All rolls now have real generators

Phase 40 completes the generator retirement pass. Every roll in the full-engine simulation path now has a real, named generator. The calibration pass can begin.

## Game Boundaries — Halftime Foul Reset + Opening Tip + Overtime

### Why these three are one build

All three items touch the Governor's game-lifecycle logic and are meaningfully coupled. The opening tip uses `TipPossession.CreateFromTip`, which requires the arrow to be `Off`. Overtime requires the arrow to be reset to `Off` before each OT tip. The halftime foul reset must be guarded so it doesn't fire before OT tips. Doing them separately would require intermediate states that don't represent valid basketball.

### Halftime foul reset

NCAA rule: team fouls reset at the regulation half boundary only. Overtime periods carry forward whatever foul count teams ended regulation with. A team in the double bonus at the end of regulation begins overtime in the double bonus.

The reset lives in `FoulTracker.ResetForNewHalf()`, called by the Governor inside the regulation `while` loop's half-transition block. The guard `half < _cfg.Halves` is load-bearing: the transition block runs one final time when the last regulation half drains, and the guard prevents the reset from firing there. Without the guard, fouls would be wiped before the first OT tip.

`GovernorLoopCheck` no longer asserts foul counts as part of pass/fail. Its existing fixture (pre-loading fouls above the bonus threshold before the run) was designed to prove foul persistence across possessions — a different invariant from the halftime reset. The persistence proof remains valid (the fixture checks that fouls survive individual possession transitions); the reset proof moved to `GameBoundaryCheck` sub-check 2, which uses a controlled config to run two 1-second halves and assert that both teams' foul counts are zero after the run.

### Opening tip seam

`TipPossession.CreateFromTip(GameState game, IRng rng, int possessionNumber)` is the single entry point for any coin-flip tip-off. It enforces the precondition that the possession arrow is `Off`, delegates to `JumpBall.Resolve` (which handles the 50/50 draw and arrow set), and returns a fully-constructed `PossessionState`.

**Precondition enforcement is architectural.** The throw on `ArrowState != Off` prevents any caller from accidentally converting a routine alternating-possession jump ball into a fake tip. `Sub-check 5` in `GameBoundaryCheck` permanently proves this: a game with the arrow set to `Home` causes `CreateFromTip` to throw.

**Controlled fixtures are exempt.** `GovernorLoopCheck` explicitly sets the arrow `On` before its run (to predict the final arrow state from jump-ball count). This is a deliberate test setup, not a game start, and must not use `TipPossession.CreateFromTip`. All isolated roll checks that construct possession states for testing specific rolls are similarly exempt.

**All real game starts now use the tip.** `Program.Game.cs`, `ObservationRunV1`, `StressTestArchetypeRosters`, and `AttributionSanityCheck` all moved to `TipPossession.CreateFromTip`. The shared `state` in `Program.cs` used by `ShowSamples` and isolated roll checks was explicitly confirmed out of scope (A10) — changing it would consume from the deterministic RNG stream used by downstream roll checks.

### Overtime

If regulation ends tied, the Governor runs 5-minute OT periods (configurable via `GovernorConfig.OvertimeSeconds`) until a winner exists. Each OT period:
1. `_game.ResetPossessionArrow()` — sets arrow back to `Off` for a fresh tip contest.
2. `TipPossession.CreateFromTip` — resolves the tip and returns the first OT possession. The possession number comes directly from `state.PossessionNumber`, which already holds the next sequential number after the regulation loop exits (the local function spawns before returning — A11 confirmed at audit).
3. The OT `while (otRemaining > 0.0)` loop runs the same `RunOnePossession` local function used in regulation, stamping `Half = _cfg.Halves + otPeriod` (first OT = 3, second OT = 4, etc.).
4. No foul reset at OT boundaries — fouls carry per NCAA rule.

`GovernorRunResult` now carries `int OvertimePeriods` as a trailing parameter. 0 = regulation finish; 1 = one OT; etc. All existing consumers use named properties — no positional pattern-matching in the codebase — so the trailing addition is non-breaking.

### `RunOnePossession` local function

The possession-loop body was extracted into a local function inside `Governor.Run` so it can be called from both the regulation loop and the OT loop without duplicating ~100 lines of threading code. The local function captures run-level accumulators naturally from the enclosing `Run` method. Only the values that differ between the regulation and OT callers are passed explicitly: `state`, `periodRemaining`, and `periodNumber`.

**Extraction boundary.** The local function includes code from intent selection through score write, record creation, and spawning the next `PossessionState`. It does NOT include the `if (halfRemaining <= 0.0)` half-transition block, any half increment, any foul reset call, or any period-clock reinitialization. Period transitions belong exclusively to the regulation and OT caller loops.

### What is not this session

- **Overtime foul-carry direct regression test.** The structural guarantee (guard `half < _cfg.Halves` prevents reset after final regulation half) plus source-level verification is the proof for this session. A separate controlled-foul OT regression test is explicitly deferred.
- **Height-driven tip contest.** `JumpBall.Resolve`'s 50/50 draw is the existing placeholder. The `FUTURE SEAM` comment in `JumpBall.cs` remains — plugs in once the player/attribute layer adds a center matchup.
- **Score-aware end-of-regulation strategy.** The end-of-half intent pie (`HoldShootLast`, `ShootEarly`, `NoShot`) is score-blind. Late-game strategy (fouling to stop the clock, heaving a three to tie) is a named future feature.

### Session 01 — Wingspan-Driven Opening Tip

**The model.** The team with the longer-wingspan player wins the tip more often. The jumper for each team is the player with the highest `Wingspan` rating in the current lineup (slots 1–5, null slots skipped). Win probability is computed from the gap between the two jumpers' Wingspan ratings via a linear formula: `0.50 + (gap / 7.0) * 0.40`, clamped to [0.10, 0.90]. A 7-rating-point gap on the 0–99 scale → ±40% shift from 50/50. No tip is ever a guaranteed win (no hard zeros, per Principle 1). Curve is calibration-pending.

**Wingspan is a reach rating, not literal inches.** The 0–99 scale is the same as all other player attributes. A 7-rating-point gap is intentionally treated as a major advantage because the tip is a specific athletic contest where reach dominates — the aggressive curve reflects that. Calibration will tune the scale.

**Fallback behavior.** When no roster is populated for a side, `MaxWingspan` returns 50. Both sides returning 50 → probability exactly 0.50 → pure 50/50. This preserves backward compatibility for any check that constructs a `GameState` without seating players. A real lineup facing an empty side correctly holds the wingspan advantage (only one side falls back to 50).

**Implementation seam.** `MaxWingspan` reads from `game.RosterFor(side).PlayerAt(lineup.SlotAt(slot))` — the same seam used by all attribute-driven generators. When substitutions exist, `PlayerAt` always returns the current occupant, so mid-game jump balls (Arrow ON → routine alternating possession, no wingspan read) and OT tips (Arrow OFF → MaxWingspan fires against whoever is currently on the court) both work correctly without any additional wiring.

**Check design.** Sub-check 5 in `GameBoundaryCheck` derives its assertion threshold from the actual roster on the court — not hardcoded values. It seats the config roster, reads max wingspan for each side, computes `expectedHomeProb` from the same formula `JumpBall` uses, and asserts the observed rate across 10,000 tips lands within `RateTolerance * 2.0` of that expected value. This makes the check valid with any lineup: when rosters change, the threshold auto-updates.

## Session 02 — GravityContribution + SpacingContribution Formula Updates; Transition Retired

### GravityContribution: perimeter gravity is real (small)

The prior formula gave Outside zero weight in gravity, treating perimeter shooting as purely spacing. This was architecturally clean but slightly wrong. A dominant perimeter threat pulls the defense toward the arc even without attacking the rim — the defense must account for the possibility of the three. That is real gravity, not just spacing.

New formula: `0.35×Finishing + 0.25×Close + 0.25×Access + 0.10×Mid + 0.05×Outside`

The 0.05 Outside weight is deliberately small. Rim pressure remains the primary gravity signal (Finishing + Close = 0.60). The delta versus the prior formula is exactly `0.05 × (Outside − Access)`: perimeter-dominant players tick up, post-dominant players tick down modestly. Both directions are intentional and should not be corrected during calibration without explicit design approval.

**Two behavioral consequences in AttentionGenerator (both intentional):**
1. The Outside term flows into the defensive attention allocation and team gravity/openness calculation.
2. The Outside term flows into the passing-converter activation route — `postRoute` reads `GravityContribution / 100.0`, so elite perimeter threats now have a slightly higher activation floor.

### SpacingContribution: OffBallMovement promoted from dormant to active

A shooter who can get open without the ball (cutting, relocating, using screens) is a more dangerous spacing threat than one who stands in the corner. Before this session, OffBallMovement was authored data that no generator read. This session promotes it into a compound multiplier that amplifies spacing when paired with real shooting ability.

New formula:
```
BaseSpacing         = 0.75 x Outside + 0.25 x Mid
SpacingContribution = BaseSpacing x (1 + (OffBallMovement / 100) x (Outside / 100) x 0.30)
```
Result clamped to [0, 100].

Key design properties:
- **OffBallMovement does almost nothing without shooting ability.** Outside near 0 gives a multiplier near 1.0. A non-shooter who runs all day does not create spacing.
- **Stationary shooters score slightly lower than the old formula.** Outside base weight dropped from 0.85 to 0.75. A good shooter with low OffBallMovement gets a small compound bump that does not fully recover the base weight reduction. This is intentional — OffBallMovement is now a real separator.
- **Mid weight rose from 0.15 to 0.25 in the base.** A stretch player who can step to the elbow creates real spacing regardless of position. This is Emmett's basketball call.
- **The 0.30 literal is a calibration placeholder.** Do not promote to a config field or named constant until the calibration pass establishes a target range.

### Transition derived property: retired

`Transition = (Athleticism + Finishing) / 2` let Athleticism (itself a derived composite of five physical attributes) and Finishing count again through a third channel. The retirement removes double-counting. Nothing is lost: any generator that wants to model open-floor scoring ability can read Athleticism and Finishing directly.

### The validation surface for derived player properties

Formula changes at the player-model layer do not produce visible shifts in aggregate game stats. Their effect flows through AttentionGenerator into Roll H C1/C2/C3 and Roll E selection tilt — real but several layers deep and small in magnitude. The direct validation surface is the per-player Grav and Spac column values in the GameLifecycle check player table: these show computed derived values per player and can be checked against formula predictions. Calibration of magnitudes is deferred to the calibration pass.

## Session 03 — HelpDefense: Stage 2 of the Interior Defensive Sequence

### The four-stage interior defensive sequence

Interior shot defense is modeled in four distinct stages, each with a single job and a single attribute family:

| Stage | Attribute | Job |
|---|---|---|
| 1 | PerimeterDefense / PostDefense | Primary defender's initial contest (matchup make door, wired Phase 6) |
| 2 | HelpDefense | Secondary help reduces make% after the first defender is beaten (C6, Phase 41) |
| 3 | RimProtection | Shot-altering / block threat (block door, Phases 7 + 36) |
| 4 | Wingspan / Height | Physical reach ceiling on blocks and rebounds (Phases 7 + 35 + 36) |

The rule: **each rating has one job in one stage.** HelpDefense reduces make%; RimProtection drives blocks. They compound (a help defender who also blocks is the most dangerous interior combo) but fire at different moments and must not both subtract from the same make%. This is not a convention — it is a hard structural separation proven by sub-check (f): varying only off-ball HelpDefense changes makePct but not blockWeight (byte-identical); varying only the matched defender's RimProtection changes blockWeight but not the C6 suppression component (identical drop at RimP=10 vs RimP=90).

### HelpDefense: off-ball-only, accelerating

HelpDefense measures the ability of a player to rotate from off-ball positions and contest an interior scorer after the primary defender has been beaten. Three design properties:

**Off-ball-only.** The matched defender (the one contesting the shot at Stage 1) is excluded from the C6 aggregate — unconditionally, regardless of his HelpDefense rating. He already had his Stage 1 contest; including him in Stage 2 would be double-counting.

**Accelerating aggregation with a fixed denominator.** The formula:
```
offBallShare = sum(HelpDefense/100 for each off-ball slot, null=0) / 4.0
helpDefenseSuppression = Scale × offBallShare^Exponent
```
The denominator is always 4.0 — never the count of populated helpers. This is what makes one good helper a sliver and four a defensive identity. A mean-over-populated formula would make one elite helper as effective as four; the fixed denominator correctly rewards the roster-construction decision to build around team help defense. The exponent must be strictly > 1.0 (enforced by Load() invariant) — linear or diminishing aggregation would fail sub-check (b).

**Correlated with size, not gated by it.** Big men tend to have higher HelpDefense because they rotate to protect the rim. But a guard with high HelpDefense is an explicit, valuable unlock — the config fixture assigns Marcus Webb (a guard) HelpDefense=55 to exercise this path. Generation-layer size correlation is a note for the player-pool layer, not a constraint here.

### Why RimProtection also moves make% at Rim zone

Sub-check (f)'s original design assumed RimProtection only affected the block door. Phase 6 output disproved this: a rim specialist with RimP=90 and PostD=40 gives up lower make% at Rim than a balanced defender (all=50). `Matchup.EffectiveRating` for the Rim zone includes a RimProtection term in the defensive blend — Stage 3 feeds Stage 1 through the matchup. This is correct basketball: a rim protector deters attackers even before the block. The proof of C6 independence does not require byte-identical makePct across RimProtection values; it requires byte-identical HelpDefense drops. Since `helpDefenseSuppression` has no RimProtection term, the logistic baselines cancel exactly and the proof holds analytically.

### Calibration placeholders

`HelpDefenseSuppressionScale = 0.15` and `HelpDefenseAggregateExponent = 2.0` are calibration placeholders. At these values, four elite off-ball helpers (HelpDefense=99) suppress interior make% by ~14.7 percentage points — aggressive but not unreasonable as a pre-calibration starting point. The Screening counterweight (next session) will create a two-sided gap that calibration tunes as a pair, not in isolation.

### What is not this session

- **Screening counterweight.** The two-sided make% gap (high Screening vs. high HelpDefense) assembles next session. No Screening term, hook, or config was added here.
- **OffBallDefense.** A separate future attribute for perimeter help defense. Remains in the dormant-pending-module comment.
- **Generation-layer size correlation.** Noted for the player-pool layer; HelpDefense is authored directly regardless of position.

## Session 04 — Screening: The Offensive Counterweight to HelpDefense

### The two-sided interior make% gap

Sessions 03 and 04 together complete a symmetric pair. Neither term is meaningful in isolation — they are designed to be tuned as a unit during calibration:

| Side | Term | Direction | Aggregate |
|---|---|---|---|
| Offense | Screening (C5.5) | + makePct | All 5 offensive slots, sum / 5.0 |
| Defense | HelpDefense (C6) | − makePct | 4 off-ball defenders (matched excluded), sum / 4.0 |

The net make% effect on any interior halfcourt possession is `+screeningBonus − helpDefenseSuppression`. This is emergent from two independent signed terms, never computed as a difference inside the engine. A team with no screeners against an elite help-defense team gets the full C6 suppression unbuffered. A team with elite screeners against a poor help-defense team gets the full bonus unmuted. Symmetric strengths roughly cancel — proven analytically and confirmed by sub-checks (f) and (g).

### Why the shooter is included in the Screening aggregate (no exclusions)

C6 excludes the matched defender because he already fired at Stage 1. C5.5 has no analogous exclusion: the shooter's own Screening rating counts as one of five symmetric slots. A screen-setting shooter contributes to the team's screening environment in the seconds before the release. Excluding him would create a feedback loop with Roll E's shot selection (which implicitly accounts for the shooter's Screening via usage tilt) and would break the aggregate's slot symmetry. Sub-check (a) proves symmetry: a lineup with shooter Screening=99 and a lineup with one teammate Screening=99 (all else equal) produce byte-identical make%.

### The accelerating formula and fixed denominator (mirror of C6)

```
screeningShare = sum(Screening/100 for each populated offensive slot, null=0) / 5.0
screeningBonus = ScreeningBonusScale × screeningShare^ScreeningAggregateExponent
```

The denominator is always 5.0 — never the count of populated screeners. One elite screener gets share=0.99/5=0.198; five get share=0.99. At Exponent=2.0, the ratio is 25× (5²) — the same accelerating payoff C6 achieves at 16× (4²) with its 4.0 denominator. A mean-over-populated formula would make one elite screener as effective as five; the fixed denominator correctly rewards the roster-construction decision. Sub-checks (b) and (e) prove the 25× ratio and the (2/5)²=0.16 partial-roster ratio respectively.

### The deferred-clamp discipline (load-bearing)

C1 and C4 each apply their own upper clamp (`if (makePct > 1.0) makePct = 1.0`) because they have no immediately-following negative partner. C5.5 does not clamp. The single `Math.Clamp(makePct, 0.0, 1.0)` lives at the end of the C6 block and settles both signed terms together.

If C5.5 upper-clamped to 1.0 before C6 ran, the symmetric-cancellation contract would fail in the upper make% range. Example: pre-C5.5 makePct=0.95, C5.5 lifts to 1.097, premature clamp drops to 1.0, then C6 subtracts 0.147 → final 0.853 (instead of the correct 0.95). Sub-check (g) is the regression guard for this bug: it constructs a high-baseline fixture (bothOffBaseline=0.926502 > saturation threshold 0.852985) and asserts cancellation still holds. A premature-clamp implementation would show ~7.35pts error at that fixture.

### Symmetric cancellation at full capacity

At `ScreeningBonusScale=0.15` and `ScreeningAggregateExponent=2.0`, the C5.5 maximum bonus (5×Screening=99, denom 5.0) = `0.15 × (4.95/5)² = 0.147015`. C6's maximum suppression (4×HelpDefense=99, denom 4.0) = `0.15 × (3.96/4)² = 0.147015`. Identical to floating-point precision. Elite-vs-elite cancels algebraically — the matched defender's Stage 1 contest (the matchup logistic) is the only net differentiator. This is the intended design: the Screening/HelpDefense pair compresses the make% range rather than inflating or deflating it.

### What is not this session

- **Perimeter Screening bonus.** C5.5 is gated to `zone ∈ {Rim, Short}`. The perimeter unlock (Mid/Long/Three) is a one-line gate change that lands when `OffBallDefense` is authored as the perimeter defensive counterweight.
- **OffBallDefense.** The perimeter defensive counterweight. Remains in the dormant-pending-module comment.
- **Calibration.** Both `ScreeningBonusScale` and `HelpDefenseSuppressionScale` are placeholders set to 0.15 with Exponent=2.0. They must be tuned as a pair against real D1 data — tuning one without the other destroys the symmetric-cancellation property.

## Session 05 — ReboundPhysical Weight Ordering: Strength Primary, Height/Wingspan Equal Secondary

### The design decision

`ReboundPhysical(p, cfg)` is the team-size composite used in the pre-staging stage of `OffensiveReboundShare`. It determines which team has the physical edge in the rebound battle before skill enters. Phase 43 locks the relative ordering of its three components:

- **Strength is the lead factor.** Strength represents box-out dominance — the physical battle for position in the lane before the ball arrives. Who holds their ground, seals their man, and controls space. This is the most direct determinant of whether a player wins the pre-staging battle.
- **Height and Wingspan are equal secondary factors.** Both represent reach and length — the ability to outreach a competitor once in position. They are legitimately equal because they serve the same function (extending to the ball) through different physical mechanisms (standing reach vs. arm length).

### The weights

```
ReboundStrengthWeight  = 0.525
ReboundHeightWeight    = 0.4875
ReboundWingspanWeight  = 0.4875
Total                  = 1.5   (historical placeholder total, preserved)
```

The total magnitude is unchanged from the prior all-equal state (3 × 0.5 = 1.5). Phase 43 changes **the relative ordering only**, not the overall influence of the physical battle. Calibrating total magnitude — how much the physical size battle matters relative to skill — is deferred to the calibration pass.

### What this supersedes

Session 00 settled an 80% Wingspan / 20% Height split when Strength was not yet an explicit factor in `ReboundPhysical`. That two-factor decision is replaced by the current three-factor model. Session 00's ratio is retained as historical context only; it does not govern the current weighting.

### Scope of effect

`ReboundPhysical` is called only inside `OffensiveReboundShare` (lines 459 and 461 of `Matchup.cs`). It drives the team-level pre-staging size shift. The individual attribution pickers (`OffensiveRebounderPicker`, `DefensiveRebounderPicker`) are confirmed by source read to use `OffensiveRebounding × PositionalWeight × ReboundWingspanMultiplier` — they do not call `ReboundPhysical`. The weight change therefore affects the team rebound battle only, not individual credit distribution.

### Invariants

All three `ReboundPhysical` weight components are enforced nonnegative in `MatchupConfig.Load()`. They need not sum to any particular value (a weighted read, not a probability distribution). A negative coefficient would invert the physical meaning of that attribute in the team battle — guarded loud at startup.

### Calibration note

The exact values (0.525 / 0.4875 / 0.4875) are calibration placeholders that preserve the historical total of 1.5. The ordering (Strength leads; Height = Wingspan) is the locked design decision. Calibration of magnitudes happens against real D1 data during the calibration pass and may change these values while preserving the ordering invariant (proven by sub-check (a) of `Phase43ReboundPhysicalWeightsCheck`).


## Session 06 — OffBallDefense: Perimeter Suppression + Selection Compression

### The completed interior/perimeter defensive make% system

Sessions 03, 04, and 06 together build the full two-sided defensive make% system. The three C-terms that operate on `makePct` in `RollHGenerator` now form a symmetric, zone-partitioned structure:

| Term | Attribute | Direction | Zone coverage | Denominator |
|---|---|---|---|---|
| C5.5 | Screening (off) | + makePct | All 5 zones | 5.0 (all offensive slots) |
| C6 | HelpDefense (def) | − makePct | Rim/Short full, Mid 0.30, Long/Three 0 | 4.0 (4 off-ball helpers) |
| C7 | OffBallDefense (def) | − makePct | Long/Three full, Mid 0.30, Rim/Short 0 | 4.0 (4 off-ball helpers) |

C6 and C7 are exact zone-inverses of each other. Mid is the overlap zone where both have partial effect (0.30 of full). The three signed terms settle under a single `Math.Clamp(makePct, 0.0, 1.0)` at the end of C7 — the deferred-clamp discipline from Session 04, extended to cover three terms.

### Why zone-specific multipliers replace binary gates

Sessions 03 and 04 gated C6 on `{Rim, Short}` and C5.5 on `{Rim, Short}` as a temporary design: we had interior defense (HelpDefense) but no perimeter counterpart yet. The binary gates were placeholders, not architecture. Session 06 replaces them with the real structure:

- C6 fires for all halfcourt possessions; a `helpDefZoneMultiplier` switch (1.0/1.0/0.30/0.0/0.0 for Rim/Short/Mid/Long/Three) sends it to zero where help defense doesn't apply. This is cleaner than a binary gate — the math is always the same, only the magnitude varies.
- C7 mirrors this with the inverse pattern. The symmetry is complete and legible in the config: two Mid multiplier knobs, each at 0.30.

### The three-term deferred-clamp contract

The single `Math.Clamp` lives after C7 and settles all three signed terms together. The algebraic consequence:

```
makePct(Scr=99, OBD=99) = makePct(Scr=0, OBD=0)
```

at any Long or Three fixture, because `ScreeningBonusScale × (0.99)^2 = OffBallDefenseSuppressionScale × (0.99)^2 = 0.147015`. Elite screeners and elite off-ball deniers cancel to floating-point precision. This cancellation holds at any make% level because no premature clamp exists between C5.5 and C7.

A premature clamp after C6 (which has zero effect at Long/Three) would be transparent at current config magnitudes — C5.5 at perimeter zones never pushes the logistic above 1.0 given the current Long/Three ceilings (~0.60). The correct proof of the deferred-clamp contract is therefore the algebraic one: confirm the three-term arithmetic composes additively, which the harness sub-check (e) does.

### OffBallDefense: the attribute

Off-ball perimeter denial — the ability to make it hard for guards and wings to catch the ball and start offensive actions. Distinct from PerimeterDefense (which is about contesting the dribble and the shot) and from Steals (live-ball turnovers). Not size-gated: a guard or wing can have high OffBallDefense; a big who patrols the perimeter less typically has lower values. Typical ranges: elite off-ball deniers 70+; guards/wings moderate (45–65); bigs lower (30–50).

### Selection compression: the design

`BendByAttention` in `RollEGenerator` now runs a compression pass after the existing tilt → floor/rail sequence. The compression targets above-equal-share offensive focal points — players whose selection share exceeds `1/populatedCount` — and reduces their excess proportionally, redistributing the freed mass to below-equal-share slots.

The compression is role-split by the shooter's Postness relative to `PostnessNeutral`:

```
perimeterWeight = max(0, 1 − postness / PostnessNeutral)
interiorWeight  = max(0, min(1, postness / PostnessNeutral − 1))
compression     = offBallDefAgg × perimeterWeight + helpDefAgg × interiorWeight
```

Three regions:
- **Below PostnessNeutral (guards):** `perimeterWeight` fades from 1.0 at postness=0 to 0.0 at PostnessNeutral. OffBallDefense drives compression; HelpDefense does not.
- **At PostnessNeutral exactly:** Both weights are zero. A role-neutral player is affected by neither defensive system.
- **Above PostnessNeutral (posts):** `interiorWeight` rises from 0.0. HelpDefense drives compression; OffBallDefense does not.

`PostnessNeutral = 50.0` is computed from `Matchup.Postness` at all-50 attributes with the current 1/3-each coefficient set. It is a `MatchupConfig` knob, not a hardcoded constant, so calibration can shift it without touching the formula.

### Why compression uses all five defenders (no exclusion)

C7 (make%) excludes the matched defender because he already contributed to make% at Stage 1 (the logistic). Selection compression has no analogous Stage 1 — it is a pre-shot-attempt team-aggregate effect. All five defenders contribute to the team's ability to deny the ball in the halfcourt; excluding the matched defender would arbitrarily remove a meaningful contributor. The denominator is 5.0 (fixed at team capacity), consistent with all other aggregate knobs.

### HelpDefense as a dual consumer

HelpDefense now feeds two engine consumers:
1. **C6 (RollHGenerator):** Suppresses make% when the post actually gets a shot off. Interior zones only.
2. **Selection compression (RollEGenerator/BendByAttention):** Compresses the selection tilt toward interior focal points before any shot attempt exists.

These are independent effects, not double-counting. A great help-defense team makes interior posts harder to feed AND harder to convert when fed. The two effects are separated by the roll boundary: selection compression fires at Roll E time; make% suppression fires at Roll H time.

### The PostnessNeutral pivot is a design guarantee, not a calibration artifact

The zero-compression zone at `PostnessNeutral` is intentional. A role-neutral player (Postness = PostnessNeutral) is neither a perimeter focal point nor an interior focal point in a meaningful sense — compressing his selection share with either defensive attribute would be a false signal. The harness sub-check (f.neutral) proves this: a player constructed to sit exactly at the pivot is confirmed to have both OBD-only and HD-only compression equal to zero, even when that player is above equal-share (has a focal-point share that could theoretically be compressed). The zero-compression is not a numerical accident — it is algebraically enforced by the two weight formulas crossing zero at the same point.


## Session 07 — Hustle: Relative Team-Aggregate Effect Across Five Consumers

### What kind of attribute Hustle is

Hustle is the engine's first fully **relative** attribute wired across multiple consumers. Only the *gap* between the two teams' mean Hustle matters; the absolute level does not. Two teams that both hustle at 90 play exactly as if both hustled at 50 — the gap is zero, so every Hustle effect is zero. This is the defining property and the source of the attribute's self-limiting balance: a high-Hustle team earns nothing against an equally high-Hustle opponent. The advantage is real only against a less scrappy team, and it scales with how much less scrappy that team is.

This is deliberately different from an absolute attribute like Screening, which needed a natural counterweight (OffBallDefense / HelpDefense) to keep it from running away. A relative attribute counterbalances itself — the opposing team's rating is the counterweight, built into the gap. Balance for Hustle therefore comes from **rarity in the player population** (how often a roster of five genuine hustlers actually assembles), not from a formula ceiling.

### The five consumers

| # | Consumer | Site | Shape | Direction |
|---|---|---|---|---|
| 1 | Rebound battle | `Matchup.OffensiveReboundShare` (reaches Roll I + Roll M) | GapFn, pre-bend | Higher-hustle team gains share |
| 2 | Turnover disruption | Roll B `TeamDisruptionShares` + Roll F `DisruptionShares` | GapFn, pre-saturation | Higher-hustle defense forces more |
| 3 | Defensive foul cost | Roll B `TeamDisruptionShares` + Roll F `DisruptionShares` | GapFn, pre-saturation, defense-only | Higher-hustle defense fouls more |
| 4 | Transition defense | `RollHGenerator` C8 (FastBreak only) | GapFn, own clamp | Higher-hustle defense suppresses break make% |
| 5 | Attribution credit | Offensive/defensive rebounder + stealer pickers | per-player tanh | Higher-Hustle player absorbs more credit |

Consumers 1–4 are team-vs-team gap effects. Consumer 5 is the one within-team effect.

### GapFn for teams, tanh for players — why the two shapes differ

The team consumers all run the gap through `GapFn(gap, steepness, exponent, scale) = steepness × sign(gap) × (|gap|/scale)^exponent` — a signed power law with **zero slope at zero** and **convex** growth. This is the correct shape for a gap between two teams: a one- or two-point difference in average effort should be almost imperceptible (you cannot feel a roster that is a hair scrappier), while a large gap should compound (a team that dramatically out-hustles should see it across the box score). The exponent (default 2.0, enforced > 1.0) is what gives the flat bottom and the acceleration.

The attribution pickers face a structurally different question. They are not comparing two teams — they are dividing a fixed pot of rebound or steal credit *within* one team, based on each player's *own* Hustle relative to the 50-neutral midpoint. That is a per-player tilt, identical in shape to the existing `ReboundWingspanMultiplier`: `1 + steepness × tanh((rating − 50) / scale)`. tanh is right here precisely because it is near-linear in the middle and saturating at the extremes — a player slightly above average should get slightly more credit, smoothly. Reusing the established wingspan-multiplier shape keeps the pickers internally consistent.

The harness proves the team path is genuinely GapFn and not tanh: at a one-point gap, the GapFn shift is ~25× smaller than the raw-tanh equivalent (24.99× measured). If a future maintainer ever "simplifies" `HustleGapShift` to a tanh, that one-point gap would suddenly become ~25× more perceptible, and the flat-bottom contract would break — the helper carries a doc-comment warning against exactly this.

### The pre-saturation insertion seam

For both turnover and foul disruption, the Hustle nudge is added to the disruption/foul shift **before** the tanh that bends the arm toward its configured ceiling — not as a post-bend addition to the final share. This is the single most important correctness property of the disruption consumers, and it is what the ceiling-sensitive harness sub-check exists to prove.

The mechanism: `disruptionShift = pressureLift + pressureGate × matchupShift + hustlePressureNudge`, then `finalShare = base + span × tanh(disruptionShift / referenceShift)`. Because the nudge enters before the tanh, its effect on the final share depends on how much headroom remains. Near the ceiling the tanh slope is shallow, so the same Hustle advantage produces a smaller increment than it does at neutral pressure where the slope is steep. The harness confirms this directly: with a fixed defensive Hustle advantage, the Roll F turnover increment falls from 0.0599pp at neutral pressure to 0.0235pp near the ceiling, and the final share never crosses the ceiling. A post-bend addition would carry the full increment regardless of headroom and could push the arm past its ceiling — the exact failure this seam prevents.

### Defense-only foul cost, and why it is smaller than the turnover gain

Extra ball pressure forces more turnovers but also draws more reach-in fouls — that trade-off is the foul consumer. It is **defense-only**: the nudge uses `max(0, -hustleGap)`, which is positive only when the defense out-hustles the offense. An offense that out-hustles its defender does not somehow cause the defense to foul more; the harness confirms an offensive Hustle advantage produces exactly zero change in the defensive foul arm.

The foul weight (0.02) is deliberately set below the turnover weight (0.04) so that a hustling defense's *net* effect is favorable: it forces more turnovers than the fouls it costs. This is a within-disruption calibration contract, verified behaviorally in the harness (foul Δ 0.0034pp < turnover Δ 0.0599pp at the same defensive advantage), not a Load invariant — calibration may retune both weights while preserving the foul < turnover ordering.

### Transition defense (C8): FastBreak-only, self-clamping

C8 is a new term in `RollHGenerator`, placed immediately after the C5.5/C6/C7 settle clamp and gated by `state.FastBreak`. The placement matters: C5.5, C6, and C7 are all halfcourt-gated, so on a FastBreak the make% entering C8 is the raw matchup value, untouched by any earlier C-term. C8 computes the gap as `(defense − offense)` — a positive gap means the defense out-hustles and gets back in transition — runs it through GapFn, subtracts the result from make%, and applies its own `Math.Clamp`. On a FastBreak, C8's clamp is the only one that fires; on a half-court possession, C8 never runs and the half-court make% is byte-identical regardless of the Hustle gap (the gap touches no half-court make% term). The harness proves both halves: FastBreak make% drops under a hustling defense, half-court make% does not move.

### The fixed-denominator-5 aggregate

`TeamMeanHustle` always divides by 5 and treats null or missing lineup slots as the neutral value 50, never as omissions. The effect is graceful degradation and correct rarity: a single elite hustler (Hustle 80) on an otherwise-average roster moves the team mean only to 56.0, not 80. One specialist is nearly imperceptible; a full five-man roster of hustlers is what produces a felt team identity. This matches the fixed-capacity discipline used by every other lineup aggregate in the engine — partial lineups never inflate per-player influence, and the rarity of assembling five specialists is what keeps the effect bounded in a real player population.

### Scope of effect and what stays untouched

The rebound consumer lives entirely inside `OffensiveReboundShare`, computed from the offense/defense lists that method already receives — no signature change, and therefore both rebound callers (Roll I live-miss, Roll M free-throw) inherit the effect with no caller edits. The disruption consumers are added as optional parameters to `DisruptionShares`/`TeamDisruptionShares` defaulting to 0.0, so every prior caller and every prior harness check is byte-identical when the parameters are omitted. No routing, no resolver structure, and no `PossessionState` field changed this session. The Hustle property itself already existed on `Player`; this session only wired it into outcomes.

### Invariants

Each of the four team-level GapFn families is enforced at Load: steepness > 0, exponent > 1 (the convex/flat-bottom contract), scale > 0, and weight in the open interval (0, 1). The four per-player picker knobs are enforced: steepness > 0, scale > 0. There is intentionally **no** cross-consumer ordering invariant in Load (e.g. foul-weight < turnover-weight is not asserted at load time) — that is a calibration contract proven in the harness, not a structural constraint, so calibration retains the freedom to retune within the proven-correct shape.

### Calibration note

Every Hustle value shipped this session — all four GapFn families and all four picker knobs — is a calibration placeholder. The locked design decisions are the *shapes and seams*: relative gap (not absolute), GapFn for teams and tanh for players, pre-saturation insertion for disruption, defense-only foul cost, FastBreak-only transition suppression, fixed-denominator-5 aggregation, and the foul < turnover ordering. Magnitudes will be tuned against real data during the calibration pass and may move freely while those structural decisions hold.

---

## Phase 46 — Individual Matchup Denial (Session 08)

### What this phase does

Phase 46 adds per-slot individual denial to `BendByAttention` in `RollEGenerator`. For each offensive slot, the defender assigned to that same slot compares his denial attributes against the ball-handler's access attributes. A bounded per-slot multiplier emerges from the matchup: defenders who win suppress that player's share of ball-touches; offenses that win get a small boost. Phase 44's team-aggregate OffBallDefense perimeter compression is retired here and replaced by this per-man mechanism. HelpDefense interior compression is retained unchanged.

### Why per-slot instead of team-aggregate

Phase 44's OffBallDefense term modeled perimeter denial as a sum of all five defenders' ratings — a team-level aggregate that compressed the share of any above-equal-share perimeter focal point. This overrepresents reality: perimeter denial is a one-on-one skill. The defender guarding the ball-handler is what makes it hard for him to get the ball, not the team's aggregate OBD rating. A team with four elite perimeter deniers and a weak slot-1 matchup should not deny the focal-point ball-handler — the slot-1 defender is the relevant agent. The team-aggregate model produces the wrong shape for a per-player attribute. Phase 46 retires it in favor of a mechanism where each defender's denial affects only the player he is guarding.

### Channel structure and the sum-to-1 split

Denial operates through two skill channels and one physical channel:

**Perimeter channel**: `OffBallDefense − OffBallMovement`. High OBD and low OBM make it hard for the player to get open cuts and dribble entries. Full weight at postness=0 (pure guard); fades to zero at postness=2×PostnessNeutral.

**Post channel**: `PostDefense − (Strength + PostMoves)/2`. High PostDefense and a low (Strength, PostMoves) average make it hard for a post player to receive entry passes and establish position. Zero weight at postness=0; rises to full at postness=2×PostnessNeutral.

The two channel weights sum to exactly 1.0 across the full postness range — a pure guard is 100% perimeter, a pure post is 100% post, a mid-postness player is a blend. This is **not** the same as Phase 44's `helpInteriorWeight`, which is zero at or below `PostnessNeutral` and rises above it. The denial split is a sum-to-1 partition of full weight; the compression weight is a threshold function starting at zero. The two systems use physically separate variable names (`denialPerimeterWeight`/`denialPostWeight` vs `helpInteriorWeight`) precisely because conflating them would produce incorrect math. Code comments flag this distinction explicitly.

**Physical channel**: `Athleticism gap (def − off)`. A more athletic defender makes it harder to get free regardless of positional skill — quickness to deny driving lanes, length to contest catches. Reuses `ReferenceScale` (the same scale throughout the make door) rather than adding a new scale knob.

### The bounded multiplier

`denialMult = exp(−log(MaxDenialMultiplier) × tanh(denialShift / DenialReferenceShift))`

This mirrors the tilt multiplier, negated so a positive shift (defender wins) suppresses the share. Properties: exactly 1.0 at zero shift (neutral matchup); bounded in [1/MaxDenialMultiplier, MaxDenialMultiplier] for all real shifts; never drives a share to zero. At MaxDenialMultiplier=1.5, a maximally denied slot receives ≈0.667× its pre-denial share; a maximally open slot receives 1.5×.

A null slot (no defender or no offensive player in that slot) gets denialMult=1.0 implicitly — the loop continues without modifying `denied[i]`.

### Pipeline position

The full pipeline after Phase 46: **tilt → floor/rail → DENIAL → normalize → interior-help-compression → redistribute → normalize → floor/rail**.

Denial sits after the first floor/rail (so it operates on shares that already respect the selection floor and rail) and before the interior HelpDefense compression (so a player who already has fewer touches due to denial is less likely to be identified as a focal point by the compression pass). The final floor/rail enforces the selection constraints a second time, as before.

The `finalConstrained` fallback path (when `freedMass = 0`, meaning no slot was compressed) now returns `denied` instead of `constrained`, because the denial pass has already been applied regardless of whether compression fires.

### OBD retirement: what stays, what goes

The team-aggregate OffBallDefense perimeter compression term (Phase 44's `offBallDefAgg × perimeterWeight`) is removed. The two config knobs that fed it (`OffBallDefenseCompressionExponent`, `OffBallDefenseCompressionScale`) are removed from `MatchupConfig` and `config.json`. The corresponding Load guards are removed.

HelpDefense interior compression stays exactly as it was, operating on the post-denial shares. The `helpInteriorWeight` computation and the redistribution logic are unchanged; only the array they operate on shifts from `constrained[]` to `denied[]`.

The Phase 44 retirement guard in `Phase44OffBallDefenseCheck` sub-check (f) constructs a fixture where all per-slot denial gaps are analytically zero (matched defenders, OBD = OBM, PostDefense = (Strength + PostMoves)/2, athleticism attributes mirrored). With all denial multipliers equal to 1.0, the only observable difference between the baseline and the guard (which has higher OBD for defenders 2–5) is whether the retired team-aggregate term fires. The harness confirms it does not: |diff| = 0.00E+000.

### Phase 44 (g) sub-check update

The original (g) assertion "OBD-only has no effect on a post player" was testing the old team-aggregate system's property: `perimeterWeight = 0` for high-postness players, so OBD had zero effect. After Phase 46, OBD reaches post players through the per-slot denial system via the `denialPerimeterWeight` channel (which is 0.1 for a player at postness=90 with neutral=50). For the specific fixture in (g) — a star with PostMoves=75, Strength=90 facing a defender with PostDefense=50 and OBD=80 — the offense wins on the post channel by a large margin (postGap = 50 − 82.5 = −32.5), which outweighs the perimeter-channel denial. The net denialShift is negative (offense wins overall), so denialMult > 1.0 and the star's share increases. The direction assertion is replaced with an observation-only print; (g) now asserts only that HD interior compression fires. The per-slot denial direction across all postness levels is covered by the nine sub-checks in `Phase46IndividualDenialCheck`.

### Invariants

Seven Phase 46 invariants enforced at Load: `DenialExponent > 1` (convex/flat-bottom contract, same as `SkillExponent`); `MaxDenialMultiplier > 1` (meaningful bound, not trivial at 1.0); `DenialReferenceShift > 0`; `DenialSkillSteepness > 0`; `DenialPhysSteepness > 0`; `DenialSkillWeight ∈ (0, 1)`; `DenialPhysWeight ∈ (0, 1)`.

### Calibration note

All seven denial knobs are calibration placeholders. The locked design decisions are the shapes and seams: per-slot (not team-aggregate), two-channel blended skill gap plus athleticism physical channel, sum-to-1 channel split by postness, bounded tanh multiplier, denial before compression in the pipeline. Magnitudes will be tuned during the calibration pass and may move freely while the structural decisions hold.

---

## Weight as a Body-Input to Strength and Quickness (forward spec — player-generation module)

### Status: design locked, no code. Build home is the future player-generation module, not the stress-test archetype scaffolding.

This section records a settled design decision for how Weight will work once the real player-generation system exists. It is **not** implemented. It deliberately does not touch the archetype factory in `Program.Stress.cs`, because those named archetypes are throwaway calibration scaffolding — the real system will roll players from authored ranges rather than from nine hard-coded templates, and the logic below belongs to that range-rolling system. There is nothing to validate in a harness run today: this shapes what ratings a player is *born with*, and has no consumer in the possession engine.

### What Weight is — and what it is not

Weight is **not a consumer attribute**. Nothing in the possession engine reads it directly. It is an **input to generation** that shapes two other ratings: Strength (pulled up) and Quickness/Speed (pulled down). After a player is generated, the engine reads his final Strength and Quickness/Speed numbers normally — Weight has already done its work and is not consulted again during a game.

This is the scouting-realism principle: a scout watching a heavy, powerful forward sees a *strong* player — not "a Strength rating with a live Weight modifier attached." The mass is the *explanation* for the rating, not a separate ingredient re-applied at runtime. So Weight resolves entirely at birth and the matchup engine stays clean, reading final ratings exactly as it does now.

### The causal chain at generation

1. **Body first.** Height and Weight are rolled before Strength and Quickness. (In the range-rolling system, each is drawn from its authored range for the kind of player being generated — bigs tall and heavy, guards short and light.)

2. **Mass-for-frame is the operative quantity — not raw Weight.** What matters is weight *relative to height*. A 6'7" 260 player is enormous mass-for-frame; a 6'11" 215 player is light-for-frame. This single derived idea carries the whole mechanism: it is high for the burly forward and low for the wispy seven-footer, regardless of their tier or position.

3. **Mass-for-frame slides the Strength range upward.** A player heavy for his height has the *center* of his Strength draw-range shifted up; light-for-frame shifts it down. The draw still happens within the shifted range with normal spread.

4. **Mass-for-frame slides the Quickness and Speed ranges downward.** Same quantity, opposite sign. Heavy-for-frame costs lateral quickness and straight-line speed — the ranges shift down, the draw still happens with spread.

### The critical design property: bias, not law

Mass-for-frame **slides the center of a range**; it does **not** compute the rating directly. This distinction is the whole point and must survive into implementation:

- **Rejected model:** `Strength = f(mass-for-frame)`. This is mathematical lockstep — more mass *always* means proportionally more strength, no exceptions. Not wanted.
- **Locked model:** mass-for-frame moves *where the Strength range sits*, then Strength is drawn randomly within that shifted range. A heavy-for-frame player is *probably* strong, but a low roll inside his (raised) range can still produce a heavy player who is only average-strong. A light-for-frame player can roll high and surprise. The tendency lives in where the range sits; the outlier lives in the roll.

The population shows a reliable tendency — heavier-for-frame players skew stronger and slower — while any individual player is free to defy it. There are ranges and outliers, exactly as everywhere else in the engine.

### The freak (Zion) falls out for free — not special-cased

If mass-for-frame *always* dropped quickness, there would be no quick heavy player by construction. Because mass-for-frame only slides the Quickness range down (rather than dictating the value), a player can still roll the top of his shifted-down range. A heavy player who rolls high quickness despite the downward slide *is* the freak — the mass-driven strength without the usual quickness tax. He is rare because most heavy players roll somewhere in the middle of a range whose center was pulled down; he is not a bolted-on exception. The triad (heavy + strong + quick + athletic) emerges naturally from the slide-and-draw mechanism, and its rarity is a mathematical consequence of the shifted range, not a hand-placed special case.

### Calibration dials (tuned on arrival, not now)

Two magnitudes govern the feel, and both are deferred to the calibration pass when a generated population exists to measure:

- **Slide size vs. spread width.** If the slide is large relative to the spread, mass-for-frame nearly determines Strength and outliers are rare. If small relative to spread, it is a gentle lean with many exceptions. The locked *target shape*: sized so the **extremes** (a 260-for-6'7 vs a 215-for-6'11) almost never overlap on Strength — the burly forward reliably out-muscles the wispy seven-footer — while **moderate** mass differences overlap freely and produce plenty of exceptions. Clean separation at the extremes, lots of mixing in the middle. Same shape applies to the quickness tax: extreme mass clearly costs quickness, moderate mass barely registers.
- **Quickness-tax weight and the freak override.** How hard mass-for-frame pulls Quickness/Speed down, which directly sets how common quick bigs are. A heavier tax with wider athletic spread makes the freak rarer and more special.

Locking specific constants now would be guessing against data that does not exist. As with every calibration placeholder in the project, the *shapes and seams* are locked — body-first ordering, mass-for-frame as the operative quantity, slide-the-range (never compute-the-value), Strength up / Quickness-Speed down, freak-as-emergent-outlier — and the magnitudes move freely during calibration while those structural decisions hold.

### What does NOT change

No matchup-engine code. No consumer reads Weight. Strength and Quickness/Speed keep their current meanings and their current consumers; only the *way they are assigned at generation* changes, and only inside the future player-generation module. The archetype factory in `Program.Stress.cs` is left alone — it will be replaced by the range-rolling system that hosts this logic.

## Phase 47 — Passing Compound (rank-weighted, bottom-heavy) (Session 09)

### What this phase does

Team passing — the crispness of a lineup's ball movement — was a flat average of the five players' Passing ratings (each counted equally). It is now a **rank-weighted, bottom-heavy compound**: the populated passers are ranked weakest→strongest, the weakest carries full weight (1.0), and each better passer's weight multiplies by `PassingRankWeight` (placeholder 0.75). The weighted sum is normalized by the sum of the weights actually used. The result feeds `conversionQuality` (Roll H's bonus-only passing block) exactly as the mean did, and stays in [0,1]. The only thing that changed is the internal shape of the team-passing number; nothing downstream changed.

### Why bottom-heavy — the mirror of playmaking decay

The two ball-skill aggregates in `AttentionGenerator` are deliberate opposites:

- **Playmaking decays top-down.** Activation contributions are ranked high→low and weighted by `PlaymakingDecay` (each subsequent contributor counts less). This rewards the *peak* — one elite distributor nearly maxes it; a second adds less. "One ball."
- **Passing climbs downward.** The weight grows toward the *weakest* passer, so the largest weight lands on your fifth-best. This rewards the *floor* — the offense is only as fluid as the player most able to disrupt its ball movement. "No weak link to hunt."

An offense with five sharp passers should compound into a near-scrambled defense on its own — getting *close to* (not equal to) what one elite playmaker provides.

### Additive, non-negative — no penalty, no cap

Every passing contribution is non-negative. The model never subtracts value from a passer and never imposes a threshold or explicit weak-link penalty. Because the result is a *normalized* bottom-heavy weighted average, a weak fifth passer lowers the team compound more than an equally-weak top-ranked passer would — but that is a structural consequence of the weighting, not a penalty term. Passing multiplies ability players already have (it scales conversion quality); it never grants ability or creates shots.

### Depth raises the ceiling — structural, not a calibration hope

At equal total passing, five solid passers beat four-elite-plus-one-dud, because the biggest weight lands on a solid fifth man instead of a dud. This DIRECTION holds for any `PassingRankWeight < 1.0` — it is a structural consequence of the shape, not something calibration has to find. Worked example at the 0.75 placeholder: (90,90,90,90,10) → compound ≈ 0.6378 vs (74,74,74,74,74) → 0.7400; the even lineup wins by a wide margin. A second point in the space: (90,30,30,30,30) → ≈ 0.3622 vs (42,42,42,42,42) → 0.4200, even again. Calibration controls only how large the advantage is and whether it is appropriately consequential at game level.

### The normalization-to-[0,1] contract

The compound divides the weighted sum by the sum of the weights actually used. Consequences: an all-0.99 lineup yields exactly 0.99 (the rating ceiling is 99, not 100 — each input is Passing/100); an all-0 lineup yields exactly 0.0; every legal lineup lands in [0,1]. This preserves the retired mean's output range, so the two downstream scales (`DirectPassingScale`, `ActivationScale`) keep their meaning and the [0,1] clamp on `conversionQuality` is not silently doing the work. Absent slots are not added to the value array (they are absent, not zero-rated passers); a zero-populated lineup yields 0.0.

### How the harness proves the shape (not just "more passing is more")

A naive "good passers beat bad passers" check would pass even if the weight direction were backwards. The check therefore includes two equal-total depth proofs that specifically distinguish bottom-heavy from top-heavy: (c) at total 370, five-even beats four-elite-plus-a-dud; (e) at total 210, five-even beats one-sharp-plus-four-weak. Flat-knob degeneration (d) sets `PassingRankWeight = 1.0` and confirms two NON-uniform lineups reproduce the retired arithmetic mean exactly through the production `AttentionGenerator.Generate` path — non-uniform is essential, because a uniform lineup equals its value under any knob and so cannot catch a config that is ignored or hardcoded. Because `conversionQuality` is independent of the usage shares, the checks pass a fixed neutral `finalShares` and hold every non-Passing attribute identical, so a comparison can only be won by the passing compound itself; the coefficient on the compound is recovered empirically from a uniform lineup rather than re-deriving the playmaking-activation math.

### Invariants

- `PassingRankWeight ∈ (0, 1]` (Load guard, mirrors `PlaymakingDecay`). 1.0 = flat (pure arithmetic mean, the retired behavior); lower = more bottom-heavy.
- `passingCompound ∈ [0, 1]`; all-0.99 → 0.99, all-0 → 0.0; zero-populated → 0.0.
- Every term additive and non-negative; no subtraction, no threshold, no cap.
- The deliberate inverse of `PlaymakingDecay`'s top-down direction.

### Calibration note (deferred — do not tune until feature-complete)

1. **Equal-total depth direction is structural; effect SIZE is calibration.** Five solid passers must beat four-elite-plus-a-dud at equal total whenever `PassingRankWeight < 1.0` (proven, not tuned). Calibration sets how large that advantage is and whether it matters in game-level results.
2. **One elite playmaker + four shooters should beat five sharp passers with no creation/playmaking** — cross-system check (passing vs playmaking-unlocks-openness); verify at calibration.
3. **One playmaker + four shooters beats two non-shooting playmakers** — spacing-cost vs playmaking-value ordering; verify at calibration.

## Phase 48 — Fatigue Meter (stateful stamina: drain / recover / halftime) (Session 10, 2026-06-23)

The first piece of per-player state that REMEMBERS across possessions. Team fouls already persist per-team; this is the first thing the engine carries per-player from one possession to the next. It ships as a meter ONLY: it is computed and stored, and **nothing reads it to change any outcome this session**. Full-game observation output is byte-identical to pre-change except the config hash (config.json gained a `Fatigue` section). The athleticism effect — reading the meter to discount *effective* athleticism at the five consumer sites (`Matchup`, `RollE`, `RollA`, `RollJ`, `RollK`) — is the next session, deliberately split off so the meter's correctness is proven before anything depends on it.

### Where it lives and how it is driven

`FatigueTracker` hangs on `GameState` next to `FoulTracker` and is structurally its sibling — a dedicated class, constructed with its config, mutated through methods, read through `LevelFor`. It differs only in storage: fatigue is per-player, so the level lives in a `PlayerId`-keyed dictionary, not two team ints. It holds no entries until a player first takes the floor; `LevelFor` returns 0.0 (fresh) for any unseen id, so reserves and future substitutes materialize naturally on first appearance with no roster pre-seeding.

The Governor drives it at exactly two points. Accrual fires once in the possession tail, after the outcome resolved — so every on-floor player of BOTH sides accrues exactly ONE possession of fatigue per top-level possession, never per Roll, free-throw, rebound continuation, retained inbound, or internal retry (all of those happen inside the single resolver call that the tail wraps). Halftime recovery fires from the regulation half-boundary block, under the same `half < Halves` guard as the foul reset — so it runs exactly once, at the half-1→2 boundary, and never in the separate overtime loop. Both reach the players through the same lineup→roster seam the attribution layer uses, and neither touches RNG.

### The drain curve — trickle then cliff, carried by the meter itself

Accrual is convex: `ΔF = BaseDrain × drainFactor(Endurance) × (1 + Convexity × (F/Ceiling)^Exponent)`, then clamped to `Ceiling`. A fresh player barely tires; as his level climbs, each possession costs more, and the bottom falls out near the ceiling. The convexity lives in the METER, not in some future consumer — so when the athleticism effect is wired in, it reads a level that already encodes the trickle-then-cliff shape, and the effect itself can stay a simple monotone discount. The increment is strictly positive below the ceiling and strictly increasing in current fatigue (`Exponent > 1`), so a late step always exceeds an early one.

### Endurance scales both directions

Authored `Endurance` (0–99) is normalized the engine-standard way — `e = clamp(Endurance/100, 0, 1)`, so a raw 80 becomes 0.80. It drives two bounded, strictly-monotone multipliers: `drainFactor = 1 + DrainEnduranceSensitivity × (1 − e)` (lower Endurance → bigger drain, so the gasser tires faster and reaches the cliff in fewer possessions) and `recoveryFactor = 1 + RecoveryEnduranceSensitivity × e` (higher Endurance → bigger recovery, so the gasser also recovers slower). Authored ratings never change at runtime — fatigue is a derived quantity layered on top, exactly as the no-runtime-mutation guardrail requires.

### Recovery — fast but partial, one primitive shared with halftime

Recovery is multiplicative decay of the current level: `F × clamp(1 − RecoveryRate × recoveryFactor(Endurance) × elapsedSeconds, 0, 1)`. It strictly decreases fatigue toward zero and never crosses below it; a short rest knocks the meter down meaningfully but never returns a tired player all the way to fresh (the multiplier is `< 1` but `> 0` for any finite short rest). A very long rest drives the multiplier to its zero-floor clamp — full reset — which is the correct boundary behavior, not a bug.

Halftime is **not a special case**. `ApplyHalftimeRecovery` calls the exact same private recovery primitive with `elapsedSeconds = HalftimeRestEquivalentSeconds` (a large rest-equivalent). Because the primitive is partial and Endurance-scaled, the most-gassed players retain the most residual fatigue into the second half automatically — no carve-out, no "halftime recovery amount" to tune separately. The harness proves the equivalence directly: a halftime-recovered player and a `Recover(HalftimeRestEquivalentSeconds)` player at the same level land identically to 1e-12.

### Pace via count; recovery via elapsed clock (the intentional asymmetry, D6)

Pace enters fatigue purely through how OFTEN accrual is called — once per possession — not through any per-second or per-player-effort term (per-player effort tracking was explicitly rejected as untunable). Recovery, by contrast, is a function of ELAPSED game-clock seconds off the floor, not possession count. The asymmetry is deliberate: a fast game should tire players through more possessions, but it must not over-restore a benched player just because more possessions elapsed. No off-floor recovery runs this session (no substitutions yet), but `Recover` already takes elapsed seconds so the meter can never later bake in a pace-undoes-rest relationship.

### Determinism

Every operation is a pure function of (current level, the player's authored Endurance, the elapsed/possession input). It draws no randomness, so a replayed seed reproduces the meter trajectory exactly and the meter perturbs no other RNG stream. This was verified, not assumed: a clean pre-change build and the post-change build produce an identical 758-line observation artifact except the single config-hash line, and same-seed runs reproduce both gameplay and meter levels exactly.

### The construction call

Adding required state to `GameState` would mean editing all ~100 `new GameState(...)` sites. The coach/roster/lineup fields already solved this by constructing internally with a default, so fatigue follows that precedent: an optional last constructor parameter defaulting to an empty placeholder-config tracker, leaving every existing site unchanged. The foul tracker stays required-and-injected (its thresholds vary by config at the call site); the fatigue meter is defaulted-internally (no site that ignores fatigue should have to mention it). This is reversible — if config-bearing trackers are later wanted everywhere, the injection is mechanical.

### What this session deliberately does NOT do

No consumer reads the meter; no outcome changes. The five athleticism consumer sites are untouched (next session). Substitutions are untouched (own session). No magnitude is calibrated — every config value is a placeholder. Tuning against a stub-heavy, not-yet-feature-complete engine would chase a moving target.

### Invariants

- Level ∈ [0, `Ceiling`]; fresh (unseen player) reads 0.0.
- Accrual increment strictly positive below the ceiling; strictly increasing in current fatigue (`Exponent > 1`); pinned at the ceiling once reached.
- `drainFactor ∈ [1, 1 + DrainEnduranceSensitivity]`, strictly decreasing in Endurance; `recoveryFactor ∈ [1, 1 + RecoveryEnduranceSensitivity]`, strictly increasing in Endurance.
- Recovery multiplier ∈ [0, 1]; recovery strictly decreases fatigue (for level > 0) and never below zero.
- Halftime recovery routes through the identical recovery primitive (no separate equation); fires exactly once, at the regulation half-1→2 boundary, never in OT.
- Accrual fires exactly once per completed top-level possession per on-floor player; reads no RNG.
- Config guards: `Ceiling > 0`, `BaseDrain > 0`, `Convexity ≥ 0`, `Exponent > 1`, sensitivities `≥ 0`, `RecoveryRate > 0`, `HalftimeRestEquivalentSeconds > 0`.

### Calibration note (deferred — do not tune until the attribute-coverage pass is complete)

1. **Where the cliff sits.** How many possessions a median-Endurance player plays before the bottom falls out. At the placeholder values the curve is mild over the first ~30 possessions and bites near the ceiling; whether that maps to realistic game-minutes is a magnitude question, not a shape one.
2. **Halftime reset depth.** How much residual fatigue the most-gassed players carry into the second half — set by `HalftimeRestEquivalentSeconds` against `RecoveryRate`.
3. **Recovery-rate vs drain-rate ratio.** The balance that matters most once substitutions exist and benched players actually recover; meaningless to tune before the off-floor path is driven.

---

## Phase 49 — Fatigue → Effective Athleticism (the meter bites)

Phase 48 built a per-player fatigue meter that nothing read. Phase 49 makes it read: a tired player's **effective** athleticism is discounted wherever the engine reads the derived `Athleticism` composite for gameplay. Authored ratings never change — the discount is a derived quantity layered on top, exactly as the no-runtime-mutation guardrail requires.

### The discount — linear in the meter, two dials, hard-floored

For a read at fatigue `level` (∈ [0, Ceiling]):

```
effectiveAthleticism = authoredAthleticism × (1 − drop × level/Ceiling)
drop = DefenseAthleticismDrop  if the read is a defensive role
       OffenseAthleticismDrop  otherwise
```

It is **linear in the meter on purpose.** The convex trickle-then-cliff already lives in the METER (Phase 48), so effective athleticism is automatically convex in possessions-played without stacking a second curve here. A fresh player (level 0) reads his authored athleticism EXACTLY — discount 1.0, the inertness anchor. A fully-gassed player (level pinned at Ceiling) bottoms at `(1 − drop)` of authored: with the placeholder dials, 90% on offense, 80% on defense. The dials are guarded to `[0, 1)`, so the floor is always positive — a gassed player is a **step slow, never a statue, never zero.**

### Defense steeper than offense — and what that produces

`DefenseAthleticismDrop > OffenseAthleticismDrop` is a **structural invariant**, not a calibration choice (the config Load enforces it, with the all-zero inertness control as the only exception). The basketball claim: a tired player loses a first step and a defensive slide faster than he loses his offensive burst.

The consequence at the make door is exact. Two equally-gassed players in a matchup, equal authored athleticism, produce a physical gap

```
attackerEffective − defenderEffective = A × (DefenseAthleticismDrop − OffenseAthleticismDrop) × (level/Ceiling) > 0
```

— i.e. **equal fatigue tilts toward the offense.** This is a formula property (`DefDrop > OffDrop`), not a tuning hope; it survives any calibration that preserves the ordering. At full gas with A=99 it is ~9.9 rating points of offense-favoring shift feeding `GapFn` (before saturation).

### The athletic axis is the derived `Athleticism` composite ONLY

Fatigue discounts the derived `Athleticism` composite at the read-sites. The **raw** physicals (Strength, Speed, Quickness, First step, Vertical) that feed *other* composites — and any other composite built from them — are read at full strength. This is deliberate: discounting both the composite and the raw inputs that also flow elsewhere would penalize a tired player twice through a back door. One axis, one discount.

### Matchup stays pure/static — the overload pattern

`Matchup.EffectiveRating` gained a 6-arg overload taking caller-supplied `attackerEffectiveAthleticism` / `defenderEffectiveAthleticism`; the physical gap uses those, the skill baseline and skill gap are untouched. The 4-arg signature is kept and **delegates** to the 6-arg with raw athleticism — which is exactly the fresh (no-fatigue) case — so the analytic make-curve sweep and the no-defender fallback keep their existing behavior bit-for-bit. Matchup never reaches the fatigue tracker: the caller (a generator, which holds the `GameState`) computes the effective values via `FatigueTracker.EffectiveAthleticism` and passes them in. The same defaulted-purity precedent the rest of Matchup follows.

### The five read-sites (and the role each reads)

1. **Make door** — `Matchup.EffectiveRating` via `RollHGenerator` (with-defender branch): shooter on the offensive drop, defender on the defensive drop. The no-defender branch is skill-only and unchanged.
2. **Selection denial** — `RollEGenerator`'s per-man `athGap`: defender on the defensive drop minus offender on the offensive drop. **Structural note:** this lives in `BendByAttention`, the defended-pie pass — NOT in `GenerateWithPressure`, which produces offensive *usage intent* only (pre-denial). Anyone probing how a defender's athleticism moves the selected share must read the post-`BendByAttention` pie, not `FinalShares`.
3. **Entry disruption** — `RollAGenerator`'s two team athleticism aggregates: offense on the lighter drop, defense on the steeper.
4. **Transition** — `RollJGenerator`'s `MeanEffectiveAthleticism(side, isDefense)`: offense on the offensive drop, defense on the defensive drop. Null/unseated slots still yield a neutral 0.0 gap (regression anchor for isolated checks).
5. **Putback** — `RollKGenerator`'s offense composite: the rebounder on the offensive drop (he is going back up).

### Determinism and the equivalence control

The discount draws no randomness — it is a pure function of (authored athleticism, current level, role) — so a replayed seed reproduces outcomes exactly and the effect perturbs no RNG stream. Setting both dials to 0.0 (the inertness control) makes the discount identically 1.0 at every level, which reproduces the Phase-48 (meter-inert) behavior; the dual-mode harness check asserts exactly this (no movement under zero drops, documented movement under live drops), and the fresh-anchor sub-check shows the make door is bit-exact fresh==raw under live drops.

### What this session deliberately does NOT do

No substitutions (own session), so in observation the five starters per side play every possession and only halftime relieves them — their athleticism sags down the stretch, and that end-game fade is now in the corpus. No magnitude is calibrated — both dials are placeholders. The skill axis is untouched. This is the first session whose frozen-corpus output is intentionally not byte-identical to its predecessor; re-baselining `frozen-corpus-v1` is parked for the substitution session, when on/off-floor recovery makes the corpus meaningful.

### Invariants

- `effectiveAthleticism = authored × (1 − drop × level/Ceiling)`; fresh (level 0) → authored exactly; ceiling → `(1 − drop) × authored`.
- Linear in `level` (no second curve here — convexity is the meter's).
- Floor `(1 − drop) × authored` is strictly positive (dials in `[0, 1)`); effective athleticism never ≤ 0 for a positive authored value, never below the floor.
- `DefenseAthleticismDrop > OffenseAthleticismDrop` (structural; all-zero inertness control is the only exception) → defense discounted more at equal level → equal-fatigue matchup favors offense.
- Discount applies to the derived `Athleticism` composite only; raw physicals feeding other composites are read at full strength.
- `Matchup` is pure/static; the 4-arg path equals the fresh case bit-for-bit; the effect draws no RNG.

### Calibration note (deferred — do not tune until the attribute-coverage pass is complete)

1. **Full-gas floor depth.** How much athleticism a maxed-out meter should strip — the `(1 − drop)` floors (placeholder 0.90 offense / 0.80 defense). Whether a fully-gassed player should be 10%/20% slower or more/less is a magnitude question; the floor shape (linear to a positive floor) is locked.
2. **Offense-vs-defense gap size.** The spread between the two dials sets how much equal fatigue tilts toward offense. The *direction* is locked (`Def > Off`); the *magnitude* of the tilt is open.
3. **The combined meter-cliff × discount curve.** What a player's effective athleticism looks like as a function of possessions-played is the convolution of the Phase-48 drain curve and this linear discount. Tuning either in isolation is misleading; the realistic target is the combined late-game fade, and that is best judged once substitutions exist and minutes are real.
4. **Interaction with the no-substitution corpus.** Until subs land, starters never rest mid-half, so the current corpus shows a heavier end-game fade than a real rotation would. Do not calibrate the dials against the no-sub corpus — it overstates fatigue's footprint.
