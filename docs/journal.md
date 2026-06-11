# Project Charm — Session Journal

Newest entries first. What was built, decided, and left stubbed each session.

---

## Session 7 — The on-court slot (identity) layer

**Built**
- `Slot.cs` — a bare on-court identity: `readonly record struct Slot(TeamSide Side,
  int Number)`. Numbered 1–5 to mimic basketball addressing, but the number is
  IDENTITY, not ROLE — slot 1 is not "the point guard." No attributes, no fill,
  no rating, no modifier hook (not even an inert one). Mirrors `TeamSide`: pure
  identity, owned by nothing, named by everything. The number is intrinsic and
  stable, so a stat attributed to "Home slot 3" stays coherent across future
  substitutions (a sub swaps who fills the slot, never what the slot is).
- `Lineup.cs` — one team's on-court five. Per-team (NOT a shared both-sides
  bundle like `FoulTracker`), because this is the attachment point every heavy
  per-team/per-player system hangs off later (stat lines, the rated players that
  fill slots, the selection roll); each team's machinery grows independently.
  Stands up holding five empty numbered slots. `OnCourt` is read-only (subs go
  through a future method, never by reaching into the five); `SlotAt(1..5)` names
  one slot — the entity a future stat attributes to and the selection roll picks
  among.

**Edited**
- `GameState.cs` — added `HomeLineup` / `AwayLineup` (one `Lineup` each,
  constructed in the ctor) plus `LineupFor(TeamSide)`. This is the seam the future
  attribute generator walks: possession role -> `LineupFor` -> `SlotAt` ->
  (later) the filling player -> attributes. Mirrors how `state.Defense` indexes
  the foul counter. Score / timeouts still inert.
- `Program.cs` — added `SlotLayerCheck`: prints the ten slots, asserts five per
  team numbered 1–5 on the correct side, and proves "Home slot 3" resolves as a
  named attribution target. Wired into the run tally.

**Verified (full harness run, all checks green)**
- Ten slots print on the correct sides, numbered 1–5; naming a slot resolves.
- The slots are fully inert: every prior rate (Roll A/B/C/D, seam signals, jump
  ball) is unchanged, confirming the layer influences no roll.

**Decided** — see design doc: *The on-court slot layer*. Slot = fixed numbered
identity; role, position, and matchup pairing are all deferred to assignment
layers ABOVE this one, chosen on purpose so management nodes (lineup-setting,
subs, rotations, matchup assignment) stack as clean consumers. Three scopes
collapsed to the right two: the roster + on-court five is one owned `Lineup` per
team (persistent, mutates via subs, like the foul count); "which slot has the
ball this possession" is deferred to `PossessionState` when selection is built.

**Left stubbed / deferred (next session and beyond)**
- The husk/fill object and the player-selection roll — NEXT session. The
  `IntoPlayerSelection` stub stays a stub; ~75% of possessions still wall there.
  The selection roll will reach into a lineup via the `LineupFor` -> `SlotAt`
  seam built this session.
- The player/attribute model (height, skill, athleticism, usage, hierarchy) — its
  own later design conversation; the heart of the sim, not smuggled in here.
- Substitution mechanism (the five are mutable; the swap logic is future work).
- Counting-stat attribution layer (this session only proved a slot is *nameable*
  as a target; it builds no attribution).
- Lineup-setting / matchup-assignment layer (who fills which slot; who guards
  whom) — sits above the slot, consumes both lineups.

---

## Session 6 — Roll D: non-shooting defensive foul + the foul/bonus layer

**Built**
- `FoulOutcomes.cs` — two enums. `FoulFlavor` (ReachIn / Blocking / OffBall):
  Roll D's pie, descriptive theater only, does NOT route. `BonusType` (None /
  OneAndOne / Double): the COMPLETE interface to the future free-throw node —
  every FT rule (shot count, front-end reboundability) is derivable from this
  single value, so nothing upstream encodes FT mechanics.
- `FoulTracker.cs` — the half-scoped object that owns both teams' foul counts and
  answers the only question asked of it: `BonusFor(team)`. Constructed with
  config-driven thresholds; validates `0 < bonus < double` on construction.
  Banding on the post-increment count: `< bonus` = None; `[bonus, double)` =
  OneAndOne; `>= double` = Double. Deliberately ignorant of free throws.
