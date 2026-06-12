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
