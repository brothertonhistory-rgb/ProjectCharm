## Session 20 — Contextification #1 (Transition Output): Push enters the shot chain via a FastBreak marker (2026-06-12)

**The premise this session opens.** The possession-flow roll web is complete. Every
remaining open stub closes as a **CONTEXT on an existing roll, never a new roll**: a
context selects a different pie (weights may go to zero) but never changes where an
outcome routes. This is the first of a five-session **contextification arc** (the full
work order is recorded in `design.md` this session). #1 closes the transition-output park.

**Built.** Roll J's `Push` arm — "we decided to run" — no longer parks at the dead-end
`TransitionStub`. It now routes into **player selection (Roll E)**, the *same* node
`Settle` uses, carrying a **`FastBreak` marker** so a fast break produces a shot through
the shared rolls everyone else uses, tilted by a transition context rather than a separate
beat. `IntoTransition` + `TransitionStub` are **retired, kept in the corner** (dead but
present, swept later — your call to keep the diff small).

**The distinguisher: a marker on carried state, not a new ContinuationKind.** Both `Push`
and `Settle` entered Roll J off a board, so both carry a non-null `TransitionContext` —
that field records how the possession *started*, and cannot tell "we ran" from "we pulled
it out." The decision Roll J made is a new fact: `bool FastBreak` on `PossessionState`
(default false). `Push` stamps it true; `Settle` leaves it false. Roll E's generator reads
it to pick the pie. This is the **`Putback`-bit precedent** applied to a new edge — two
pies on one `IntoPlayerSelection` edge, a payload bit selecting between them — not the
enum-explosion of a parallel `IntoTransitionSelection` kind.