- `RollD.cs` — the shared foul-type node (many feeders, one node). Rolls flavor
  (theater), increments the fouling = defensive team's count, reads the bonus,
  returns a CONTINUE routed on the bonus: None -> `ResumeInbound`; OneAndOne /
  Double -> `ResolveFreeThrows`. Takes `GameState` (unlike A/B/C) because it
  mutates persistent state. Carries `Bonus` (functional) and `Flavor` (theater)
  on the result.
- `RollDConfig.cs` — flavor weights + `BonusThreshold` (7) + `DoubleBonusThreshold`
  (10) + epsilon, from the `"RollD"` section of `config.json`.
- `RollDStubPieGenerator.cs` — builds the flavor pie from config. NO live signal
  wire (unlike B/C): flavor does not route, so there's nothing functional for a
  signal to move; adding one would imply flavor mattered. Pie still validates.

**Edited**
- `PossessionState.cs` — `Offense` / `Defense` changed from `string` to `TeamSide`.
  Kills the stringly-typed seam at the foul path (wrong counter = wrong bonus =
  wrong game). See Decided: identity vs. role.
- `RollResult.cs` — `Continue` gained optional `Bonus` (`BonusType?`) and
  `Flavor` (`FoulFlavor?`), null on every non-foul continuation, set only by
  Roll D. Property named `Flavor` (not `FoulFlavor`) to avoid a type/name
  collision for future legibility.
- `GameState.cs` — inert `HomeTeamFouls` / `AwayTeamFouls` ints replaced by a
  live `FoulTracker Fouls` (ctor now takes one). Score / timeouts still inert.
- `EntryOutcomes.cs` — added `ResumeInbound` and `ResolveFreeThrows` kinds.
- `Resolver.cs` — `ResolveFoulType` now executes Roll D inline and feeds its
  Continue back through the loop (Roll C integration pattern); the two new kinds
  route to stubs. `FoulTypeResolverStub` retired from the constructor. Both
  Roll A and Roll B foul feeders now light up at once.
- `Stubs.cs` — `FoulTypeResolverStub` retired; `ResumeInboundStub` and
  `ResolveFreeThrowsStub` added (the latter echoes the bonus payload).
- `Program.cs` / `config.json` — `"RollD"` section; Roll D observability (flavor
  pie + a foul-count walk showing the 7 and 10 crossings); two batch checks.

**Verified (by static contract audit + logic mirror in Python; SDK unavailable)**
- Threshold banding: fouls 1–6 None, 7–9 OneAndOne, 10+ Double — exact.
- Flavor pie sums to 1, validates, converges to 60/30/10 within tolerance.
- Routing-check control-flow mirror: route flips at exactly 7 and 10; fouls land
  on the defense only (offense stays 0); ends at 12 fouls / Double.
- Handoff invariant `ended + routed-to-stub == BatchSize` preserved: both Roll D
  routes end at `STUB:` destinations, so the full-chain batch (where Roll A's
  ~3% foul exits now flow live through Roll D) still balances. The accumulating
  shared-game foul count naturally exercises both branches mid-batch.
- All retired symbols (`FoulTypeResolverStub`, `HomeTeamFouls`/`AwayTeamFouls`,
  `"HOME"`/`"AWAY"` literals) fully gone.

**Decided**
- **Team identity is fixed per game; offense/defense is a per-possession role
  over it.** Every game — neutral court included — stamps both teams Home/Away up
  front (arbitrary but stable on a neutral floor). `PossessionState` now speaks
  `TeamSide` natively, so Roll D increments the right half-counter with zero
  string mapping. The wrong-counter failure mode is now unrepresentable. Actual
  neutral-court label assignment is game-setup, deferred.
- **The bonus type is the entire FT contract.** OneAndOne vs. Double is all the
  free-throw node needs to derive: 1-and-1 front-end miss = live ball -> rebound
  roll; double-bonus first miss = dead ball -> immediate second attempt. Roll D
  and the tracker encode NONE of that — it would leak FT logic upstream.
- **Flavor is theater, bonus is functional — two different kinds of rider** on
  the same result. Flavor is logged and never read; bonus is logged AND consumed
  downstream. The flavor generator gets no signal wire for exactly this reason.
- **Roll D takes `GameState`; A/B/C did not.** It is the first roll to mutate
  persistent cross-possession state (the team foul). That's inherent to what a
  foul is, not a contract break — the uniform roll shape (receive state + pie,
  roll, name no successor) holds; the extra arg is the state it must touch.
