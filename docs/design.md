# Project Charm — Design Document

Why things are built the way they are. This document records rationale, not
task lists (those live in the journal). It is updated every session.

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
Off for overtime. Score, fouls, and timeouts remain placeholder fields — typed
and named, not yet read or written during possession resolution.

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

## Roll table of contents

| Roll | Name | Status |
|---|---|---|
| A | Entry — Inbounds (Dead Ball) | Built (stubbed generator + stubbed successors) |
| B | Halfcourt Initiation | Built (stubbed generator + stubbed successors) |
| C | Turnover Classification | Built (stubbed generator; terminal — no successors) |
| — | Jump ball (arrow node) | Built (50/50 tip placeholder; arrow complete) |

## Known required infrastructure (not yet built)

- **Foul-type resolver** — decides defensive non-shooting vs. offensive and what
  roll / possession change it triggers.
- **Time roll** — apportions game-clock seconds for non-invariant outcomes. Every
  terminal except the shot-clock violation defers its time here — including all
  of Roll C's and the jump-ball terminals.
- **Player-selection roll** — decides which player the possession runs through;
  Roll B's `Proceed` exit lands here.
- **Player/steal attribution layer** — runs over outcomes whenever a counting
  stat is generated; assigns the offensive turnover and (on live-ball slices) the
  defensive steal to specific players. Orthogonal to the possession chain; reads,
  never gates. Roll C's classification is one of its inputs.
- **Next-possession entry** — the awarded team (after a jump ball) and the team
  inbounding after a dead-ball turnover both need to *start a new possession*.
  Likely a sibling of Roll A.
- **Height-driven tip contest** — replaces the jump-ball node's 50/50 placeholder
  once a player/attribute layer exists (S-curve on centers' height differential).
