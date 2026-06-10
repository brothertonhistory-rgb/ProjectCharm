# Project Charm — Design Document

Why things are built the way they are. This document records rationale, not
task lists (those live in the journal). It is updated every session.

---

## The uniform roll contract

Roll A is the first node, so its shape is the template every later roll inherits.
A roll:

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
out of Roll A while letting the one invariant-time outcome stamp itself.

### "Names no successor", precisely
A `Continue` carries a `ContinuationKind` (`IntoHalfcourtSet`,
`ResolveTurnoverType`) — a *category of its own result*, not a reference to a
node. The `Resolver` is the sole place that maps a kind to an actual successor.
When a real node replaces a stub, only that mapping changes; the roll that
emitted the continuation is never reopened. This is the seam that let Roll A be
built before any of its successors exist.

### RNG
Rolls draw from an injected `IRng`, seedable, so batch runs and observability are
reproducible.

---

## Roll A — Entry: Inbounds (Dead Ball)

**Simulates:** the first touch of a possession that begins with a dead-ball
inbound pass.

**Pie shape:** three slices over `EntryOutcome` — `CleanEntry`, `Turnover`,
`ShotClockViolation`. (Stub weights today; see below.)

**Three exits:**

| Outcome | Result | Routes to |
|---|---|---|
| Clean entry | `Continue(IntoHalfcourtSet)` | halfcourt-set node *(stub)* |
| Turnover | `Continue(ResolveTurnoverType)` | turnover-type resolver *(stub)* |
| Shot-clock violation | `Terminal("ShotClockViolation")`, elapsed = full clock | possession ends, ball switches |

**Why the violation is a terminal *here*, not a later node.** A shot-clock
violation is invariant: no shot, the full clock off, never any more or less.
There is no path variance to simulate on the way to it, so collapsing it into a
single terminal at entry is strictly cheaper and loses nothing. It is also why
its elapsed time can be stamped on the result with no time roll.

**Why time is otherwise absent from Roll A.** Game-clock seconds are their own
future roll. Roll A is timeless on its continue paths; the resolver will route to
the time roll downstream. The violation is the lone exception, for the invariant
reason above.

**The pie generator is stubbed.** `StubPieGenerator` returns the configured base
weights with one live wire: a single 0–1 `pressure` scalar nudges the turnover
slice (then renormalizes). This is a placeholder to prove the generator→roll seam
carries signal — **not** basketball logic. Verified: turnover rate moves 8.0% →
16.3% as pressure goes 0 → 1. The real attribute → matchup → pie generator will
replace this stub without touching Roll A.

**Discipline in place from line one.** Every number is in `config.json`
(`RollAConfig`); nothing is hardcoded. Any pie can be printed with its inputs and
resulting outcome (`ShowSamples`). A 100k batch harness confirms the three
outcome rates match the configured pie within tolerance and that every exit hands
off cleanly (terminals end, continues route to their stubs).

---

## Roll table of contents

| Roll | Name | Status |
|---|---|---|
| A | Entry — Inbounds (Dead Ball) | Built (stubbed generator + stubbed successors) |