**Why a state field, not a Continue payload.** `Putback`/`Bonus`/`TurnoverContext` are
transient — consumed by the very next node. `FastBreak` must **persist across hops**
(E → F → G → H) because the deferred Roll G / Roll H transition tilts will read it later —
so it lives on `PossessionState` alongside `SelectedSlot`/`ShotType`/`Result`, not on the
`Continue`. A single bool because there is exactly one break flavor today (the same "single
bit suffices" call as `Putback`); richer break memory appends later as a nullable field.

**Roll E's generator grew a context branch.** `FastBreak=true` → the transition selection
pie; otherwise the flat halfcourt pie — the same context-selects-a-pie shape as Roll C/J/K.
Placeholder transition weights this session, deliberately **non-flat** (30/30/25/10/5: two
guards and a wing run, the bigs trail) so the harness can *prove* the break path draws its
own pie — flat-vs-flat would be unobservable. The real speed/athleticism favoring is the
**deferred attribute seam**; Roll E reads no attributes yet, exactly as the halfcourt pie
is a flat placeholder.

| Roll J arm | Routes to | Marker |
|---|---|---|
| Settle | `IntoPlayerSelection` → Roll E | FastBreak = **false** (halfcourt pie) |
| Push | `IntoPlayerSelection` → Roll E | FastBreak = **true** (transition pie) |
| Turnover / DefensiveFoul / JumpBall | unchanged | — |

**The marker does not leak past the break.** The only edge that re-enters Roll E for a
*fresh* play is Roll K's `ResetOffense` (kick-out after an offensive board). A transition
possession that pushed, missed, and rebounded would otherwise carry `FastBreak=true` into
that reset and wrongly draw the transition pie. So `ResetOffense` now wipes `FastBreak`
alongside slot/zone/result — a reset is a fresh **halfcourt** play. `PutBack` (the other
Roll K continue) goes to Roll H, not Roll E, so it draws no wrong pie; leaving the marker
set there is harmless now (G/H are transition-blind) and is the G/H follow-up's call.

**Validation (reasoned + Monte-Carlo-traced, pending the harness run).**
- `RollESelectionBatchCheck` gained a **pie-selection sub-check**: `FastBreak=true` draws
  exactly 30/30/25/10/5, `FastBreak=false` draws flat 20s, and the two pies differ (so
  selection is observable). This is the authoritative "transition pie only on Push" proof.
- `RollJBatchCheck` rewritten: `Push` and `Settle` now both exit via `IntoPlayerSelection`,
  split by `FastBreak`; every `Push` exit is asserted to carry the marker; zero unrouted.
  (Rates unchanged — only Push's *routing* moved.)
- `RollKReboundBatchCheck` now feeds a `FastBreak=true` post-miss state and asserts every
  `ResetOffense` clears it (the §2a leak guard, treating the marker as a carried field).
- The whole-game loop's "rebound → Roll J end-to-end" check dropped its `STUB:Transition`
  assertion (Push no longer parks there) down to "rebounds enter Roll J"; the Push→Roll E
  wiring is proven in the isolated batches. Every `STUB:Transition` / `IntoTransition`
  reference across the harness was swept.

**Deferred unchanged.** The Roll G (shot location) and Roll H (make/miss) transition tilts
are the immediate follow-up — the marker rides on state for them to read, but their
generators stay transition-blind now. All weight tuning is deferred (placeholders). The
remaining four contextification items (block recovery, steal feeder, bonus-fork extract,
Roll C expansion + Roll A reshape) each get their own session.

## Session 19 — Roll M (free-throw rebound resolution): the FT loop's downstream closes, two pies grow a second context (2026-06-12)

**Built.** The **free-throw rebound roll** — Roll M — the node a **missed final free throw**
drains into. It closes the `STUB:FTRebound` park Roll L opened last session, the same
stub→roll swap every prior session ran. The `ResolveFTRebound` edge now executes Roll M
instead of parking; the chain runs A → … → L → **M** end-to-end.

**Roll M is Roll I's shape, twice tilted.** It is a board-battle gate with mixed
terminals/continues plus the shared loose-ball foul fork — structurally Roll I — with two
deliberate differences: a **more defensive board population** (everyone lined along the lane
off a free throw, no shooter crashing in, so the offensive-board share is lower than off a
live field-goal miss) and an added **out-of-bounds pair**. It opens **no new stub and no new
`ContinuationKind`**: every one of its seven arms routes to a node that already exists.

| Roll M arm | Routes to | End/continue |
|---|---|---|
| DefensiveRebound | transition to defense, `FreeThrowRebound` context → Roll J | TERMINAL |
| OffensiveRebound | offensive-rebound node → Roll K, `FreeThrow` source | CONTINUE |
| LooseBallFoulOnDefense | charge defense + bonus fork (5th feeder) | CONTINUE |
| LooseBallFoulOnOffense | dead ball to defense at Roll A, no charge | TERMINAL |
| OutOfBoundsOffOffense | dead ball to defense at Roll A, no charge | TERMINAL |
| OutOfBoundsOffDefense | sideline inbound, no charge / no fork | CONTINUE |
| JumpBall | shared arrow node | CONTINUE |

**Reuse over duplication — two existing rolls grew a second context.** Roll M owns no FT-rebound
shooting or transition logic; it **routes into Roll K and Roll J** via context tickets, exactly
the ticket/station discipline of Roll C's turnover context:
- **Roll K** gained a labeled `OffensiveReboundSource { LiveBall, FreeThrow }` ticket on the
  offensive-rebound continuation. Roll M stamps `FreeThrow`; **every legacy feeder (Roll I)
  stamps nothing → reads as `LiveBall`**, so the field-goal path is byte-for-byte unchanged. The
  FT pie is more putback / less reset (the offense is right under the rim off an FT board).
- **Roll J** gained a `FreeThrowRebound` value on its `TransitionSource`, selecting a tamer, more
  conservative run-or-not pie (off a made/missed FT everyone got back, so the break runs less).
  The `Rebound` pie is untouched.

A **labeled tag, not a bool**, in both cases — so a third source (off a tip, a steal) grows by
append rather than forcing a second bool.

**The OOB pair is the foul pair minus the whistle.** `OutOfBoundsOffOffense` lands exactly where
`LooseBallFoulOnOffense` lands (dead ball to the defense at Roll A) — different reason label,
identical routing, no charge. `OutOfBoundsOffDefense` is the one arm that is *always* a plain
sideline inbound: no foul means no bonus question, so unlike the loose-ball-defense arm it
**never forks** even when the defense is in the bonus.

**Roll M fires once per FT trip.** A missed putback off its *offensive* board re-enters **Roll I**
(a live field-goal miss), not back into Roll M — so Roll M adds no new convergence loop; the
existing Roll K putback↔rebound proof still bounds the possession.

**`FTReboundStub` retired, kept in the corner.** The resolver no longer injects it and no check
invokes it; it is flagged for future removal as cheap re-park insurance. The seven harness
resolver sites dropped the stub arg and gained the Roll M generator (net-zero arg count).
`FreeThrowSpins` is now threaded onto all four stub-return paths so the FT-spin count survives an
FT rebound that lands at a sideline inbound.

**Validation.** A new `RollMReboundBatchCheck` proves the seven-way split converges, all arms
route as designed, the foul-charge discipline holds **per draw off the team-foul delta** (only the
loose-ball-defense arm charges; the OOB pair and every other arm charge nothing), and the §2a bonus
crossing splits the loose-ball-defense arm sideline→FT mid-batch. A new `RollMContextSelectionCheck`
proves both new tickets select the right downstream pie (and that each differs from its legacy
sibling). `RollLFreeThrowCheck` was **isolated** from Roll M by pinning its Roll M to the clean
`DefensiveRebound` terminal — the same one-arm-pie technique `RollKBonusForkCheck` uses — so the FT
loop's exact spin bands and accumulation-free property survive a now-live downstream. Every
`STUB:FTRebound` reference across the harness was swept.

**One regression, caught and corrected.** Two upstream shot-handoff checks (`RollGHandoffCheck`,
`RollHHandoffCheck`) walk a possession to completion and audited slot+zone+result on the *final*
stub landing. Roll M now lets a shooting-foul possession recurse through bonus-FT / Roll-K-reset
paths whose state legitimately carries no shooter facts (the deferred FT-shooter seam), so ~3 in
100k landed at a fact-echo stub with an absent fact tail and the audit cried FAIL. That is not a
dropped fact — it is a routed-deeper landing — and one-hop fact integrity is proven authoritatively
by `RollHResolutionBatchCheck` (fails=0) regardless. The two checks now bucket those deep landings
as routed-deeper and report the count informationally rather than failing on it. Same class as the
Session 14 lesson: a check's assumption silently expired the moment a downstream node went live.

**Deferred unchanged.** Real attribute-driven board tilt (size / box-out / lane positioning), the
FT-shooter identity, points accounting, and end-game / clock logic all remain on the horizon. Roll M
stamps no new `PossessionState` fact — which slot grabbed the board is the deferred attribution layer.

## Session 18 — Roll L (free-throw resolution): the FT loop, many feeders one node (2026-06-12)

**Built.** The **free-throw resolution roll** — Roll L — the node every trip to the line
lands at. It closes the **two longest-standing parked FT stubs at once**:
`ShootingFreeThrows` (and-1 + fouled-miss, parked since Roll H) and `ResolveFreeThrows`
(bonus FTs, parked since the Roll D / I / J / K forks). Many feeders, one node.

**Roll L is the engine's purest primitive — and the only roll that breaks the result
contract.** A free throw is the shooter against a flat `Make` / `Miss` pie, spun once per
attempt. The make probability carries **no context**: identical whether the trip came from
an and-1, a fouled three, or a bonus foul. So Roll L reads no state, takes no ticket, names
no successor, selects no parameter set, and returns a **bare `FreeThrowOutcome`** (not a
`RollResult`). It is just "does this attempt go in." Every other roll classifies its own
continuation; Roll L deliberately does not, because it has no routing role.

**The sequence is conductor-owned loop arithmetic, not a parameterized pie.** How many times
Roll L spins, and whether the last spin is live, is a structural fact of *how the foul
happened* — read by the resolver at the entry edge, never a stamp Roll L sees. The two FT
edges became the two entry points to one `DriveFreeThrows` loop; they differ only in the
shot count they hand it:

| Arriving trip | Shots | Derived from |
|---|---|---|
| And-1 (`MadeAndFouled`) | 1 | `ShootingFreeThrows` edge, `Result` |
| Fouled miss, non-three (`MissFouled`, zone ≠ Three) | 2 | `ShootingFreeThrows` edge, `Result` + `ShotType` |
| Fouled miss, three (`MissFouled`, zone = Three) | 3 | `ShootingFreeThrows` edge, `ShotType` |
| Bonus `OneAndOne` | 1-and-1 (conditional 2nd) | `ResolveFreeThrows` edge, `Bonus` |
| Bonus `Double` | 2 | `ResolveFreeThrows` edge, `Bonus` |

**Uniform per-spin routing.** Every intermediate shot (any shot before the last in a fixed
2- or 3-shot set) is **dead regardless of make or miss** — it just retriggers the next
attempt; the ball never goes live between shots. Only the **last** shot evaluates live/dead:
**make → TERMINAL** `DeadBallTo(defense)` (opponent inbounds at Roll A — the same consequence
as a made field goal, reused from Roll H's `Made`); **miss → CONTINUE** to the new
`STUB:FTRebound` (live board). The 1-and-1 is the one conditional: the front end is
**conditionally last** — miss it and it IS the last shot (live → FT-rebound, the second
forfeited); make it and a now-last second shot follows the normal rule. An and-1 is a fixed
1-shot set, so its single shot is the last shot.

**`STUB:FTRebound` is a new park** — the future FT-rebound roll's holding pen (offensive /
defensive board off a missed FT, plus any foul on that rebound). A **plain-label** stub,
deliberately NOT a `ShotFacts` echo: a bonus trip has no shooter selected, no zone, no result,
so echoing those would fire NO_SLOT and falsely flag a dropped fact.

**The loop is hard-bounded (≤ 3 spins; 1-and-1 ≤ 2), so no 10,000-iteration guard.** It
asserts the spin count never exceeds 3 — a derivation bug surfaces loud rather than
silently. No score is wired: a made FT is 1 point, a downstream derivation the future points
pass reads off the make/miss fact, exactly as a field goal's 2/3 is.

**Clean ctor swap (the agreed full-clean).** The resolver dropped its two retired FT-stub
slots and gained the Roll L generator + the FT-rebound stub. All **six** existing harness
resolver sites were updated (plus a seventh built in the new Roll L check). The two retired
stub classes are kept as harness fact-echo helpers: `ResolveFreeThrowsStub` is still used by
the Roll I side-check to confirm the `Bonus` payload rides through; `ShootingFreeThrowsStub`
is retained per plan but currently has no caller (it was only ever a live-ctor arg).

**Observability counter, not a payload.** `RollResult.cs` was left untouched — the shot
count is derived resolver-local from `Result` / `ShotType` / `Bonus`, all already on the
carried state. The one append is `RoutingOutcome.FreeThrowSpins`, an output observability
counter **exactly parallel to `PutbackAttempts`** (init-only, default 0, in `Resolver.cs`,
not `RollResult.cs`), so the harness can prove the exact per-trip spin count and the ≤ 3
bound. It is the harness-readable output seam, never an odds-bearing input ticket.

**Validation (pending Emmett's harness run — reasoned + Monte-Carlo-traced per §2).** The new
`RollLFreeThrowCheck` drives each trip type through the resolver and proves: the raw make
rate ≈ the flat config make% (.72); each trip spins Roll L the right number of times (and-1
= 1, fouled two / double = 2, fouled three = 3, 1-and-1 = 1 or 2), max never > 3; and the
**END-vs-FTRebound split is the signature of the rule** — fixed n-shot trips route on the
last shot only (intermediates dead), so END rate == p ≈ .72; a 1-and-1 ends only when BOTH
shots make, so END rate == p² ≈ .518. A made final FT hands the ball to the opponent. A
Python Monte-Carlo mirroring `DriveFreeThrows` matched every rate within 0.0023 and the spin
bands exactly. The F / G / H handoff checks were updated so FT landings absorb into their
existing "deeper" / FT-resolved buckets; the Governor check is unchanged (FT trips now show
as `FreeThrowsMade` terminals or `STUB:FTRebound` parks in its observability breakdown).
FT resolution charges no foul and touches no arrow, so it is **accumulation-free** (§2a) —
the only mid-batch crossing is upstream, fixing `Bonus` before the trip ever arrives.

**Out of scope (parked):** the FT-rebound roll itself (`STUB:FTRebound`), fouls on the FT
rebound, lane / off-ball violations (not modelled), the real FT-rating attribute generator
and the road penalty (seam at 0), the bonus-FT shooter identity (deferred seam), points
accounting (the separate attribution pass), and end-game deliberate-foul / clock logic.

---

## Session 17 — Roll K (offensive-rebound loop-back): the first possession-EXTENDING node (2026-06-12)

**Built.** The **offensive-rebound resolution roll** — Roll K — replacing the parked
`OffensiveReboundStub` on the `ResolveOffensiveRebound` edge (the same stub→roll swap
every prior session ran). It is the engine's highest-volume open node and a structural
first: until now every roll either ended a possession or handed it forward ONE step. Roll
K is the first roll that keeps the SAME possession alive and **loops it back up the
chain** — a putback goes straight back up at the rim, a reset kicks it out and runs a
fresh play. The loop lives **entirely inside the resolver's `while` walk**; the Governor
never sees it.

**Roll K — seven arms, mixed ends (the Roll I shape).** Two keep the offense's ball and
extend the possession, one keeps it via the bonus fork, three flip it, one ties it up:

| Arm | Routing | Lands |
|---|---|---|
| `PutBack` (.40) | `IntoShotResolution`, zone forced Rim, **putback ticket** | Roll H's distinct putback pie |
| `ResetOffense` (.47) | `IntoPlayerSelection`, prior shot facts **wiped** | Roll E on a blank slate |
| `DefensiveFoul` (.05) | charge defense, bonus-fork | sideline inbound OR free throws |
| `JumpBall` (.01) | `ResolveJumpBall` | shared arrow node |
| `OffensiveFoul` (.02) | TERMINAL `DeadBallTo(defense)` | Roll A (other team) |
| `DeadBallTurnover` (.03) | TERMINAL `DeadBallTo(defense)` | Roll A (other team) |
| `LiveBallTurnover` (.02) | TERMINAL `TransitionTo(defense)` | Roll A (steal-style temp-route) |

Signature `(state, pie, game, rng)` — the Roll D / I / J shape — because `DefensiveFoul`
mutates `GameState`. It is the **FOURTH feeder** into the shared charge-and-fork (after
D, I, J): copied verbatim, not reinvented.

**Possession-extension without leaking the count.** `PutBack` and `ResetOffense` are
CONTINUES that resolve back into the same walk, so a single `resolver.Route(...)` call can
now cycle PutBack → Roll H → miss → Roll I → OffensiveRebound → PutBack … and reset → Roll
E → … . The Governor only ever hears about a possession when it ENDS (a terminal) or PARKS
(a stub), so its one-in-one-out invariant `terminal + parked == cap` is **untouched**, and
the possession count does NOT increment on a reset or a putback. `GovernorLoopCheck` passes
unchanged for exactly this reason — the loop is invisible to it.

**The putback is its own shot population (ticket/station, instance #3).** A go-back-up is
point-blank, often through contact — a different make/miss/foul distribution from a normal
located attempt. So `PutBack` stamps a **putback ticket** (`Continue.Putback`) and forces
the zone to Rim; Roll H's generator reads the ticket and returns a **distinct putback
pie** (its own `Putback*` weights in `RollHConfig`) instead of the located-shot pie. This
is the third live instance of the ticket/station mechanism (after Roll C's turnover
context and Roll J's transition context): a feeder stamps, the node reads, signal flows one
way. A single bool suffices because there is exactly one putback flavor — the attribute
tilts (size / athleticism / rim rating / the contesting defender) live in the deferred
generator that reads the carried slot, NOT in more ticket variants. A missed putback
re-enters Roll I on the existing flat pie; the selected slot rides through the whole loop
untouched, so the future same-player rebound tilt (a big who misses his own putback is
favored to re-grab it) has the rebounder to read.

**Reset = a clean slate, back at player selection.** `ResetOffense` wipes the prior shot's
facts (`SelectedSlot`, `ShotType`, `Result` → null) and re-enters at **Roll E**, drawing
the inherent selection odds. It re-enters at E, NOT Roll B: the offensive-rebound pie
already absorbed the turnover / foul / jumpball hazards, so routing through B would
double-charge them.

**The loud convergence guard.** Because the walk can now cycle, the resolver's `Route` got
a real safety guard: an `iterations` counter with a 10,000 ceiling that THROWS (it does not
silently break) if a possession ever fails to converge — a real bug surfaced loudly rather
than swallowed. A new `RoutingOutcome.PutbackAttempts` tally counts the putback shots a
walk takes, so the harness can PROVE convergence rather than assume it.

**Validated (Monte Carlo, pre-harness).** Roll K's seven rates land on
`.40/.01/.05/.02/.03/.02/.47` within tolerance; every arm reached; PutBack always carries
the ticket + Rim; ResetOffense always wipes the prior shot; the three flip terminals all
hand the ball to the defense; the DefensiveFoul fork crosses the bonus correctly (the
fourth feeder behaves identically to D/I/J). The putback pie is confirmed DISTINCT from the
normal Rim pie (make .50 vs ~.38) and matches its configured `Putback*` weights. The
**nested putback↔rebound loop converges**: across 100k possessions driven into Roll K, the
putback-depth survival distribution strictly decays (≈58% take zero putbacks, ≈42% one,
≈1.4% two, a handful three, one four), the **max observed depth is 4** (ceiling is 20), and
**zero** possessions hit the iteration guard — the odds bleed the loop out exactly as
intended.

**Harness.** New: `RollKReboundBatchCheck` (seven rates + every-arm-routes + putback-shape
+ reset-wipe + flip-to-defense + bonus-fork, fresh game crossing the bonus),
`RollKPutbackPieCheck` (ticket selects a pie distinct from the Rim pie, matching the
configured weights, then consumed correctly — the Roll C context check applied to Roll H),
`RollKBonusForkCheck` (fourth-feeder parity across thresholds), and
`OffensiveReboundConvergenceCheck` (drives 100k offensive rebounds through the real
resolver loop, reads `PutbackAttempts`, asserts strict survival decay + bounded max + guard
never hit). `RollGHandoffCheck` and `RollHHandoffCheck` were updated for Roll K being live:
the offensive-rebound landing no longer parks at `STUB:OffensiveRebound` (dropped from their
required sets) — it executes Roll K and fans out, so both now bucket the Roll-K/reset
fan-out as "routed deeper" while still asserting the core post-shot destinations, zero
unrouted, and facts intact. A plain `Miss` was dropped from `RollHHandoffCheck`'s
result-on-a-stub set (it now flows deeper rather than parking). All prior checks still pass.

**Files.** New engine: `Rolls/OffensiveReboundOutcomes.cs`, `Config/RollKConfig.cs`,
`Generators/RollKStubPieGenerator.cs`, `Rolls/RollK.cs`. Modified engine:
`Core/RollResult.cs` (+`Putback` ticket on `Continue`), `Core/Resolver.cs` (Roll K
generator + `ResolveOffensiveRebound` executes Roll K + putback pie pass-through +
`PutbackAttempts` on `RoutingOutcome` + the iteration guard; `OffensiveReboundStub` retired
from the live chain), `Config/RollHConfig.cs` (+seven `Putback*` pie weights),
`Generators/RollHStubPieGenerator.cs` (+putback-pie branch), `Core/Stubs.cs`
(`OffensiveReboundStub` redocumented as a harness-only fact-echo helper). Harness:
`Program.cs` (Roll K wiring into all five resolver constructions + four new checks + the two
handoff-check updates), `config.json` (RollK section + RollH `Putback*` weights).

## Session 16 — Roll J (transition-entry gate) & the ticket/station mechanism (2026-06-12)

**Built.** The first **live transition entry**. A defensive rebound no longer
temp-routes to Roll A — it spawns a possession that enters **Roll J**, a real run-or-not
gate. And because Roll J needs to stamp a turnover context, the long-sketched
**ticket/station context-tag mechanism** went from idea to working code, instantiated
twice in one session (Roll C as a context-consuming node, Roll J as another).

**Roll J — the run-or-not gate.** Five arms, ALL continues (Roll J names no terminal of
its own): `Settle` (.65) → `IntoPlayerSelection` (run a halfcourt set, Roll E); `Push`
(.25) → `IntoTransition` → the new `TransitionStub` (we run — what the break *produces*
is a later build, parked here); `Turnover` (.06) → `ResolveTurnoverType` STAMPED
`TurnoverContext.Transition`; `DefensiveFoul` (.035) → charge the foul to `state.Defense`
(the rebound-losing team scrambling back), read the bonus, fork to sideline inbound
(below bonus) or free throws (in bonus); `JumpBall` (.005) → `ResolveJumpBall`. Signature
`(state, pie, game, rng)` — the Roll D / Roll I shape — because the foul arm mutates
`GameState`. It is the THIRD feeder into the shared charge-and-fork, copied not
reinvented.

**Transition ENTRY vs the transition ROLL — kept separate on purpose.** Roll J decides
only *whether we run*. What the fast break produces (numbers advantage, leak-outs,
transition shot mix) is a different node, parked at `TransitionStub`. Keeping these apart
is what lets Roll J stay a small flat five-way gate instead of a fast-break simulator.

**The ticket/station mechanism, realized twice.** A shared node is reached by multiple
feeders; each feeder stamps a contextual ticket on the object it hands forward; the node
reads the ticket to pick its parameter set and NEVER queries who fed it.
- **Roll C now selects by context.** Its generator picks a **Halfcourt** set (the legacy
  `.30/.22/.18/.20/.10`, reached by every pre-Roll-J feeder — they stamp nothing and read
  as Halfcourt by default) or a **Transition** set (`.25/.15/.20/.35/.05`, more live
  strips the other way). The context parameter sits LAST with a default, so every existing
  call site compiles untouched; only the resolver passes a context, and only when the
  ticket carries one. The Halfcourt path is **byte-for-byte unchanged**.
- **Roll J selects by the arriving transition ticket.** One source is live this session
  (`Rebound`); the generator builds the rebound pie and fails loud on any other source. No
  orphan steal numbers ship.

Roll J's Turnover arm is the **forcing case** — the first station ever to stamp a
non-default `TurnoverContext`, which is why Roll C had to learn to read one now.

**Carrier = a structured growable record, not an enum.** `TransitionContext(TransitionSource
Source)` rides the cross-possession consequence→entry seam.
`PossessionConsequence` gained an optional `TransitionContext` and a
`TransitionReboundTo(team)` factory; the Governor threads it onto the spawned
`PossessionState`; the resolver's entry switch reads it. A record grows by ADDING A FIELD
(plug-in, not teardown) — the reason we did not explode `EntryType` into
`TransitionOffRebound` / `TransitionOffSteal` / … . `TransitionSource` has one value
(`Rebound`); `Steal` is deliberately undeclared until it arrives with its own pie and
routing.

**Rebound-first scope.** Only Roll I's `DefensiveRebound` is wired live to Roll J (it now
uses `TransitionReboundTo(state.Defense)`). Steals still emit a plain `TransitionTo` with
a null context, so the resolver sends them to Roll A unchanged. When the steal feeder
lands, the change is ONE line in the entry switch (every `Transition` start → Roll J) plus
the steal pie — the seam is already shaped for it.

**Two deferred modifier seams on Roll J (documented, NOT built).** The Push/Settle split
is where two SEPARATE, INDEPENDENT future inputs land, never fused into one weight:
**rebounder tilt** (attribute — a guard pushes more than a center) and **coach tempo**
(strategy — the team's up/down-tempo setting). Both attach at the GENERATOR, exactly like
the height-driven tip contest and Roll C's pressure wire; Roll J the roll never changes
when they arrive.

**Single-hop ticket memory only.** Roll C reads one `TurnoverContext` off the immediate
`Continue`; Roll J reads one `TransitionSource` off the immediate entry. Multi-hop
accumulation and provenance (a steal-born break or an entry-stage turnover pushing the
downstream pie harder) is a future clean-append onto the same record, NOT built here.

**Validated (Monte Carlo, pre-harness).** Roll J's five rates land on
`.65/.25/.06/.035/.005` within tolerance; every arm is reached; every Turnover ticket is
stamped `Transition`; zero possessions go unrouted. The DefensiveFoul fork crosses the
bonus correctly (fouls 1–6 → sideline/None; 7–9 → free throws/OneAndOne; 10+ →
Double). Roll C's Halfcourt pie reproduces the legacy rates byte-for-byte; its Transition
pie reproduces `.25/.15/.20/.35/.05`; the pressure wire renormalizes and raises the
live-strip share on the selected set. The harness adds: `RollJBatchCheck` (100k, five
rates + all-arms-reached + every-Turnover-stamped + zero-unrouted, fresh game crossing
the bonus mid-batch so the foul arm splits sideline/FT), `RollJBonusForkCheck`
(all-mass-on-DefensiveFoul across thresholds), `RollCContextCheck` (drives both contexts
directly, asserting selected pie == configured weights AND resolved rates match), and an
extended `GovernorLoopCheck` that counts possessions entering Roll J off a rebound and
parks at `STUB:Transition` (reachable ONLY via Roll J's Push — airtight proof the live
wire fired).

**Files.** New: `Core/TransitionContext.cs`, `Rolls/TransitionOutcomes.cs`,
`Config/RollJConfig.cs`, `Generators/RollJStubPieGenerator.cs`, `Rolls/RollJ.cs`.
Modified engine: `Rolls/TurnoverOutcomes.cs` (+`TurnoverContext`),
`Rolls/EntryOutcomes.cs` (+`IntoTransition`), `Core/RollResult.cs` (turnover context on
`Continue`; transition context + `TransitionReboundTo` on `PossessionConsequence`),
`Rolls/RollI.cs` (rebound arm uses the new factory), `Core/Stubs.cs` (+`TransitionStub`),
`Core/PossessionState.cs` (+optional `TransitionContext`), `Config/RollCConfig.cs`
(+Transition weights), `Generators/RollCStubPieGenerator.cs` (context selection),
`Core/Resolver.cs` (Roll J generator + entry gate + `IntoTransition` route + context
pass-through), `Core/Governor.cs` (threads `TransitionContext` onto the spawn; doc
updates). Harness: `Program.cs` (wiring + three new checks + extended Governor check),
`config.json` (RollJ section + RollC Transition weights).

## Session 15 — The Thin Governor (possession-to-possession loop) (2026-06-12)

**Built.** The thin Governor — the layer that turns "resolve ONE possession" into
"play a sequence of possessions." Until now nothing read a terminal to start the next
possession, so the engine had **never run a second possession.** This closes the loop.
It is a deliberate TEMP building: its guts are provisional (flat clock, zero score,
possession-cap stop, temp-route-every-entry-to-Roll-A, parked→default-flip); its seams
are permanent. The teardown contract is recorded in `design.md`.

**The seam — terminals now carry a structured consequence.** A new small record
`PossessionConsequence(TeamSide NextOffense, EntryType NextEntry)` says what a terminal
MEANS for the next possession: who gets the ball, and how that possession starts. It
lives where the terminal is GENERATED (each roll names its own), never parsed from a
reason string by the Governor — the same philosophy as "a roll names its continuation
kind, the resolver maps it." It is MINIMAL by design (no points/clock/foul/momentum) and
grows by clean append when those consumers exist.

**Required, not nullable.** The consequence is a required positional field on `Terminal`,
so an un-named consequence is a COMPILE error at the construction site, not a silent null
the Governor guesses at. All 13 terminal sites were updated to name theirs:
- Roll A's three violations → dead-ball to the other team.
- Roll C's five turnovers → other team; dead slices `DeadBallInbound`, the two live
  slices (`BadPassIntercepted`, `LostBallLiveBall`) `Transition`.
- Roll H's `Made` and `MissOutOfBoundsLost` → dead-ball to the other team. (A made basket
  is inbounded under the hoop — ball out of bounds with an inbounding player — so it is a
  DEAD ball, not a live push. Emmett's call.)
- Roll I's `DefensiveRebound` → `Transition` to the rebounding (defense) team; its
  `LooseBallFoulOnOffense` → dead-ball to the other team.
- The Resolver's jump-ball terminal → dead-ball to the AWARDED team (the one terminal
  whose next offense is set by the arrow/tip, not by "the other team").

**The resolver surfaces the ended-on terminal.** `RoutingOutcome` gains
`Terminal? EndedOn` (init-only, null default). The terminal case sets it; a stub-park
leaves it null. `Destination` / `PossessionEnded` are untouched, so every prior check
still reads exactly what it read before. The resolver also gained `RunPossession(start)`
— it now owns the TOP of the chain (generate Roll A's pie → execute Roll A → `Route`) so
the Governor drops a START STATE and never names a roll. (Its ctor took the Roll A
generator + config; the four harness `new Resolver(...)` sites were updated.)

**EntryType reconciled to ONE enum.** `EntryType` grew a `Transition` member. There is no
parallel start-state concept — a possession's entry IS its start-state. Per the locked
call, the Governor temp-routes EVERY entry (even `Transition`) through Roll A this
session; the tag is honest for when the live-ball entry node (Roll J) lands.

**The Governor (`Core/Governor.cs`).** Owns the loop and nothing else. Per possession:
ask the resolver to run it; if it ended on a terminal, read that terminal's consequence;
if it PARKED at a stub (`EndedOn` null), apply the DEFAULT consequence — ball to the
other team, dead-ball restart at Roll A. Then spawn possession N+1 (offense from the
consequence, defense the other side, number +1, entry the consequence's tag), until the
config'd cap. It writes score (literal 0, to the real `GameState` field — real path,
placeholder value) and accumulates a flat placeholder time LOCALLY (no clock field was
added — adding one is clock-building, out of scope; the possession cap is the stop rule).

**Why park→default-flip can't explode an FT-heavy chain, and can't reintroduce the
Session-14 bug.** The stub never produces a free throw — it absorbs and parks ONE
possession; the Governor only decides what comes AFTER it (flip forward once). And every
stub-park is handled by ONE uniform path keyed on `EndedOn == null` — there is no
per-stub branch to forget. The Session-14 failure (a fact-parser that handled only the
below-bonus landing and not the in-bonus one once the shared game crossed the bonus
mid-batch) is structurally impossible here: the Governor doesn't parse the stub label to
pick the consequence; it applies the default for ANY null terminal. The per-stub
breakdown is observability, never routing.

**Cross-possession invariants — exercised in sequence for the FIRST time, and validated.**
The arrow, foul counts, and lineups all live on the shared `GameState` and persist
because the same resolver (same game) runs every possession; the Governor never resets or
clobbers them. `GovernorLoopCheck` confirms, on a 200-possession run sharing one game:
- possession numbers contiguous 1..200; offense/defense flips match each possession's
  applied consequence; first possession is Home / DeadBallInbound;
- **terminal-ended + parked == 200, zero possessions lost** (the load-bearing invariant —
  a dropped park is exactly how the count would silently leak);
- **arrow persistence** — seeded ON (Home), it is never Off thereafter and flips exactly
  once per jump ball, so the final arrow is predicted exactly by jump-ball parity;
- **foul accumulation** — Home seeded to the bonus threshold; fouls only climb (no
  half-reset this session) so the bonus STAYS crossed; monotonic non-decrease;
- **lineups survive** — same objects, five slots each.
The check also prints the terminal-vs-parked split, the per-stub park breakdown (which
SHIFTS as the bonus crosses mid-loop — `ResolveFreeThrows` parks appear — visible proof
the §2a accumulation is exercised), and the first 10 possessions.

**Validation.** Reasoned + Monte-Carlo-traced (2000 seeded sequential-loop runs held
every invariant, including the bonus crossing mid-loop), pending Emmett's harness run. No
.NET SDK in the sandbox.

**Config.** New `"Governor"` section: `PossessionCap` (200), `SecondsPerPossession` (18.0),
loaded by a new `GovernorConfig` exactly like the per-roll configs.

**Out of scope (held the walls):** no real clock, no scoring, no Roll J / entry variety
(every spawn temp-routes to Roll A), no offensive-rebound loop-back, no half-reset, no
real stop conditions, no consequence enrichment, no parked-stub resolution, no roll
internal-behavior changes beyond naming each terminal's consequence.

## Session 14 — Roll I (Rebound Resolution) (2026-06-12)

**Built.** Roll I, the rebound roll — the node a missed shot drains into. Replaces
the parked `ReboundStub` on the existing `ResolveRebound` edge (the same stub→roll
swap as C/D/E/F/G/H). Four outcomes, mixed ends (two terminals, two continues),
structurally closest to Roll H. This is the first roll whose job includes handing
the ball to the OTHER team, so it is the first place we honor the locked rule:
anything that switches which team has the ball is a TERMINAL (the possession-end
flag that will later trigger that possession's stat accumulation).

**The four outcomes (`ReboundOutcome`, new enum, declaration order significant):**
- `DefensiveRebound` (0.68) — defense secures the board; ball switches teams →
  TERMINAL. Next possession is a LIVE push into the future transition roll
  (design knowledge, not routed here).
- `OffensiveRebound` (0.29) — offense secures the board; SAME possession stays
  alive → CONTINUE to the new `ResolveOffensiveRebound` kind → `OffensiveReboundStub`.
- `LooseBallFoulOnDefense` (0.02) — foul on the defense; offense retains → CONTINUE.
  The ONLY GameState-touching arm: charges the DEFENSIVE team foul via `FoulTracker`
  and reads the bonus exactly as Roll D — below bonus → `ResolveSidelineInbound`
  (reused `SidelineInboundStub`); in bonus → `ResolveFreeThrows` carrying the
  `Bonus` payload (reused `ResolveFreeThrowsStub`, Roll D's bonus FT path).
- `LooseBallFoulOnOffense` (0.01) — foul on the offense; ball switches teams →
  TERMINAL. Charges NO foul and touches no GameState (Roll C's `OffensiveFoul`
  precedent). Next possession is a DEAD-ball inbound at Roll A (design knowledge,
  not routed here).

Two flip the ball (terminals), two keep it (continues). The flip pair splits on
live-vs-dead exactly like Roll C's turnover axis: defensive rebound is a live flip
(→ transition); offensive foul is a dead flip (→ Roll A).

**Signature `(state, pie, game, rng)`** — like Roll D, because of the one
GameState-touching arm. The other three arms read nothing off GameState. Roll I
stamps NO new `PossessionState` fact (which slot grabbed the board is the deferred
attribution layer); the terminal reason names the outcome (Roll C pattern), the
stub labels record the continue landings.

**Files added.** `Rolls/ReboundOutcomes.cs` (the enum), `Rolls/RollI.cs` (the roll),
`Config/RollIConfig.cs` (loads the `"RollI"` section — four base weights + Epsilon,
no live-wire scalar), `Generators/RollIStubPieGenerator.cs` (flat four-way pie from
config, no signal argument). Config gained the `"RollI"` section.

**Files edited.**
- `EntryOutcomes.cs` — added the `ResolveOffensiveRebound` continuation kind.
  (`ResolveSidelineInbound` and `ResolveFreeThrows` already existed — reused for the
  foul arm, nothing added there.)
- `Stubs.cs` — RETIRED `ReboundStub`; added `OffensiveReboundStub` (echoes
  slot:zone:result via `ShotFacts.Describe`, lands fact-complete like the other
  post-H stubs).
- `Resolver.cs` — swapped the `ResolveRebound` case from `ReboundStub.Receive` to
  execute-Roll-I-and-loop (generate pie → `RollI.Execute` → feed result back),
  exactly the C/D/E/F/G/H move. Added the `RollIStubPieGenerator` field + ctor
  param. Retired the `_rebound` field; added `_offensiveRebound` + the
  `ResolveOffensiveRebound` case. Ctor is now 16 args (8 generators + game + rng +
  6 stub nodes).

**Harness — the Miss ripple (Session 11/13 precedent).** With Roll I live, a `Miss`
no longer lands at `STUB:Rebound`; it flows THROUGH Roll I to one of five landings:
`END:DefensiveRebound`, `END:LooseBallFoulOnOffense`, `STUB:OffensiveRebound`,
`STUB:SidelineInbound`, `STUB:ResolveFreeThrows`. Every upstream check updated:
- Threaded `RollIConfig` + `RollIStubPieGenerator` + `OffensiveReboundStub` (and
  dropped `ReboundStub`) through ALL four `Resolver` constructions (Main + the three
  handoff checks).
- `RollHHandoffCheck` / `RollGHandoffCheck`: their `STUB:Rebound` destination is
  gone, replaced with Roll I's landings. The two new terminals
  (`END:DefensiveRebound`, `END:LooseBallFoulOnOffense`) are matched BEFORE the
  generic stub parse / `END:` catch (the same trap S11/13 handled for `END:Made` /
  `END:MissOutOfBoundsLost`), since terminals carry no slot:zone:result tail. Both
  destination sets grew from six to eight.
- `RollFHandoffCheck`: the "shot → resolved" bucket swapped `STUB:Rebound` for Roll
  I's two terminals + `STUB:OffensiveRebound` (sideline/FT were already in the
  resolved/foul sets), with the two new `END:` reasons caught BEFORE the generic
  `END:` → turnover line.
- Main `BatchCheck`: no structural change — the generic `PossessionEnded` /
  `StartsWith("STUB:")` split already absorbs Roll I's two terminals (as `ended`)
  and three continues (as `routed-to-stub`); only the explanatory comment changed.
  Invariant `ended + routed-to-stub == BatchSize`, `unrouted == 0` preserved.
- Added `RollIReboundBatchCheck` (four rates vs. pie within tolerance; every exit a
  clean terminal-or-continue of the expected kind; slot+zone+result ride through the
  stub landings; driven through a real `Miss` so the state carries slot+zone+result)
  and `RollIBonusForkCheck` (a foul-only pie driving the defense's foul count across
  the thresholds, confirming SidelineInbound below the bonus and ResolveFreeThrows
  with the right Bonus at/above — mirrors `RollDBonusRoutingCheck`). Added Roll I
  observability (the four-way pie + sample misses showing each landing). Banner now
  reads A→…→I.

**Decided.**
- Team-switch ⇒ terminal. Defensive rebound and the offensive loose-ball foul end
  the possession because the ball changes hands; the terminal is the future
  stat-accumulation trigger. Offensive rebound and the defensive loose-ball foul
  keep the same possession (continues).
- The loose-ball-defense arm REUSES `FoulTracker` (charge + bonus),
  `SidelineInboundStub` (below bonus), and `ResolveFreeThrowsStub` (in bonus) — free
  to diverge later via a distinct continuation kind or a context tag without
  reopening Roll I.
- The offensive-foul terminal charges nothing (Roll C's `OffensiveFoul` precedent).
- Flat placeholder weights (68/29/2/1). Offensive-rebound rate is a possession-count
  calibration knob, deferred to the real attribute-driven generator.

**Left stubbed / deferred.**
- The offensive-rebound roll itself (replaces `OffensiveReboundStub`; its own odds,
  one branch looping back to the half-court roll → player selection — a later session).
- The transition roll (defensive rebound is a terminal; "transition" is the next
  possession's entry — future work, consumed by the future spawner/Governor).
- The next-possession spawner / Governor (terminals just end the possession; nothing
  reads the terminal to start the other team's possession yet).
- The attribute-driven Roll I generator (the deferred offensive-rebound-rate model).
- `MissOutOfBoundsRetained` / block-recovery rerouting: unchanged; block recovery
  does NOT feed Roll I this session.

**Verified (live harness, Emmett's machine — ALL CHECKS PASSED).** Full G→H→I
chain: all destinations reached, zero unrouted, all five zones ride through the
stub landings. Roll I rates converge within tolerance (def 68.1% / off 28.9% /
foul-def 2.0% / foul-off 1.0% at N≈44.6k misses), all four arms exercised. Bonus
fork crosses at exactly 7 (OneAndOne) and 10 (Double), charging the defense each
draw. Every prior check still ok.

**One fix during validation (the shared-game bonus crossing).** First harness run
flagged `unrecognized=878` in the G/H handoff checks and the Roll I batch. Cause: the
handoff checks share ONE `game` across all 100k iterations, so the defense's foul
count climbs and crosses the bonus mid-batch. Once in the bonus, the
loose-ball-defense arm correctly routes to `STUB:ResolveFreeThrows:{Bonus}` (not
`SidelineInbound`) — but its label carries a Bonus token, not slot:zone:result, so
the fact-parsers miscounted it. Fix: recognize `STUB:ResolveFreeThrows` as a valid
Roll I landing and short-circuit it BEFORE the fact-parser (same shape as the
terminal short-circuits), in all three handoff parsers + the Roll I batch's
defense-foul arm (now split into below-bonus SidelineInbound + in-bonus
ResolveFreeThrows sub-counts). This is the live analogue of Roll D's bonus crossing,
just surfacing through Roll I's foul arm — expected behavior, not an engine bug.

---

## Session 13 — Relocate Block (Roll F → Roll H, zone-weighted) (2026-06-12)

**Moved.** `Blocked` left Roll F and became the seventh outcome of Roll H. A block
depends on WHERE the shot comes from (rim attempts get swatted far more than
threes), and the zone does not exist until Roll G stamps it — so block physically
could not be location-weighted where it used to live. Roll H sits after Roll G with
the zone already on the shot object, so it can read it. This is wiring up data that
was already flowing.

**Roll F (now four-way).** Dropped to `ShotAttempt` 85.5% / `Turnover` 9% /
`NonShootingFoul` 5% / `JumpBall` 0.5%. The old `Blocked` weight (3.5%) folded into
`ShotAttempt`. `Blocked` removed from `PlayerActionOutcome`. Roll F no longer emits
`ResolveBlock`.

**Roll H (now seven-way; block zone-aware).** `Blocked` added to `ShotResult`
(appended last) and to H's pie. The generator now reads the stamped zone and sizes a
per-zone block weight `b(zone)` — Rim 12%, Short 6%, Mid 3%, Long 2%, Three 1% —
carving it off the top and scaling the six make/miss outcomes by `(1 − b(zone))`.
Within a zone the make/miss SHAPE is unchanged except for the block carve-out; the
six stay location-blind. Config holds one shared six-way shape + five block numbers
(not a 35-number per-zone table). Every zone's pie sums to 1 by construction for any
b in [0, 1). Blended block rate over Roll G's zone mix = **5.68%** (up from the old
flat 3.5%).

**Routing.** `Blocked` routes `Continue(ResolveBlock)` → the existing
`BlockRecoveryStub`. The resolver's `ResolveBlock` edge is keyed on the continuation
kind, NOT the source roll, so it was left untouched — Session 13 only moved the FEED
point from F to H. `ContinuationKind.ResolveBlock` reused; no new continuation kind.
`BlockRecoveryStub` upgraded from slot-only to `ShotFacts.Describe`
(`slot:zone:result`), since block now lands fact-complete after Roll H like the other
post-H stubs. Block routing is zone-BLIND even though block weight is zone-aware:
every block lands at the same node.

**Harness.**
- `RollFActionBatchCheck`: four-way now; dropped the block bucket.
- `RollFHandoffCheck`: dropped the "blocked → block stub" destination (block no
  longer flows through F); four F exits.
- `RollGHandoffCheck` / `RollHHandoffCheck`: added `STUB:BlockRecovery` as a sixth
  destination; `Blocked` added to the result-ride-through set.
- `RollHResolutionBatchCheck`: rewritten to vary the zone per draw (walk Roll G fresh
  each iteration) and check the seven OBSERVED rates against ZONE-BLENDED expectations
  (blended block = Σ P(zone)·b(zone); each make/miss = base × (1 − blended)). Adds a
  per-zone block readout proving the gradient (Rim ≫ Three) plus a
  blended-rate-vs-target line. Per-zone gate uses 3× tolerance (a single zone's block
  sample is small); the hard gates are the gradient and the blended rate.
- Roll H observability: prints the per-zone block weights and generates the pie per
  sample shot's zone.

**Decided.** Only block is zone-aware this pass — Make/Miss and the foul outcomes
stay location-blind by design; per-zone shooting percentages are a separate future
tuning pass. Block WEIGHT is zone-aware but block ROUTING is zone-blind: weighting
(how often) and routing (what next) are different concerns.

**Left stubbed / deferred.**
- The block-recovery roll itself (replaces `BlockRecoveryStub`; may later feed
  rebounds — its own call, a later session).
- Per-zone make/miss shooting-% tuning (the six outcomes are still one shared shape).
- The attribute-driven Roll H generator (the deferred matchup model).

**Verified (SDK-less sandbox: pie arithmetic + Monte Carlo of the rewritten checks;
live harness is Emmett's machine).** Every zone's seven-way pie sums to exactly 1;
gradient monotonic; blended block 5.68%. Monte Carlo: seven blended rates, per-zone
block, gradient, and blended target all pass within tolerance. No orphaned `Blocked` /
`BaseBlocked` references; code-only braces and parens balance across all 12 files.

---

## Session 12 — Engine folder reorganization (2026-06-12)

Pure organizational refactor of `Charm.Engine`: sorted the flat ~43-file pile into four by-kind folders. **No behavior changes.** In C#, folder layout is purely cosmetic — files keep the `Charm.Engine` namespace regardless of folder, and the SDK-style project globs `.cs` recursively, so no `.csproj` edits and no harness changes. Moved with `git mv` so history follows each file.

Layout:
- `Rolls/` — RollA–RollH bodies + their outcome enums (EntryOutcomes, HalfcourtOutcomes, TurnoverOutcomes, FoulOutcomes, SelectionOutcomes, PlayerActionOutcomes, ShotLocation, ShotResult)
- `Generators/` — StubPieGenerator + RollB–RollH stub pie generators
- `Config/` — RollAConfig–RollHConfig
- `Core/` — Pie, RollResult, Resolver, GameState, PossessionState, Rng, JumpBall, Stubs, Slot, Lineup, FoulTracker

Known warts logged for the future doc/refactor pass (NOT fixed here — splitting a file is a code change, out of scope):
- `EntryOutcomes.cs` carries `ContinuationKind` (shared routing vocab) along to `Rolls/`.
- `PossessionState.cs` carries `EntryType` (roll-flavored enum) along to `Core/`.
- Stale `obj/Release/net8.0/` build-artifact folder still on disk from the pre-.NET-10 days; harmless, left untouched.

Judgment call: `FoulTracker.cs` → `Core/`. Not a roll/generator/config; it's the persistent foul-count accounting Roll D reads through `GameState`, i.e. shared spine.

Validation: harness rebuilt and ran — banner through `-> H`, every check `ok`, **ALL CHECKS PASSED**, rates matching Session 11 within batch jitter. Clean compile + full pass is conclusive the move was behavior-neutral (a broken glob or namespace fails to compile, it does not silently shift numbers).

# Project Charm â€” Session Journal

Newest entries first. What was built, decided, and left stubbed each session.

---

## Session 11 â€” Roll H (make/miss)

**Built**
- `ShotResult.cs` â€” `ShotResult` enum, six members in declaration order: `Made`,
  `MadeAndFouled`, `Miss`, `MissFouled`, `MissOutOfBoundsLost`,
  `MissOutOfBoundsRetained`. The THIRD durable per-possession fact's type, after
  `SelectedSlot` (E) and `ShotType` (G). Shot quality is deliberately NOT a slice â€”
  it is a make/miss percentage, folded into the deferred generator.
- `RollH.cs` â€” the make/miss roll, a WELD of three earlier patterns: Roll F's gate
  skeleton (switch over the rolled outcome), Roll A's MIXED ends (some Terminal,
  some Continue â€” the first roll since A to mix them), and Roll G's stamp-a-fact
  (`state with { Result = outcome }` before routing). Signature `(state, pie, rng)`
  â€” reads nothing off `GameState` and no stamps either. Both terminals carry the
  stamped state so the future Governor reads `Result` + `ShotType` off them.
- `RollHConfig.cs` â€” loads the `"RollH"` section (`System.Text.Json`, cloned from
  `RollGConfig`). Six base weights + Epsilon. No live-wire scalar.
- `RollHStubPieGenerator.cs` â€” builds the flat-ish six-way pie from config. NO live
  wire and location-BLIND (does not read `ShotType`); mirrors Roll E/F/G. The real
  attribute-driven generator (shooter-vs-defender matchup, gravity, skill/ath gates,
  logistic make-%) replaces it later without touching Roll H or the resolver.

**Edited**
- `PossessionState.cs` â€” added the nullable `ShotResult? Result` field (the THIRD
  per-possession fact, after `ShotType`), mirroring the `ShotType` record +
  `with`-expression pattern.
- `EntryOutcomes.cs` â€” added three `ContinuationKind`s: `ResolveRebound`,
  `ResolveShootingFreeThrows`, `ResolveSidelineInbound`. Refreshed the
  `IntoShotResolution` doc (now triggers the live Roll H, not a dead-end stub).
  Noted the FT-node-sharing open fork on `ResolveFreeThrows` /
  `ResolveShootingFreeThrows`.
- `Resolver.cs` â€” `IntoShotResolution` converted from stub-receive to
  execute-and-loop (generate pie â†’ `RollH.Execute` â†’ feed result back), exactly like
  the C/D/E/F/G swaps. Added `RollHStubPieGenerator` field + ctor param. Retired the
  single `_shotResolution` stub-node field; added three new stub-node fields
  (`_rebound`, `_resolveShootingFreeThrows`, `_sidelineInbound`) and their three
  routing cases. Ctor is now 15 args (7 generators + game + rng + 6 stub nodes).
- `Stubs.cs` â€” RETIRED `ShotResolutionStub`; added `ReboundStub`,
  `ShootingFreeThrowsStub`, `SidelineInboundStub`, plus a shared
  `ShotFacts.Describe` helper that echoes all three facts
  (`STUB:{node}:{Side}slot{N}:{Zone}:{Result}`), surfacing `NO_SLOT` / `NO_ZONE` /
  `NO_RESULT` loud if any is missing.
- `Program.cs` â€” load `RollHConfig`, build the generator, threaded it + the three
  new stubs through all four `Resolver` constructions (Main + three handoff checks).
  Added `RollHResolutionBatchCheck` (direct six-way) and `RollHHandoffCheck` (feeds
  `IntoShotResolution` to isolate H). REPURPOSED the old `RollGHandoffCheck` into a
  Gâ†’H integration check (`IntoShotType` now flows through both rolls). FIXED
  `RollFHandoffCheck`'s shot bucket (a shot now flows Fâ†’Gâ†’H to terminals/new stubs,
  no longer the retired shot-resolution stub). Added Roll H observability. Banner now
  reads A â†’ B â†’ â€¦ â†’ G â†’ H.
- `config.json` â€” added the `"RollH"` section (Made 0.43 / MadeAndFouled 0.03 / Miss
  0.47 / MissFouled 0.04 / MissOutOfBoundsLost 0.02 / MissOutOfBoundsRetained 0.01,
  sums to 1; Epsilon 1e-9).

**Decided**
- **Make/miss is one roll, mixed ends.** Made and MissOutOfBoundsLost are TERMINAL
  (the possession's two cleanest endings); the other four CONTINUE. First roll since
  A to mix terminal and continue arms â€” confirmed not to need splitting.
- **Point value and FT count are DOWNSTREAM derivations, not stored.** Roll H records
  only which of the six outcomes happened; 2-vs-3 and the 1/2/3 free-throw count are
  derived later from the `(Result, ShotType)` pair. Roll H stays pure: no points, no
  fouls, no stats, no `GameState`.
- **Roll H reads no stamps yet.** The "make/miss reads both stamps" intuition is
  correct but belongs to H's deferred GENERATOR (the matchup tilt), not the roll. The
  roll reads only its pie; the stub is location-blind.
- **Shooting free throws kept SEPARATE from Roll D's bonus free throws** (different
  shot-count rules). Whether the two FT paths unify into one node later is an OPEN
  FORK â€” flagged, not decided. `SidelineInbound` may likewise later share a
  loose-ball / inbound node with block recovery â€” also flagged, not merged.

**Left stubbed / deferred**
- `ReboundStub` (the big dependency â€” offensive board keeps the SAME possession,
  defensive board flips it; rebound system designed but unbuilt â€” DESIGN before
  build).
- `ShootingFreeThrowsStub` (free-throw success roll, the next obvious frontier).
- `SidelineInboundStub` (offense-retained OOB inbound).
- `BlockRecoveryStub` (unchanged â€” loose-ball resolution, built next to rebounds).
- Roll H's real attribute-driven pie generator (the deferred "90% of the work":
  shooter-vs-defender matchup, gravity, skill/athleticism gates, logistic make-%).

**Verified (by pie/routing mirror in Python, then the live harness on Emmett's
machine â€” SDK-less sandbox, same as prior sessions)**
- Six-way make/miss distribution converges within tolerance over 1M draws (Made
  43.0 / Miss 47.0 / MissFouled 4.0 / MadeAndFouled 3.0 / MissOOBLost 2.0 /
  MissOOBRetained 1.0).
- Every Roll H exit carries a stamped `Result` matching the rolled outcome and the
  routing arm (terminal vs. continue); anomalies = 0. All three facts (slot, zone,
  result) ride through every exit.
- Clean handoff `IntoShotResolution` â†’ Roll H: zero unrouted, all five destinations
  reached (2 terminals + 3 stubs), slot+zone+result intact on every stub landing,
  both FT-foul outcomes land at the shooting-FT stub.
- Gâ†’H integration (`IntoShotType` routed through both): zero unrouted, all five
  destinations reached, all five zones ride through to the stub landings.

---

## Session 10 â€” Roll G (shot location)

**Built**
- `ShotLocation.cs` â€” `ShotLocation` enum, five members in declaration order:
  `Three`, `Long`, `Mid`, `Short`, `Rim` (`Long` = long two). Location ONLY; each
  zone one clean meaning, so each has a real-world FG% to calibrate against later.
- `RollG.cs` â€” the shot-location roll, structurally a clone of Roll E (stamp a
  fact, continue to the SAME next beat), NOT a gate like Roll F. Drops Roll E's
  `GameState` â€” a zone is just an enum value, nothing to look up â€” so the signature
  is `(state, pie, rng)`. Rolls the five-way pie, stamps `state with { ShotType =
  zone }`, returns `Continue(IntoShotResolution)` for all five zones.
- `RollGConfig.cs` â€” loads the `"RollG"` section (`System.Text.Json`, cloned from
  `RollFConfig`). Five base weights + Epsilon. No live-wire scalar.
- `RollGStubPieGenerator.cs` â€” builds the flat-ish five-way pie from config. NO
  live wire (mirrors Roll E/Roll F). Real attribute-driven generator replaces it
  later without touching Roll G or the resolver.

**Edited**
- `PossessionState.cs` â€” added the nullable `ShotLocation? ShotType` field (the
  SECOND per-possession fact, after `SelectedSlot`), mirroring the `SelectedSlot`
  record + `with`-expression pattern. Named `ShotType` (reads cleanly at call
  sites); typed `ShotLocation`.
- `EntryOutcomes.cs` â€” added one `ContinuationKind`, `IntoShotResolution` (â†’ the
  future make/miss roll, Roll H). Refreshed the `IntoShotType` doc (now triggers
  the live Roll G, not a stub).
- `Resolver.cs` â€” `IntoShotType` converted from stub-receive to execute-and-loop
  (generate pie â†’ `RollG.Execute` â†’ feed result back), exactly like the C/D/E/F
  swaps. Added `RollGStubPieGenerator` field + ctor param. Added the
  `IntoShotResolution` case routing to the new stub. Retired the `_intoShotType`
  stub-node field (replaced by a `_shotResolution` node).
- `Stubs.cs` â€” retired `ShotTypeStub`; added `ShotResolutionStub`, which echoes
  BOTH the carried `SelectedSlot` AND the stamped `ShotType`
  (`STUB:ShotResolution:{Side}slot{N}:{Zone}`), so the harness confirms both facts
  rode through. Surfaces `NO_SLOT` / `NO_ZONE` loud if either is missing.
- `Program.cs` â€” load `RollGConfig`, build the generator, threaded it +
  `ShotResolutionStub` through all three `Resolver` constructions (Main, the Roll F
  handoff check, the new Roll G handoff check). Updated `RollFHandoffCheck`'s shot
  destination (now `STUB:ShotResolution`). Added `RollGLocationBatchCheck` and
  `RollGHandoffCheck`. Banner now reads A â†’ B â†’ â€¦ â†’ G.
- `config.json` â€” added the `"RollG"` section (`Three 0.36 / Long 0.08 / Mid 0.10
  / Short 0.11 / Rim 0.35`, sums to 1; Epsilon 1e-9).

**Decided**
- **Shot quality is NOT its own beat.** It folds into the make/miss PERCENTAGE at
  Roll H â€” a great look and a poor look differ only in conversion odds, never as a
  stored value. Splitting it out would create a bucket with no clean reference
  number, against the localized-bucket rule. This settled the one open fork: shot
  quality lives inside Roll H, so the stub after G is a genuine shot-RESOLUTION
  node (`IntoShotResolution` / `ShotResolutionStub`).
- **Roll H's make/miss pie (designed, not built):** Made / Made-fouled /
  Miss-to-rebound / Miss-fouled / Miss-OOB-possession-change, plus a sixth â€”
  Miss-deflects-off-defender-offense-retains (sideline inbound, future roll). Point
  value (2 vs. 3) comes from G's stamped `ShotType`, not a pie slice â€” which is why
  Roll H reads BOTH `SelectedSlot` and `ShotType`.
- **Attention is one conserved mechanic.** Teammate gravity (a post drawing a
  defender off the shooter's man) and lone-shooter pressure (attention collapsing
  onto the only threat) are the SAME thing â€” defensive attention allocated across
  the five matchups â€” read once per possession, one-directional, no feedback loop.
  Wide (touches all five) is safe; looping is the danger. Lives in Roll H's
  deferred generator.

**Left stubbed / deferred**
- `ShotResolutionStub` (the make/miss node â€” Roll H, the next frontier and the
  first roll that turns dominance into points).
- `BlockRecoveryStub` (unchanged â€” loose-ball resolution, built later next to
  rebounds).
- Roll G's real attribute-driven pie generator (shot selection by role/matchup).

**Verified (by pie/routing mirror in Python, then the live harness on Emmett's
machine â€” SDK-less sandbox, same as prior sessions)**
- Five-way location distribution converges within tolerance (Three 36.09 / Rim
  34.98 / Short 10.87 / Mid 10.07 / Long 7.99).
- Every Roll G exit is a clean `IntoShotResolution` Continue with `ShotType`
  actually stamped on the carried state (anomalies = 0).
- Clean handoff `IntoShotType` â†’ Roll G â†’ `ShotResolutionStub`: 100,000 routed,
  zero unrouted, slot AND zone intact on every exit, all five zones reach the stub.
- Full-chain observability samples land at `STUB:ShotResolution:{slot}:{zone}`; all
  prior checks (Aâ€“F, jump ball, slot layer, seam signals) still pass.

---

## Session 9 â€” Roll F (player action) + Roll B jump-ball sliver

**Built**
- `PlayerActionOutcomes.cs` â€” `PlayerActionOutcome` enum, five members in
  declaration order: `ShotAttempt`, `Turnover`, `NonShootingFoul`, `Blocked`,
  `JumpBall`. The success/proceed-deeper slice (`ShotAttempt`) is first, mirroring
  `CleanEntry`/`Proceed` on Rolls A/B.
- `RollF.cs` â€” the player-action GATE, a structural clone of Roll B. No terminal;
  every outcome a `Continue`. Takes only `(state, pie, rng)` â€” reads nothing off
  GameState, mutates nothing, stamps nothing on PossessionState. Five switch arms:
  `ShotAttempt`â†’`IntoShotType`, `Turnover`â†’`ResolveTurnoverType`,
  `NonShootingFoul`â†’`ResolveFoulType`, `Blocked`â†’`ResolveBlock`,
  `JumpBall`â†’`ResolveJumpBall`. THREE reuse existing shared nodes (C, D, jump-ball
  node); TWO open new pipes.
- `RollFConfig.cs` â€” loads the `"RollF"` section (`System.Text.Json`, matching
  RollC/RollD loaders exactly â€” no guessing this time). Five base weights +
  Epsilon. No live-wire scalar.
- `RollFStubPieGenerator.cs` â€” builds the flat-ish five-way pie from config. NO
  live wire (mirrors Roll E/Roll D). Real attribute-driven generator replaces it
  later without touching Roll F or the resolver.

**Edited**
- `EntryOutcomes.cs` â€” added two `ContinuationKind`s: `ResolveBlock` (â†’ block
  recovery) and `IntoShotType` (â†’ the future Roll G). Refreshed the
  `IntoPlayerAction` doc (now hands to Roll F, not a stub).
- `Resolver.cs` â€” `IntoPlayerAction` converted from stub-receive to
  execute-and-loop (generate pie â†’ `RollF.Execute` â†’ feed result back), exactly
  like the C/D/E swaps. Added `RollFStubPieGenerator` field + ctor param. Added
  `ResolveBlock` and `IntoShotType` cases routing to the two new stubs. Retired
  the `_intoPlayerAction` stub-node field.
- `Stubs.cs` â€” retired `PlayerActionStub`; added `BlockRecoveryStub` and
  `ShotTypeStub` (both echo the carried `SelectedSlot`, so the harness confirms a
  real slot rode through to the shot/block beat).
- `Program.cs` â€” load `RollFConfig`, build the generator, updated the `Resolver`
  construction (new generator param + two new stubs, `PlayerActionStub` gone).
  Added a Roll F observability block (selects via Roll E first, then resolves the
  action), a `RollFActionBatchCheck` (five-way convergence + clean-Continue), and
  a `RollFHandoffCheck` (routes real Eâ†’F exits through a fresh resolver, confirms
  all five destinations are reached with zero unrouted). Added the `JumpBall` arm
  to the Roll B mapping switch in `BatchCheck`.

**Roll B jump-ball sliver (folded in this session â€” required by the audit)**
- `HalfcourtOutcomes.cs` â€” added `JumpBall` member (4th slice).
- `RollB.cs` â€” added the `JumpBall` switch arm emitting `ResolveJumpBall`.
- `RollBConfig.cs` â€” added `BaseJumpBall` (0.005), carved from `BaseProceed`
  (0.85 â†’ 0.845) so the pie still sums to 1.
- `RollBStubPieGenerator.cs` â€” added the `JumpBall` slice to the weights dict
  (the foul wire still nudges only the foul slice). NOTE: this generator file was
  not in the read set and was rebuilt from the B/C pattern â€” verify it matches the
  working copy; the only substantive change is the added slice.
- `config.json` â€” `RollB` section gets `BaseJumpBall` + lowered `BaseProceed`.

**Verified (by static contract audit + pie/routing mirror in Python; SDK
unavailable in this env)**
- Roll F five-way pie (0.82 / 0.09 / 0.05 / 0.035 / 0.005) sums to exactly 1 and
  converges within the 0.5% tolerance across 1,000,000 draws.
- Roll B with the jump-ball sliver (0.845 / 0.12 / 0.03 / 0.005) sums to 1 and
  converges within tolerance.
- Every Roll F outcome maps to a continuation kind; three to existing kinds
  (`ResolveTurnoverType`, `ResolveFoulType`, `ResolveJumpBall`), two to new
  (`ResolveBlock`, `IntoShotType`). Zero unrouted by construction.
- The 10-second/shot-clock backcourt violations remain Roll A terminals, NOT Roll
  C slices â€” so a Roll F turnover cannot become one. The physical impossibility is
  excluded by routing for free, no suppression needed.

**Decided**
- **Roll F is a flat gate, nothing more.** What tilts its pie â€” the handle,
  defender length/hands, rim protection, shot selection â€” is the deferred
  player/attribute model. Holding the line here; the smarter generator drops in
  later through the same seam.
- **No live wire (like Roll E).** The only honest signal for Roll F is an
  attribute, and F sits one inch from the player model. A placeholder wire would
  pantomime the deferred signal. Critically, a signal like defensive pressure is a
  possession-level INPUT that F is only one reader of (it also pushes shot quality
  on the back end) â€” wiring it into F alone would bake in the wrong ownership.
- **`ShotAttempt` over `ShotGetsOff`** â€” flat tone, pairs with the `IntoShotType`
  kind it emits.
- **Two new kinds, two new stubs:** `ResolveBlock`â†’`BlockRecoveryStub`,
  `IntoShotType`â†’`ShotTypeStub`. Mirrors the Session 8 `IntoPlayerAction` /
  `PlayerActionStub` naming.
- **Roll F takes no GameState.** A flat gate reads nothing and mutates nothing;
  the jump-ball arrow flip happens in the jump-ball node, the foul charge in Roll
  D â€” not in F. So `(state, pie, rng)` like Roll B, not D/E.
- **Roll F stamps nothing on PossessionState.** Only Roll E's `SelectedSlot` rides
  forward; the future Roll G will add `ShotType`.

**Decided (designed, NOT built â€” context-shifted turnover/foul odds)**
- A turnover in the halfcourt (from Roll F) should have a different MIX than a
  backcourt entry turnover (from Roll A) â€” more live strips, more offensive fouls.
  This lives in **Roll C's generator**, not in Roll C or Roll F: "many feeders,
  one node" means one classification ROLL, never one PIE. Each feeder can hand the
  generator its context and get back a pie shaped to it.
- The provenance the generator needs is likely already free on `PossessionState`:
  `SelectedSlot` is null before Roll E and set after, so a turnover with a null
  slot came from the backcourt/halfcourt-init beat and one with a slot came from
  Roll F. No new plumbing required when this is built. Deferred
  (attribute-model-adjacent); logged so it isn't lost.

**Stubbed / deferred**
- `STUB:PlayerAction` is gone from sample output, replaced by the five resolved
  actions (turnoverâ†’C terminal, foulâ†’D, blockedâ†’block stub, shotâ†’shot-type stub,
  jump ballâ†’terminal). The chain now dead-ends at `STUB:ShotType` â€” the future
  Roll G, the next frontier.
- The block-recovery roll (OOB off defense/offense, scramble) â€” routes to its stub.
- Roll G (shot type â†’ stamps `ShotType` on PossessionState) and Roll H
  (make/miss/fouled-in-the-act, with its own and-1 resolution feeding free throws).
- The player/attribute model that eventually tilts Roll F's pie (and every other).
- The jump-ball retain/turnover branch (defense-retains terminal / offense-retains
  sideline inbound) â€” both destinations still unbuilt; the terminal-on-resolve
  placeholder stands, now fed by A, B, and F.

---

## Session 8.a â€” Routing audit, backcourt framing, Roll A violations

(Same session as Roll E below; continued work after the roll landed.)

**Audited (verified from source, not memory)**
- Read every roll's outcomes and the resolver's routing switch to build an
  accurate map. Key finding: the engine is NOT a single chain â€” it's a spine of
  action rolls draining into shared SINK nodes. Rolls A and B both feed Roll C
  (turnover) and Roll D (foul); "many feeders, one node" is the real wiring.
- Corrected an earlier mental model: Roll A is the busiest node (5 exits, now 7),
  and jump ball was reachable only from Roll A (Roll B had no jump-ball exit).
- The verified routing table is now recorded in design.md as its own section.

**Decided â€” backcourt / frontcourt division (organizing principle)**
- Roll A is the ENTIRE backcourt phase of an offensive possession: inbound,
  advance, get set in the halfcourt. Everything that can interrupt a possession
  before it's set lives in Roll A. `CleanEntry` is the single success path.
- Everything after Roll A is frontcourt (B = halfcourt initiation; E/F/G/H =
  player gets the action and it resolves). This explains why A is busy and B is a
  near-pure gate. Roll A is also where backcourt TIME will be apportioned later.

**Built â€” two new Roll A violation terminals**
- `FiveSecondInbound` â€” failure to inbound in 5s. Zero elapsed (clock never
  started). Terminal. Weight ~0.30%.
- `TenSecondBackcourt` â€” failure to clear the division line in 10s after a
  successful inbound. Stamps a fixed 10s. Terminal. Weight ~0.50%.
- Both are zero-variance terminals, like the existing shot-clock violation; each
  stamps its own invariant elapsed time, no time roll needed. A backcourt TURNOVER
  (bad pass OOB, stepping on the line) is NOT a new slice â€” it rides the existing
  `Turnover â†’ ResolveTurnoverType` path; Roll C classifies it by ball-state.

**Edited (files)**
- `EntryOutcomes.cs` â€” added the two new enum members (after `ShotClockViolation`).
- `RollA.cs` â€” two new terminal switch arms.
- `Config.cs` â†’ RENAMED to `RollAConfig.cs` (held `class RollAConfig` all along;
  the generic filename caused a 20-minute hunt this session â€” fixed for
  consistency with the other `Roll?Config.cs` files. Class name unchanged.)
- `RollAConfig.cs` â€” added `BaseFiveSecondInbound`, `BaseTenSecondBackcourt`,
  `TenSecondElapsedSeconds`.
- `StubPieGenerator.cs` â€” two new slices in the pie + the renormalize total.
- `config.json` â€” two new top-level Roll A weights + `TenSecondElapsedSeconds`.
- `Program.cs` â€” FIXED a latent bug: the batch check mapped every `Terminal` to
  `ShotClockViolation`. With three terminals now it switches on the `Reason`
  string, so each violation tallies to its own bucket.

**Verified (harness run)**
- Seven Roll A outcomes, all within tolerance; the two new violations land at
  0.315% / 0.467%. Nothing downstream regressed. ALL CHECKS PASSED.

**Decided â€” jump ball, intended vs. built (NOT yet built)**
- Held balls should be reachable from every live-ball ACTION beat: Roll A (has
  it), Roll B, and Roll F â€” NOT Roll E (selection isn't a physical contest) nor
  the shot-resolution rolls (a held ball there is a block or foul). Adding the
  `JumpBall` slice to B and F is folded into the Roll F build prompt.
- The arrow read IS a branch (settled, NOT buildable yet): DEFENSE holds the
  arrow â†’ terminal â†’ awarded team's new possession from Roll A; OFFENSE holds it â†’
  retains â†’ sideline inbound with different weights. Both destinations need
  unbuilt infrastructure (next-possession-entry layer; a sideline-inbound node),
  so the current terminal-on-resolve stands as the honest placeholder. Full
  writeup in design.md's Jump ball section.

**Also produced**
- A verified routing-map diagram and a Roll F insertion diagram (in-chat).
- The Roll F session prompt, updated to include the B/F jump-ball slivers.

---

## Session 8 â€” Roll E (player selection)

**Built**
- `SelectionOutcomes.cs` â€” `SelectionOutcome` enum, five members `Slot1`â€“`Slot5`.
  These are slot NUMBERS, not roles; each member maps to a slot number 1â€“5 by its
  declaration position. Walked in declaration order by `Pie`, like every other
  roll's outcome enum.
- `RollEConfig.cs` â€” loads the `"RollE"` section. Five explicit base weights
  (0.20 each) plus `Epsilon`. No live-wire scalar (see Decided).
- `RollEStubPieGenerator.cs` â€” builds the flat five-way pie. No signal argument
  (mirrors Roll D's flavor generator, not B/C's signal-carrying generators). The
  real usage/attribute-driven generator replaces this later without touching the
  roll or resolver.
- `RollE.cs` â€” rolls the pie, maps the outcome to a slot number, names the real
  slot on the OFFENSE's lineup via `game.LineupFor(state.Offense).SlotAt(n)`,
  stamps it onto the possession with `state with { SelectedSlot = slot }`, and
  returns `Continue(IntoPlayerAction)`. Takes `GameState` (like Roll D) to reach
  the lineup â€” not to mutate it. Walks, for the first time, the seam Session 7
  left: possession role -> `LineupFor` -> `SlotAt` -> a named slot.

**Edited**
- `PossessionState.cs` â€” added `Slot? SelectedSlot` (nullable, defaults null). The
  per-possession fact "this slot has the action," a slot REFERENCE into the
  game-scoped lineup, same shape as `Offense`/`Defense`. Null until Roll E runs
  and on possessions that end/divert before selection.
- `EntryOutcomes.cs` â€” added `ContinuationKind.IntoPlayerAction`; refreshed the
  `IntoPlayerSelection` doc (now a real roll, not a stub).
- `Resolver.cs` â€” `IntoPlayerSelection` converted from stub-receive to
  execute-and-loop (generate pie -> `RollE.Execute` -> feed result back), exactly
  like the Roll C/D cases. Added `RollEStubPieGenerator` field + ctor param. Added
  the `IntoPlayerAction` case routing to the new player-action stub. Retired the
  `_intoPlayerSelection` stub-node field.
- `Stubs.cs` â€” retired `PlayerSelectionStub`; added `PlayerActionStub`, which
  reads `continuation.State.SelectedSlot` and reports the named slot.
- `config.json` â€” added the `"RollE"` section (flat 0.20 Ã—5 + Epsilon).
- `Program.cs` â€” load `RollEConfig`, build the generator, updated the `Resolver`
  construction (new param + `PlayerActionStub`). Added a Roll E sample-selection
  printout to `ShowSamples` and a `RollESelectionBatchCheck` proving the flat 20%
  convergence and that every exit is a clean slot-stamped `IntoPlayerAction`.

**Decided**
- The selected slot lands on `PossessionState` (a durable per-possession fact read
  across many future chain hops), NOT as a `Continue` payload. Roll D's `Bonus`
  rides the Continue because it is transient input for the very next node; the
  selected slot is the opposite â€” same reasoning, opposite conclusion.
- The pie ranges over an enum (`SelectionOutcome`), not an int index, because
  `Pie<TOutcome>` is enum-constrained and an enum keeps the contract identical to
  B/C/D with no special-casing.
- Flat odds are written as five explicit 0.20 weights in config (not a computed
  "uniform default"), so the seam is visibly real and tunable, and a future
  generator overwrites numbers rather than flipping a mode.
- No live-wire signal this session: selection's first real signal is usage, part
  of the deferred attribute model â€” nothing functional for a signal to move yet.
- The continuation after selection is `IntoPlayerAction` (not "shot sequence"):
  what comes next is whatever happens TO the player â€” shot, turnover, drawn foul â€”
  not only a shot.

**Stubbed / deferred**
- `PlayerActionStub` is the new chain dead-end. `STUB:PlayerSelection` is gone from
  sample output, replaced by a named selected slot â€” the seam is live. The chain
  now dead-ends at the player-action sequence (the future shot-creation /
  shot-quality / make-miss / rebound / shooting-foul rolls), which is the next
  frontier.
- What tilts the selection pie (usage, hierarchy, ball-dominance, attributes,
  coaching) is the player/attribute model â€” its own design conversation, untouched.
- The husk/fill object (the rated player occupying a slot) is still not needed:
  selection points at the SLOT, which is nameable on its own.

**Open flag for next session**
- `RollEConfig.Load` was written using `System.Text.Json` (`JsonDocument` +
  `GetProperty`). If the other `*Config.Load` methods parse a different way, match
  their style so the codebase stays consistent â€” functionally equivalent, but
  worth aligning.

---

## Session 7 â€” The on-court slot (identity) layer

**Built**
- `Slot.cs` â€” a bare on-court identity: `readonly record struct Slot(TeamSide Side,
  int Number)`. Numbered 1â€“5 to mimic basketball addressing, but the number is
  IDENTITY, not ROLE â€” slot 1 is not "the point guard." No attributes, no fill,
  no rating, no modifier hook (not even an inert one). Mirrors `TeamSide`: pure
  identity, owned by nothing, named by everything. The number is intrinsic and
  stable, so a stat attributed to "Home slot 3" stays coherent across future
  substitutions (a sub swaps who fills the slot, never what the slot is).
- `Lineup.cs` â€” one team's on-court five. Per-team (NOT a shared both-sides
  bundle like `FoulTracker`), because this is the attachment point every heavy
  per-team/per-player system hangs off later (stat lines, the rated players that
  fill slots, the selection roll); each team's machinery grows independently.
  Stands up holding five empty numbered slots. `OnCourt` is read-only (subs go
  through a future method, never by reaching into the five); `SlotAt(1..5)` names
  one slot â€” the entity a future stat attributes to and the selection roll picks
  among.

**Edited**
- `GameState.cs` â€” added `HomeLineup` / `AwayLineup` (one `Lineup` each,
  constructed in the ctor) plus `LineupFor(TeamSide)`. This is the seam the future
  attribute generator walks: possession role -> `LineupFor` -> `SlotAt` ->
  (later) the filling player -> attributes. Mirrors how `state.Defense` indexes
  the foul counter. Score / timeouts still inert.
- `Program.cs` â€” added `SlotLayerCheck`: prints the ten slots, asserts five per
  team numbered 1â€“5 on the correct side, and proves "Home slot 3" resolves as a
  named attribution target. Wired into the run tally.

**Verified (full harness run, all checks green)**
- Ten slots print on the correct sides, numbered 1â€“5; naming a slot resolves.
- The slots are fully inert: every prior rate (Roll A/B/C/D, seam signals, jump
  ball) is unchanged, confirming the layer influences no roll.

**Decided** â€” see design doc: *The on-court slot layer*. Slot = fixed numbered
identity; role, position, and matchup pairing are all deferred to assignment
layers ABOVE this one, chosen on purpose so management nodes (lineup-setting,
subs, rotations, matchup assignment) stack as clean consumers. Three scopes
collapsed to the right two: the roster + on-court five is one owned `Lineup` per
team (persistent, mutates via subs, like the foul count); "which slot has the
ball this possession" is deferred to `PossessionState` when selection is built.

**Left stubbed / deferred (next session and beyond)**
- The husk/fill object and the player-selection roll â€” NEXT session. The
  `IntoPlayerSelection` stub stays a stub; ~75% of possessions still wall there.
  The selection roll will reach into a lineup via the `LineupFor` -> `SlotAt`
  seam built this session.
- The player/attribute model (height, skill, athleticism, usage, hierarchy) â€” its
  own later design conversation; the heart of the sim, not smuggled in here.
- Substitution mechanism (the five are mutable; the swap logic is future work).
- Counting-stat attribution layer (this session only proved a slot is *nameable*
  as a target; it builds no attribution).
- Lineup-setting / matchup-assignment layer (who fills which slot; who guards
  whom) â€” sits above the slot, consumes both lineups.

---

## Session 6 â€” Roll D: non-shooting defensive foul + the foul/bonus layer

**Built**
- `FoulOutcomes.cs` â€” two enums. `FoulFlavor` (ReachIn / Blocking / OffBall):
  Roll D's pie, descriptive theater only, does NOT route. `BonusType` (None /
  OneAndOne / Double): the COMPLETE interface to the future free-throw node â€”
  every FT rule (shot count, front-end reboundability) is derivable from this
  single value, so nothing upstream encodes FT mechanics.
- `FoulTracker.cs` â€” the half-scoped object that owns both teams' foul counts and
  answers the only question asked of it: `BonusFor(team)`. Constructed with
  config-driven thresholds; validates `0 < bonus < double` on construction.
  Banding on the post-increment count: `< bonus` = None; `[bonus, double)` =
  OneAndOne; `>= double` = Double. Deliberately ignorant of free throws.
- `RollD.cs` â€” the shared foul-type node (many feeders, one node). Rolls flavor
  (theater), increments the fouling = defensive team's count, reads the bonus,
  returns a CONTINUE routed on the bonus: None -> `ResumeInbound`; OneAndOne /
  Double -> `ResolveFreeThrows`. Takes `GameState` (unlike A/B/C) because it
  mutates persistent state. Carries `Bonus` (functional) and `Flavor` (theater)
  on the result.
- `RollDConfig.cs` â€” flavor weights + `BonusThreshold` (7) + `DoubleBonusThreshold`
  (10) + epsilon, from the `"RollD"` section of `config.json`.
- `RollDStubPieGenerator.cs` â€” builds the flavor pie from config. NO live signal
  wire (unlike B/C): flavor does not route, so there's nothing functional for a
  signal to move; adding one would imply flavor mattered. Pie still validates.

**Edited**
- `PossessionState.cs` â€” `Offense` / `Defense` changed from `string` to `TeamSide`.
  Kills the stringly-typed seam at the foul path (wrong counter = wrong bonus =
  wrong game). See Decided: identity vs. role.
- `RollResult.cs` â€” `Continue` gained optional `Bonus` (`BonusType?`) and
  `Flavor` (`FoulFlavor?`), null on every non-foul continuation, set only by
  Roll D. Property named `Flavor` (not `FoulFlavor`) to avoid a type/name
  collision for future legibility.
- `GameState.cs` â€” inert `HomeTeamFouls` / `AwayTeamFouls` ints replaced by a
  live `FoulTracker Fouls` (ctor now takes one). Score / timeouts still inert.
- `EntryOutcomes.cs` â€” added `ResumeInbound` and `ResolveFreeThrows` kinds.
- `Resolver.cs` â€” `ResolveFoulType` now executes Roll D inline and feeds its
  Continue back through the loop (Roll C integration pattern); the two new kinds
  route to stubs. `FoulTypeResolverStub` retired from the constructor. Both
  Roll A and Roll B foul feeders now light up at once.
- `Stubs.cs` â€” `FoulTypeResolverStub` retired; `ResumeInboundStub` and
  `ResolveFreeThrowsStub` added (the latter echoes the bonus payload).
- `Program.cs` / `config.json` â€” `"RollD"` section; Roll D observability (flavor
  pie + a foul-count walk showing the 7 and 10 crossings); two batch checks.

**Verified (by static contract audit + logic mirror in Python; SDK unavailable)**
- Threshold banding: fouls 1â€“6 None, 7â€“9 OneAndOne, 10+ Double â€” exact.
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
  over it.** Every game â€” neutral court included â€” stamps both teams Home/Away up
  front (arbitrary but stable on a neutral floor). `PossessionState` now speaks
  `TeamSide` natively, so Roll D increments the right half-counter with zero
  string mapping. The wrong-counter failure mode is now unrepresentable. Actual
  neutral-court label assignment is game-setup, deferred.
- **The bonus type is the entire FT contract.** OneAndOne vs. Double is all the
  free-throw node needs to derive: 1-and-1 front-end miss = live ball -> rebound
  roll; double-bonus first miss = dead ball -> immediate second attempt. Roll D
  and the tracker encode NONE of that â€” it would leak FT logic upstream.
- **Flavor is theater, bonus is functional â€” two different kinds of rider** on
  the same result. Flavor is logged and never read; bonus is logged AND consumed
  downstream. The flavor generator gets no signal wire for exactly this reason.
- **Roll D takes `GameState`; A/B/C did not.** It is the first roll to mutate
  persistent cross-possession state (the team foul). That's inherent to what a
  foul is, not a contract break â€” the uniform roll shape (receive state + pie,
  roll, name no successor) holds; the extra arg is the state it must touch.
- **Bonus thresholds 7 / 10 (classic NCAA 1-and-1 then double).** Picked for the
  clean three-state shape that the FT reboundability rule keys off. Tunable; FT
  resolution is stubbed, so the numbers can be revisited when it's real.

**Left stubbed (out of scope, untouched)**
- Free-throw resolution (`ResolveFreeThrowsStub` only) â€” incl. the 1-and-1 vs.
  double reboundability logic, which lives there and reads `BonusType`.
- The resumed-inbound / possession-continues node (`ResumeInboundStub`).
- Per-player foul attribution (which defender) â€” future counting-stat layer.
- Half-reset of team fouls â€” future; clears/replaces the one `FoulTracker`.
- Shooting fouls (future post-player-selection roll); offensive fouls (Roll C's).
- Real attribute -> flavor-pie generator.

---

## Session 5 â€” The possession arrow and the jump-ball node

**Built**
- `GameState.cs` â€” possession arrow upgraded from a two-valued `TeamSide` to a
  three-valued `ArrowState` (`Off` / `Home` / `Away`). `Off` exists because the
  opening (and each overtime) tip is a real contest, not an arrow read. New
  methods: `SetPossessionArrow(team)` (turns it on), `ResetPossessionArrow()`
  (back to Off for OT); `FlipPossessionArrow()` now throws if the arrow is Off.
  Score/foul/timeout placeholders untouched.
- `JumpBall.cs` â€” the shared jump-ball node (no pie; a jump ball is a state
  operation, not a weighted-outcome roll). Arrow Off -> 50/50 coin flip, winner
  gets the ball, arrow set ON pointing at the LOSER. Arrow On -> award the
  pointed-at team, then flip. Returns a `JumpBallAward(AwardedTo, WasTipContest)`.
- `Resolver.cs` â€” now holds a `GameState`. `ResolveJumpBall` resolves the jump
  ball (mutating the arrow) and ends the possession with a terminal naming the
  award (`JumpBallTip:Home` / `JumpBallArrow:Away`). `JumpBallResolverStub`
  retired from the constructor.
- `Program.cs` â€” harness wires a `GameState`, drops the jump-ball stub, and adds
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
  so there is no switch to cancel â€” flipping would be wrong. Omitted on purpose.
  (This also collapsed backcourt/frontcourt foul branches and sideline/baseline
  inbound distinctions â€” no court location means no spatial special cases.)
- **The tip is a 50/50 coin flip for now, marked as the future height-contest
  seam.** Intended future model: tip-win probability from centers' height
  differential, non-linear (1" â‰ˆ negligible; ~8" â‰ˆ near-certain) â€” an S-curve,
  not linear. Plugs into the tip branch once a player/attribute layer exists;
  nothing else in the node changes. Documented, not built â€” the tip is one event
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

## Session 4 â€” Roll C: turnover classification (the shared terminal node)

**Built**
- `TurnoverOutcomes.cs` â€” Roll C's outcome enum, five slices on the dead-ball vs.
  live-ball axis: `BadPassDeadBall`, `BadPassIntercepted`, `LostBallDeadBall`,
  `LostBallLiveBall`, `OffensiveFoul`. (Changed from the charter's cause-based
  four-slice set â€” see Decided.)
- `RollC.cs` â€” the shared turnover node. Every slice is a terminal; classifies
  type and ends the possession. First terminal-producing roll in the engine.
- `RollCConfig.cs` â€” Roll C's tunable numbers, loaded from the `"RollC"` section
  of `config.json`.
- `RollCStubPieGenerator.cs` â€” stub generator with one live wire (`pressure`
  nudges the `LostBallLiveBall` slice, then renormalizes).
- `Resolver.cs` â€” `ResolveTurnoverType` now executes Roll C and feeds its
  terminal back through the loop (the Roll B integration pattern), instead of
  terminating at a stub's `.Receive()`. `TurnoverTypeResolverStub` retired and
  dropped from the resolver's constructor; `RollCStubPieGenerator` added.
- `Program.cs` / `config.json` â€” harness extended: Roll C observability, a
  Roll C batch check (five rates vs. pie, every exit a clean terminal), and a
  Roll C pressure-signal check.

**Verified (by static contract audit + pie math; SDK unavailable in this env)**
- Roll C base weights sum to exactly 1; pie validates on construction.
- Pressure wire is live: `LostBallLiveBall` 20.0% (pressure 0) â†’ 27.3%
  (pressure 1) after nudge + renormalization; other slices stay normalized.
- Resolver's `ResolveTurnoverType` â†’ Roll C â†’ Terminal terminates cleanly; no
  infinite-loop path (Terminal is a hard loop exit).
- Harness handoff invariant `ended + routed-to-stub == BatchSize` preserved.
  NOTE: turnovers now count as `ended` (terminal) rather than `routed-to-stub`,
  so the split shifts vs. Session 3 â€” expected, not a regression.

**Decided**
- **Five slices on the dead/live axis, not the charter's cause-based four.**
  Dead-ball vs. live-ball is the distinction that drives the *next* possession
  (dead â†’ inbound reset; live â†’ defense in transition), so it is the right cut.
  `BadPass` and `LostBall` each split into a dead and a live variant;
  `OffensiveFoul` is always dead. `ShotClockViolation` stays absent â€” Roll A
  owns it as an invariant terminal; duplicating it would be wrong.
- **Roll C is a pure terminal; player attribution is a separate layer, not a
  roll.** Assigning *which* player gets the turnover (and which defender gets the
  steal on live-ball outcomes) is bookkeeping that runs over outcomes whenever a
  counting stat is generated â€” it reads who was involved (offensive ball-handler
  already selected upstream; crediting defender named by matchup) and assigns
  credit. It does not gate the chain or feed back into resolution. So it sits
  outside Roll C entirely, as future stat infrastructure, consistent for offense
  and defense alike. Roll C only classifies type and ends the possession.
- **Live/dead distinction is carried by the slice name on `Terminal.Reason`**
  for now. A future `Terminal` may formalize a structured ball-state field if the
  entry roll needs it; logged as anticipated, not built.
- **Roll C is the first terminal-producing roll, so it integrates like Roll B**
  (resolver executes it, feeds the result back through the loop), not like a stub
  (which returns a destination string). The shared-node routing is unchanged â€”
  every feeder still emits `ResolveTurnoverType`; only its destination moved from
  stub to Roll C.

**Left stubbed (out of scope, untouched)**
- Player/steal attribution layer (offensive turnover + defensive steal credit).
- Future entry roll consuming turnover type to pick inbound location (sideline
  vs. baseline) and press odds.
- Player-selection roll and its pie generator.
- Foul-type resolver; jump-ball resolver; time roll.
- Real attribute â†’ pie generators for any roll, Roll C included.

---

## Session 3 â€” Roll B and the conductor loop

**Built**
- `HalfcourtOutcomes.cs` â€” Roll B's outcome enum (`Proceed`, `Foul`, `DeadBallTurnover`).
- `RollB.cs` â€” three-slice gate; no terminal; follows the uniform roll contract exactly.
- `RollBConfig.cs` â€” Roll B's tunable numbers loaded from the `"RollB"` section of `config.json`.
- `RollBStubPieGenerator.cs` â€” stub generator with one live wire (`physicality` nudges the foul slice).
- `EntryOutcomes.cs` â€” `IntoPlayerSelection` added to `ContinuationKind`.
- `Stubs.cs` â€” `HalfcourtSetStub` removed (retired); `PlayerSelectionStub` added.
- `Resolver.cs` â€” conductor now loops: route â†’ run â†’ take new ticket â†’ repeat until terminal. `IntoHalfcourtSet` routes to Roll B instead of the dead stub.
- `Program.cs` / `config.json` â€” harness extended to walk the full Aâ†’B chain and check both pies and the physicality signal wire.

**Verified (harness run)**
- 100k batch: Roll A's five rates match configured pie within tolerance.
- Roll B's three rates match configured pie within tolerance across 88,115 clean entries.
- handoff: 2,025 ended + 97,975 routed-to-stub = 100,000, zero unrouted.
- Physicality wire is live: foul rate 12.1% (physicality 0) â†’ 20.2% (physicality 1).

**Decided**
- The resolver is the conductor: it owns the loop and is the only place the chain is defined. Adding a roll = write the station + one line in the resolver. Nothing else changes.
- `HalfcourtSetStub` retired â€” Roll B is the halfcourt initiation, so the stub had no purpose.
- Roll B's config lives in a `"RollB"` section of `config.json` rather than flattening into `RollAConfig`. Unifying into a single sectioned config is logged as debt.

**Left stubbed (out of scope, untouched)**
- Player-selection roll and its pie generator.
- Foul-type resolver (offensive vs. defensive non-shooting).
- Turnover-type resolver.
- Jump-ball resolver.
- Time roll.
- Real attribute â†’ pie generators for any roll.

---

## Session 2 â€” Fouls, jump balls, and the GameState skeleton

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
- `GameState` is built as a defined-but-inert skeleton this session â€” arrow flips,
  everything else is a placeholder field with no resolution logic.

**Left stubbed (out of scope, untouched)**
- Foul-type resolver (defensive non-shooting vs. offensive, and its triggers).
- Jump-ball resolver (consuming/flipping the arrow).
- Real attribute -> pie generator; halfcourt-set node; turnover-type resolver.
- Score/foul/timeout tracking logic on `GameState`.
- Transition, rebounds, live-ball-steal entries; any roll other than A.

---

## Session 1 â€” Build Roll A (Entry: Inbounds, Dead Ball)

**Built**
- Repo scaffolding: `Charm.sln`, .NET `.gitignore`, `Charm.Engine` (class library)
  and `Charm.Harness` (console app).
- The uniform roll contract that every later roll will follow:
  - `Pie<TOutcome>` â€” generic weighted-odds container, validates on construction.
  - `RollResult` â€” sealed `Terminal` / `Continue`, with nullable `ElapsedSeconds`.
  - `ContinuationKind` â€” a continue carries a result category, not a successor.
  - `IRng` / `SystemRng` â€” seedable randomness for reproducibility.
- `RollA.Execute`; `StubPieGenerator` with one live `pressure` wire; `Resolver`
  plus halfcourt and turnover stubs; `RollAConfig` + `config.json`; harness with
  observability, 100k batch check, and pressure-signal check.

**Decided**
- Roll A is timeless except the violation terminal, which stamps its own
  invariant elapsed time. All other time is a future time roll.
- The violation lives as a terminal at entry because it has no path variance.
- Pie validation lives at construction = the generator->roll seam.
- A continue names a `ContinuationKind`, never a node; the resolver owns routing.
