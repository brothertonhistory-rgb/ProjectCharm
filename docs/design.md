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