- **Bonus thresholds 7 / 10 (classic NCAA 1-and-1 then double).** Picked for the
  clean three-state shape that the FT reboundability rule keys off. Tunable; FT
  resolution is stubbed, so the numbers can be revisited when it's real.

**Left stubbed (out of scope, untouched)**
- Free-throw resolution (`ResolveFreeThrowsStub` only) — incl. the 1-and-1 vs.
  double reboundability logic, which lives there and reads `BonusType`.
- The resumed-inbound / possession-continues node (`ResumeInboundStub`).
- Per-player foul attribution (which defender) — future counting-stat layer.
- Half-reset of team fouls — future; clears/replaces the one `FoulTracker`.
- Shooting fouls (future post-player-selection roll); offensive fouls (Roll C's).
- Real attribute -> flavor-pie generator.

---

## Session 5 — The possession arrow and the jump-ball node

**Built**
- `GameState.cs` — possession arrow upgraded from a two-valued `TeamSide` to a
  three-valued `ArrowState` (`Off` / `Home` / `Away`). `Off` exists because the
  opening (and each overtime) tip is a real contest, not an arrow read. New
  methods: `SetPossessionArrow(team)` (turns it on), `ResetPossessionArrow()`
  (back to Off for OT); `FlipPossessionArrow()` now throws if the arrow is Off.
  Score/foul/timeout placeholders untouched.
- `JumpBall.cs` — the shared jump-ball node (no pie; a jump ball is a state
  operation, not a weighted-outcome roll). Arrow Off -> 50/50 coin flip, winner
  gets the ball, arrow set ON pointing at the LOSER. Arrow On -> award the
  pointed-at team, then flip. Returns a `JumpBallAward(AwardedTo, WasTipContest)`.
- `Resolver.cs` — now holds a `GameState`. `ResolveJumpBall` resolves the jump
  ball (mutating the arrow) and ends the possession with a terminal naming the
  award (`JumpBallTip:Home` / `JumpBallArrow:Away`). `JumpBallResolverStub`
  retired from the constructor.
- `Program.cs` — harness wires a `GameState`, drops the jump-ball stub, and adds
  a jump-ball check covering all four behaviors.

**Verified (by hand-trace + coin-flip fairness; SDK unavailable in this env)**
- Opening tip is fair (~50.0% home) and the arrow is set to the tip-LOSER every
  time.
- On-arrow jump ball awards the pointed-at team and flips (Home -> Away).
- Five consecutive alternating-possession awards strictly alternate.
- Flipping an `Off` arrow throws (guards against using the arrow before a tip).

**Decided**
- **The arrow is a small state operation, not an elaborate rules engine.** The
  NCAA throw-in minutiae (violation double-flip, foul-doesn't-flip, halftime
  flip) were deliberately NOT modeled. A jump-ball award sends a team into a
  normal inbound; whatever happens next flows through the existing turnover/foul
  rolls. The arrow only awards and flips.
- **No halftime arrow flip.** The NCAA halftime flip exists solely to cancel a
  court-end switch. Charm models offense/defense as roles with no spatial floor,
  so there is no switch to cancel — flipping would be wrong. Omitted on purpose.
  (This also collapsed backcourt/frontcourt foul branches and sideline/baseline
  inbound distinctions — no court location means no spatial special cases.)
- **The tip is a 50/50 coin flip for now, marked as the future height-contest
  seam.** Intended future model: tip-win probability from centers' height
  differential, non-linear (1" ≈ negligible; ~8" ≈ near-certain) — an S-curve,
  not linear. Plugs into the tip branch once a player/attribute layer exists;
  nothing else in the node changes. Documented, not built — the tip is one event
  per game, the lowest-leverage thing to attribute, and it needs player objects
  that do not exist yet.
- **A jump ball ends the current possession.** The awarded team's ensuing
  possession is a NEW possession (future work), so `ResolveJumpBall` terminates
  rather than chaining into an inbound.

**Left stubbed (out of scope, untouched)**
- Foul-type resolver; player-selection roll; time roll.
- Real attribute -> pie generators (and the height-driven tip contest).
- The awarded team's ensuing possession / next-possession entry roll.
- Score/foul/timeout tracking on `GameState` (still inert placeholders).

---

## Session 4 — Roll C: turnover classification (the shared terminal node)

**Built**
- `TurnoverOutcomes.cs` — Roll C's outcome enum, five slices on the dead-ball vs.
  live-ball axis: `BadPassDeadBall`, `BadPassIntercepted`, `LostBallDeadBall`,
  `LostBallLiveBall`, `OffensiveFoul`. (Changed from the charter's cause-based
  four-slice set — see Decided.)
- `RollC.cs` — the shared turnover node. Every slice is a terminal; classifies
  type and ends the possession. First terminal-producing roll in the engine.
- `RollCConfig.cs` — Roll C's tunable numbers, loaded from the `"RollC"` section
  of `config.json`.
- `RollCStubPieGenerator.cs` — stub generator with one live wire (`pressure`
  nudges the `LostBallLiveBall` slice, then renormalizes).
- `Resolver.cs` — `ResolveTurnoverType` now executes Roll C and feeds its
  terminal back through the loop (the Roll B integration pattern), instead of
  terminating at a stub's `.Receive()`. `TurnoverTypeResolverStub` retired and
  dropped from the resolver's constructor; `RollCStubPieGenerator` added.
- `Program.cs` / `config.json` — harness extended: Roll C observability, a
  Roll C batch check (five rates vs. pie, every exit a clean terminal), and a
  Roll C pressure-signal check.

**Verified (by static contract audit + pie math; SDK unavailable in this env)**
- Roll C base weights sum to exactly 1; pie validates on construction.
- Pressure wire is live: `LostBallLiveBall` 20.0% (pressure 0) → 27.3%
  (pressure 1) after nudge + renormalization; other slices stay normalized.
- Resolver's `ResolveTurnoverType` → Roll C → Terminal terminates cleanly; no
  infinite-loop path (Terminal is a hard loop exit).
- Harness handoff invariant `ended + routed-to-stub == BatchSize` preserved.
  NOTE: turnovers now count as `ended` (terminal) rather than `routed-to-stub`,
  so the split shifts vs. Session 3 — expected, not a regression.

**Decided**
- **Five slices on the dead/live axis, not the charter's cause-based four.**
  Dead-ball vs. live-ball is the distinction that drives the *next* possession
  (dead → inbound reset; live → defense in transition), so it is the right cut.
  `BadPass` and `LostBall` each split into a dead and a live variant;
  `OffensiveFoul` is always dead. `ShotClockViolation` stays absent — Roll A
  owns it as an invariant terminal; duplicating it would be wrong.
- **Roll C is a pure terminal; player attribution is a separate layer, not a
  roll.** Assigning *which* player gets the turnover (and which defender gets the
  steal on live-ball outcomes) is bookkeeping that runs over outcomes whenever a
  counting stat is generated — it reads who was involved (offensive ball-handler
  already selected upstream; crediting defender named by matchup) and assigns
  credit. It does not gate the chain or feed back into resolution. So it sits
  outside Roll C entirely, as future stat infrastructure, consistent for offense
  and defense alike. Roll C only classifies type and ends the possession.
- **Live/dead distinction is carried by the slice name on `Terminal.Reason`**
  for now. A future `Terminal` may formalize a structured ball-state field if the
  entry roll needs it; logged as anticipated, not built.
- **Roll C is the first terminal-producing roll, so it integrates like Roll B**
  (resolver executes it, feeds the result back through the loop), not like a stub
  (which returns a destination string). The shared-node routing is unchanged —
  every feeder still emits `ResolveTurnoverType`; only its destination moved from
  stub to Roll C.

**Left stubbed (out of scope, untouched)**
- Player/steal attribution layer (offensive turnover + defensive steal credit).
- Future entry roll consuming turnover type to pick inbound location (sideline
  vs. baseline) and press odds.
- Player-selection roll and its pie generator.
- Foul-type resolver; jump-ball resolver; time roll.
- Real attribute → pie generators for any roll, Roll C included.

---

## Session 3 — Roll B and the conductor loop

**Built**
- `HalfcourtOutcomes.cs` — Roll B's outcome enum (`Proceed`, `Foul`, `DeadBallTurnover`).
- `RollB.cs` — three-slice gate; no terminal; follows the uniform roll contract exactly.
- `RollBConfig.cs` — Roll B's tunable numbers loaded from the `"RollB"` section of `config.json`.
- `RollBStubPieGenerator.cs` — stub generator with one live wire (`physicality` nudges the foul slice).
- `EntryOutcomes.cs` — `IntoPlayerSelection` added to `ContinuationKind`.
- `Stubs.cs` — `HalfcourtSetStub` removed (retired); `PlayerSelectionStub` added.
- `Resolver.cs` — conductor now loops: route → run → take new ticket → repeat until terminal. `IntoHalfcourtSet` routes to Roll B instead of the dead stub.
- `Program.cs` / `config.json` — harness extended to walk the full A→B chain and check both pies and the physicality signal wire.

**Verified (harness run)**
- 100k batch: Roll A's five rates match configured pie within tolerance.
- Roll B's three rates match configured pie within tolerance across 88,115 clean entries.
- handoff: 2,025 ended + 97,975 routed-to-stub = 100,000, zero unrouted.
- Physicality wire is live: foul rate 12.1% (physicality 0) → 20.2% (physicality 1).

**Decided**
- The resolver is the conductor: it owns the loop and is the only place the chain is defined. Adding a roll = write the station + one line in the resolver. Nothing else changes.
- `HalfcourtSetStub` retired — Roll B is the halfcourt initiation, so the stub had no purpose.
- Roll B's config lives in a `"RollB"` section of `config.json` rather than flattening into `RollAConfig`. Unifying into a single sectioned config is logged as debt.

**Left stubbed (out of scope, untouched)**
- Player-selection roll and its pie generator.
- Foul-type resolver (offensive vs. defensive non-shooting).
- Turnover-type resolver.
- Jump-ball resolver.
- Time roll.
- Real attribute → pie generators for any roll.

---

## Session 2 — Fouls, jump balls, and the GameState skeleton

**Built**
- Roll A's pie went from three slices to **five**: clean / turnover / violation /
  foul / jump ball.
- `Foul` -> `Continue(ResolveFoulType)` and `JumpBall` -> `Continue(ResolveJumpBall)`,
  following the turnover pattern (classify, hand off, decide downstream).
- `FoulTypeResolverStub` and `JumpBallResolverStub` added so both new exits are
  accounted for.
- `GameState` skeleton: persistent across possessions. Possession arrow
  (`TeamSide` Home/Away) with a working `FlipPossessionArrow`. Score, team fouls,
  and timeouts as typed placeholder fields, not yet wired.
- Config gained `BaseFoul` and `BaseJumpBall`; harness counts all five outcomes
  and demonstrates the arrow flip.
- Target framework moved to net10.0 to match the local machine.

**Verified (harness run)**
- 100k batch: all five rates match the configured pie within tolerance
  (clean 88.0 / turnover 6.0 / violation 2.0 / foul 3.0 / jump ball 1.0).
- Clean hand-off: 97,971 routed + 2,029 ended = 100,000, zero unrouted.
- Possession arrow flips Home -> Away.
- Seam still carries signal: turnover 6.0% (pressure 0) -> 14.5% (pressure 1).

**Decided**
- Fouls and jump balls are options on (eventually) virtually every pie; both are
  continues because they have real variance, unlike the invariant violation.
- The possession arrow must be tracked persistently, so it lives on `GameState`,
  not `PossessionState`.
- `GameState` is built as a defined-but-inert skeleton this session — arrow flips,
  everything else is a placeholder field with no resolution logic.

**Left stubbed (out of scope, untouched)**
- Foul-type resolver (defensive non-shooting vs. offensive, and its triggers).
- Jump-ball resolver (consuming/flipping the arrow).
- Real attribute -> pie generator; halfcourt-set node; turnover-type resolver.
- Score/foul/timeout tracking logic on `GameState`.
- Transition, rebounds, live-ball-steal entries; any roll other than A.

---

## Session 1 — Build Roll A (Entry: Inbounds, Dead Ball)

**Built**
- Repo scaffolding: `Charm.sln`, .NET `.gitignore`, `Charm.Engine` (class library)
  and `Charm.Harness` (console app).
- The uniform roll contract that every later roll will follow:
  - `Pie<TOutcome>` — generic weighted-odds container, validates on construction.
  - `RollResult` — sealed `Terminal` / `Continue`, with nullable `ElapsedSeconds`.
  - `ContinuationKind` — a continue carries a result category, not a successor.
  - `IRng` / `SystemRng` — seedable randomness for reproducibility.
- `RollA.Execute`; `StubPieGenerator` with one live `pressure` wire; `Resolver`
  plus halfcourt and turnover stubs; `RollAConfig` + `config.json`; harness with
  observability, 100k batch check, and pressure-signal check.

**Decided**
- Roll A is timeless except the violation terminal, which stamps its own
  invariant elapsed time. All other time is a future time roll.
- The violation lives as a terminal at entry because it has no path variance.
- Pie validation lives at construction = the generator->roll seam.
- A continue names a `ContinuationKind`, never a node; the resolver owns routing.
