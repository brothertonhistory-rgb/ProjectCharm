# Project Charm — Session Journal

Newest entries first. What was built, decided, and left stubbed each session.

---

## Session 1 — Build Roll A (Entry: Inbounds, Dead Ball)

**Built**
- Repo scaffolding: `Charm.sln`, .NET `.gitignore`, `Charm.Engine` (class library)
  and `Charm.Harness` (console app).
- The uniform roll contract that every later roll will follow:
  - `Pie<TOutcome>` — generic weighted-odds container, validates on construction
    (non-negative finite weights, sum to 1 within epsilon, all slices present).
  - `RollResult` — sealed `Terminal` / `Continue`, with nullable `ElapsedSeconds`
    on the base.
  - `ContinuationKind` — a continue carries a result category, not a successor.
  - `IRng` / `SystemRng` — seedable randomness for reproducibility.
- `RollA.Execute` — consumes a pie, rolls, returns one typed result; the three
  exits (clean → continue, turnover → continue, violation → terminal).
- `StubPieGenerator` — configured base weights + one live `pressure` wire on the
  turnover slice. Placeholder, clearly marked.
- `Resolver` + `HalfcourtSetStub` / `TurnoverTypeResolverStub` — routing only;
  stubs just acknowledge a clean hand-off.
- `RollAConfig` + `config.json` — all numbers, editable, nothing hardcoded.
- Harness: observability samples, 100k batch check, pressure-signal check.

**Verified (harness run)**
- 100k batch outcome rates match the configured pie within tolerance
  (clean 90.04% / turnover 8.00% / violation 1.97%).
- Clean hand-off: 98,032 routed to stubs + 1,968 ended = 100,000, zero unrouted.
- Seam carries signal: turnover rate 8.0% (pressure 0) → 16.3% (pressure 1).

**Decided**
- Roll A is timeless except the violation terminal, which stamps its own
  invariant elapsed time (full shot clock). All other time is a future time roll.
- The violation lives as a terminal at entry because it has no path variance.
- Pie validation lives at construction = the generator→roll seam.
- A continue names a `ContinuationKind`, never a node; the resolver owns routing.

**Left stubbed (out of scope, untouched)**
- Real attribute → pie generator.
- Halfcourt-set ("next") node and turnover-type resolver.
- Transition, rebounds, live-ball-steal entries; any roll other than A.

**Next session candidates** (not started)
- The node a clean entry routes into, or the turnover-type resolver, or the time
  roll — to be chosen at kickoff. Roll A does not need reopening for any of them.
