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

Built as a **skeleton**: the possession arrow has real behavior (it flips via
`FlipPossessionArrow`); score, fouls, and timeouts are placeholder fields —
typed and named so the shape is defined, but not yet read or written during
possession resolution. Nothing in Rolls A or B touches `GameState`; the future
jump-ball and scoring resolvers will.

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

## Roll table of contents

| Roll | Name | Status |
|---|---|---|
| A | Entry — Inbounds (Dead Ball) | Built (stubbed generator + stubbed successors) |
| B | Halfcourt Initiation | Built (stubbed generator + stubbed successors) |

## Known required infrastructure (not yet built)

- **Possession arrow tracker** — skeleton exists on `GameState`; the jump-ball
  resolver that consumes and flips it is a future roll.
- **Foul-type resolver** — decides defensive non-shooting vs. offensive and what
  roll / possession change it triggers.
- **Time roll** — apportions game-clock seconds for non-invariant outcomes. Every
  terminal except the shot-clock violation defers its time here.
- **Player-selection roll** — decides which player the possession runs through;
  Roll B's `Proceed` exit lands here.
