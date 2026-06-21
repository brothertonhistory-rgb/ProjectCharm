## Session 01 — Wingspan-Driven Opening Tip (2026-06-20)

**Scope:** Replace the 50/50 coin-flip tip with a wingspan-gap-driven probability model. Two files changed; no roll logic, generator, or config touched.

**What shipped:**

`src/Charm.Engine/Core/JumpBall.cs` — EDIT. The `ArrowState.Off` branch (opening / OT tip) now computes a real contest. Two private static helpers added:

`MaxWingspan(GameState game, TeamSide side)` — enumerates slots 1–5 via `roster.PlayerAt(lineup.SlotAt(slot))`; returns the highest `Wingspan` among populated players. Returns 50 when no roster is populated (preserves 50/50 only when both sides are unpopulated — a real lineup facing an empty side correctly holds the advantage).

`HomeWinProbability(int homeWingspan, int awayWingspan)` — linear gap formula: `0.50 + (gap / 7.0) * 0.40`, clamped to [0.10, 0.90] via `Math.Clamp`. A 7-rating-point gap on the 0–99 scale → ±40% shift from 50/50. No tip is ever a guaranteed win.

The coin flip `rng.NextUnitInterval() < 0.5` replaced with `rng.NextUnitInterval() < HomeWinProbability(homeMax, awayMax)`. Arrow logic, `WasTipContest` flag, and alternating-possession branch untouched. Class-level XML summary updated: FUTURE SEAM language removed, height-differential S-curve reference removed, wingspan gap model described.

`src/Charm.Harness/Program.Checks.GameLifecycle.cs` — EDIT. Sub-check 5 replaced entirely. New shape: seats the config roster via `SeatStartersFromConfig`, reads actual max wingspan for each side using a local `ReadMaxWingspan` helper (same logic as `JumpBall.MaxWingspan`), derives `expectedHomeProb` from the same formula, then runs 10,000 tips and asserts the observed home rate lands within `RateTolerance * 2.0` of the expected value. No hardcoded thresholds — the assertion is derived from whoever is on the court, so it remains valid with any lineup. Arrow-still-Off and arrow-points-at-winner invariants preserved from prior version. Precondition guard (arrow ON → throws) preserved unchanged. Sub-check label updated to `"wingspan-driven tip directionality + precondition guard"`.

**Harness result:** All checks passed. Sub-check 5 reported `home max wingspan=90, away max wingspan=92`, `expected home win prob=38.57%`, observed within tolerance. Stress test confirmed directional behavior in live games (EliteVsWeak bucket shows expected attribute-driven outcome separation).

**Design call flagged during build:** Sub-check 5 uses `SeatStartersFromConfig` and derives its assertion threshold from the actual roster on the court — not hardcoded values. This makes the check valid with any lineup. When real rosters eventually replace the config roster, the threshold will auto-update; only the printed expected probability line changes.

## Session 76 — Game Boundaries: Halftime Foul Reset + Opening Tip + Overtime (2026-06-20)

**Scope:** Three game-boundary correctness items in one build. No changes to any possession roll, generator, matchup math, or roll config. This is a correctness build — the harness oracle does not apply; `output.txt` was refreshed after the new behavior was accepted.

1. **Halftime foul reset** — team foul counts reset to zero at the regulation half boundary only; fouls carry into overtime per NCAA rules.
2. **Opening tip seam** — `TipPossession.CreateFromTip` resolves the tip and returns the starting possession; used for both game start and overtime tips; all real game starts now use it.
3. **Overtime** — if regulation ends tied, the Governor runs 5-minute OT periods until a winner exists.

**What shipped (10 files — 1 new engine file, 3 engine edits, 6 harness edits):**

`src/Charm.Engine/Core/TipPossession.cs` — NEW. Static class. Single method `CreateFromTip(GameState game, IRng rng, int possessionNumber)`. Preconditions: `PossessionArrow` must be `ArrowState.Off` (throws `InvalidOperationException` if not); `possessionNumber` must be positive. Calls `JumpBall.Resolve` (already correct for `Off` state: 50/50 draw, sets arrow to loser, returns winner). Returns a `PossessionState` with `Offense = winner`, `Defense = other`, `Entry = DeadBallInbound`.

`src/Charm.Engine/Core/FoulTracker.cs` — EDIT. Added `ResetForNewHalf()` method: sets both `_homeFouls` and `_awayFouls` to zero. Updated class-level doc: removed "That reset is future infrastructure — noted, not built"; replaced with "Called at the regulation half boundary by the Governor via `ResetForNewHalf()`."

`src/Charm.Engine/Config/GovernorConfig.cs` — EDIT. Added `OvertimeSeconds` property (default `300.0` = 5 minutes × 60). Load-time invariant: `OvertimeSeconds > 0` (throws if violated). Independent of `HalfSeconds` so the two clocks are separately tunable.

`src/Charm.Engine/Core/Governor.cs` — EDIT. Three changes.

Change 1: `GovernorRunResult` record gains `int OvertimePeriods` as a trailing parameter (0 = regulation finish; 1 = one OT; etc.). All consumers use named properties — no positional deconstruction anywhere in the codebase — so the trailing addition is safe. The single `return new GovernorRunResult(...)` call passes `otPeriod`.

Change 2: The possession-loop body extracted into a local function `RunOnePossession(ref PossessionState state, ref double periodRemaining, int periodNumber)`. The local captures run-level accumulators naturally. `periodNumber` replaces `half` as the `PossessionRecord.Half` value, allowing OT periods to stamp `Half = 3, 4, …` directly. Extraction boundary: includes intent draw → resolver → score write → record creation → state spawn. Does NOT include the period-transition block (half increment, foul reset, clock reset) — those belong to the caller loops.

Change 3: Regulation loop updated with the foul-reset guard:
```
if (halfRemaining <= 0.0)
{
    if (half < _cfg.Halves)
        _game.Fouls.ResetForNewHalf();
    half++;
    halfRemaining = _cfg.HalfSeconds;
}
```
After the regulation `while` loop, an OT loop was added:
```
var otPeriod = 0;
while (_game.HomeScore == _game.AwayScore)
{
    otPeriod++;
    _game.ResetPossessionArrow();   // fresh contest (arrow -> Off)
    state = TipPossession.CreateFromTip(_game, _rng, possessionNumber: state.PossessionNumber);
    var otRemaining = _cfg.OvertimeSeconds;
    while (otRemaining > 0.0)
        RunOnePossession(ref state, ref otRemaining, _cfg.Halves + otPeriod);
}
```
No foul reset at OT boundaries — fouls carry forward per NCAA rule. The guard `half < _cfg.Halves` ensures the reset fires only between regulation halves and never after the final regulation half.

**A11 confirmed at audit:** `RunOnePossession` spawns the next state before returning (last line of the local function), so `state.PossessionNumber` already holds the next sequential number when the OT loop calls `CreateFromTip` — pass directly, do not add one.

`src/Charm.Harness/config.json` — EDIT. Added `"OvertimeSeconds": 300.0` to the `Governor` section.

`src/Charm.Harness/Program.Game.cs` — EDIT. Extracted `var governorRng = new SystemRng(seed + 1)` before the `Governor` constructor (so it has a name for the tip call). Removed `game.SetPossessionArrow(TeamSide.Home)`. Replaced `first` construction with `TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1)`.

`src/Charm.Harness/Program.Observation.cs` — EDIT. In `ObservationRunV1`: removed `firstState` declared outside the game loop; removed `game.SetPossessionArrow(TeamSide.Home)` from inside the loop; added `var firstState = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1)` inside the loop after `governorRng` is declared.

`src/Charm.Harness/Program.Stress.cs` — EDIT. In `StressTestArchetypeRosters`: removed `game.SetPossessionArrow(TeamSide.Home)`; replaced `firstState` construction with `TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1)`.

`src/Charm.Harness/Program.Checks.GameLifecycle.cs` — EDIT. Three changes.

Change A (`GovernorLoopCheck`): removed `foulOk` from pass/fail computation. Updated comment at lines ~142–144: replaced "Fouls only ever increment (no half-reset this session)" with statement that the halftime reset is verified in `GameBoundaryCheck`. Replaced the foul report line with observability text: `fouls(Home): pre-run={homeFoulsBefore} post-run=... | halftime reset: covered by deterministic GameBoundaryCheck`. `foulOk` removed from `allOk` expression.

Change B (`AttributionSanityCheck`): removed `firstState` from outside the game loop. Added `var firstState = TipPossession.CreateFromTip(game, governorRng, possessionNumber: 1)` inside the loop after `governorRng` is declared.

Change C (new `GameBoundaryCheck` method): added `private static bool GameBoundaryCheck(string configPath)` and wired `ok &= GameBoundaryCheck(configPath)` after `GovernorLoopCheck` in `Program.cs`. Five sub-checks:
- Sub-check 1: `FoulTracker.ResetForNewHalf()` unit test — pre-loads Home to 10 fouls (double bonus) and Away to 8 (1-and-1); asserts correct bonus states; calls reset; asserts both counts == 0 and both bonus states == None.
- Sub-check 2: Governor lifecycle integration — controlled config with `HalfSeconds=1.0`, `NoShot=1.0` (regulation ends 0–0 trivially), then sets `HomeScore=1` to prevent OT, runs governor, asserts foul counts for both teams == 0 after run (reset fired between halves) and `OvertimePeriods == 0`.
- Sub-check 3: OT entered, correct half numbering, gap-free sequence — controlled regulation (`HalfSeconds=1.0`, `EndOfHalfConfig.Load(configPath)` for the intent config so OT possessions at 300s get `intent=null` and can score), seed 73001. Asserts `OvertimePeriods >= 1`, first OT record has `Half == cfgGov.Halves + 1`, final score not tied, and `firstOtRecord.Number == lastRegRecord.Number + 1` (gap-free).
- Sub-check 4: No tied finals in 200 normal games — smoke test; asserts zero tied final scores.
- Sub-check 5: Tip fairness (~50% ± tolerance) and precondition enforcement — 10,000 tip draws, each with a fresh `GameState` and incrementing seed; asserts Home rate within `RateTolerance` of 50%; asserts arrow is non-Off after each call; asserts arrow points at the loser. Also constructs a game with `ArrowState.Home` and asserts `CreateFromTip` throws `InvalidOperationException`.

`src/Charm.Harness/Program.cs` — EDIT. Added `ok &= GameBoundaryCheck(configPath)` after `GovernorLoopCheck`.

**Key design decisions:**

- **Foul reset guard `half < _cfg.Halves` is load-bearing.** Without it, `ResetForNewHalf()` would fire after the final regulation half, erasing foul counts before overtime begins — directly violating the NCAA rule. The loop exits when `half > _cfg.Halves`, so the half-transition block runs one final time on the last half's drain; the guard prevents the reset from firing there.

- **`TipPossession.CreateFromTip` precondition: arrow must be `Off`.** This prevents a caller from accidentally converting a routine alternating-possession jump ball into a fake tip. `GovernorLoopCheck`'s controlled fixture explicitly sets the arrow `On` before its run — that check is deliberately not a real game start and correctly does not use `TipPossession.CreateFromTip` (A8 confirmed and preserved).

- **`ObservationRunV1`'s `firstState` moved inside the loop.** The variable was declared outside with hardcoded Home offense and used every game, meaning every game in the corpus started with Home offense regardless of tip outcome. The fix moves the construction inside the loop after `governorRng` is available, using the tip result.

- **`AttributionSanityCheck` same pattern.** Same stale outer declaration removed; tip-based construction added inside.

- **Sub-check 3 uses real `EndOfHalfConfig`, not a handmade one.** An earlier attempt used `HoldThresholdSeconds=999` (causing every OT possession to draw `NoShot` intent and never score). The fix: `EndOfHalfConfig.Load(configPath)` has `HoldThresholdSeconds=30`, so OT possessions starting at 300s get `intent=null` and the resolver runs normally, allowing scoring. Regulation halves (1.0s each) still end correctly because 1.0 < 30, so the intent block fires for those.

- **Controlled fixtures preserved.** `GovernorLoopCheck` (A8), `Phase30CoachingLayer2Check`'s `MeanAPL` helper, `Phase33`/`Phase34` governor invariant runs, and `Phase10ReboundDoorCheck` all retain their explicit `SetPossessionArrow` calls — they are isolated controlled test fixtures, not real game starts, and must not use `TipPossession.CreateFromTip`.

- **`Program.cs` shared fixture unchanged.** The deterministic `state` constructed in `Program.cs` for `ShowSamples` and isolated roll checks uses hardcoded `Offense: TeamSide.Home`. Changing it to use `TipPossession.CreateFromTip` would consume from the same `rng` used by downstream deterministic checks and alter arrow state before roll tests that are not modeling a new game. Confirmed out of scope (A10).

**A note on the partial-class reorganization.** The harness's `Program.cs` had been split into partial classes in a prior reorganization session (committed as "reorg: Pass 2+3") before this session's files were dropped in. The first build attempt failed with 57 "does not exist in the current context" errors because the reorganized `Program.Checks.GameLifecycle.cs` was a new file not matching the earlier monolithic one. The fixes were applied against the reorganized file, and the build succeeded on the second attempt.

**Harness results (ALL CHECKS PASSED):**

- `GovernorLoopCheck`: foul line now reads `fouls(Home): pre-run=7 post-run=10 | halftime reset: covered by deterministic GameBoundaryCheck`. No FAIL possible from foul counts. All other checks in this block unchanged.
- `GameBoundaryCheck` sub-check 1: `FoulTracker.ResetForNewHalf direct test -> ok`
- `GameBoundaryCheck` sub-check 2: `Governor fires halftime reset -> ok`
- `GameBoundaryCheck` sub-check 3: `OT entered, Half==3 on first OT possession -> ok` (seed 73001 produced a tied 0–0 regulation end with the controlled 1-second halves; OT ran to a winner)
- `GameBoundaryCheck` sub-check 4: `0 tied final scores across 200 games -> ok`
- `GameBoundaryCheck` sub-check 5: `tip fairness ~50% ± tolerance -> ok` (observed ~50%); `precondition guard (arrow ON → throws) -> ok`
- ALL CHECKS PASSED. STRESS TEST PASSED.

**Stress test observations (hypotheses only — no calibration grades):**
- Zero tied final scores across all 4,000 stress test games — the OT loop is working.
- Athletic over Skill 75% win rate (mirror gap 0.6%). The gap is notable and was flagged in the pre-phase 40 calibration notes — worth watching but not acted on here.
- Shooting loses to Athletic 80–20 — the engine currently values athletic rim presence much more than perimeter shooting, consistent with prior sessions.

## Session 75 — Phase 40: Retire Last Two Flavor Stubs (RollDGenerator, RollOffensiveFoulGenerator) (2026-06-20)

**Scope:** Rename the last two stub generators in the full-engine path to real generators. No logic changes, no config changes, no new math. Pure rename session — corpus hash stable.

**What shipped (4 files changed, 2 new, 2 deleted):**

`src/Charm.Engine/Generators/RollDGenerator.cs` — NEW. Exact copy of `RollDStubPieGenerator.cs` with class renamed and doc comment rewritten. Stays flat by deliberate design: `ResolveFoulType` fires before Roll G stamps `ShotType`, so `state.ShotType` is null at every Roll D call site — no zone information is available. `SelectedSlot` may be non-null on the Roll F feeder path, but flavor is non-routing theater with no downstream consumer; a player-attribute model here would imply flavor matters. It does not.

`src/Charm.Engine/Generators/RollOffensiveFoulGenerator.cs` — NEW. Exact copy of `RollOffensiveFoulStubPieGenerator.cs` with class renamed and doc comment rewritten. Two-context flat weight set (Frontcourt/Backcourt via `state.Frontcourt`) stays flat by design — same reasoning: flavor is theater, does not route, has no downstream consumer. The Frontcourt split correctly captures the dominant real-world pattern: illegal screens dominate halfcourt sets; charges and push-offs dominate backcourt bring-ups.

`src/Charm.Engine/Generators/RollDStubPieGenerator.cs` — DELETED.

`src/Charm.Engine/Generators/RollOffensiveFoulStubPieGenerator.cs` — DELETED.

`src/Charm.Engine/Core/Resolver.cs` — EDIT (surgical, four changes). Field and constructor parameter types updated: `RollDStubPieGenerator` → `RollDGenerator`; `RollOffensiveFoulStubPieGenerator` → `RollOffensiveFoulGenerator`. No other changes.

`src/Charm.Harness/Program.cs` — EDIT (global rename, two passes). Pass 1: all 32 occurrences of `RollDStubPieGenerator` → `RollDGenerator` (including method signatures at lines 521 and 573). Pass 2: all 29 occurrences of `RollOffensiveFoulStubPieGenerator` → `RollOffensiveFoulGenerator`. No other changes.

**Key design decisions:**

- **Both stay flat — by design, not by accident or incompleteness.** Roll D's `ResolveFoulType` fires before Roll G; `state.ShotType` is always null at Roll D call sites — zone context is architecturally unavailable. Roll D offensive foul flavor stays flat for the same reason Roll C's pressure wire was retired in Phase 37: adding matchup signal implies the field routes somewhere. These fields do not route. Future sessions must not re-litigate this without first establishing a downstream consumer.

- **This is identical in nature to the Roll C rename in Phase 37.** The "real generator" is the stub with an honest name and an honest doc comment. The doc comments now explicitly document why each stays flat so the reasoning is preserved.

- **No interfaces exist for either generator.** Confirmed by pre-build audit: no `IRollDPieGenerator` or `IRollOffensiveFoulPieGenerator` file exists. Concrete types used directly everywhere; global find-replace covers all sites.

- **`RollBStubPieGenerator.cs` remains** — noted during audit, confirmed out of scope.

**Corpus hash note:** Config hash `e48085ff2196764bcca5512258050a4beb3609f8af74c767fd5acb8e8b46ec26` — identical to Phase 38/39 runs. The Session 74 journal entry recorded `039faca2...`, which came from an earlier run against a different config state; the most recent validated run (uploaded pre-session output.txt) already showed `e48085ff...`, confirming hash stability across this rename.

**Harness results (all checks passed):**

- `RollDFlavorBatchCheck`: passed with identical rates — same weights, no math change.
- `RollDBonusRoutingCheck`: passed unchanged.
- Corpus hash: `e48085ff...` — stable, matches most recent known-good run.
- `Mechanics: ALL OK`. All prior checks passed.
- ALL CHECKS PASSED. STRESS TEST PASSED.


## Session 74 — Phase 39: Assist Attribution Core (AssistPicker, on-walk) (2026-06-20)

**Scope:** Wire assist attribution on-walk, exactly like STL/BLK/DRB: a probabilistic credit on every eligible made field goal, picked from the four non-shooter offensive players, stamped into a new `AstBySlot` field threaded through `RoutingOutcome → PossessionRecord → harness`. 6 files changed, 1 new.

**What shipped (6 files changed, 1 new):**

`src/Charm.Engine/Core/AssistPicker.cs` — NEW. Static class on the `BlockerPicker` / `OffensiveRebounderPicker` pattern. Two public methods: `LineupPassingFactor(state, game, cfg)` — deterministic, no RNG; returns a tanh-shaped multiplier on the zone base rate based on the mean `AssistWeight` of the five offensive players (shooter included — team property); and `Pick(state, game, cfg, rng)` — excludes the shooter (weight 0 for `state.SelectedSlot`), weights every other populated offensive player by `max(1, AssistWeight(p, cfg))`, one RNG draw, cumulative walk, throws `InvalidOperationException` on empty non-shooter lineup. Private `AssistWeight(Player p, MatchupConfig cfg)` blends `Passing × AssistPassingWeight + Playmaking × AssistPlaymakingWeight + BasketballIQ × AssistIqWeight`. Coefficients sum to 1.0 — deliberate deviation from the block/rebound convention, documented in both the class XML doc and `MatchupConfig`.

`src/Charm.Engine/Config/MatchupConfig.cs` — EDIT. Assist block appended after the `Blk*` block: five `AssistedRate*` props + `AssistedRate(ShotLocation)` switch; `AssistPassingWeight` / `AssistPlaymakingWeight` / `AssistIqWeight`; `AssistPassMidpoint` / `AssistPassScale` / `AssistPassSwing`; `AssistRateFloor` / `AssistRateCeiling`. Load invariants: coefficients `>= 0` and sum to `1.0 ± epsilon`; each zone rate in `[0,1]`; `AssistRateFloor < AssistRateCeiling`.

`src/Charm.Engine/Core/Resolver.cs` — EDIT (surgical, four changes). `var astBySlot = new SlotGroup()` declared alongside `blkBySlot`; assist roll inserted in the made-FG branch after the BLK stamp block, gated by `!c.Putback && shotSt.SelectedSlot is not null && shotSt.Result is Made or MadeAndFouled`; `public SlotGroup AstBySlot { get; init; }` added to `RoutingOutcome` (pure append, init-only default — every positional `new RoutingOutcome(false, "STUB:…")` untouched); `AstBySlot = astBySlot` added to the Terminal return.

`src/Charm.Engine/Core/Governor.cs` — EDIT (surgical, mirror BlkBySlot). `SlotGroup AstBySlot = default` appended to `PossessionRecord`; `var possessionAstBySlot = new SlotGroup()` alongside `possessionBlkBySlot`; `possessionAstBySlot = outcome.AstBySlot` in the resolver branch; `possessionAstBySlot` appended last in `records.Add(...)`.

`src/Charm.Harness/Program.cs` — EDIT. `PlayerBoxTotals.Ast` field added and included in `AllEqual`; `bsAst[10]` accumulator declared; `cohortAst[10]` accumulator declared; per-slot AST read from `r.AstBySlot[s]` (offensive roster, mirrors OrbBySlot pattern); `totalAstBySlot` running total accumulated per-game; AST reconciliation (`bsAst.Sum() == totalAstBySlot`) added after the TO reconciliation; `AST` column added to all three box headers and row writers; `Phase39AssistCheck` wired after `Phase36BlockerCheck` with five sub-checks (PG share > non-passer share; shooter never self-assists; zone probs within bounds; per-zone ordering; empty lineup throws).

`src/Charm.Harness/config.json` — EDIT. 14 new `Matchup` keys added: `AssistPassingWeight`, `AssistPlaymakingWeight`, `AssistIqWeight`, `AssistPassMidpoint`, `AssistPassScale`, `AssistPassSwing`, `AssistedRateThree`, `AssistedRateLong`, `AssistedRateMid`, `AssistedRateShort`, `AssistedRateRim`, `AssistRateFloor`, `AssistRateCeiling`.

**Key design decisions:**

- **Draw, not trace.** An assist is a probabilistic credit on a made bucket, not a tracked pass. Same philosophy as STL/BLK/DRB — given a trigger event, who gets credit? This keeps the engine free of per-event state and consistent with every prior attribution picker.

- **Zone × lineup-passing factor.** Zone sets the base assisted rate; the offensive lineup's collective passing modulates it. Without the multiplier, passing would only decide *who* gets credited, never the team rate — that misses the core signal (Minnesota 71% vs W Illinois 37%). The tanh factor centered at `AssistPassMidpoint=50` (league-average) scales above/below for better/worse passing lineups. Factor range: (0.75, 1.25) with `AssistPassSwing=0.25`.

- **Coefficient sum-to-one is correct here.** `AssistWeight` coefficients (`0.50 / 0.35 / 0.15`) sum to 1.0. This is a deliberate deviation from `BlockerWeight` and the rebound positional weights, which intentionally do NOT sum to one (the picker normalizes among players so absolute scale is irrelevant there). The sum-to-one constraint keeps `AssistWeight` on the 0–100 attribute scale, making `AssistPassMidpoint=50` the league-average reference for `LineupPassingFactor`. This is correct and is not an inconsistency to "fix" against the block/rebound convention.

- **Putbacks flatly ineligible.** A putback means no pass occurred after the offensive rebound — an assist is impossible by definition. Gated by `!c.Putback` in the Resolver, independently of `SelectedSlot` state. The null-`SelectedSlot` guard is a separate, independent safety for the bonus-FT putback edge (Roll E never ran).

- **`AssistedRateThree` and `AssistRateCeiling` corrected post-green.** The v1 prompt set `AssistedRateThree = 0.83` and `AssistRateCeiling = 0.80`, which caused threes to always saturate at the ceiling regardless of passing quality. The real data showed 80% as the *floor* for above-the-break threes, with corner threes at ~95%. Corrected to `AssistedRateThree = 0.88` (blended corner ~95% / above-the-break ~80–81%) and `AssistRateCeiling = 0.95`. Strong-passing lineups can now push three-point assist rates near the corner-three reality.

- **Iso/motion concentration slider deferred.** The W Illinois problem (good passers who don't carry the offensive load → low team rate) requires knowing which players *take the shots*, not just who can pass. That is the slider's job. Phase 39 captures the lineup-collective grain; per-player load concentration is a named future feature.

**Harness results (all checks passed):**

- `Phase39AssistCheck`: all five sub-checks passed — PG share (35.8%) > non-passer share (21.4%); shooter zero self-assists across 100k draws; all zone probs within [floor, ceiling]; ordering correct; empty lineup throws.
- Reconciliation: `Σ per-player AST == Σ AstBySlot.Total` — passed.
- Corpus hash shifted from Phase 38 (expected — assist roll consumes one RNG draw on every eligible made FG, plus a second on assisted makes).
- `Mechanics: ALL OK`. All prior checks passed.
- ALL CHECKS PASSED.

## Session 73 — Phase 38: Wire RollKGenerator Into All Full-Engine Simulations (2026-06-19)

**Scope:** Replace `RollKStubPieGenerator` with the already-built `RollKGenerator` at the nine harness sites that construct a full-engine resolver (observation corpus, stress test, and seven all-real attribution checks). The stub survives as a deliberate test double at 20 isolation sites where flat weights are required. No engine changes, no config changes, no new files, no deletions. 1 file changed.

**What shipped (1 file changed):**

`src/Charm.Harness/Program.cs` — EDIT (9 surgical generator swaps, nothing else). Four **Pattern A** sites (`ObservationRunV1`, `StressTestArchetypeRosters`, `AttributionSanityCheck`, `Phase25ShootingFoulAttributionCheck`) replaced `new RollKStubPieGenerator(cfgK)` with `new RollKGenerator(cfgK, cfgMatchup, game)`. Five **Pattern B** sites (`Phase31RebounderPickerCheck`, `Phase33TurnoverCommitterCheck`, `Phase34TurnoverAttributionCheck`, `Phase35DefensiveReboundCheck`, `Phase36BlockerCheck`) replaced `new RollKStubPieGenerator(RollKConfig.Load(configPath))` with `new RollKGenerator(RollKConfig.Load(configPath), matchupCfg, govGame)`. All 20 stub sites that are deliberate test doubles remain unchanged. `Resolver.cs`, `config.json`, `RollKStubPieGenerator.cs`, and `IRollKPieGenerator.cs` are untouched.

**Key design decisions:**

- **Stub is a legitimate test double, not dead code.** `RollMContextSelectionCheck` asserts that the returned pie equals the flat config weights exactly — a real generator would correctly break that assertion. The handoff checks, press checks, and `Phase30CoachingLayer2Check` deliberately keep all other rolls flat so they measure only the targeted behavior; injecting attribute-driven Roll K behavior into those resolvers would contaminate the signal. The stub stays until those checks are refactored to match (which is not a goal).

- **Config hash unchanged.** `config.json` was not touched. The hash line is identical to Phase 37: `e48085ff2196764bcca5512258050a4beb3609f8af74c767fd5acb8e8b46ec26`. The corpus output changes came from the generator swap, not a config edit.

- **Corpus direction confirmed.** The Python pre-check predicted EliteVsWeak ORB→points would widen while AverageVsAverage barely moved. Stress-test results: EliteVsWeak ORB% = 48.8% (Team A) vs 12.9% (Team B); AverageVsAverage ORB% = 28.9% vs 29.1%. The gap is exactly where the matchup math should put it.

**Harness results (all checks passed):**

- `Phase32` (`RollKReboundBatchCheck`, `RollKBonusForkCheck`): unchanged `[OK]` — already used the real generator.
- All nine flipped checks passed: `AttributionSanityCheck`, `Phase25`, `Phase31`–`Phase36` all reported `PASSED`.
- Observation run: `Mechanics: ALL OK`. Config hash unchanged.
- Stress test: `STRESS TEST PASSED`. ORB% movement matches corpus-direction prediction.
- ALL CHECKS PASSED.

## Session 72 — Phase 37: RollCGenerator — Flat Context-Driven Type-Mix, Activate EntryBackcourt, Retire Pressure Wire (2026-06-19)

**Scope:** Replace `RollCStubPieGenerator` with `RollCGenerator` (flat, no pressure wire, no matchup signal), activate the `EntryBackcourt` context in `RollCContextCheck`, and delete `PressureSignalCheck` and its helper `RollCLiveStripRate`. Roll C's resolver and Roll C itself untouched. 5 files changed, 1 new, 1 deleted.

**What shipped (5 files changed, 1 new, 1 deleted):**

`src/Charm.Engine/Generators/RollCGenerator.cs` — NEW. Sealed class, same `(RollCConfig cfg)` constructor as the stub. `Generate` signature: `(PossessionState state, TurnoverContext context = TurnoverContext.Halfcourt)`. Three-arm context switch identical to the stub — Halfcourt, Transition, EntryBackcourt — returning `new Pie<TurnoverOutcome>(weights, _cfg.Epsilon)` directly. The nudge-and-renormalize block is gone entirely: pressure was always hardcoded 0.0 at every call site, so removing it is a mathematical no-op (confirmed by corpus hash stability). `state` is carried for signature parity only. Class doc explains: flat context-driven weights, no player-attribute tilt; pressure changes turnover rate (Roll A/B/F), not turnover type.

`src/Charm.Engine/Generators/RollCStubPieGenerator.cs` — DELETED.

`src/Charm.Engine/Core/Resolver.cs` — EDIT (surgical, three changes). Field and constructor parameter renamed `RollCStubPieGenerator` → `RollCGenerator`. `pressure: 0.0` removed from the `Generate` call in `ResolveTurnoverType`; comment updated to note the pressure parameter has been retired (Phase 37).

`src/Charm.Engine/Config/RollCConfig.cs` — EDIT (surgical, one removal). `PressureLostBallLiveBallNudge` property and its doc comment removed. The "live wire" language in the surrounding comment block removed. All other properties (15-per-context weights × 3 contexts, 3 elapsed-time properties, `Epsilon`) untouched.

`src/Charm.Harness/Program.cs` — EDIT (five passes). Pass 1: mechanical rename of all 35 `RollCStubPieGenerator` occurrences to `RollCGenerator`. Pass 2: `pressure: 0.0` removed from all four Roll C `Generate` call sites (observability block, `RollCBatchCheck`, `RollCContextCheck` foreach loop, `RollCExpansionCheck` EntryBackcourt call). Pass 3: `PressureSignalCheck` and `RollCLiveStripRate` method definitions deleted; `ok &= PressureSignalCheck(...)` call removed from main wiring block. Pass 4: `RollCBatchCheck` label cleaned — `(pressure=0.00)` removed. Pass 5: `EntryBackcourt` added as third entry in `RollCContextCheck` contexts array, covering the seven non-zero outcomes (BadPassDeadBall, BadPassIntercepted, LostBallDeadBall, LostBallLiveBall, ShotClockViolation, FiveSecondInbound, TenSecondBackcourt).

`src/Charm.Harness/config.json` — EDIT. `PressureLostBallLiveBallNudge` key removed from `RollC` section.

**Key design decisions:**

- **No pressure parameter on the real generator.** The stub's pressure wire was a seam-test placeholder — it proved the generator→roll seam could carry signal, not that pressure should tilt the type-mix. Pressure changes how often a team turns it over (Roll A/B/F). Once you turn it over, it's a bad pass or a lost ball because of what happened on that play, not because of how pressured the ball-handler was. The parameter was always called at 0.0; its removal is a no-op, confirmed by the corpus hash being unchanged (`e48085ff...`).

- **EntryBackcourt is now live in `RollCContextCheck`.** Roll A already stamps `TurnoverContext.EntryBackcourt` when `state.Frontcourt == false` (wired prior sessions). This session activates the corresponding harness sub-check. The seven non-zero EntryBackcourt outcomes are: the four universal types (BadPassDeadBall, BadPassIntercepted, LostBallDeadBall, LostBallLiveBall) plus the three backcourt-only violations (FiveSecondInbound, TenSecondBackcourt, ShotClockViolation). Halfcourt-only types (Travel, ThreeSecondViolation, Carry, OffensiveFoul, OffensiveGoaltending) and BackcourtViolation are zero — you haven't crossed halfcourt yet.

- **`RollCExpansionCheck` required no structural changes.** It already exercised EntryBackcourt in Part 1 (drives `TurnoverContext.EntryBackcourt` directly). The only touch was the type rename and stripping `pressure: 0.0` from call sites.

- **Corpus hash stable.** `config hash: e48085ff2196764bcca5512258050a4beb3609f8af74c767fd5acb8e8b46ec26` — identical to Phase 36. The mathematical no-op prediction held.

**Harness results (all checks passed):**

- `RollCBatchCheck`: Halfcourt rates within tolerance; label now `Roll C` without `pressure=0.00`.
- `RollCContextCheck`: all three contexts passing — Halfcourt, Transition, EntryBackcourt all `selection ok`, all seven EntryBackcourt rates within tolerance.
- `RollCExpansionCheck`: unchanged behavior, all passes.
- `PressureSignalCheck`: gone — no missing-check failures.
- Corpus hash: unchanged.
- ALL CHECKS PASSED.

## Session 71 — Phase 36: BlockerPicker — BLK Attribution On-Walk, Retire Last Harness WeightedDraw (2026-06-19)

**Scope:** Create `BlockerPicker` — a zone-aware weighted draw across all five defenders — stamp it on-walk at every `ShotResult.Blocked` exit, thread `BlkBySlot` (a `SlotGroup`) through `RoutingOutcome` → `Governor` → harness, and retire the last remaining harness `WeightedDraw` (the seed+2 BLK stream). 7 files changed, 1 new.

**What shipped (7 files):**

`src/Charm.Engine/Core/BlockerPicker.cs` — NEW. Static class on the `DefensiveRebounderPicker` structural pattern (populate → weights with floor-of-1 → cumulative walk). Signature: `Pick(PossessionState state, GameState game, MatchupConfig matchupCfg, IRng rng)`. Zone read from `state.ShotType ?? ShotLocation.Rim` — null falls back to Rim (defensive guard; in current routing `ShotType` is never null at a block site because Roll K's PutBack arm forces `ShotLocation.Rim` before re-entering Roll H, and Roll G stamps it on all other paths). Does not read `SelectedSlot` — BLK attribution is a pure all-five-defenders draw; `SelectedSlot` is offense-side shooter attribution and is irrelevant here. Stage 2 weight: `max(1, Matchup.BlockerWeight(zone, player, cfg))`. Throws on empty defense.

`src/Charm.Engine/Core/Matchup.cs` — EDIT. New public static method `BlockerWeight(ShotLocation zone, Player p, MatchupConfig cfg)` appended before the closing brace. A straight weighted sum of six blocking attributes with zone-specific coefficients from config: `BlkRimProtection(zone) * p.RimProtection + BlkPerimeterDefense(zone) * p.PerimeterDefense + BlkPostDefense(zone) * p.PostDefense + BlkHeight(zone) * p.Height + BlkWingspan(zone) * p.Wingspan + BlkVertical(zone) * p.Vertical`. No tanh, no gap function — unlike the shot-block door (`BlockWeight`) which models the contest probability, this is an attribution weight. The floor of 1 is applied by the caller, not here.

`src/Charm.Engine/Config/MatchupConfig.cs` — EDIT. 30 new auto-properties (6 attributes × 5 zones) + 6 switch methods (`BlkRimProtection(zone)`, `BlkPerimeterDefense(zone)`, `BlkPostDefense(zone)`, `BlkHeight(zone)`, `BlkWingspan(zone)`, `BlkVertical(zone)`) + Phase 36 Load invariants (all 30 coefficients must be `>= 0`; no sum-to-one constraint — same convention as `ReboundPhysical`). Properties appended after `StealerPostnessScale`; invariants appended before `return cfg`. Calibration placeholders — shape is what matters: RimProtection and Height lead at Rim/Short; PerimeterDefense leads at Three/Long; Wingspan meaningful everywhere; Mid between the two extremes.

`src/Charm.Engine/Core/Resolver.cs` — EDIT (surgical, five changes). Change 1: `var blkBySlot = new SlotGroup()` declared alongside `var blkCount = 0` (line ~509 area). Change 2: replaced `blkCount++` (and its stale comment "Phase 23: count blocks for harness BLK-credit draws") with the Phase 36 stamp block — `blkCount++; blkBySlot = blkBySlot.WithSlot(BlockerPicker.Pick(shotSt, _game, _matchup, _rng).Number, 1)`. Change 3: stale comment on `BlkCount` in `RoutingOutcome` record updated — removed "harness still issues BLK credits via weighted draw". Change 4: `BlkCount` XML doc updated to describe its role as the reconciliation invariant target. Change 5: `public SlotGroup BlkBySlot { get; init; }` appended to `RoutingOutcome` record; `BlkBySlot = blkBySlot` appended to the Terminal return. `BlkCount` stays — the harness still asserts `BlkBySlot.Total == BlkCount`.

`src/Charm.Engine/Core/Governor.cs` — EDIT (surgical, four sub-edits). `SlotGroup BlkBySlot = default` appended as the last parameter of `PossessionRecord` (after `DefensiveRebounderSlot`). `var possessionBlkBySlot = new SlotGroup()` added alongside `possessionDefensiveRebounderSlot`. `possessionBlkBySlot = outcome.BlkBySlot` threaded in the resolver branch. `possessionBlkBySlot` passed last in `records.Add(...)`.

`src/Charm.Harness/Program.cs` — EDIT (six passes). Pass 1: retired BLK `WeightedDraw` loop; replaced with engine-stamped `BlkBySlot` read (slot loop `s=1..5`, `r.BlkBySlot[s]`). Pass 2: removed `var rng = new Random(seed + 2)` declaration and its 4-line comment block; updated `foulRng` comment to note seed+2 is retired and seed+3 (shooting fouls) is the only remaining harness-side draw. Pass 3: updated stale Phase 25 comment near shooting-foul attribution that still said "BLK — the one remaining post-hoc WeightedDraw". Pass 4: updated `AttributionSanityCheck` NOTE line to state BLK is now engine-stamped. Pass 5: deleted `WeightedDraw` method definition (zero callers confirmed before deletion). Pass 6: `Phase36BlockerCheck` added (7 sub-checks + 2 governor invariants); wired after `Phase35DefensiveReboundCheck`.

`src/Charm.Harness/config.json` — EDIT. 30 new keys in `Matchup` section, mirroring `MatchupConfig` defaults exactly.

**Key design decisions:**

- **`BlkBySlot` is a `SlotGroup`, not `int?`.** BLK can fire multiple times per possession — a putback can be blocked, and a possession can contain multiple field-goal attempts. `StealerSlot` and `DefensiveRebounderSlot` are `int?` because each fires at most once. `BlkBySlot` mirrors `OrbBySlot` for the same reason: it accumulates across all block events in the possession via `WithSlot(slot, 1)` on each fire.

- **`BlockerPicker` does not read `SelectedSlot`.** `SelectedSlot` is offense-side shooter attribution stamped by Roll E. The BLK picker is a pure five-defender weighted draw — the zone (`ShotType`) determines which defensive attributes are weighted most, not which offensive slot took the shot. The prompt's claim that Roll K's PutBack arm nulls `SelectedSlot` is incorrect per live source — PutBack does `state with { ShotType = ShotLocation.Rim }` only; it is the ResetOffense arm that nulls both. On a blocked putback, `SelectedSlot` is therefore not null, but it is still irrelevant to the picker.

- **`BlockerWeight` is a straight weighted sum, not a tanh.** The existing `Matchup.BlockWeight` (the shot-block door in Roll H's generator) models the probability a block occurs given a specific matchup. `Matchup.BlockerWeight` is an attribution weight — given a block has already occurred, which defender is credited. These are distinct quantities and deliberately separate methods.

- **The null-ShotType guard in `BlockerPicker` is a defensive fallback only.** In current routing, `ShotType` is never null at a block site: Roll G stamps it on all non-putback paths; Roll K's PutBack arm forces `ShotLocation.Rim`. The null fallback (→ Rim) is correct if it ever fires — putbacks are forced Rim — but it cannot fire today.

- **`BlkCount` is retained.** The harness still uses it as the reconciliation target: `BlkBySlot.Total == BlkCount` is asserted on every possession in `Phase36BlockerCheck` Invariant A. Removing it would lose the cross-check.

- **This is the last harness `WeightedDraw`.** After Phase 36: ORB moved engine-side in Phase 31; TO committer in Phase 33; STL in Phase 34; DRB in Phase 35; BLK in Phase 36. The seed+2 RNG stream (`var rng = new Random(seed + 2)`) is removed entirely. `foulRng` (seed+3, shooting fouls) is the only remaining harness-side attribution draw.

**Python pre-validation (all 4 checks passed):**

- Zone-aware direction: rim protector (RimProtection=85) share at Rim=24.2%, Three=19.4%; perimeter defender (PerimeterDefense=85) share at Rim=19.1%, Three=24.0%. Correct direction at both poles.
- Wingspan meaningful at all zones: Wingspan=85 beats Wingspan=50 at every zone (Rim through Three). All 5 zones OK.
- Floor holds: guard in dominant-big lineup gets 9.6% at Rim, 20.0% at Three — both > 0.
- Null ShotType fallback: rim_protector weight at Rim=64.0 vs Three=51.0 — Rim fallback correct.

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED.

Phase 36 check (7 sub-checks + 2 governor invariants):
- Sub-check 1: RimProtector(s1)=24.07%, others ~19% — rim protector leads at Rim. [OK]
- Sub-check 2: PerimDefender(s1)=24.16%, others ~19% — perimeter defender leads at Three. [OK]
- Sub-check 3: Wingspan=85 > Wingspan=50 at all five zones (Rim through Three). [OK]
- Sub-check 4: guard floor at Rim=9.13%, Three=15.10% — both > 0. [OK]
- Sub-check 5: same seed → identical sequence. [OK]
- Sub-check 6: empty defense throws. [OK]
- Sub-check 7: null ShotType → Rim fallback; results identical to explicit Rim. [OK]
- Invariant A: all 6 block possessions have BlkBySlot.Total == BlkCount (1:1). [OK]
- Invariant B: all 123 non-block possessions have BlkBySlot default (all zeros). [OK]

Existing BLK reconciliation (`bsBlkTotal == totalBlkCount`) in `AttributionSanityCheck` and stress-test sanity check: still [OK] — now reading from engine-stamped `BlkBySlot` instead of post-hoc draws.

Observation run (1,000 games, frozen corpus): all mechanics OK. Corpus hash shifted as expected — `BlockerPicker` consumes one `_rng` draw per block per possession; all downstream engine draws on those possessions shift. All prior checks passed. Stress test: all 8 buckets passed; cross-bucket patterns stable. BLK rates in the per-player box score are low (Okafor leads home at 0.9/game, others 0.5–0.7) — noted as a calibration observation, not a bug; BLK calibration is deferred until all generators are wired.

## Session 70 — Phase 35: Rebounding — Wingspan in Battle + Attribution, Defensive Rebound Attribution On-Walk (2026-06-19)

**Scope:** Three tightly coupled changes to the rebounding layer. (1) Wingspan added to `ReboundPhysical` — the team-size composite used in the battle's Stage 1 size shift. (2) A shared `ReboundWingspanMultiplier` helper added to `Matchup.cs`, applied to both pickers so within-team wingspan tilts ORB and DRB attribution identically. (3) `DefensiveRebounderPicker` created on the Phase 34 / `StealerPicker` pattern, stamped on-walk at every `DefensiveRebound` terminal — retiring the last post-hoc rebound `WeightedDraw`. 8 files changed, 1 new.

**What shipped (8 files):**

`src/Charm.Engine/Core/Matchup.cs` — EDIT (two changes). Change 1: `ReboundPhysical` extended to add `cfg.ReboundWingspanWeight * p.Wingspan` alongside the existing Strength and Height terms. Change 2: new public static helper `ReboundWingspanMultiplier(double playerWingspan, double lineupMeanWingspan, MatchupConfig cfg)` — returns `1.0 + cfg.ReboundWingspanSwing * Math.Tanh((playerWingspan − lineupMeanWingspan) / cfg.ReboundWingspanScale)`. Both pickers call this shared helper so the within-team wingspan math cannot drift between ORB and DRB attribution.

`src/Charm.Engine/Config/MatchupConfig.cs` — EDIT (surgical, three additions). `ReboundWingspanWeight` (0.5, battle) added alongside `ReboundStrengthWeight` and `ReboundHeightWeight`. `ReboundWingspanSwing` (0.10) and `ReboundWingspanScale` (15.0) added after `ReboundShooterNerf` for the attribution helper. Load invariants: `ReboundWingspanWeight >= 0`; `ReboundWingspanSwing in [0, 1)`; `ReboundWingspanScale > 0`.

`src/Charm.Engine/Core/OffensiveRebounderPicker.cs` — EDIT. Stage 1 now collects `Wingspan` alongside postness; computes `meanWingspan` over the lineup. Stage 2 weight formula extended to `max(1, OffensiveRebounding × PositionalWeight(postness) × ReboundWingspanMultiplier × shooterNerf)`. The wingspan multiplier is computed against the offensive lineup mean — purely within-team, rebounding-specific, not folded into `Postness`.

`src/Charm.Engine/Core/DefensiveRebounderPicker.cs` — NEW. Static class on the exact Phase 34 / `StealerPicker` pattern, reading `state.Defense`. Weight: `max(1, DefensiveRebounding × PositionalWeight(postness) × ReboundWingspanMultiplier)` — the offensive picker's formula minus `shooterNerf` (the defense has no shooter). Same 3-stage structure: populate loop with postness + wingspan → mean → cumulative walk with last-populated fallback. Throws on empty defense.

`src/Charm.Engine/Core/Resolver.cs` — EDIT (surgical, three changes). Change 1: `int? defensiveRebounderSlot = null` local declared alongside `stealerSlot`. Change 2: stamp block in `case Terminal t:` — `if (t.Reason == "DefensiveRebound") defensiveRebounderSlot = DefensiveRebounderPicker.Pick(...)`. Change 3: `DefensiveRebounderSlot = defensiveRebounderSlot` appended to the Terminal `RoutingOutcome` return; `public int? DefensiveRebounderSlot { get; init; }` field appended to `RoutingOutcome`. Stale comments in the `RoutingOutcome` record updated: removed "metadata for harness draws" description of `TurnoverOffSlot`/`TurnoverWasLiveBall`; removed "harness draws from BallHandling" from `TurnoverOffSlot` comment (Phase 33 moved this engine-side); removed "harness issues exactly one STL" from `TurnoverWasLiveBall` comment (Phase 34 moved this engine-side via `StealerSlot`); updated `BlkCount` comment to note BLK is the one remaining harness draw.

`src/Charm.Engine/Core/Governor.cs` — EDIT (surgical, four sub-edits). `int? DefensiveRebounderSlot = null` appended as the last parameter of `PossessionRecord`. `int? possessionDefensiveRebounderSlot = null` local added alongside `possessionStealerSlot`. `possessionDefensiveRebounderSlot = outcome.DefensiveRebounderSlot` threaded in the resolver branch. Last argument of `records.Add(...)` extended with `possessionDefensiveRebounderSlot`.

`src/Charm.Harness/Program.cs` — EDIT (five passes). Pass 1: retired DReb `WeightedDraw` at ~line 5430; replaced with engine-stamped read — `r.DefensiveRebounderSlot ?? throw new InvalidOperationException("Phase 35: DefensiveRebounderSlot null on a defensive-rebound possession — wiring break.")`. Pass 2: updated two stale stream comments (seed+2 block and BLK comment block) from "DReb/BLK remain as harness WeightedDraws" to "only BLK remains". Pass 3: updated stale Phase 31 comment about DReb draw being "out of scope". Pass 4: `Phase10ReboundDoorCheck` — added `wingspan` parameter to `Mk` helper; added sub-check (h) verifying wingspan direction in the battle (longer defense lowers off-share, longer offense raises it). Pass 5: `Phase31RebounderPickerCheck` — added `wingspan` parameter to `MkP` helper; added sub-check 7 verifying wingspan tilt within the offensive picker. `Phase35DefensiveReboundCheck` added (6 sub-checks + 2 governor invariants); wired after `Phase34TurnoverAttributionCheck`. Sub-check 6's initial rank-ordering comparison was replaced mid-session with a robust dominant-slot + within-3% assertion, after identifying that two intentionally identical slots (1 and 5) produced tie-sensitive `OrderByDescending` failures.

`src/Charm.Harness/config.json` — EDIT. Three new keys in `Matchup` section: `ReboundWingspanWeight` (0.5), `ReboundWingspanSwing` (0.10), `ReboundWingspanScale` (15.0).

**Key design decisions:**

- **Wingspan in `ReboundPhysical` changes the battle; `ReboundWingspanMultiplier` changes attribution only.** `ReboundPhysical` is called only in `OffensiveReboundShare`'s Stage 1 (team-vs-team size shift) — confirmed by grep at draft time. Adding wingspan there widens the advantage a long-armed team gets at the team level without touching any attribution picker. The attribution helper is a separate tanh, rebounding-specific by design, so it does not affect `Postness`-dependent callers (turnover pickers, steals).

- **`ReboundWingspanMultiplier` is shared via `Matchup.cs`, not duplicated.** Both pickers call `Matchup.ReboundWingspanMultiplier` so the within-team wingspan math is defined once. This mirrors how `Matchup.Postness` and `Matchup.PositionalWeight` are shared — the two rebound layers (battle and attribution) are provably consistent by construction.

- **`DefensiveRebounderPicker` is the offensive picker minus `shooterNerf`.** Confirmed in adversarial check #5: `StealerPicker.cs` (the closest structural sibling) has no `isShooter`, `SelectedSlot`, or `shooterNerf` concept; the defense has no shooter. The formula is otherwise identical — same `PositionalWeight(postness)` and `ReboundWingspanMultiplier` terms, with `DefensiveRebounding` in place of `OffensiveRebounding`.

- **`DefensiveRebound` terminals route through the same `case Terminal t:` block as all other terminals.** Both Roll I and Roll M set `result` and call `continue`, re-entering the main while-switch. There is exactly one `return new RoutingOutcome(...)` call in `Route()`. Stamping `defensiveRebounderSlot` there fires on every DReb possession from both feeders with no path exceptions — confirmed by source read.

- **`ReboundPhysical` callers confirmed non-overlapping with pickers.** Grep at draft time: `ReboundPhysical` is called only in `OffensiveReboundShare` (lines 427 and 429 of `Matchup.cs`) and referenced in `RollIGenerator` and `RollMGenerator` doc-comments. No picker calls it. Adding wingspan to `ReboundPhysical` changes the battle without double-applying at attribution.

- **Wingspan is rebounding-specific — not added to `Postness`.** `Postness` is called by four consumers: `TurnoverInteriorPicker`, `TurnoverCommitterPicker`, `StealerPicker`, and `OffensiveRebounderPicker`. Adding wingspan to `Postness` would silently change all four. The within-team wingspan tilt lives only in `ReboundWingspanMultiplier`, called exclusively by the two rebound pickers.

- **Sub-check 6 tiebreak issue resolved mid-session (documented).** The initial `SequenceEqual(rank)` comparison failed because slots 1 and 5 are intentionally identical players — their shares are statistically indistinguishable, so `OrderByDescending` can break the tie either way across two independent RNG runs. The fix asserts what the sub-check actually means: the dominant slot (slot 4) leads on both sides, and per-slot shares agree within 3% between the two pickers. This is the correct level of assertion for a tied-player lineup.

**Python pre-validation (all 4 checks passed):**

- Battle direction: neutral off-share 0.3000; longer defense → 0.2925 (< neutral ✓); longer offense → 0.3085 (> neutral ✓).
- Defensive picker direction: C=36.2%, PF=28.4%, SF=15.4%, SG=11.1%, PG=8.9% — bigs dominant, guard floor 8.9% > 1% ✓.
- Wingspan tilt (Swing=0.10, Scale=15.0): short-arm 47.1%, long-arm 52.9% — gap 5.8% (gentle, < 15%) ✓.
- Offensive picker direction unchanged after wingspan factor: C (36.5%) > nerfed-PG-shooter (3.3%) ✓.

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED. (Required two Program.cs deliveries — Phase 35 engine pass, then sub-check 6 tiebreak fix.)

Phase 10 sub-check (h) — wingspan battle direction:
- Neutral off-share matches baseline. Longer defense lowers off-share. Longer offense raises off-share. [OK]

Phase 31 sub-check 7 — wingspan tilt in offensive picker:
- short-arm (wingspan=50) share: 19.59%; long-arm (wingspan=70) share: 21.73%. [OK]

Phase 35 check (6 sub-checks + 2 governor invariants):
- Sub-check 1: C=33.73%, PF=27.50%, SF=16.55%, SG=12.38%, PG=9.83% — bigs favored. [OK]
- Sub-check 2: PG=10.04% > 1% — guard floor holds. [OK]
- Sub-check 3: short-arm 19.60%, long-arm 21.74% — wingspan tilt correct direction. [OK]
- Sub-check 4: same seed → identical sequence. [OK]
- Sub-check 5: empty defense throws. [OK]
- Sub-check 6: dominant slot leads on both sides; ORB/DRB shares agree within 3%. [OK]
- Invariant A: all 46 DefensiveRebound possessions have non-null DefensiveRebounderSlot (1:1). [OK]
- Invariant B: all 86 non-DReb possessions have null DefensiveRebounderSlot. [OK]

Observation run (1,000 games, frozen corpus): config hash `50cd44d7...`. All mechanics OK. Corpus hash shifted as expected — three simultaneous stream changes: (1) `ReboundPhysical` with wingspan shifts which team wins boards; (2) `OffensiveRebounderPicker` consumes a new mean-wingspan computation internally; (3) `DefensiveRebounderPicker` consumes one engine RNG draw per DReb possession. All prior checks passed. Stress test: all 8 buckets passed; cross-bucket patterns stable.

## Session 69 — Phase 34: Turnover Attribution Completion — Type-Aware Committer Dispatch + StealerPicker (2026-06-19)

**Scope:** Two pieces completing the turnover accounting chapter. Piece 1: type-aware committer dispatch — replaced the Phase 33 uniform `TurnoverCommitterPicker` call with a three-branch resolver dispatch that correctly handles all 15 turnover reasons. Piece 2: `StealerPicker` on-walk — moved STL credit from a harness `WeightedDraw` into the engine, stamped on `RoutingOutcome.StealerSlot`, threaded through `PossessionRecord`, read as exact attribution in the harness. A third pass updated two stale harness checks whose assumptions the Phase 34 changes invalidated. 7 files changed, 2 new.

**What shipped (7 files):**

`src/Charm.Engine/Core/TurnoverInteriorPicker.cs` — NEW. Static class, structurally identical to `TurnoverCommitterPicker`, handling ThreeSecondViolation, OffensiveGoaltending, and OffensiveFoul — events where post players are disproportionately likely committers. Weight formula: `max(1, Strength × interiorMult)` where `interiorMult = TurnoverInteriorGuardFloor + (1 − GuardFloor) × ((raw + 1) / 2)`. The `(raw + 1) / 2` (not inverted) is the key difference from `TurnoverCommitterPicker` — a post (raw > 0) gets mult near 1.0 while a guard (raw < 0) gets mult near `TurnoverInteriorGuardFloor`. Same 3-stage structure and one-draw contract. Throws on empty offense.

`src/Charm.Engine/Core/StealerPicker.cs` — NEW. Static class, reads the **defensive** lineup (`state.Defense`). Base attribute: `Steals`. Direction: same perimeter-favored formula as `TurnoverCommitterPicker` — `mult = StealerPostFloor + (1 − PostFloor) × (1 − (raw + 1) / 2)` — guards favored, posts suppressed. The only differences from `TurnoverCommitterPicker` are the defensive side, the `Steals` base attribute, and the config keys (`StealerPostFloor`, `StealerPostnessScale`). Same 3-stage structure and one-draw contract. Throws on empty defense.

`src/Charm.Engine/Core/Resolver.cs` — EDIT (surgical, three changes). Change 1: replaced the Phase 33 uniform 15-reason `if` block with a three-branch dispatch — team violations (`FiveSecondInbound`, `TenSecondBackcourt`, `ShotClockViolation`) set `turnoverWasLiveBall = false` and leave `turnoverOffSlot` null; interior violations + offensive foul route to `TurnoverInteriorPicker`; ball-handler violations route to `TurnoverCommitterPicker` unchanged. Change 2: added `int? stealerSlot = null` local variable and a `StealerPicker.Pick(...)` call inside the ball-handler branch on the `turnoverWasLiveBall = true` paths. Change 3: appended `StealerSlot { get; init; }` property to `RoutingOutcome` and added `StealerSlot = stealerSlot` to the Terminal return.

`src/Charm.Engine/Core/Governor.cs` — EDIT (surgical, four sub-edits). Appended `int? StealerSlot = null` as the last parameter of `PossessionRecord`; added `int? possessionStealerSlot = null` local accumulator; threaded `possessionStealerSlot = outcome.StealerSlot`; passed `possessionStealerSlot` as the last argument in `records.Add(...)`.

`src/Charm.Engine/Config/MatchupConfig.cs` — EDIT (surgical). Appended Phase 34 property block: `TurnoverInteriorGuardFloor` (0.10), `TurnoverInteriorPostnessScale` (40.0), `StealerPostFloor` (0.10), `StealerPostnessScale` (40.0). Load invariants added for all four.

`src/Charm.Harness/Program.cs` — EDIT (three passes). Pass 1 (Phase 34 engine): retired Phase 33 throw on null `TurnoverOffSlot`, replaced with null-safe pattern; retired STL `WeightedDraw`, replaced with engine-stamped `StealerSlot` read with throw on null; wired `Phase34TurnoverAttributionCheck`; updated two stale seed+2 stream comments. `Phase34TurnoverAttributionCheck` method added (9 sub-checks + 3 governor invariants). `WeightedDraw` not deleted — DReb and BLK remain as harness draws. Pass 2/3 (harness fixes after first run): updated Phase 33 invariant to exclude team violations from null-slot check (they're correctly null — Phase 34 Invariant A already verifies them); updated Phase 23 attribution check to compare per-player TOs against `totalToPoss − totalTeamViolToPoss` in both the observation run and sanity check paths.

`src/Charm.Harness/config.json` — EDIT. Four new keys in `Matchup` section: `TurnoverInteriorGuardFloor` (0.10), `TurnoverInteriorPostnessScale` (40.0), `StealerPostFloor` (0.10), `StealerPostnessScale` (40.0).

**Key design decisions:**

- **Type-aware dispatch with three branches, not one.** Phase 33 treated all 15 turnover reasons identically. Phase 34 separates them: team violations get no individual credit (the team committed this), interior violations and offensive fouls tilt toward posts, ball-handler violations stay with Phase 33's handling-weighted logic. The separation lives in the Resolver dispatch block — neither picker knows about reasons; the caller decides which picker to invoke.

- **Team violations are still turnovers; they just get no per-player credit.** `IsTurnoverPossession` returns true for all 15 reasons including the three team types. Aggregate TO counts are unaffected. Only the per-player attribution skips team violations. This required updating two harness checks (Phase 33 invariant, Phase 23 attribution check) that previously assumed all TOs carry individual credit.

- **Interior picker inverts the tanh direction, not a separate formula.** The multiplier formula is identical to `TurnoverCommitterPicker` except `(raw + 1) / 2` replaces `1 − (raw + 1) / 2`. One sign flip is the entire difference between "posts suppressed" and "posts favored." Both pickers read the same `Matchup.Postness` coefficients with no new postness math.

- **StealerPicker reads the defensive lineup.** This is the first attribution picker that works on the defense side rather than the offense side. The `state.Defense` field is the correct lookup; `game.LineupFor(state.Defense)` and `game.RosterFor(state.Defense)` return the defensive lineup and roster. The guard-favored formula direction is the same as `TurnoverCommitterPicker` — guards defend ball-handlers and generate steals — applied to the defensive side's postness distribution.

- **`stealerSlot` is inside the ball-handler branch, not a shared accumulator.** Because `turnoverWasLiveBall` can only be true on `BadPassIntercepted` and `LostBallLiveBall`, the `StealerPicker.Pick(...)` call lives immediately after the `turnoverWasLiveBall` assignment inside the ball-handler branch. It is never called on team or interior violation paths. `stealerSlot` is initialized to null and remains null on all non-live-ball possessions — Invariant C asserts this in the governor run.

- **RNG stream shift is documented and expected (A5).** `StealerPicker` moving on-walk consumes one `_rng.NextUnitInterval()` draw per live-ball turnover possession. Every downstream engine draw on those possessions shifts — the same documented consequence as Phase 31 and Phase 33. The config hash is unchanged (`421d916...`); same-seed reproducibility within Phase 34 holds.

**Python pre-validation (mandatory Step 0 — all 5 checks passed):**

Interior picker: C=0.382 > PG=0.090 (posts dominant); PF=0.274 > SF=0.153 ✓; PG=0.090 > 0 (floor holds) ✓.
Stealer picker: PG=0.341 > C=0.048 (guards dominant); SG=0.296 > PF=0.103 ✓; C=0.048 > 0 (floor holds) ✓; PG=0.341 > SF=0.213 (Steals tilt within perimeter) ✓.

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED. (Required two Program.cs deliveries — engine pass, then harness-check fixes.)

Phase 34 check (9 sub-checks + 3 governor invariants):
- Interior Sub-check 1: C=38.03%, PF=27.42%, SF=15.28%, PG=9.10% — posts favored. [OK]
- Interior Sub-check 2: PG=8.97% > 0.05 — floor holds. [OK]
- Interior Sub-checks 3–4: reproducibility, empty-offense throw. [OK]
- Stealer Sub-check 5: PG=34.01% > C=4.77% — guards favored. [OK]
- Stealer Sub-check 6: C=4.77% > 0.02 — floor holds. [OK]
- Stealer Sub-check 7: PG=34.15% > SF=21.20% — Steals tilt within perimeter. [OK]
- Stealer Sub-checks 8–9: reproducibility, empty-defense throw. [OK]
- Invariant A: 3 team-violation possessions have null TurnoverOffSlot. [OK]
- Invariant B: 15 live-ball turnover possessions have non-null StealerSlot. [OK]
- Invariant C: 116 non-live-ball possessions have null StealerSlot. [OK]

Phase 33 invariant (updated): TurnoverOffSlot non-null on all 27 individual-turnover possessions; 3 team violations correctly null. [OK]
Phase 23 attribution check: per-player TO 21653 == individual-TO possessions 21653 (team violations 2766 correctly unattributed). [OK]

Observation run (1,000 games, frozen corpus): config hash `421d9161...`. All mechanics OK. Stream shift from Phase 34 means corpus output differs from Phase 33 for same seeds — expected. All prior checks passed. Stress test: all 8 buckets passed; cross-bucket patterns stable and unchanged.

## Session 68 — Phase 33: Roll C Session 1 — Turnover Committer Picker (2026-06-19)

**Scope:** Promoted the pre-selection turnover-committer attribution from a post-hoc harness `WeightedDraw` into an engine-side `TurnoverCommitterPicker`, stamped onto `RoutingOutcome.TurnoverOffSlot` during the possession walk. Mirrors the Phase 31 move exactly: an attribution that was a harness draw becomes part of the engine, computed once, on the walk. The picker decides which offensive player committed a turnover on the paths where Roll E had not yet selected a player (Roll A entry/bring-up and Roll B halfcourt-initiation turnovers). The post-selection path (Roll F, where `SelectedSlot` is already non-null) is unchanged — no RNG draw, direct credit stands. 5 files changed, 1 new.

**What shipped (5 files):**

`src/Charm.Engine/Core/TurnoverCommitterPicker.cs` — NEW. Static class, mirroring `OffensiveRebounderPicker` exactly in structure. Signature: `Pick(PossessionState state, GameState game, MatchupConfig matchupCfg, IRng rng)`. Weight per populated offensive slot: `max(1, BallHandling × perimeterMult(postness, lineupMeanPostness))`. The perimeter multiplier slides from ~1.0 for low-postness (guard) toward `TurnoverCommitterPostFloor` for high-postness (post) via the same `Matchup.Postness` and tanh formula used across all matchup math — no new postness logic, only a new consumer mapping it the opposite direction (posts suppressed, not favored). Same 3-stage structure: populate loop → mean → cumulative walk with last-populated fallback. Throws on empty offense lineup.

`src/Charm.Engine/Core/Resolver.cs` — EDIT (surgical). In the Phase 23 TO stamp block, replaced the single-line direct credit with a null-coalescing call: `turnoverOffSlot = t.State.SelectedSlot?.Number ?? TurnoverCommitterPicker.Pick(t.State, _game, _matchup, _rng).Number`. Post-selection turnovers (SelectedSlot non-null) credit that slot with no draw; pre-selection turnovers consume one `_rng` draw. Placement is inside the turnover-reason branch only, so non-turnover possessions are unaffected.

`src/Charm.Engine/Config/MatchupConfig.cs` — EDIT (surgical). Appended Phase 33 property block after Phase 32 putback section: `TurnoverCommitterPostFloor` (0.10), `TurnoverCommitterPostnessScale` (40.0). Load invariants added: `PostFloor` in (0, 1]; `PostnessScale > 0`.

`src/Charm.Harness/Program.cs` — EDIT. Retired the post-hoc TO `WeightedDraw` fallback — replaced with a throw on null `TurnoverOffSlot` (the post-condition the engine now guarantees). `Phase33TurnoverCommitterCheck` added (8 sub-checks + invariant governor run); wired via `ok &= Phase33TurnoverCommitterCheck(configPath)` after the Phase 32 call. `WeightedDraw` not deleted — 3 surviving callers remain (STL, DReb, BLK).

`src/Charm.Harness/config.json` — EDIT. Two new keys in `Matchup` section: `TurnoverCommitterPostFloor` (0.10), `TurnoverCommitterPostnessScale` (40.0).

**Key design decisions:**

- **Static class, not `I...Generator` interface.** The committer picker is not a swappable generator — it is an attribution helper with a fixed formula, like `OffensiveRebounderPicker` and `DefenderPicker`. No interface created. A future reason-aware committer model (illegal-screen → screener, Roll C Session 2 / Roll D foul-attribution work) drops in adjacent to this picker without touching it.

- **Pre-selection only; post-selection path unchanged.** The `SelectedSlot?.Number ?? Pick(...)` null-coalescing pattern is the correct seam. When `SelectedSlot` is non-null (Roll F path), no draw is consumed and the stream is unaffected. When `SelectedSlot` is null (Roll A / Roll B feeders), one `_rng` draw is consumed. Roll A and Roll B are confirmed to reach `ResolveTurnoverType` without passing through Roll E, so `SelectedSlot` is always null on those paths.

- **RNG stream shift is documented and expected (A2).** The draw is consumed from the engine's `_rng`, not from the harness's separate `new Random(seed + 2)`. Moving the pick on-walk therefore shifts every downstream draw on pre-selection turnover possessions — the same documented stream shift Phase 31 produced when `OffensiveRebounderPicker` moved on-walk. The corpus hash changed (`936d978d...`); same-seed reproducibility within Phase 33 holds.

- **Perimeter multiplier maps postness the opposite direction from `PositionalWeight`.** `Matchup.PositionalWeight` gives posts a weight above 1.0 (for rebounding, posts dominate). For turnovers the direction inverts: `raw = tanh((postness − mean) / Scale)`, `mult = PostFloor + (1 − PostFloor) × (1 − (raw + 1) / 2)`. A guard (raw < 0) gets mult near 1.0; a post (raw > 0) gets mult near `PostFloor`. The floor of 1 on the full weight ensures a maximally-suppressed post with high BallHandling still has a positive draw probability.

- **`TurnoverOffSlot` is now a universal post-condition.** Every turnover possession exits the engine with a non-null `TurnoverOffSlot`. The harness fallback is retired and replaced with a loud throw, mirroring the Phase 31 `OrbBySlot.Total == OrbWon` invariant. The invariant is asserted in a controlled governor run as a harness sub-check.

**Python pre-validation (mandatory Step 0 — all 7 checks passed):**

1. Posts suppressed: C share < PG share AND PF share < SF share. PG=0.336, SG=0.290, SF=0.206, PF=0.108, C=0.059. ✓
2. Combo guards split evenly: G1/G2 ratio = 1.041 (within 0.85–1.20). ✓
3. Perimeter floor: SF=0.206 > 0.10. ✓
4. Post suppressed but non-zero: C=0.059 in (0, 0.10). ✓
5. Handling tilt within perimeter: PG=0.336 > SF=0.206. ✓
6. Floor of 1 holds: zero-BH weight = 1.000. ✓
7. Same-seed pick reproducible (→ PG). ✓

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED.

Phase 33 check (8 sub-checks + invariant):
- Posts suppressed (C < PG, PF < SF): PG=33.58%, SG=29.08%, SF=20.50%, PF=10.97%, C=5.88%. [OK]
- Combo guards split evenly: G1=31.60%, G2=30.33%, ratio=1.042. [OK]
- Perimeter floor (SF > 10%): 20.50%. [OK]
- Post suppressed but non-zero (0 < C < 10%): 5.88%. [OK]
- Handling tilt within perimeter (PG > SF): 33.58% > 20.50%. [OK]
- Floor of 1 (BH=0 slot still receives draws): 0.83%, no throw. [OK]
- Reproducibility (same seed → same pick sequence). [OK]
- Null-roster throw. [OK]
- Invariant: TurnoverOffSlot non-null on all 23 turnover possessions (of 133 total). [OK]

Observation run (1,000 games, frozen corpus): all mechanics OK. New corpus hash `936d978d...` — expected A2 stream shift. All prior checks passed. Stress test: all 8 buckets passed; cross-bucket patterns stable.

## Session 67 — Phase 32: Roll K Real Generator — Putback Attempt Rate (2026-06-19)

**Scope:** Replaced `RollKStubPieGenerator` with a real, attribute-driven `RollKGenerator`. The generator is the first consumer of `PossessionState.ReboundSlot` (stamped by Phase 31) and tilts the `PutBack`/`ResetOffense` mass split based on the rebounder's physical profile versus the defensive team's interior deterrence, with a per-zone modifier scaling putback weight down for perimeter misses. The five minority arms (`DefensiveFoul`, `OffensiveFoul`, `DeadBallTurnover`, `LiveBallTurnover`, `JumpBall`) stay flat at config. Follows the standard interface + retype pattern. 8 files changed, 2 new.

**What shipped (8 files):**

`src/Charm.Engine/Generators/IRollKPieGenerator.cs` — NEW. Two-arg interface mirroring `IRollIPieGenerator`: `Generate(PossessionState state, OffensiveReboundSource source)`. `state` provides `ReboundSlot` and `ShotType`; `source` selects LiveBall or FreeThrow base weight set. Null `ReboundSlot` → flat config fallback; null `ShotType` → zone modifier 1.0.

`src/Charm.Engine/Generators/RollKGenerator.cs` — NEW. Ctor: `(RollKConfig cfg, MatchupConfig matchup, GameState game)`. Offense composite: `offScore = Σ(PutbackOff*Weight × rebounder attribute)` over Strength, Height, Athleticism (computed property), Finishing. Defense composite: self-weighted team mean where each defender's interior score is its own weight, so elite rim protectors dominate non-linearly. Gap → shift via `Matchup.GapFn` (shared ReferenceScale). Shift → bend via `tanh(shift / PutbackReferenceShift)` — the `PutbackReferenceShift` is the tanh denominator only, not the GapFn scale. Zone modifier applied after the tanh bend; result clamped to `[PutbackFloor, PutbackCeiling]`. Null-rebounder fallback (DEC-6 equivalent) returns flat config pie. Runtime overflow guard: throws if `finalPutback + flatTotal >= 1.0`.

`src/Charm.Engine/Generators/RollKStubPieGenerator.cs` — EDIT. Added `: IRollKPieGenerator`; widened `Generate` to `Generate(PossessionState state, OffensiveReboundSource source)`. State accepted but ignored; behavior unchanged.

`src/Charm.Engine/Core/Resolver.cs` — EDIT. Three surgical changes: `_rollKGenerator` field retyped `RollKStubPieGenerator` → `IRollKPieGenerator`; ctor param retyped same; dispatch call updated to `_rollKGenerator.Generate(reboundState31, c.OffensiveReboundSource ?? OffensiveReboundSource.LiveBall)` — passes the state that already carries `ReboundSlot` from the Phase 31 picker.

`src/Charm.Engine/Config/MatchupConfig.cs` — EDIT. Appended Phase 32 property block: 4 offense composite weights, 3 defense composite weights, `PutbackReferenceShift`, 5 zone modifiers. Load invariants added: `PutbackReferenceShift > 0`; all five zone modifiers `> 0`.

`src/Charm.Engine/Config/RollKConfig.cs` — EDIT. Appended `PutbackFloor` (0.15) and `PutbackCeiling` (0.70). Load invariants added: floor/ceiling validity; startup overflow guards confirming `PutbackCeiling + flat-arm-sum < 1.0` for both LiveBall and FreeThrow source modes. Load method restructured from single-expression return to guarded block.

`src/Charm.Harness/Program.cs` — EDIT. Live generator swapped to `new RollKGenerator(cfgK, cfgMatchup, game)` after `SeatStartersFromConfig` (alongside other real-generator constructions). Two method signatures widened: `RollKReboundBatchCheck` and `RollKBonusForkCheck` — `RollKStubPieGenerator genK` → `IRollKPieGenerator genK`. Three `genK.Generate(src)` call sites updated to `genK.Generate(state, src)` in `RollKReboundBatchCheck` and `RollMContextSelectionCheck`. `Phase32PutbackAttemptRateCheck` added (8 sub-checks); wired via `ok &= Phase32PutbackAttemptRateCheck(configPath)`.

`src/Charm.Harness/config.json` — EDIT. 13 new keys in `Matchup` section (offense weights ×4, defense weights ×3, `PutbackReferenceShift`, zone modifiers ×5). 2 new keys in `RollK` section (`PutbackFloor`, `PutbackCeiling`).

**Key design decisions:**

- **`PutbackReferenceShift` is the tanh denominator only — not the GapFn scale.** GapFn uses the shared `ReferenceScale` (25.0, same as every other matchup door). `PutbackReferenceShift` (20.0) is used only in `tanh(shift / PutbackReferenceShift)` to control how quickly the tilt saturates toward the floor/ceiling. This mirrors the existing block/foul pattern in `Matchup.cs` and is what makes the C# match the Python pre-validation exactly.

- **Self-weighted defense composite.** Each defender's interior score (`RimProtection × 0.55 + Height × 0.25 + Strength × 0.20`) is used as its own weight in the team mean: `defScore = Σ(score²) / Σ(score)`. A single elite rim protector dominates the team score non-linearly. Five identical all-50 defenders produce a team score equal to a single all-50 defender's score — neutral reference point.

- **Null `ReboundSlot` short-circuits to flat config.** If Phase 31 didn't stamp a rebounder (or the slot is unpopulated), the generator returns the same flat-weight pie as the stub. This is the DEC-6 equivalent for this generator — correctness without crashing on uninitialized state.

- **Zone modifier applies after the tanh bend, before the clamp.** `finalPutback = Clamp(adjustedPutback × zoneMod, floor, ceiling)`. The zone modifier can push an offense-dominant matchup below the natural adjusted value, or pull an already-high value up to the ceiling — the clamp is the safety net.

- **FreeThrow source uses `cfg.FreeThrowPutBack` as its base.** The `basePutback` variable branches on source before the tanh calculation, so the same formula runs for both source modes with different starting points. The five flat arms are also source-selected from config. Null fallback returns the appropriate flat-source pie.

**Python pre-validation (mandatory Step 0 — all 8 checks passed):**

1. Offense dominant (big vs weak guards, Short): 0.6812 > 0.40 baseline. ✓
2. Defense dominant (guard vs elite rim protectors, Short): 0.2740 < 0.40 baseline. ✓
3. Neutral at Short: 0.4000 ≈ 0.40 (|diff| < 0.01). ✓
4. Neutral at Rim > baseline: 0.4400 > 0.40 (rim-board boost). ✓
5. Zone modifier: Three=0.2610 < Rim=0.5743 for same matchup. ✓
6. Self-weighted defense dominates simple mean (55.23 > 34.00 for one-elite-rest-weak). ✓
7. Null ShotType (FT board): 0.5221, in valid range. ✓
8. Null-rebounder fallback confirmed by design; generator short-circuits before formula. ✓

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED.

Phase 32 check (8 sub-checks):
- Offense dominant (Short): PutBack=0.6814 > baseline 0.4000. [OK]
- Defense dominant (Short): PutBack=0.2681 < baseline 0.4000. [OK]
- Neutral at Short: PutBack=0.4000 ≈ baseline (|diff|<0.02). [OK]
- Neutral at Rim > baseline: 0.4400 > 0.4000, ≤ ceiling 0.7000. [OK]
- Zone modifier: Three=0.2616 < Rim=0.5755. [OK]
- Null ShotType (FT board): 0.6814 in [0.1500, 0.7000]. [OK]
- Null ReboundSlot fallback: LiveBall=0.4000 exactly, FreeThrow=0.5500 exactly. [OK]
- Flat arms sum=0.130000 constant across all zones. [OK]

Observation run (1,000 games, frozen corpus): all mechanics OK; config hash updated. Prior per-player box score columns unchanged. Stress test: all 8 buckets passed; Athletic over Skill advantage (72.8%) confirmed symmetric across mirror buckets (gap 0.2%) — side neutrality holding.

## Session 66 — Phase 31: Offensive Rebounder Picker (2026-06-19)


**Scope:** The first in-possession picker: engine-side attribution of which offensive player secured the offensive rebound, conditional on Roll I having already awarded the board to the offense. Previously ORB credit in the box score was assigned via a post-hoc `WeightedDraw` in the harness. Phase 31 stamps the result onto `PossessionState.ReboundSlot` (a new nullable `Slot?` field, separate from `SelectedSlot`) and threads `OrbBySlot` (a `SlotGroup`) from the resolver walk out through `RoutingOutcome` → `PossessionRecord` → harness attribution. Six files changed: one new (`OffensiveRebounderPicker.cs`), five modified.

**What shipped (6 files):**

`src/Charm.Engine/Core/OffensiveRebounderPicker.cs` — NEW. Static class with `Pick(GameState game, PossessionState state, IRng rng, MatchupConfig matchup, RollHConfig cfgH)`. Weights each populated offensive slot by `max(1, OffensiveRebounding × PositionalWeight(Postness) × shooterNerf)`, where `shooterNerf = ReboundShooterNerf (0.35)` when the candidate's slot matches the shooter AND `ShotType` is `Three`, `Long`, or `Mid`; no nerf on `Rim`/`Short`; no nerf when `ShotType` is null (bonus FT boards where no shooter was selected). Uses the existing `Matchup.Postness` and `Matchup.PositionalWeight` public static methods — no new matchup math, only a new consumer. Returns the winning `Slot` (offense side).

`src/Charm.Engine/Core/PossessionState.cs` — `Slot? ReboundSlot = null` appended as the last positional parameter. Default null; stamped by the picker at `ResolveOffensiveRebound`; cleared to null by Roll K's `ResetOffense` wipe list.

`src/Charm.Engine/Core/Resolver.cs` — `OrbBySlot` (`SlotGroup`) property added to `RoutingOutcome` with empty-group default; `orbBySlot` local accumulator initialized in `Route()`; picker called at `case ContinuationKind.ResolveOffensiveRebound:` (between Roll I's award and Roll K's execution), result stamped onto state via `state = state with { ReboundSlot = ... }` and accumulated into `orbBySlot`; `OrbBySlot = orbBySlot.ToReadOnly()` included in the Terminal return.

`src/Charm.Engine/Rolls/RollK.cs` — `ReboundSlot = null` added to `ResetOffense`'s blank-slate `with` expression. Surgical one-liner alongside the existing wipe list.

`src/Charm.Engine/Core/Governor.cs` — `OrbBySlot` added to `PossessionRecord` as a trailing field; threaded from a local `var orbBySlot` in `Run()` that accumulates from `outcome.OrbBySlot` across the walk and is passed positionally to `records.Add(...)`.

`src/Charm.Harness/Program.cs` — The post-hoc `WeightedDraw` ORB block in `AttributeGame` replaced with per-slot reads from `r.OrbBySlot`; `Phase31RebounderPickerCheck` added (6 sub-checks + invariant governor run); wired via `ok &= Phase31RebounderPickerCheck(configPath)`. Also added `SeedMinimalRoster(GameState g)` private static helper (5 identical all-50 players per side) called after bare `GameState` constructions across ~24 sites that route through the full resolver — see key design decisions below.

**Key design decisions:**

- **Option A (conditional-within-side) chosen over Option B (unified 10-player contest).** Option B would require tearing down Roll I's working team-math and rebuilding it as a per-player contest. Option A adds the picker strictly downstream of Roll I's team verdict: Roll I decides the team; the picker decides the individual within that team. The offense-vs-defense split is never re-litigated.

- **`ReboundSlot` is separate from `SelectedSlot`.** Who shot and who rebounded are two distinct per-possession facts. Both must persist for downstream consumers; they live as independent nullable fields on `PossessionState`.

- **Known limitation documented: within-side share inflation.** Because the picker distributes probability across the 5 offensive players rather than all 10, a weak rebounder's share (~3%) exceeds its true OR% contribution (~1%). A full Option B unified contest is the named future fix; deferred as a calibration task, not a structural blocker.

- **`ReboundSlot` has no consumer yet in Roll K.** Phase 32 is the first consumer: it tilts putback share toward the player who secured the board. The field rides through the current chain harmlessly.

- **OrbBySlot invariant.** `OrbBySlot.Total == OrbWon` is asserted in the harness invariant governor run — every offensive rebound credits exactly one player. Confirmed on all 12 possessions with ORB > 0 in the controlled 132-possession run.

- **SeedMinimalRoster harness fix pattern (new standing rule for future sessions).** Phase 31 is the first resolver-walk code that reads from the game's roster. Any harness check that creates a bare `GameState` and routes through the full resolver can now reach `ResolveOffensiveRebound` and crash on a null roster. Fix: `SeedMinimalRoster(GameState g)` is called immediately after bare `GameState` construction at those sites. Four categories must NOT receive `SeedMinimalRoster`: (1) `RollHResolutionBatchCheck` — bound to a bare game so `PlayerAt()` returns null and the generator falls back to stub/baseline rates; adding seeding shifts make% outside tolerance; (2) `AttributionSanityCheck`, the `ObservationRun` game loop, and `Phase25ShootingFoulAttributionCheck` loop — these do their own `SetStarter` seeding and conflict; (3) `StressTestArchetypeRosters` game loop — uses `SeatRoster()` which wraps `SetStarter`; (4) Phase 31's invariant sub-check uses `govGame` as the variable name to avoid global-replace collision. **Standing rule: any new harness check creating a bare `GameState` passed to a full resolver must add `SeedMinimalRoster(game)` right after construction.**

**Python pre-validation (mandatory Step 0 — all checks passed):**

Simulation with a 5-player offensive lineup (dominant big OR=95, three weak guards OR=20, moderate big OR=60) at `SCALE=40`, `shooterNerf=0.35`:

1. Direction: dominant slot wins 70.3%, each weak guard ~7.4%. Ratio 9.47× — exceeds the 3× threshold. ✓
2. Neutral (all-50, no nerf): each slot exactly 20.0%. ✓
3. Buried guard: slot-5 guard share 3.5% at extreme scenario. Under the 6% bound. ✓
4. Shooter nerf: shooter-big share drops 24.8% → 10.3% on Three. Nerf firing correctly. ✓
5. Floor: all weights ≥ 1 by construction; no zero-weight slots. ✓
6. Reproducibility: 10,000 draws — no throws; same-seed draws byte-identical. ✓

**Calibration note:** At `SCALE=40` the interior anchor captures ~58% of rim residual probability. The prompt draft estimated ~37% (computed with `SCALE≈100`). Flagged as a calibration item for the tune-magnitudes pass; consistent with the wire-the-form mandate and not a blocker.

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED.

Phase 31 check (6 sub-checks):
- Direction: 9.35× multiplier (dominant big ~68%, each weak guard ~7.2%). Threshold 3× cleared. [OK]
- Neutral: each slot exactly 20.0% at all-50 no-nerf. [OK]
- Buried guard: slot-5 share 2.01% at extreme scenario. Under 6% bound. [OK]
- Shooter nerf: slot-5 non-shooter big drops 35.78% → 16.36% on Three zone. Nerf firing correctly. [OK]
- Floor/throw: no zero-weight slots; no throws across 1,000 controlled draws. [OK]
- Reproducibility: two identical draws from same seed produce byte-identical results. [OK]

Invariant governor run (132 possessions, 12 with ORB > 0): `OrbBySlot.Total == OrbWon` on all 12. [OK]

Observation run (1,000 games, frozen corpus):
- ORB% 27.64% (unchanged — picker changes WHO gets credit, not the rate). ✓
- Per-player ORB/g: Okafor (big, slot 4) 2.8 vs Webb (guard, slot 1) 0.6 — 4.7× gap. Picker working correctly.
- Phase 24 OReb directional (Anchor vs perimeter roles): 22–25× ratio. [OK]
- Aggregate stats (PPP 1.0999, pace 132.3, FG% 52.05%) unchanged vs Phase 30 — picker consumes one extra RNG draw per ORB but does not alter rates.

Stress test: Athletic beats Skill 72.8–73.0% (both mirror directions); mirror gap 0.2%. STRESS TEST PASSED.

Notable stress test data: Shooting loses to Athletic 22.6% (Athletic 75%). Shooting team takes 35% threes at only 21% rim attempts; Athletic team gets 54% of shots at the rim — perimeter volume cannot overcome rim dominance at current config. EliteVsWeak: 100-0 across 500 games (clean extreme separation). StarVsBalanced: star team wins 58.2% (star effect real and measurable). Mirror gap across Buckets 7/8: 0.2% (side-neutrality excellent). Interior players (PostScorer, RimRunner) commit 1.3–2.0 SFL/g; perimeter players 0.7–1.0 — correct Phase 25 pattern still holding.

**Frozen corpus note:** RNG stream shifted from Phase 30 (one extra draw per ORB event). Aggregate distributions are identical — same probabilities, different stream. This is the expected behavior when a picker is inserted downstream of the rate decision.

## Session 65 — Phase 30: ShotSelectionBias + PaceBias (Coaching Layer 2) (2026-06-18)

**Scope:** Two new `CoachProfile` fields — `ShotSelectionBias` (1–10) and `PaceBias` (1–10) — wired into the engine. `ShotSelectionBias` fills in the `CoachingPull.Apply` seam so Roll G's shot-zone distribution reflects the coach's offensive system. `PaceBias` wires into two existing seams: Roll J's transition-frequency modifier and the Governor's possession-length draw. Nine files changed. One latent bug in Roll J's modifier application fixed as a consequence of testing.

**What shipped (9 files):**

`src/Charm.Engine/Core/CoachProfile.cs` — REPLACED. Two new properties added after `HeliocentricBias`: `ShotSelectionBias` and `PaceBias` (both double, 1.0–10.0, default 5.0). Constructor expanded to three parameters (all defaulting to 5.0) with a shared private `ValidateBias` helper to avoid repeated validation blocks. Class-level doc updated: deferred seams section revised to remove `ShotSelectionBias` and `PaceBias` (now live), retain `FreelanceDial` (still undesigned). All existing `new CoachProfile()` and `new CoachProfile(bias)` call sites compile unchanged — defaults carry forward.

`src/Charm.Engine/Core/CoachingPull.cs` — REPLACED (was identity stub). Real nudge math wired. `nudge = (ShotSelectionBias − 5.0) / 5.0`, mapping [1,10] → [−0.8, +1.0]. Inside zones (Rim, Short): multiplied by `(1 − nudge × 0.40)`. Outside zones (Long, Three): multiplied by `(1 + nudge × 0.40)`. Mid: neutral zone, unchanged. Floor clamp of 1.0 per zone. Null-coach fallback = 5.0 = identity. Returns raw adjusted tendencies (not normalized) — normalization is `RollGGenerator`'s responsibility. Malleability still null/1.0 (deferred seam, documented for next session).

`src/Charm.Engine/Generators/RollGGenerator.cs` — Surgical: two doc-comment updates (`identity in v1` → `live in Phase 30`) and the `CoachingPull.Apply` call site updated to pass the real offensive coach (`_game.CoachFor(state.Offense)`) instead of `coach: null`. One new local (`offCoach`). No other changes.

`src/Charm.Engine/Generators/RollJGenerator.cs` — Two changes. (1) Class-level doc updated: pace modifier described as live (was "config-only seam / future coaching session"). (2) Pace block replaced: `rawPaceBias` reads `_game.CoachFor(ctx.OffenseSide.Value).PaceBias` when `OffenseSide` is stamped, falls back to `_cfg.TeamPaceBias + 5.0` (→ neutral lift = 0) when null. Mapped pace `= (rawPaceBias − 5.0) / 5.0`; `paceLift = mappedPace × PaceScale`. **Bug fix also in this file:** the original modifier-application block subtracted the full `paceLift + athlLift` from Settle regardless of whether Push had clamped — when `FreeThrowPush=0.08` minus a slow-pace lift of 0.09 went negative and clamped to 0, the pie summed to 1.01 and threw `PieValidationException`. Fix: compute `actualDelta = clampedPush − basePush`, then subtract `actualDelta` from Settle (not the raw lift). All five Roll J source pies now sum to exactly 1.0 at any bias value. This was a latent bug that only surfaced when PaceBias pushed the lift large enough to floor the FreeThrow source's Push weight.

`src/Charm.Engine/Core/Governor.cs` — `DrawPossessionSeconds` gains a `TeamSide offense` parameter. Reads `_game.CoachFor(offense).PaceBias`, computes `paceAdj = (5.0 − PaceBias) / 5.0 × PaceCenterScale`, shifts the draw center: `center = max(Floor + 1.0, Center + paceAdj)`. Fast coach (bias > 5) → negative adj → shorter possessions. Slow coach (bias < 5) → positive adj → longer possessions. Neutral (5.0) → zero adj → center unchanged. Single call site at Governor line 339 updated to pass `state.Offense`.

`src/Charm.Engine/Config/RollClockConfig.cs` — `PaceCenterScale` (double, default 1.5, invariant `>= 0`) added after `ResetClockSeconds`. Load method restructured: null-coalesce throws first, then `PaceCenterScale < 0` throws, then `return cfg`. Class-level doc updated: "future seam" note replaced with "live in Phase 30." Center property doc updated to reflect per-possession shift. [CALIBRATION PLACEHOLDER]

`src/Charm.Engine/Config/RollJConfig.cs` — Comment block for `TeamPaceBias` and `PaceScale` updated: "future coaching session replaces this" language removed; now accurately describes the knob as a signed fallback used only when `OffenseSide` is null.

`src/Charm.Harness/Program.cs` — `Phase30CoachingLayer2Check` added with four sub-cases. Wired into main pass via `ok &= Phase30CoachingLayer2Check(configPath)`. Two field-name bugs fixed en route: `PossessionRecord.ElapsedSeconds` (does not exist) → `PossessionRecord.Elapsed`.

`src/Charm.Harness/config.json` — `"PaceCenterScale": 1.5` added to the `Clock` section.

**Key design decisions:**

- **Nudge formula is intentionally asymmetric.** `(bias − 5.0) / 5.0` maps bias 1 → −0.8 and bias 10 → +1.0. The inside-system ceiling (−0.8) is slightly less than the outside-system ceiling (+1.0). Accepted: the asymmetry is small and 5 is the neutral point regardless. A symmetric mapping would require changing the formula; the asymmetry was flagged in the prompt and carried forward deliberately.

- **Mid is a neutral zone at every bias.** Only Rim/Short and Long/Three move. A coach cannot redistribute volume to or from the mid-range zone via `ShotSelectionBias` — that would require a separate mid-bias axis. This simplification avoids the five-zone coherence problem that's explicitly deferred.

- **Player identity is preserved at extreme bias.** The floor clamp (1.0 per zone) ensures no zone is fully suppressed. The Shaq test (Rim=80, Three=10, bias=10) confirms rim (48.0) stays dominant over three (14.0) even in a maximum outside system. The Korver test (Three=80, Rim=10, bias=1) confirms three (54.4) stays dominant over rim (13.2) even in a maximum inside system.

- **`CoachingPull.Apply` does not normalize.** It returns raw adjusted tendency values. `RollGGenerator` owns normalization — inserting it here would interfere with the matchup multipliers that run after the coaching nudge.

- **`_cfg.TeamPaceBias` role changed but not removed.** It is now a signed fallback scalar used only when `TransitionContext.OffenseSide` is null (isolated harness checks without a stamped game context). Its comment block was updated to reflect this. `PaceScale` is unchanged.

- **Roll J clamp-asymmetry bug.** The pre-existing modifier block clamped Push to 0 but still subtracted the full intended lift from Settle. When FreeThrowPush (0.08) plus a slow-pace lift (−0.09) went negative and floored at 0, Settle received the full +0.09 boost, producing a pie summing to 1.01. The fix uses `actualDelta = clampedPush − basePush` to drive Settle — only the amount Push actually changed is mirrored. This bug was latent before Phase 30; pace lifts from the config-only `TeamPaceBias = 0.0` never exposed it.

**APL timing gap (deferred — documented here for the record):**

`ElapsedSeconds` is set on exactly three terminal outcomes: `ShotClockViolation`, `FiveSecondInbound`, and `TenSecondBackcourt` (all in Roll C). Every other terminal — turnovers, made baskets, fouls, OOB, jump balls — returns `ElapsedSeconds = null`, causing the Governor to fall back to `DrawPossessionSeconds` (full shot-clock distribution, center ~17s). Early-exit possessions (turnovers on Roll A/B, press-break fouls) receive a full-clock time draw instead of the short one they actually consumed. This is the primary driver of APL running ~18s vs the real D1 average of ~14–15s. Not a wiring bug — the seam is correct and the machinery works. Fix deferred to after all rolls are wired, when the time-draw model can be designed and tuned once against a stable possession chain.

**Python pre-validation (mandatory Step 0 hard gate — all 7 checks passed):**
1. Neutral identity: `ShotSelectionBias=5.0` → nudge=0.0 → all five return values equal authored inputs exactly.
2. Nudge math at bias 1/5/10 with equal tendencies (20 each): bias 5 → identity; bias 1 → rim/short=26.4, three/long=13.6, mid=20; bias 10 → three/long=28.0, rim/short=12.0, mid=20.
3. Shaq test (Rim=80, Three=10, bias=10): rim=48.0 > three=14.0 (player identity preserved).
4. Korver test (Three=80, Rim=10, bias=1): three=54.4 > rim=13.2 (player identity preserved).
5. Floor clamp (ThreeTendency=1, bias=1): raw=0.68 → clamped to 1.0.
6. PaceBias → PaceLift at bias 1/5/10 (PaceScale=0.15): bias 5 → lift=0.0; bias 10 → lift=0.15; bias 1 → lift=−0.12.
7. Clock center adjustment at bias 1/5/10 (Center=17.0, PaceCenterScale=1.5): bias 5 → center=17.0; bias 10 → center=15.5; bias 1 → center=18.2.

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED.

Phase 30 signals in `Phase30CoachingLayer2Check`:
- Neutral regression [1a–1d]: defaults confirmed, identity confirmed, Roll J null-side Push matches stamped neutral Push, Governor paceAdj at bias 5 = 0 exactly.
- ShotSelectionBias direction [2a–2f]: bias 5 identity confirmed; bias 1 boosts rim (66.0 > 50) and suppresses three (27.2 < 40), mid unchanged (20==20); bias 10 boosts three (56.0 > 40) and suppresses rim (30.0 < 50); Shaq test passes; Korver test passes; floor clamp confirmed (1.0000).
- PaceBias Roll J direction [3a–3c]: fast coach (bias 8) Push 0.390 > slow coach (bias 2) Push 0.210; fast Settle 0.510 < slow Settle 0.690; null OffenseSide no crash.
- PaceBias APL direction [4a]: slow coach (bias 2) mean APL 18.898s > fast coach (bias 8) mean APL 17.266s — 1.6s spread at PaceCenterScale=1.5.

Frozen corpus observation run (1,000 games) output **identical to Phase 29** — all frozen corpus players use `new CoachProfile()` (all three fields = 5.0 = neutral), zero behavior change anywhere.
## Session 64 — Phase 29 Session 1: Player Hierarchy + Heliocentric Bias in Roll E (2026-06-18)

**Scope:** First coaching layer. Each player carries an authored `HierarchyRank` (1–10); each team's coach carries a `HeliocentricBias` (1–10); `RollEGenerator` blends these with the existing attribute-based usage scores to produce a usage pie that reflects both who the players are and how the coach wants to use them. Seven files changed.

**What shipped (7 files):**

`src/Charm.Engine/Core/CoachProfile.cs` — REPLACED (was fieldless scaffold). `HeliocentricBias` (double, 1.0–10.0, default 5.0) added as the first real field. Validated in the constructor (not a compact-record constructor — standard constructor with explicit property assignment, chosen after the compact-syntax form caused a compiler error on the target runtime). Future coaching fields (`ShotSelectionBias`, `FreelanceDial`, `PaceBias`) are deferred seams — absent from the record deliberately; no code, no comment stubs.

`src/Charm.Engine/Core/Player.cs` — `HierarchyRank` (int, range [1,10], default 5) added after `PlayerId`. Default 5 produces weight 1.0 at any heliocentric exponent — the regression anchor. Usage-time guard in `RollEGenerator` validates the range and throws `InvalidOperationException` on an authored value outside [1,10]. The max-3-per-number authoring constraint is not enforced here — that belongs to the future roster authoring layer.

`src/Charm.Engine/Core/GameState.cs` — `HomeCoach` and `AwayCoach` (`CoachProfile`, both initialized to `new CoachProfile()` inline) added, mirroring the `HomeRoster`/`AwayRoster` pattern: constructed in the body, not as constructor parameters. All 59 existing `new GameState(...)` sites compile unchanged. `SetCoach(TeamSide, CoachProfile)` mutator added (mirrors `SetPossessionArrow`); `CoachFor(TeamSide)` helper added (mirrors `RosterFor`).

`src/Charm.Engine/Config/RollEConfig.cs` — Two new knobs: `HierarchyExponentNeutral` (double, default 1.0, invariant `>= 0`) and `HierarchyExponentMax` (double, default 2.0, invariant `>= Neutral`). Load-time validation added. Conservative defaults per the pre-validation flag that rank-10 players can approach the rail at bias 9.

`src/Charm.Engine/Generators/RollEGenerator.cs` — Hierarchy blend block inserted in `GenerateWithPressure` AFTER the `MinUsageScore` floor (`rawScores[i] = Math.Max(score, _cfg.MinUsageScore)`), BEFORE the `UsageExponent` sharpening pass. The block reads `_game.CoachFor(state.Offense).HeliocentricBias`, derives the `hierarchyExponent` via piecewise-linear interpolation, and multiplies each slot's raw score by `(HierarchyRank / 5.0)^hierarchyExponent`. FastBreak passthrough returns before this code and is unaffected. Note: hierarchy may push a post-MinUsageScore raw score downward for low-ranked players — intentional; the floor/rail machinery is the participation protection. `MinUsageScore` is NOT reapplied after the multiply.

`src/Charm.Harness/Program.cs` — `StampPlayerId` updated to carry `HierarchyRank = p.HierarchyRank` (gap fix: the function manually copies every init property; omitting the new one silently assigned rank 0 to all stamped players, which would have thrown the RollEGenerator guard). `Phase29HierarchyBiasCheck` added with five sub-cases (direction at standard bias, heliocentric amplification, egalitarian compression, pairwise regression anchor, attention directional). Wired into main pass via `ok &= Phase29HierarchyBiasCheck(configPath)`. The direction sub-case uses `>=` for floor-pinned slots rather than strict `>` — rank-4 and rank-2 players with identical attributes legitimately collapse to the usage floor together, which is correct engine behavior, not a bug.

`src/Charm.Harness/config.json` — `HierarchyExponentNeutral: 1.0` and `HierarchyExponentMax: 2.0` added to the `RollE` section.

**Key design decisions:**

- **`HeliocentricBias = 5.0` is not hierarchy-off.** It is standard authored-hierarchy expression: rank 10 gets 2× the weight of rank 5; rank 1 gets 0.2×. `HeliocentricBias = 1.0` is hierarchy-off / egalitarian — the exponent collapses to 0 and all weights become 1.0 regardless of authored rank. The regression anchor (frozen corpus output identical to Phase 28) comes from all existing players defaulting to `HierarchyRank = 5`, which produces weight 1.0 at any exponent — not from the bias value.

- **Piecewise-linear exponent interpolation.** Bias [1,5] maps to exponent [0, `HierarchyExponentNeutral`]; bias [5,10] maps to exponent [`HierarchyExponentNeutral`, `HierarchyExponentMax`]. Monotone and continuous through bias = 5 — no discontinuity. At bias 1: exponent 0 → all weights 1.0 (attributes only). At bias 5: exponent 1.0 → standard expression. At bias 10: exponent 2.0 → full heliocentric.

- **Hierarchy feeds attention intentionally.** `BendByAttention` takes `gen.FinalShares` — shares that already have hierarchy baked in. Higher FinalShare for a rank-10 player → higher AttentionShare → selection tilt and pressure interactions respond. A coach feeding the star more possessions will also draw more defensive attention to that player. Correct basketball behavior, not a bug.

- **`StampPlayerId` gap was a required fix, not scope expansion.** Without it, every stamped player (frozen corpus, stress test) would have `HierarchyRank = 0`, triggering the `InvalidOperationException` guard in `RollEGenerator` on the first possession. The fix is one line in an existing function.

- **Compact record constructor syntax rejected.** The build prompt specified `public CoachProfile { ... }` (compact constructor). This caused multiple compiler errors on the target runtime. Replaced with a standard constructor (`public CoachProfile(double heliocentricBias = 5.0)`) with explicit property assignment — identical behavior, unambiguous syntax.

**Pre-validation flag (from Python mandatory gate):** At bias 9.0 with a high-attribute rank-10 player in a diverse lineup, the rank-10 player's share can brush the rail (0.52). The prompt's magnitude note anticipated this: `HierarchyExponentMax = 2.0` is the conservative starting point; it may want to come down once real lineups with realistic rank distributions are exercised. Not a blocker — the rail is doing its job; the calibration pass owns the magnitude.

**Python pre-validation (mandatory Step 0 hard gate — all 6 checks passed):**
1. Exponent mapping bias 1→10: monotone, continuous through bias 5, no discontinuity.
2. Weight anchors: bias 5, rank 5 → 1.0; rank 10 → 2.0; rank 1 → 0.2. Bias 1: all ranks → 1.0.
3. Full pipeline with representative 5-player lineup (ranks 10/8/6/4/2): rank-10 does not hit rail at bias 5; approaches rail at bias 9 (flagged, not a blocker).
4. Regression: all rank-5, any bias → weights all 1.0 → shares identical across all bias values.
5. Egalitarian: bias 1 with any ranks == bias 5 with all rank-5 (same shares, attributes only).
6. MinUsageScore vs floor/rail: rank-1 player's post-floor raw score pushed downward by hierarchy multiply at high bias; floor/rail machinery (not MinUsageScore) provides the final participation floor.

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED.

Phase 29 signals in `HierarchyBiasCheck`:
- Direction at bias 5.0 (ranks 10/8/6/4/2, identical attributes): shares decrease monotonically; rank-4 and rank-2 both floor-pinned at 0.09 (correct — floor is the participation protection).
- Heliocentric amplification (bias 9.0): rank-10 share meaningfully higher than at bias 5.0; rank-10–rank-2 gap widened (0.3200 → 0.4142).
- Egalitarian compression (bias 1.0, mixed ranks): shares identical to all-rank-5/bias-5 to 6 decimal places.
- Pairwise regression anchor: all-rank-5/bias-5 = all-rank-5/bias-1 to 6 decimal places.
- Attention directional: rank-10 FinalShare 0.5000 > rank-5 FinalShare 0.1250; rank-10 AttentionShare 0.2747 > rank-5 AttentionShare 0.1813.

Frozen corpus observation run (1,000 games) output **identical to Phase 28** — all frozen corpus players default to `HierarchyRank = 5`, all games use the default `HeliocentricBias = 5.0`, hierarchy weights all 1.0, zero behavior change.
## Session 63 — Phase 28: Attention-Location Tilt + Steal-Origin Split + Roll J Real Generator (2026-06-18)

**Scope:** Three scopes in one session. Roll G attention-location tilt (amplifier inside `ApplyDietShift`), steal-origin split wiring (`TransitionContext` extension + three steal-site updates), and Roll J real generator (`IRollJPieGenerator` interface + `RollJGenerator` with two independent modifiers). Thirteen files changed.

**What shipped (13 files):**

`src/Charm.Engine/Generators/IRollJPieGenerator.cs` — NEW. The generation contract for Roll J's run-or-not pie. Single per-call input: `Generate(TransitionContext)`. All game context (config, lineups, matchup) injected through the implementing class's constructor, mirroring `RollGGenerator`. Two implementations: `RollJStubPieGenerator` (existing, now implements the interface) and `RollJGenerator` (new, Phase 28).

`src/Charm.Engine/Core/TransitionContext.cs` — Rewritten. Adds `StealOrigin` enum (`BackcourtVictim`, `FrontcourtVictim`) and two new optional fields on the record: `Origin?` (the steal-origin split field, Phase 28) and `OffenseSide?` (the new offense team identity, stamped by all three transition helpers). The static `Steal` shorthand remains as a null-origin fallback for legacy/test tickets. Full design rationale in comments.

`src/Charm.Engine/Core/RollResult.cs` — `TransitionStealTo` signature changed from `(TeamSide team)` to `(TeamSide team, StealOrigin origin)`. All three transition helpers (`TransitionStealTo`, `TransitionReboundTo`, `TransitionFreeThrowReboundTo`) now stamp `OffenseSide = team` on the ticket so the real generator can compute the directional athleticism gap without adding `TeamSide` to the `Generate` interface.

`src/Charm.Engine/Rolls/RollC.cs` — Both live steal arms updated. `BadPassIntercepted` and `LostBallLiveBall` now call `TransitionStealTo(state.Defense, state.Frontcourt ? StealOrigin.FrontcourtVictim : StealOrigin.BackcourtVictim)`. The role-flip: `Frontcourt == false` (victim still in backcourt) → thief near basket → `BackcourtVictim` (high run); `Frontcourt == true` (victim in halfcourt set) → thief goes full court → `FrontcourtVictim` (low run).

`src/Charm.Engine/Rolls/RollK.cs` — `LiveBallTurnover` updated to `TransitionStealTo(state.Defense, StealOrigin.FrontcourtVictim)`. Fixed stamp, not a ternary: proven by source that `state.Frontcourt == true` when Roll K fires (offense had already crossed halfcourt, shot, and rebounded before losing the ball live — a putback-traffic turnover is not a pick-six).

`src/Charm.Engine/Config/RollGConfig.cs` — `AttentionShiftAmplifier` added (default `1.0`, load invariant `>= 0`). Zero = ablation mode; positive = attention tilt active. Calibration placeholder.

`src/Charm.Engine/Generators/RollGGenerator.cs` — Attention-location tilt wired inside `ApplyDietShift`. Insertion point: AFTER the `requestedShift = pressure × PressureShiftScale` line, BEFORE the `intrinsicCapacity` cap. Formula: `requestedShift *= (1 + max(0, ShooterAttentionShare − 0.20) × AttentionShiftAmplifier)`. Uses `const double EqualShare = 0.20` matching Roll H's C1/C3 neutral point. Bonus-only: attention at or below equal share leaves the shift unchanged (regression anchor). A one-trick player's amplified request hits the `intrinsicCapacity` cap, leaving location stable while spilling the excess to residual — the A4 invariant confirmed in harness.

`src/Charm.Engine/Config/RollJConfig.cs` — Three new weight sets appended. `BackcourtVictim*` (five weights, Push=0.55) and `FrontcourtVictim*` (five weights, Push=0.35) replace the single `Steal*` set for classified steal tickets; old `Steal*` set remains as null-origin fallback. Three modifier knobs added: `TeamPaceBias` (default 0.0, neutral), `PaceScale` (0.15 placeholder), `AthleticismGapScale` (0.001 placeholder). Load-time direction invariants: `BackcourtVictimPush > FrontcourtVictimPush` and `FrontcourtVictimPush >= Push` (Rebound baseline).

`src/Charm.Engine/Generators/RollJGenerator.cs` — NEW. Real generator implementing `IRollJPieGenerator`. Source selection: Rebound → existing weights; FreeThrowRebound → existing weights; Steal → `BackcourtVictim*`, `FrontcourtVictim*`, or null-origin `Steal*` fallback by `ctx.Origin`. Two INDEPENDENT modifiers applied to Push/Settle (Turnover/DefFoul/JumpBall fixed): `PaceLift = TeamPaceBias × PaceScale`; `AthlLift = (offenseFiveAthl − defenseFiveAthl) × AthleticismGapScale` where team athleticism is the mean derived `Athleticism` of the active five, read via `ctx.OffenseSide` (null → gap = 0, regression anchor). Modifiers additive, never pre-fused. Constructor: `(RollJConfig, MatchupConfig, GameState)`.

`src/Charm.Engine/Generators/RollJStubPieGenerator.cs` — Adds `: IRollJPieGenerator` to the class declaration. No behavioral change.

`src/Charm.Engine/Core/Resolver.cs` — Field and constructor parameter changed from `RollJStubPieGenerator` to `IRollJPieGenerator`.

`src/Charm.Harness/Program.cs` — 24 `RollJStubPieGenerator` construction sites changed to `new RollJGenerator(cfg, matchup, game)`. Two function parameter type declarations changed to `IRollJPieGenerator`. `RollMContextSelectionCheck` gains `IRollJPieGenerator genJ` parameter (local stub construction removed); jContexts array extended with `BackcourtVictim` and `FrontcourtVictim` exact-weight entries; Phase 28 direction check added: `BC.Push > FC.Push >= Rebound.Push`. One site corrected post-build (`game` → `game2` in the Phase 15 isolated scope).

`src/Charm.Harness/config.json` — `AttentionShiftAmplifier: 1.0` added to `RollG` section. Twelve new keys added to `RollJ` section: both steal-split weight sets, `TeamPaceBias`, `PaceScale`, `AthleticismGapScale`.

**Key design decisions:**

- **`OffenseSide?` on `TransitionContext` (second optional append).** The real generator needs to know which team is the new offense to compute the directional athleticism gap — but the `Generate(TransitionContext)` interface must remain unchanged (no per-call `TeamSide` parameter). The ticket already grows by append; `OffenseSide` is the second such field alongside `Origin`. Stamped by all three transition helpers where the new offense is already known (`team` parameter). Null on hand-constructed test tickets → gap = 0, preserving the regression anchor on every isolated harness check.

- **`FrontcourtVictim` fixed for Roll K (not a ternary).** Source audit proved `state.Frontcourt == true` on every Roll K `LiveBallTurnover` path (the offense had already crossed halfcourt, attempted a shot, and rebounded before losing the ball live). A putback-traffic turnover is not a pick-six. Using the ternary from Roll C here would be technically safe but semantically wrong — the ball is always in the frontcourt at Roll K.

- **Attention amplifier placed before the `intrinsicCapacity` cap, not after.** This is the load-bearing insertion point. Placing it after the cap would mean a one-trick player's location shifts more under high attention — wrong behavior. Placing it before means the amplified request hits the same cap and spills to residual — location stable, make% drops. Confirmed by harness: Phase 17 specialist residual reads exactly 0.0600 (= intrinsicCapacity = 1 − 0.94).

- **`IRollJPieGenerator` interface created (not a concrete-type swap).** All real generators in this codebase implement interfaces; the stub and real generator are interchangeable through the interface. Resolver and every harness construction site hold the interface. The concrete-type-swap alternative was rejected as non-idiomatic.

- **Null-origin `Steal*` fallback preserved.** The old `Steal*` weight set in config is retained as the null-origin fallback for the `TransitionContext.Steal` static shorthand. This lets existing isolated harness checks (`RollJBatchCheck`, `RollJStealBatchCheck`) continue working against `TransitionContext.Steal` (null origin) without modification — regression-anchored at the weight level.

**Python pre-check (mandatory hard gate — Step 0):**

35 checks across three scopes, 0 failures. Scope 1: neutral anchor (EqualShare attention = no-attn identical), directional shift (stretch-4 confirmed), one-trick residual monotone and location constant, zero-pressure gate, bonus-only amplifier. Scope 2: role-flip direction, Roll K FrontcourtVictim proven by source, both steal origins ≥ Rebound baseline. Scope 3: neutral regression anchors on Rebound/FTRebound, pace lift across all sources, athleticism-gap directional, modifiers additive (not pre-fused), pie valid across modifier range.

**Harness output — key results:**

ALL CHECKS PASSED. STRESS TEST PASSED.

Phase 28 signals visible in output:

- **Attention amplifier confirmed (Phase 17 check b):** Specialist residual = 0.0600 exactly = intrinsicCapacity (1 − 0.94). Amplified requested shift hits the cap; excess routes to residual. Residual-penalty attribution = 12.0pts vs vol-tax = 2.5pts — residual channel is now the dominant efficiency cost for a one-trick specialist under high defensive attention. Regression anchors (zero-pressure, FastBreak exemption) hold byte-for-byte.

- **Steal-split direction confirmed (`RollMContextSelectionCheck`):** BackcourtVictim (55%) > FrontcourtVictim (35%) ≥ Rebound (30%) at the weight level. Three steal sites compiled and ran without issue.

- **Athleticism-gap modifier visible in stress test:** AthleticVsSkill bucket: Athletic Tr%=39.9% vs Skill Tr%=33.4%. SkillVsAthletic (mirrored): Athletic Tr%=39.7% vs Skill Tr%=33.9%. Win rates: 73.8% / 73.6%, mirror gap 0.2%. More-athletic offense runs more, directional and side-neutral. Pace knob at neutral (0.0) — all signal is from athleticism gap.

## Session 62 — Phase 27 Session 2: Selection Tilt + Passing Converter (2026-06-18)

**Scope:** Second and final session of the gravity/spacing/attention layer. Selection tilt and passing converter only — shot location (Roll G) deferred as planned. Fourteen files changed.

**What shipped (14 files):**

`src/Charm.Engine/Config/RollEConfig.cs` — two new tilt knobs: `MaxTiltMultiplier` (1.5 placeholder) and `TiltReferenceShift` (0.08 placeholder). Both loaded and invariant-validated in `Load`.

`src/Charm.Engine/Config/AttentionConfig.cs` — eight new conversion knobs: `IqMin`, `IqMax`, `ConversionFloor`, `DirectPassingScale`, `ActivationScale`, `PlaymakingDecay`, `OpportunityFloor`, `MaxPassingBonus`. Loaded via `JsonSerializer.Deserialize` (auto-map); invariants for `PlaymakingDecay ∈ (0,1]`, `OpportunityFloor ∈ [0,1)`, `MaxPassingBonus ∈ (0,1]` added to `Load`.

`src/Charm.Engine/Config/RollHConfig.cs` — two new passing converter knobs: `PassingOpportunityFloor` (0.10 placeholder) and `MaxPassingBonus` (0.08 placeholder). Invariants added to `Load`.

`src/Charm.Engine/Generators/AttentionGenerator.cs` — three changes: (1) `PassingAmp` constant removed — `TeamBaseOpenness` now stamps the PURE gravity×spacing value; (2) conversion quality block added before the `return`, computing `PlaymakingActivation` (top-down geometric decay across five players), `PassingCompound` (flat mean of Passing/100), and `conversionQuality = ConversionFloor + DirectPassingScale × PassingCompound + ActivationScale × PlaymakingActivation × PassingCompound`; (3) `AttentionResult` record gains fifth field `TeamConversionQuality`.

`src/Charm.Engine/Generators/IRollEGenerationProvider.cs` — `BendByAttention(RollEGeneration gen, double[] attentionShares)` added to the interface.

`src/Charm.Engine/Generators/RollEGenerator.cs` — `BendByAttention` implemented: computes per-slot gap (`FinalShares[i] − attentionShares[i]`), applies bounded multiplier (`exp(log(MaxTiltMultiplier) × tanh(gap / TiltReferenceShift))`), normalizes, re-applies floor/rail using TILTED weights as the redistribution basis (not the original `expScores` — critical: using originals would partially undo the tilt). Placed between `GenerateWithPressure` and `Generate`.

`src/Charm.Engine/Generators/RollEStubPieGenerator.cs` — `BendByAttention` passthrough added: returns `gen.Pie` unchanged. Correct for isolated harness checks that don't wire the full attention path.

`src/Charm.Engine/Generators/RollHGenerator.cs` — new C4 passing bonus block after C3: `PassingBonus = MaxPassingBonus × conversionQuality × opportunityGate` where `opportunityGate = PassingOpportunityFloor + (1 − PassingOpportunityFloor) × teamOpenness`. Bonus-only (`Math.Max(0.0)`), halfcourt + non-putback only (putback short-circuit at line 81 precedes; explicit `!state.FastBreak` guard added). Reads `state.TeamConversionQuality ?? 0.0`.

`src/Charm.Engine/Core/PossessionState.cs` — `TeamConversionQuality` nullable trailing field added. Same lifecycle as `TeamBaseOpenness` — null until Roll E runs, cleared by Roll K's `ResetOffense`.

`src/Charm.Engine/Rolls/RollE.cs` — `teamConversionQuality` parameter added to `Execute`; stamped in the atomic `with` block alongside `TeamBaseOpenness`.

`src/Charm.Engine/Rolls/RollK.cs` — `TeamConversionQuality = null` added to `ResetOffense` blank-slate `with`.

`src/Charm.Engine/Core/Resolver.cs` — halfcourt Roll E site: `BendByAttention` called between `Generate` and `RollE.Execute`; tilted pie passed to Execute; pre-tilt pressures passed unchanged (tilt changes which slot is rolled, not the pressure each slot carries). FastBreak site: `breakGenE.Pie` passed directly (untilted); `TeamConversionQuality` added to both call sites. One-pass dependency maintained throughout.

`src/Charm.Harness/Program.cs` — `RollESpyGenerator.BendByAttention` passthrough added (compile fix); all 13 direct `RollE.Execute` harness calls updated with `0.0` for `teamConversionQuality`.

`src/Charm.Harness/config.json` — tilt knobs in `"RollE"` section; conversion knobs in `"Attention"` section; passing knobs in `"RollH"` section.

**Key design decisions:**

- **PassingAmp removed from TeamBaseOpenness (bug fix from v1–v3 of the prompt).** Folding passing into the openness field caused the passing bonus to vanish whenever the defense played evenly (C1 relief = 0 at equal-share attention). The fix: `TeamBaseOpenness` reverts to the pure gravity×spacing value C1 reads; the conversion bonus lives in a fully separate Roll H block (C4) that fires regardless of attention allocation.

- **Passing CONVERTS, not GENERATES.** The converter rewards lineups that can exploit the gravity/spacing advantage already created — it does not create the advantage. Hence the opportunity gate: `opportunityGate = lerp(OpportunityFloor, 1.0, TeamBaseOpenness)`. At near-zero openness the bonus collapses to a small floor; it grows as the gravity/spacing engine creates more to exploit. Elite passers behind a dominant rim threat with four shooters get the largest payoff.

- **Direct passing term prevents vanishing at zero activation.** `conversionQuality = ConversionFloor + DirectPassingScale × PassingCompound + ActivationScale × PlaymakingActivation × PassingCompound`. The direct term lifts make% modestly even when `PlaymakingActivation ≈ 0` (a lineup of great passers who can't collapse a defense through the perimeter/post routes). Without it, Passing is only useful through the activation gate — the v4 wording fix that became a code fix.

- **Tilt re-constrains on TILTED weights.** `BendByAttention` calls `ApplyFloorAndRail(tilted, tilted, ...)` — the tilted shares serve as both the input AND the redistribution basis. Using the original `expScores` would pull redistributed mass back toward pre-tilt proportions and partially undo the tilt. Python gate test 1a confirmed the neutral anchor (usage == attention → zero drift); test 1d confirmed floor/rail hold under extreme gaps.

- **Pre-tilt pressures passed unchanged.** The selection tilt changes WHICH slot gets the ball; it does not change the volume load each slot was already carrying into this possession. Pressures (which feed the C3 penalty) are computed pre-tilt by `GenerateWithPressure` and passed to `RollE.Execute` as-is.

- **One-pass feedback-loop guard honored.** Attention is computed once from the pre-tilt `FinalShares` and never recomputed from the tilted result. The guard recorded in Session 1's design section is confirmed closed here.

- **Engineering call: tilt knobs in RollEConfig.** Selection-shaping knobs all live in one place (`UsageFloor`, `UsageRail`, `UsageExponent`, now `MaxTiltMultiplier`, `TiltReferenceShift`). AttentionConfig stays focused on how the defense allocates attention, not how the offense reacts to it.

- **Engineering call: single `TeamConversionQuality` field.** Roll H only needs the scalar to compute the bonus; the per-component decomposition (PlaymakingActivation, PassingCompound) lives in the generator and is not surfaced on PossessionState. A future attribution session can add components if needed without tearing down the field.

- **Discrepancy flagged at check-in:** prompt said 16 harness + 2 Resolver = 18 call sites; actual count was 13 + 2 = 15. Stale-reference sweep covered all 15. No effect on correctness.

**Python pre-check (mandatory hard gate — Step 0):**

All 17 checks passed across both gates:
- Gate 1 (selection tilt): neutral anchor (0 drift at usage == attention), directional shift, floor/rail satisfied post-tilt, extreme-gap feasibility, multiplier bounds `(1/1.5, 1.5)`, at-zero identity.
- Gate 2 (passing converter): attention-independence at equal share, opportunity gating (high > low; floor > 0), direct passing term lift with near-zero activation, five-vs-two partial lift, no playmaking excess penalty (monotone), bonus-only guarantee, ceiling at `MaxPassingBonus`.

**Harness output — key results:**

All checks passed. STRESS TEST PASSED.

Selection tilt visible in Bucket 5 (StarVsBalanced): star slot usage 35.0% vs 9.9–20.7% for supporting cast — tilt pushing under-attended high-usage slot higher, as intended.

ShootingVsAthletic win rate held at ~74% athletic — passing converter did not reward the pure-spacing team enough to overcome their gravity deficit. Correct behavior: spacing without gravity generates near-floor-level bonus.

Frozen corpus PPP 1.097 (slight lift vs Session 1 from passing bonus). All prior regression anchors (Phase 17, Phase 24, Phase 25) passed.

## Session 61 — Phase 27 Session 1: Defensive Attention Pie + Gravity/Spacing Rework + Roll H Make% (2026-06-18)

**Scope:** First session of the two-session gravity/spacing/attention layer. Make% only — no selection, no location, no live passing. Six files changed in the engine; harness updated throughout.

**What shipped (10 files):**

`src/Charm.Engine/Core/Player.cs` — reworked `GravityContribution` and `SpacingContribution`:

- `GravityContribution` → bounded [0,100] rim-pressure composite: `PerimeterAccess = avg(FirstStep, SelfCreation, Speed)`, `PostAccess = avg(PostMoves, Strength)`, `Access = max(PerimeterAccess, PostAccess) + 0.10 × min(...)` (bounded versatility bonus), `GravityContribution = 0.35×Finishing + 0.25×Close + 0.30×Access + 0.10×Mid`. Replaces the wrong `(Close + Mid + Outside + Finishing) / 4` which gave gravity weight to perimeter shooting.
- `SpacingContribution` → `0.85×Outside + 0.15×Mid`, no artificial floor. Replaces pure `Outside`. The competency floor lives in player generation, not here.

`src/Charm.Engine/Config/AttentionConfig.cs` — new config file. Gravity/usage blend weights, attention floor, sigmoid gate parameters (`GravitySigmoidCenter`, `GravitySigmoidSteepness`, `SecondGravityFraction`), openness interaction parameters (`GravityAloneYield`, `SpacingMultiplier`). Loaded from `"Attention"` section of config.json. Mirrors `RollEConfig.Load` pattern with invariant validation.

`src/Charm.Engine/Generators/AttentionGenerator.cs` — new generator. Reads five offensive players' `GravityContribution` and `SpacingContribution` plus Roll E's `FinalShares`. Normalizes gravity to [0,1] before blending with usage (preventing a silent ~100× scale mismatch). Runs iterative floor-constrained redistribution (floor pass of `ApplyFloorAndRail`, copied from `RollEGenerator` per A2 — extract at a third consumer). Computes `TeamGravityLevel` (sigmoid-gated top-threat term), `TeamSpacingLevel` (accumulating field across non-primary-gravity players), and `TeamBaseOpenness` (asymmetric product: `GravitySource × (α + β × SpacingField)`, β > α — gravity enables spacing). Includes neutral-pinned `PassingAmp = 1` seam. Returns `AttentionResult` record.

`src/Charm.Engine/Core/PossessionState.cs` — four new trailing nullable fields: `ShooterAttentionShare`, `TeamBaseOpenness`, `TeamGravityLevel`, `TeamSpacingLevel`. Same shape as `UsagePressure`/`UsageResidualPressure`.

`src/Charm.Engine/Rolls/RollE.cs` — extended `Execute` signature to receive attention array and team levels; stamps all four new fields in the same atomic `with` as `SelectedSlot` and `UsagePressure`.

`src/Charm.Engine/Rolls/RollK.cs` — `ResetOffense` blank-slate `with` extended to null all four new fields alongside the existing `UsagePressure` and `UsageResidualPressure`.

`src/Charm.Engine/Config/RollHConfig.cs` — three new knobs: `C1ReliefScale` (0.08 placeholder), `C2ImbalanceScale` (0.08 placeholder), `C3AttentionAmplifier` (1.5 placeholder). Invariant validation added.

`src/Charm.Engine/Generators/RollHGenerator.cs` — wired C1, C2, C3 between the matchup logistic and `BuildRealPie`:
- **C1 (bonus-only nudge):** `AttentionRelief = max(0, 0.20 − a)`. `ShooterOpenness = clamp(TeamBaseOpenness × AttentionRelief × C1ReliefScale, 0, 1)`. `makePct += c1Bonus`. Never negative. Skipped on FastBreak.
- **C2 (zone-imbalance penalty):** `imbalance = TeamSpacingLevel − TeamGravityLevel`. Positive → docks Three/Long; negative → docks Rim/Short. Skipped on FastBreak.
- **C3 (Phase 17 amplifier):** `AttentionPressure = max(0, a − 0.20)`. Both Phase 17 terms multiplied by `(1 + AttentionPressure × C3AttentionAmplifier)`. Equal-share → amplifier ×1 → Phase 17 unchanged. Zero usage pressure → zero penalty regardless of attention.
- Putback short-circuit precedes all new code (A5 confirmed).

`src/Charm.Harness/config.json` — new `"Attention"` section; three new knobs in `"RollH"`.

`src/Charm.Harness/Program.cs` — `AttentionConfig.Load` and `AttentionGenerator` construction added; `AttentionGenerator` inserted into all 23 Resolver construction sites; 13 direct `RollE.Execute` harness calls updated to 9-param form (zeroed attention defaults — correct for isolated checks).

**Key design decisions:**

- **Gravity enables spacing (not a symmetric product).** `BaseOpenness = GravitySource × (α + β × SpacingField)`. Without a dominant rim threat, spacing contributes little — the defense doesn't need to collapse, so perimeter spacing is wasted. This produces the required five-case ordering: 5-Korver and 5-Evans both low; 4-Evans+1-Korver limited; 4-Korver+1-Evans higher; 5-Durant highest. The product-of-averages form fails this ordering (proven in Python before any C# was written).

- **Sigmoid gate on gravity (not tanh).** `GravitySource = sigmoid((top − 60)/15) + 0.12 × sigmoid((second − 60)/15)`. A player needs genuine rim pressure (above ~60) to activate the defense-scrambling effect. Moderate gravity (37) barely registers; elite gravity (81) nearly maxes it. This prevents a pure-spacing lineup from generating false openness through moderate-gravity "everyone pulls slightly."

- **C1 relief against equal-share neutral, not against raw gravity.** `AttentionRelief = max(0, 0.20 − a)`. The prior draft used `max(0, g − a)` (gravity minus attention share) — wrong because `g` is an absolute player trait and `a` is a relative pie share. A star with g=0.85, a=0.35 would get a large "openness" bonus despite being heavily attended. Corrected: C1 only asks "is this shooter left relatively unattended?" — a question about attention relative to the neutral baseline, not about the shooter's own gravity.

- **C3 amplifies both Phase 17 terms.** Both the vol-tax and the residual penalty are multiplied by `(1 + AttentionPressure × C3AttentionAmplifier)`. A forced specialist under defensive attention takes the largest hit: the residual is already larger, and C3 amplifies it further. This is the correct basketball model — a non-shooter forced into heavy volume while the defense keys on him specifically should collapse.

- **Normalization is required, not optional.** Gravity [0,100] and usage [0,1] cannot be naively blended. Gravity would overwhelm usage ~100× and defeat the focal-point correction while still compiling and producing a valid pie. Both normalized to [0,1] before blending.

- **FastBreak guard in Roll H.** C1 and C2 are halfcourt effects; C3 is skipped automatically because `UsagePressure = 0.0` on FastBreak. Explicit `state.FastBreak` gate added for C1 and C2.

- **Passing seam neutral-pinned.** `PassingAmp = 1` constant in `AttentionGenerator`. The formula shape (`BaseOpenness × PassingAmp`) is already written; Session 2 makes it live by computing the term from Passing/Playmaking/Vision — no formula reshape needed.

**Python pre-check (mandatory hard gate — Step 0):**

Five-case ordering verified with realistic moderate inputs before any C# was written:
- 5-Korver: openness=0.162 (LOW ✓)
- 5-Evans: openness=0.329 (LOW ✓)
- 4-Evans+1-Korver: openness=0.428 (LIMITED ✓)
- 4-Korver+1-Evans: openness=0.664 (HIGHER ✓)
- 5-Durant: openness=0.736 (HIGHEST ✓)

Full validation suite also confirmed: focal-point relativity (lone focal Korver above-share; crowded-out below-share), C1 bonus-only shape (a=0.10 → bonus; a=0.20 → 0; a=0.35 → 0), C2 zone-specific docking (spacing-heavy docks Three; gravity-heavy docks Rim), C3 four-line behavior, six-value attribution separability.

**Harness output — key results:**

All checks passed. STRESS TEST PASSED. 500/500 valid games each bucket.

The Bucket 6 (ShootingVsAthletic) win rate split (Athletic 74% vs Shooting 24%) is the most diagnostic Phase 27 result: the athletic team's dominant rim presence activates the gravity-enables-spacing interaction, producing superior openness. The shooting-only team has no gravity source to scramble the defense. This is the intended asymmetric behavior.

Buckets 7/8 mirror gap: 0.8% — engine is side-neutral on the Athletic vs Skill matchup.

Phase 17 regression anchors all held (zero-pressure behavior identical, equal-share C3 anchor ×1).

**Calibration note:** All magnitude parameters (`C1ReliefScale`, `C2ImbalanceScale`, `C3AttentionAmplifier`, gravity/usage blend weights, sigmoid parameters, `GravityAloneYield`, `SpacingMultiplier`) are calibration placeholders. Architectural shapes are locked audit corrections.

**One flag:** The harness range check on `GravityContribution > 99` is a pre-existing check from when the formula was a simple average of 0–99 attributes. The new formula is bounded to [0,100]; a player with extremely high Finishing/Close/Access could theoretically produce 100.0. This would trigger a false-positive range failure. Not seen in this run; flag for the calibration pass.

## Session 60 — Phase 26: Slot/Archetype Cohort Box Scores + Slasher Shooting Floor (2026-06-17)

**Scope:** Two changes, one file (`src/Charm.Harness/Program.cs`). (1) Raise the Slasher archetype's shooting floor so it can function as a perimeter threat. (2) Add a slot/archetype cohort box score to every stress-test bucket, backed by a full PlayerId-by-logical-team stamping and per-variant corruption detection scheme. Engine untouched.

**What shipped (1 file):**

`src/Charm.Harness/Program.cs` — six surgical additions:

- **Slasher floor fix (A4):** `slOutside` changed from `Clamp(AtWeakness())` to `Clamp(AtBaseline())` (~30 → ~50 mean); `ThreeTendency` changed from `TStr(5, 10)` to `TStr(25, 40)`. `FreeThrow = DrawFreeThrow(slOutside, slHeight, rng)` left unchanged — it automatically reads the higher `slOutside`. Blast radius: Buckets 5 (Slasher star), 6, 7, 8 (AthleticRoster has two Slashers); Buckets 1–4 unchanged.

- **Archetype label arrays + per-bucket cohort accumulators (before variant loop):** `teamAArchetypes` / `teamBArchetypes` as `PlayerArchetype[]` locals (switch on `bucketNum`; Bucket 5's star slot explicitly `PlayerArchetype.Slasher` from `starRoleArchetypes`). Twelve `long[10]` cohort accumulator arrays (`cohortFga` … `cohortShFoul`) declared before the variant loop and reset naturally per bucket.

- **PlayerId stamping by logical team (after build switch, A1/A3):** `StampPlayerId(teamAPlayers[si], si + 1)` for si=0..4 and `StampPlayerId(teamBPlayers[si], si + 6)` for si=0..4. Done once per variant at build time; the stamped `Player[]` arrays are what `SeatRoster` re-seats each game, so the ID follows the logical team across the home/away flip.

- **Per-variant PlayerId contract validation (A2):** After stamping, validates: exactly 10 unique IDs in combined set; min=1, max=10; Team A holds exactly {1,2,3,4,5}; Team B holds exactly {6,7,8,9,10}. Violations → hard failure into `failures` list (the variant cannot produce trustworthy attribution).

- **Atomic attribution + cohort accumulation (game loop, before `vs.ValidGames++`):** `AttributeGame` wrapped in try/catch immediately after the count-invariant check. On throw → `continue` (game excluded from both denominator and all accumulators). On success → accumulate `variantFga[10]` (per-variant corruption tracker) and all twelve cohort arrays, then `vs.ValidGames++`. No possible `continue` or failure branch between cohort accumulation and the denominator increment.

- **Per-variant FGA gate (after game loop, before `allVariantStats.Add`):** If a variant produced zero valid games, records that and skips the per-slot check (avoids 10 misleading zero-FGA reports). Otherwise, checks every slot: zero FGA across the variant's valid games → hard failure with the message "indicates probable seating/PlayerId corruption or an unexpectedly unreachable slot; inspect before accepting the run."

- **Bucket-level no-zero FGA gate + cohort box score (after team-level output):** No-zero gate mirrors `AttributionSanityCheck`'s pattern (FGA only, hard failure into `failures`). Cohort box score prints under `=== COHORT BOX SCORE — Bucket N: Name ===` with a mandatory pooling-caveat header; 10 rows labeled `[A] Slot1 — Slasher`, `[B] Slot2 — AthleticBig`, etc.; full ObservationRunV1 column set (PTS/FGA/FGM/FG%/3PA/3PM/3P%/FTA/FTM/FT%/ORB/DRB/REB/STL/BLK/TO/SFL); per-game averages using `totalValid` as the denominator (same accepted-game set as team-level stats). Bucket 5 prints a note that Slot 1 is the Elite-tier star.

**Key design decisions:**

- **Cohort, not per-player.** Each bucket generates 10 different rosters from different seeds. The pooled rows are a slot/archetype cohort average — how a given archetype performs in this matchup — not a persistent named player's line. The caveat header prevents misreading.

- **PlayerId by LOGICAL team, not physical side.** The stress test flips physical home/away every other game. Stamping by physical side would split one logical player's contributions across two accumulator indices. Stamping at build time (before seating) ensures Team A's player in Slot 1 always accumulates into `cohortFga[0]` regardless of which physical side they drew.

- **Atomic acceptance boundary.** `AttributeGame` succeeds or the game is excluded from everything — cohort numerator, team-level numerator, and `ValidGames` denominator all move together or not at all. This preserves the invariant that every denominator is the same accepted-game set.

- **Per-bucket cohort locals, not `VariantStats` fields.** Cohort accumulators are bucket-level reporting state, not per-variant team-performance state. Storing them in `VariantStats` would blur the responsibility boundary. Twelve `long[10]` locals inside the bucket loop, reset per bucket.

- **Bucket 7/8 PlayerId independence.** In Bucket 8, Skill is Team A (IDs 1–5) and Athletic is Team B (IDs 6–10) — the opposite assignment from Bucket 7. This is correct: each bucket's box score is self-contained. The prompt's design note (do not try to make IDs consistent across 7 and 8) was followed.

**Python pre-check (directional, not pass/fail):**
- Slasher Outside mean: ~30 → ~50. ThreeTendency mean: ~7.5 → ~32.5. Athletic roster mean ThreeTendency: ~7.5 → ~17.5 (two Slashers at ~32.5, three others at ~7.5). Directional claim confirmed: the authored distributions rose, realized three-rate should rise materially from prior ~7.7%.

**Harness output — key results:**

*All structural checks: STRESS TEST PASSED, ALL CHECKS PASSED. 500/500 valid games each bucket. Zero failures on contract validation, per-variant FGA gate, bucket no-zero gate, or attribution.*

*Slasher fix (Buckets 5, 6, 7, 8):*

| Bucket | Context | Three-rate (team with Slashers) | Prior |
|---|---|---|---|
| 5 | StarVsBalanced | Slasher star: 2.9 3PA/g, 35.4% 3P% | n/a (per-team) |
| 6 | ShootingVsAthletic | Athletic Team B: 12.4% | ~7.7% |
| 7 | AthleticVsSkill | Athletic Team A: 12.5% | ~7.7% |
| 8 | SkillVsAthletic | Athletic Team B: 12.6% | ~7.7% |

The three-rate rose materially across all affected buckets. Buckets 1–4 unchanged (no Slasher).

*Cohort box scores — selected observations:*

**PassFirstGuard / FloorGeneral underperform.** PTS ~9–10/game across most buckets, lowest of any archetype. Consistent with the standing note that playmaking and IQ channels are not yet fully wired — these players score little because their value is deferred.

**PostScorer and RimRunner are the most efficient.** FG% 47–48% and 45–49% respectively (weighted toward close/rim), leading DRB and ORB per game. SFL (fouls committed) highest at 1.3–1.8/game, reflecting their interior positioning.

**PerimeterShooter and ThreeAndDWing dominate 3PA.** 4–5 3PA/game, 38–45% 3P%. These two archetypes drive the Shooting roster's 35.3% aggregate three-rate.

**AthleticBig posts the highest FG% (54–57%).** Pure inside-the-paint tendencies (RimTendency and close bias) and strong Finishing. Low 3PA (0.5–0.6/game) confirms no perimeter presence, as designed.

**Slasher box line (Bucket 6 [B]):** 15.6–15.7 PTS, ~12-13 FGA, 48–50% FG%, 2.0 3PA, 30–32% 3P%, 3.6–4.1 FTA/g. The Slasher now reads as a well-rounded slashing scorer who can hit the open three — not a non-shooter.

**Bucket 5 star Slasher:** 21.9 PTS, 15.8 FGA, 54.1%, 2.9 3PA. Usage at 30.8% (Slot 1), well above the 20% equal-share baseline. The usage concentration from Phase 17/21 is directly visible in the star's cohort line.

**SFL directional pattern.** Interior players (PostScorer, RimRunner, AthleticBig) commit 1.3–2.0 SFL/game; perimeter players (PerimeterShooter, FloorGeneral) commit 0.7–1.0 SFL/game. Pattern matches Phase 25's interior-tilt formula.

**Calibration roadmap (observations, not grades):**

1. **PassFirstGuard / FloorGeneral scoring floor** — deferred channel (playmaking, IQ) is the root cause. Until those routes are wired, these archetypes underperform as scorers. Structural, not a calibration dial.
2. **PostScorer FT% lower in Elite tier** — noted in Phase 22 as a consequence of the Height nudge in `DrawFreeThrow`. Elite bigs have very high Height (AtStrength), creating a large downward nudge. Needs full-population data to determine whether this is realistic or over-penalizing.
3. **Slasher 3P% (30–35%)** — now in a plausible range for a "hits the open one" player. The exact rate can be calibrated via `ThreeTendency` range and the location matchup bends. Leave as-is until team-level calibration sets anchors.
4. **Athletic roster win dominance (70% vs Skill)** — large gap across Buckets 7/8. Either the DEC-5 physical exponent is producing a very strong Athletic advantage, or these particular roster compositions have a structural mismatch. Record; investigate at team-level calibration.
5. **Shooting roster losing badly to Athletic (28%)** — the Athletic roster has strong Finishing (rim conversion) and the Shooting roster has lower rim protection. Worth tracking whether this gap narrows after calibrating the block/foul matchup parameters.

**ALL CHECKS PASSED.**

## Session 59 — Phase 25: Shooting Foul Attribution (2026-06-17)

**Scope:** Wire a shooting-foul event list from the resolver walk through to the harness attribution pass, then draw a weighted-credit fouling defender for each event using a matched-man + interior-tilt residual formula. No engine rolls changed, no config keys changed. Four files.

**What shipped:**

- `src/Charm.Engine/Core/ShootingFoulEvent.cs` (NEW) — `readonly record struct ShootingFoulEvent(ShotLocation Zone, int ShooterSlot)`. One event per `MadeAndFouled` / `MissFouled` resolution. `ShooterSlot` is 1–5 on the overwhelming majority of possessions (Roll E ran); 0 on the rare bonus-FT putback path where Roll E never fired (`SelectedSlot` was null). The 0 value is the "no matched man" sentinel that routes the harness draw to the flat fallback.

- `src/Charm.Engine/Core/Resolver.cs` — three surgical additions:
  - `var shootingFouls = new List<ShootingFoulEvent>();` alongside the other Phase 23 locals in `Route()`.
  - Inside the `ResolveShootingFreeThrows` case (before `DriveFreeThrows`): `shootingFouls.Add(new ShootingFoulEvent(c.State.ShotType!.Value, c.State.SelectedSlot?.Number ?? 0))`. Uses `!` on `ShotType` (Roll G stamped the zone; non-null) and `?? 0` on `SelectedSlot` (may be null on bonus-FT putback path). The existing Phase 23 code already used `?? 0` here; this is consistent and not a new assumption. A misleading comment in the same block ("SelectedSlot is non-null here") was left as-is — the code is correct, the comment is wrong; correcting misleading comments is a future cleanup, not a scope violation.
  - `ShootingFouls = shootingFouls.ToArray()` added to the single Terminal `RoutingOutcome` return. `RoutingOutcome` gains `public IReadOnlyList<ShootingFoulEvent> ShootingFouls { get; init; } = Array.Empty<ShootingFoulEvent>();` as a trailing init-only field with an empty-array default — pure append, no existing construction affected.

- `src/Charm.Engine/Core/Governor.cs` — same pattern as Phase 23 additions: `PossessionRecord` gains `IReadOnlyList<ShootingFoulEvent>? ShootingFouls = null` as a trailing optional param; `Run()` gains `IReadOnlyList<ShootingFoulEvent>? possessionShootingFouls = null` local, threaded from `outcome.ShootingFouls` in the resolver branch, passed positionally to `records.Add(...)`.

- `src/Charm.Harness/Program.cs` — six addition groups:
  - `PlayerBoxTotals.ShFoul = new long[10]` field added; `AllEqual` extended with `a.ShFoul.SequenceEqual(b.ShFoul)`.
  - `var foulRng = new Random(seed + 3)` in `AttributeGame` — a separate RNG so the `seed+2` draw stream (TO → STL → DReb → OReb → BLK, in that exact order) is consumed identically and all prior numbers are bit-for-bit unchanged.
  - Foul-draw block in `AttributeGame` after the BLK block: `if (r.ShootingFouls is { } sfs) foreach (var sf in sfs) { var fSlot = DrawFoulingDefender(foulRng, ...); ... t.ShFoul[fp.PlayerId - 1]++; }`.
  - `private static int DrawFoulingDefender(Random rng, TeamSide side, Roster roster, ShotLocation zone, int shooterSlot)` — new helper implementing the matched-man + interior-tilt residual formula. See design.md for the formula detail. Interior proxy: `p.Height + p.Strength + p.PostDefense`. The `shooterSlot == 0` path (bonus-FT putback) and any path where the matched slot is unpopulated both use a flat fallback over all populated defenders.
  - `ObservationRunV1`: `var bsShFoul = new long[10]` accumulator, per-game accumulation loop, Phase 25 summary diagnostic block (total events, slot-0 event count, total SFL credits, non-zero gate), SFL column in the per-player box score header and rows.
  - `Phase25ShootingFoulAttributionCheck(string configPath)` — new check wired into `Main` via `ok &= Phase25ShootingFoulAttributionCheck(configPath)` immediately after the Phase 24 call. Two sub-tests: directional (100k draws × 4 scenarios, no end-to-end confound) and end-to-end completeness (200 games, controlled roster, side-specific reconciliation, no-zero defender).

**Key design decisions:**

- **A possession can carry more than one shooting foul.** Confirmed in source: Roll K's PutBack arm routes back to `ContinuationKind.IntoShotResolution` with zone forced to `ShotLocation.Rim`. A putback can be fouled, creating a second `ResolveShootingFreeThrows` edge hit in the same walk. The event list (not a scalar) naturally handles this.

- **`?? 0` / `!` asymmetry at the edge.** `ShotType!.Value` asserts non-null because Roll G always stamps the zone before Roll H fires the foul. `SelectedSlot?.Number ?? 0` defends against null because the bonus-FT putback path (Roll K → `IntoShotResolution` when Roll E never ran) is a legitimate game path where `SelectedSlot` is null. This asymmetry was pre-existing in the Phase 23 FTA/FTM slot reads; Phase 25 copies the same pattern.

- **Interior proxy = `Height + Strength + PostDefense` (unweighted).** These are the same three attributes `Matchup.Postness` uses; the unweighted sum avoids a `MatchupConfig` dependency in `AttributeGame`. The same three attributes that distinguish an interior big from a perimeter player.

- **Matched-man share is zone-exact; residual is interior-tilted.** The defender at the same slot index as the shooter gets a fixed zone-dependent share (`Rim: 50%, Short: 65%, Mid: 70%, Long: 80%, Three: 80%`). The remaining probability is distributed across the other four defenders via `exp(signedK × (interior − meanInt) / SCALE)` where `signedK` is `+0.50` at Rim (interior-favoring) and `-0.50` at Three (perimeter-favoring). SCALE=40.0. All values are calibration placeholders.

- **seed+3 RNG isolation.** The existing draw order in `AttributeGame` is TO → STL → DReb → OReb (loop) → BLK (loop), all on `Random(seed+2)`. Adding fouls to that stream would shift every prior draw. The fix is a separate `Random(seed+3)` for fouls only. Confirmed by code read: `seed+2` stream draw order is fixed, nothing else consumes it.

**Python pre-validation (run before any C#):**

All four scenarios (guard shoots three, guard drives rim, big shoots three, big at rim) verified with the Phase 24 controlled roster (Anchor interior=230, Perim interior=115, meanInt=138):

- All distributions sum to 1.0 ✓, all weights ≥ 0 ✓
- Matched-man shares exact (80%, 50%, 80%, 50%) ✓
- Three residual: perimeter slots > interior slot ✓ (ratio ~9×)
- Rim residual: interior slot > each perimeter slot ✓ (interior gets ~58% of the residual with SCALE=40)

Note: the draft prompt estimated ~37% of the rim residual going to the big (likely computed with SCALE≈100). The actual formula with SCALE=40 gives ~58% — stronger than estimated. The "too strong" characterization still holds, just more so. Flagged in journal and in `DrawFoulingDefender` as the first calibration target.

**Harness output — key results:**

*Phase 25 directional test (100k draws × 4 scenarios):*

| Scenario | Matched slot | Observed share | Direction |
|---|---|---|---|
| Guard shoots three (slot=2, zone=Three) | Slot 2 | 0.8001 | Perim (3,4,5) > interior (1): 0.0619 vs 0.0148 ✓ |
| Guard drives rim (slot=2, zone=Rim) | Slot 2 | 0.5001 | Interior (1) > perim: 0.2929 vs 0.0682–0.0699 ✓ |
| Big shoots three (slot=1, zone=Three) | Slot 1 | 0.7999 | Residual (2–5) all perim, roughly equal ✓ |
| Big at rim (slot=1, zone=Rim) | Slot 1 | 0.5007 | Residual (2–5) all perim, roughly equal ✓ |

All four matched-share assertions: [OK]. Directional assertions (scenarios 1 and 2): [OK].

*Phase 25 end-to-end (200 games, controlled roster):*

- Home defense shooting-foul events: 1,030 | SFL credits: 1,030 ✓ (exact)
- Away defense shooting-foul events: 1,039 | SFL credits: 1,039 ✓ (exact)
- Global completeness: 2,069 events == 2,069 credits ✓
- No-zero defender: all 10 players > 0 SFL over 200 games ✓

*ObservationRunV1 (1,000 games, frozen corpus):*

Phase 25 summary: 12,105 total SFL credits across 1,000 games. Slot-0 events (bonus-FT putback path): 1 — very rare, as expected.

Per-player SFL per game (selected): Javon Okafor (big) 1.8, Darius Eze (big) 1.7, Cory Baptiste (perimeter) 0.7, Malik Thornton (perimeter) 0.7. Interior players commit more shooting fouls — directionally correct.

*Phase 24:* still PASSED — no regression. *Stress test:* all 8 buckets still PASSED — no regression.

**ALL CHECKS PASSED.**

**Calibration note (first target):** The matched-man share (50–80% by zone) and the interior tilt strength (`signedK ±0.50`, `SCALE=40`) are placeholders. With the Phase 24 controlled roster, the big defender gets ~58% of the rim-shot residual — stronger than a typical game would produce. Calibration should start here, likely by raising SCALE (weakening the interior tilt) or lowering `signedK`, once realistic rosters are in use.

**Deferred (unchanged):** True per-player attribution across substitutions; non-shooting fouls (team foul rate, offensive fouls, loose-ball fouls); calibration of the zone tables and tilt parameters; per-player foul tracking (individual foul ledger — requires a separate session).

## Session 58 — Phase 24: Attribution Sanity Check (controlled roster, 200 games) (2026-06-17)

**Scope:** Verify that the weighting system preferentially credits players with the intended attributes. One new harness check (`AttributionSanityCheck`) — no engine changes, no config changes, no new rolls. The check constructs a controlled 10-player roster, runs 200 games, and tests directional assertions against the Phase 23 attribution machinery.

**What shipped (1 file):**
- `src/Charm.Harness/Program.cs` — new `private static bool AttributionSanityCheck(string configPath)` method wired into `Main` via `ok &= AttributionSanityCheck(configPath)` before `ObservationRunV1`. The method:
  - Constructs a controlled roster inline: Slot 1 is a Rim Anchor (Height=92, Wingspan=92, DefensiveRebounding=95, OffensiveRebounding=90, RimProtection=90, FreeThrow=55, FoulDrawing=30, RimTendency=80, ThreeTendency=1); Slots 2–5 are perimeter role players (Height=35, Wingspan=35, DefensiveRebounding=5, OffensiveRebounding=5, RimProtection=5, FreeThrow=78, FoulDrawing=65, ThreeTendency=60, RimTendency=10). Same template both sides — symmetric by design.
  - Runs 200 games using the same Governor/Resolver construction as `ObservationRunV1` (same configs, `SystemRng(seed)` / `SystemRng(seed+1)`, same `firstState`). Calls `AttributeGame` per game and accumulates into `long[10]` arrays.
  - Runs 13 invariant checks copied verbatim from `ObservationRunV1`: exact-family reconciliation (FGA/FGM/3PA/3PM/FTA/FTM named == total − unattributed for Home and Away), weighted-credit identity checks (OReb/DReb/BLK/STL/TO per-player sums == engine event counts), and per-possession per-slot subset checks (3PM ≤ 3PA, 3PA ≤ FGA, FTM ≤ FTA, for all 6 slot indices).
  - Runs directional assertions per side (Home and Away): DReb anchor/role ratio > 3.0×, OReb ratio > 3.0×, BLK ratio > 2.0×, 3PA role/anchor ratio > 3.0× (integrated selection + attribution check), Anchor FT% < 65% (authored FreeThrow=55), Role combined FT% > 72% (authored FreeThrow=78). FT% assertions include minimum-sample gates (Anchor FTA ≥ 50, Role FTA ≥ 200) — insufficient sample is a hard failure.
  - Runs no-zero FGA check (all 10 players must have FGA > 0 over 200 games — wiring health check).
  - Returns `true` only if all invariants and all directional assertions pass. Local summary line `Attribution sanity check: PASSED/FAILED`. Authoritative final verdict owned by `Main`'s single banner.

**Design note — this is not causal attribution:** For weighted stats (rebounds, steals, blocks, some turnovers) no causal player exists in the engine. The check proves the weighting system preferentially credits players with the intended attributes — extreme attribute contrasts produce extreme box-score contrasts in the expected direction. That is what this session validates, not that any specific player caused any specific event.

**Python pre-validation (run before writing C#):**
- DReb weight (Anchor vs. individual role): 367 vs. 105 → ratio 3.50× > threshold 3.0× ✓
- OReb weight: 362 vs. 105 → ratio 3.45× > threshold 3.0× ✓
- BLK weight: 324 vs. 110 → ratio 2.95× > threshold 2.0× ✓
- FreeThrow=55 → 55.0%, FreeThrow=78 → 78.0% (1:1 formula confirmed live in RollLGenerator.cs) ✓
- FTA sample over 200 games: Anchor ≈372 (gate: 50), Role ≈3,228 (gate: 200) — both clear comfortably ✓

**Harness output — key results:**

*Phase 24 directional assertions (Home side, 200-game averages):*

| Player | DReb/g | OReb/g | BLK/g | 3PA/g | FT% |
|---|---|---|---|---|---|
| RimAnchor | 9.09 | 3.65 | 1.15 | 0.14 | 56.8% |
| PerimRole (avg) | 2.51 | 1.01 | 0.38 | 5.39 | 81.2% |
| Ratio | 3.62× | 3.61× | 3.00× | 39.96× role/anchor | — |

Away side ratios: DReb 3.58×, OReb 3.34×, BLK 3.04×, 3PA 45.81×. All thresholds cleared on both sides.

All 13 invariant checks: [OK]. All 12 directional assertions: [OK].

**ALL CHECKS PASSED.**

**Deferred (unchanged):** True per-player attribution across substitutions; assists; fouls per player; per-player StressTest box score; `MakePlayer` PlayerId assignment.

## Session 57 — Phase 23: Named Player Attribution Under Fixed Lineups (2026-06-17)

**Scope:** Credit each team-level event to a named player across 1,000 games. With fixed lineups, per-slot and per-player attribution are equivalent — this is the prerequisite layer, not the final substitution-aware thing. Attribution for stats the engine directly knows (which slot shot, which slot was fouled) is exact. Attribution for stats without a specific actor (defensive rebounder, steal, block, turnover on pre-Roll-E possessions) is probabilistic credit via weighted draws.

**What shipped (5 files):**
- `src/Charm.Engine/Core/SlotGroup.cs` — new `readonly record struct` carrying five per-slot counters plus an unattributed bucket; `Total`, indexer, and `WithSlot` immutable-update method.
- `src/Charm.Engine/Core/Player.cs` — added `PlayerId { get; init; }` (stable harness-assigned int, 0 = unset sentinel; not read by any engine roll; not in `Validate()`).
- `src/Charm.Engine/Core/Resolver.cs` — `RoutingOutcome` gains 7 new init-only fields: `ThreePaBySlot`, `ThreePmBySlot`, `FtaBySlot`, `FtmBySlot` (exact per-slot counters, zero new IRng calls), `BlkCount` (int, count of blocked shots), `TurnoverOffSlot` (nullable int, the offensive slot that committed the TO if Roll E had already fired), `TurnoverWasLiveBall` (bool, true on `BadPassIntercepted` and `LostBallLiveBall`). `Route()` gains 7 matching locals and 5 attribution hooks: 3PA in the non-MissFouled else block after the FGA switch; 3PM inside the Made/MadeAndFouled block after the FGM switch; `blkCount++` on `ShotResult.Blocked`; FTA/FTM slots in both FT entry cases (`ResolveFreeThrows` and `ResolveShootingFreeThrows`); TO metadata in the Terminal case before the return statement. Return statement extended with all 7 new fields.
- `src/Charm.Engine/Core/Governor.cs` — `PossessionRecord` gains the same 7 fields as trailing optional params (defaulting to `default`/`0`/`null`/`false`). `Run()` gains 7 matching per-possession locals, threads them from `outcome.*` in the normal-possession branch, and passes them positionally to `records.Add(...)`.
- `src/Charm.Harness/Program.cs` — extensive additions to `ObservationRunV1`:
  - Inline seating loop replaces `SeatStartersFromConfig` call — stamps `PlayerId` at construction time (Home S1→1..S5→5, Away S1→6..S5→10) using the new `StampPlayerId` helper before the first `SetStarter` call, sidestepping the `Roster.SetStarter` occupied-slot guard.
  - PlayerId uniqueness validation (seed==1 only): throws on any unset ID, duplicate, or count ≠ 10.
  - Phase 23 per-player accumulators (`bsFga`…`bsTo`, 11 families × 10 slots), cross-game running totals for exact-family reconciliation (3PA/3PM/FTA/FTM + their unattributed buckets, 5 weighted-credit event totals), and `attributionOk` flag.
  - Per-game Phase 23 invariants (inside the 1,000-game loop): 3PA/3PM/FTA/FTM slot-total completeness and subset checks; `TurnoverWasLiveBall` vs EndLabel consistency; non-TO metadata leakage check; per-slot subset checks for all 6 slot indices (0=Unattr, 1–5).
  - `AttributeGame` static helper: takes `(GovernorRunResult, GameState, int seed)`, constructs `Random(seed+2)` as the attribution RNG, runs exact per-slot stats (FGA/FGM/3PA/3PM/FTA/FTM) and probabilistic `WeightedDraw` credits (TO, STL, DReb, OReb, BLK); returns `PlayerBoxTotals`. Called once per game in the loop, twice on seed-1 data after the loop for reproducibility.
  - Post-loop §5f reconciliation: `CheckExact` verifies named-player totals equal total-minus-unattributed for all 6 exact families; 5 weighted-credit identity checks (per-player sum == event count).
  - §5g same-seed reproducibility: `AttributeGame` called twice on seed-1 data → `PlayerBoxTotals.AllEqual` must hold.
  - §5h per-player box score: 10 rows × 16 columns (PTS/FGA/FGM/FG%/3PA/3PM/3P%/FTA/FTM/FT%/ORB/DRB/REB/STL/BLK/TO), per-game averages over 1,000 games.
  - Combined `if (mechanicsOk && attributionOk) Console.WriteLine("  ALL CHECKS PASSED")` banner after the box score.
  - New static helpers: `GetSlotFga`, `GetSlotFgm`, `IsTurnoverPossession`, `BoxIdx`, `StampPlayerId` (copies all 37 authored attributes plus new `PlayerId`), `WeightedDraw` (attribution-only, uses `Random` not `IRng`), `AttributeGame`, `PlayerBoxTotals` (sealed class with 11 `long[10]` arrays and static `AllEqual`).

**Build issues resolved:**
1. `with { PlayerId = newId }` on a `sealed class` — C# `with` only works on records. Fixed by introducing `StampPlayerId` helper that copies all 37 authored attributes plus the new ID, and calling it before `SetStarter`.
2. `Roster.SetStarter` throws if the slot is already occupied — the initial design tried to re-seat after `SeatStartersFromConfig`, which hit the guard. Fixed by inlining the seating loop in `ObservationRunV1` and stamping `PlayerId` at construction time, before the first `SetStarter` call.

**Harness output — key results:**

*Phase 23 attribution checks:* All exact-family reconciliation checks passed (FGA/FGM/3PA/3PM/FTA/FTM named-player totals equal total-minus-unattributed for both Home and Away). All five weighted-credit identity checks passed (per-player OReb/DReb/BLK/STL/TO sums equal the engine event counts). Same-seed reproducibility confirmed.

*Per-player box score (1,000-game averages, frozen corpus):*

| Player | PTS | FGA | FGM | FG% | 3PA | 3PM | FT% | ORB | DRB | STL | BLK | TO |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| [Home] Marcus Webb | 14.1 | 11.1 | 5.4 | 48.6% | 3.2 | 1.3 | 74.9% | 1.1 | 3.0 | 1.0 | 0.6 | 3.0 |
| [Home] DeShawn Pryor | 16.8 | 12.7 | 6.5 | 51.1% | 2.6 | 1.1 | 79.8% | 1.3 | 3.3 | 0.9 | 0.6 | 2.7 |
| [Home] Trey Holloway | 14.5 | 11.9 | 5.8 | 48.4% | 2.6 | 0.9 | 66.0% | 1.5 | 4.1 | 0.8 | 0.6 | 2.7 |
| [Home] Javon Okafor | 11.5 | 8.6 | 4.7 | 55.0% | 0.3 | 0.1 | 54.8% | 1.9 | 4.9 | 0.6 | 0.9 | 2.1 |
| [Home] Cory Baptiste | 9.8 | 7.2 | 3.5 | 47.9% | 3.6 | 1.7 | 83.7% | 1.3 | 3.2 | 0.8 | 0.6 | 1.9 |
| [Away] Kendrick Shaw | 13.4 | 10.5 | 5.2 | 49.2% | 2.7 | 1.1 | 70.2% | 1.0 | 2.9 | 1.0 | 0.5 | 2.7 |
| [Away] Rashid Monroe | 16.1 | 13.0 | 6.4 | 49.0% | 2.7 | 0.9 | 68.9% | 1.3 | 3.6 | 1.0 | 0.6 | 2.9 |
| [Away] Antoine Dupree | 14.8 | 11.7 | 5.8 | 50.1% | 2.4 | 1.0 | 71.8% | 1.5 | 4.0 | 0.8 | 0.7 | 2.6 |
| [Away] Darius Eze | 12.4 | 9.1 | 5.1 | 55.7% | 0.4 | 0.1 | 58.5% | 2.1 | 5.2 | 0.6 | 0.8 | 2.0 |
| [Away] Malik Thornton | 9.0 | 6.6 | 3.1 | 47.9% | 3.5 | 1.6 | 89.3% | 1.2 | 3.2 | 0.8 | 0.6 | 2.0 |

Plausibility checks passed: bigs (Okafor S4, Baptiste S5 on Home; Eze S4, Thornton S5 on Away) lead DReb and OReb; guards lead STL; per-player FT% tracks authored FreeThrow ratings; no player has 0 DReb per game.

**ALL CHECKS PASSED.**

**Stress test:** All 8 buckets passed, 4,000 games. Athletic outperforms Skill 73%/27% (Buckets 7/8), mirror gap 3.2% (side-neutral). Star beats Balanced 57%/41%. Elite dominates Weak 99.2%/0.6%. Average vs Average 48%/48%.

**Deferred (unchanged):** True per-player attribution across substitutions; assists (own session); fouls per player; per-player StressTest box score; `MakePlayer` PlayerId assignment.

## Session 56 — Phase 22: Per-Slot FGM Readout + Tier-Decoupled FreeThrow Authoring (2026-06-17)

**Scope:** Two deliberate bundled changes: (1) per-slot FGM counters at the same Roll H chokepoint as Phase 21's FGA counters, making per-slot FG% directly derivable; (2) tier-decoupled FreeThrow authoring in `MakePlayer`, fixing the Phase 19 finding that `Clamp(AtBaseline()/AtStrength())` imposed an unrealistically strong tier gradient on free-throw percentage. No new generator, config key, roll, enum, or `ContinuationKind`.

**What shipped (3 files):**
- `Resolver.cs` — `RoutingOutcome` gains 6 new init-only fields: `Slot1Fgm`–`Slot5Fgm` and `SlotUnattributedFgm` (0 default). `Route()` gains 6 locals. Per-slot FGM switch added INSIDE the `Made or MadeAndFouled` block (makes only), mirroring the Phase 21 FGA switch (which stays OUTSIDE, firing on all official FGAs). Return statement extended.
- `Governor.cs` — `PossessionRecord` gains 6 new defaulted params. `Run()` gains 6 locals, threading, and extends `records.Add(...)`.
- `Program.cs` — (a) `ObservationRunV1`: 12 FGM running totals, per-game slot-make bin check, per-game subset invariant check, FGM accumulation, per-slot FG% output block before the NOTE line. (b) `StressTestArchetypeRosters`: `VariantStats` gains 12 FGM fields; game-loop adds 12 accumulation lines; per-bucket slot-FGM-sum reconciliation (hard failure into `failures`), subset invariant, `Slot FG% Team A/B` output lines. (c) `DrawFreeThrow` private static helper + 8 archetype FreeThrow replacements using Option A (hoist Outside/Height to locals before `new Player` initializer).

**Pre-build — adversarial preamble (all four cleared before touching C#):**
- A1 ("exclusively two IntoShotResolution emitters"): confirmed — RollG.cs:65 and RollK.cs:77 only; no third emitter.
- A2 ("FGM switch reads same shotSt as FGA switch"): confirmed by placement; both read `shotSt.SelectedSlot?.Number` in the same iteration on the same state.
- A3 ("negligible tier residual"): confirmed by Python Experiment 2 — Elite→Weak FT mean spread 0.4pt (≤2.0 threshold), corr(FT, tier-rank)=−0.016 (|corr|≤0.10 threshold). Pass.
- A4 ("no positional deconstruction of PossessionRecord"): re-confirmed by grep. No `var (...) = record` or `is PossessionRecord(...)` anywhere in the codebase.

**Python pre-validation (Experiment 1 — per-shape distribution, 200k draws each):**

| Shape | Out | Ht | Mean | SD | <55 | >80 | =45 | =95 | range |
|---|---|---|---|---|---|---|---|---|---|
| balanced guard | 50 | 40 | 70.6 | 10.0 | 5.6% | 16.9% | 0.2% | 0.5% | 45–95 |
| sharpshooter | 90 | 45 | 73.5 | 9.9 | 2.8% | 25.4% | 0.0% | 1.5% | 45–95 |
| can't-shoot center | 25 | 90 | 65.6 | 9.9 | 14.3% | 7.1% | 2.1% | 0.0% | 45–95 |
| stretch big | 75 | 88 | 69.7 | 10.0 | 6.8% | 14.7% | 0.4% | 0.3% | 45–95 |
| slasher | 30 | 42 | 68.8 | 10.0 | 8.0% | 12.8% | 0.6% | 0.2% | 45–95 |
| generic big | 35 | 85 | 66.7 | 9.9 | 11.8% | 8.8% | 1.5% | 0.0% | 45–95 |

Endpoint piles are small (worst: 2.1% at exactly 45 for can't-shoot center). Full [45,95] range reached by all shapes. Gentle trend: sharpshooter 73.5 > balanced guard 70.6 > can't-shoot center 65.6 (~8pt spread). `half=30.0` validated and carried into C#.

**FreeThrow design (Item 2 — recorded for calibration reference):** FreeThrow is authored independently of talent tier using a bell-shaped base centered at 70 (clamped [45,95]) with two soft center-nudges: Outside up (±4 at the extremes vs fixed pivot 50) and Height down (±3 at the extremes vs fixed pivot 50). The bell is generated by the mean of 3 independent uniform draws (summed-uniform, SD≈10). Outside and Height are themselves tier-scaled, so a mild indirect residual survives — measured at corr(FT,tier)≈0.016, ~0.5pt Elite→Weak spread. This is NOT "utterly independent"; it is direct tier-decoupling with a mild, intended position/shooting correlation. The six constants (center 70, min 45, max 95, Outside ±4, Height ±3, `half` 30) are calibration placeholders. DATA PROVENANCE: top-50 team FT% tables from 2025-26 NCAA season (D1/D2/D3) supplied by Emmett support removing strong direct tier coupling; D2/D3 best teams match D1 leaders. Full-population data for estimating any residual aggregate division effect deferred to calibration pass.

**Option A hoisting — rng-stream note (honest, not minimized):** `MakePlayer` uses one seeded `Random`. Hoisting Outside and Height draws to locals before `new Player(name)` shifts the rng sequence for every attribute in every archetype — not just FreeThrow. This is accepted because (1) the frozen corpus uses the hardcoded named roster, not `MakePlayer`, so its FT% is unchanged; (2) the stress test asserts on distributional aggregates, never specific per-player values. This is NOT a "pure FreeThrow change" — it resamples the entire MakePlayer roster corpus.

**Harness results — ALL CHECKS PASSED:**

*ObservationRunV1 (frozen corpus, 1000 games):*
- Mechanics ALL OK; no slot-make bin [FAIL] lines across all 1000 games; no subset violation [FAIL] lines.
- **Frozen-corpus FT% UNCHANGED** — Home 70.7%, Away 69.9%, Combined 70.3%. Confirms named roster untouched.
- Per-slot FG% printed under USAGE for the first time:
  - Home: Slot1=48.6%  Slot2=51.1%  Slot3=48.4%  Slot4=55.0%  Slot5=47.9%
  - Away: Slot1=49.2%  Slot2=49.0%  Slot3=50.1%  Slot4=55.7%  Slot5=47.9%
  - Slot4 (post/big position) higher FG% across both sides — consistent with a rim/close-shot player.

*StressTest (8 buckets, 4000 games, STRESS TEST PASSED):*
- All 8 buckets: no slot-FGM-sum [FAIL], no subset violation [FAIL].
- **FT% shifted upward across all buckets**, tier coupling removed:
  - AverageVsAverage: A=71.3%, B=70.4% (was ~53% before Phase 22)
  - WeakVsWeak: A=75.4%, B=75.4% (was ~47–48%)
  - EliteVsWeak: A=70.2%, B=70.5% — nearly identical, no large tier gap driven by authoring
  - EliteVsElite: A=66.7%, B=68.3% — slightly lower than average; Elite bigs have high Height, which nudges FT down via the Height penalty. Calibration target.
- **Bucket 5 (StarVsBalanced) — efficiency half confirmed:** Star Slot1 FGA share=29.4%, Slot1 FG%=56.6%. The star both takes more and converts more — the two halves of the usage story (Phase 21 = usage share, Phase 22 = FG%) now readable together.
- **Bucket 7/8 mirror gap: 4.0%** (Athletic 74.0% as Team A vs 70.0% as Team B) — within acceptable range; side-neutrality holds.

**Key calibration observation (record; do not grade):** Elite tier's mean FT% is noticeably lower than Weak in the stress test (66–68% vs ~75%). This is mechanically correct given the fixed-pivot model: Elite bigs have very high Height (AtStrength in the 75–88 range), creating a large downward Height nudge. Whether this matches real-world distributions needs full-population D1/D2/D3 data — a calibration-pass question.

**Git commit:** `Phase 22: Per-slot FGM readout (per-slot FG% now derivable) + tier-decoupled FreeThrow authoring`

---

## Session 55 — Phase 21: Per-Slot FGA Usage Readout + TBase Removal (2026-06-17)

**Scope:** Two independent changes. (1) Remove one dead-code line (`TBase()` in `MakePlayer`) to eliminate the CS8321 compiler warning. (2) Wire per-slot FGA counters from the Roll H chokepoint through `RoutingOutcome` → `PossessionRecord` → both harness output sections, making slot concentration directly visible. No new generator, config key, roll, enum, or `ContinuationKind`.

**What shipped (3 files):**
- `Resolver.cs` — `RoutingOutcome` gains 6 new init-only fields: `Slot1Fga`–`Slot5Fga` and `SlotUnattributedFga` (all 0 default — existing constructions untouched). `Route()` gains 6 locals and a slot-binning `switch` inside the `IntoShotResolution` else block with a `default` arm for null `SelectedSlot`. Return statement extended with all 6 new fields.
- `Governor.cs` — `PossessionRecord` gains 6 new defaulted params (`Slot1Fga`–`Slot5Fga`, `SlotUnattributedFga`). `Run()` gains 6 locals, threads them from outcome, adds them to `records.Add(...)`.
- `Program.cs` — (a) TBase one-line delete. (b) `ObservationRunV1`: 12 running totals (6 slot buckets × 2 sides), per-game slot-bin integrity check, accumulation, new USAGE output section. (c) `StressTestArchetypeRosters`: 12 new `VariantStats` fields, 12 game-loop accumulation lines, per-bucket reconciliation check, usage output.

**Mid-build finding: `SlotUnattributedFga`.**

The initial build omitted a `default` arm from the slot-binning switch. The first harness run fired `[FAIL] Seed N: slot-attempt bin` in ~18% of ObsRun games and `[FAIL] Bucket N: slot-sum` in all 8 StressTest buckets. Every mismatch was `slot-sum < FGA` by a small count (~0.1–0.2 FGA per team per game).

Diagnosis: bonus-free-throw possessions where Roll E was never called arrive at Roll K's PutBack arm with `SelectedSlot = null`. Roll K correctly preserves null (only `ShotType` is modified; the preserve-vs-wipe is explicit and symmetric with `ResetOffense`). The putback reaches IntoShotResolution, `fga++` fires, but the switch falls through with no increment.

Fix: `default: slotUnattributedFga++; break;` arm added to the slot-binning switch; `SlotUnattributedFga` threaded through `RoutingOutcome` → `PossessionRecord` → both harness sections. The completeness invariant is now `Slot1Fga+…+Slot5Fga+SlotUnattributedFga == Fga` — every FGA accounted for. The reconciliation assertion in both harness sections uses the full sum.

**Harness results — ALL CHECKS PASSED (second run, after fix):**
- ObsRun: mechanics ALL OK; no slot-bin [FAIL] lines across all 1000 games; USAGE section prints cleanly.
- StressTest: all 8 buckets clean; no slot-sum [FAIL] lines; STRESS TEST PASSED.
- All Phase 1–17 checks unchanged.
- CS8321 TBase warning: absent.

**Key observations (record; do not grade):**

*ObservationRunV1 (balanced frozen corpus, 1000 games):*
- Home: Slot1=21.4%  Slot2=24.7%  Slot3=23.0%  Slot4=16.7%  Slot5=14.0%  Unattr=0.2%
- Away: Slot1=20.7%  Slot2=25.5%  Slot3=22.9%  Slot4=17.9%  Slot5=12.9%  Unattr=0.2%
- Modest spread consistent with a balanced roster. Slots 2 and 3 carry slightly more; slots 4 and 5 less — plausible for the frozen corpus's mix of guards and wings.

*Bucket 5 (StarVsBalanced) — the key signal:*
- Team A (star at Slot 1): Slot1=**29.0%** Slot2=20.2% Slot3=20.1% Slot4=19.0% Slot5=11.5% Unattr=0.2%
- Team B (balanced): Slot1=16.5% Slot2=21.4% Slot3=19.3% Slot4=22.3% Slot5=20.3% Unattr=0.2%
- Slot 1 on the star team carries 29% — materially above the 20% equal-share baseline. The usage concentration that Phase 17 modeled as the driver of efficiency penalties is now directly readable. The balanced roster shows near-equal shares across all five slots as expected.

*Unattr across all buckets:* consistently 0.2%, confirming the unattributed population is entirely the bonus-FT-putback scenario. No signal of a gap in the normal shot path.

**Git commit:** `Phase 21: Per-slot FGA usage readout in ObservationRunV1 and StressTest + TBase removal`

---

## Session 54 — Phase 19: Roll E Live in ObservationRunV1 + StressTest (2026-06-17)

**Scope:** Swap `RollEStubPieGenerator` → `RollEGenerator` at exactly two harness construction sites — `ObservationRunV1` and `StressTestArchetypeRosters` — so the Phase 17 usage→efficiency chain fires at game scale for the first time. No engine file changes. No new generator, config, roll, enum, or `ContinuationKind`.

**Why this was a dormant subsystem.** `RollEStubPieGenerator.GenerateWithPressure` returns an all-zeros pressures array (`new double[5]`). The Resolver calls `GenerateWithPressure` (not `Generate`) at both Roll E dispatch sites. So even after Phase 17 wired the penalty into Roll G and Roll H, `UsagePressure` was always 0.0 at game scale — the Phase 17 branch in `RollHGenerator` was a complete branch-skip in every observation and stress test run. Swapping the two sites activates the full chain: Roll E stamps real pressures → Roll G bends the shot diet → Roll H penalizes the make rate.

**What shipped (1 file — 2 surgical line changes):**
- `Harness/Program.cs` — two substitutions only:
  - `ObservationRunV1` (~line 4299): `new RollEStubPieGenerator(cfgE),` → `new RollEGenerator(cfgE, game),  // Phase 19: attribute-driven usage selection`
  - `StressTestArchetypeRosters` (~line 9423): same swap, deeper indentation.
  - Diff: exactly 2 changed lines. Brace balance verified (2170/2170).
  - Stub count: baseline 21 → 19 remaining; real count: 2 → 4.

**Sites that stayed stub (19 remaining):** all isolation checks (which assert flat, known selection shares) and `RunGame` (the dev-only single-game printer). Both are correct: isolation checks need a flat pie to assert against; `RunGame`'s fidelity question is open (see below).

**No Monte Carlo this session.** No new math was introduced. `RollEGenerator`'s selection and pressure math shipped in Phase 15/17 and is proven by `RollESelectionBatchCheck` and `Phase17UsageEfficiencyCheck`, both of which run every harness pass.

**Harness results — ALL CHECKS PASSED:**
- All Phase 1–18 isolated checks: green and unchanged. None route through the two swapped sites.
- `RollESelectionBatchCheck`: green — confirms `RollEGenerator` itself is untouched.
- `Phase17UsageEfficiencyCheck`: green — confirms usage→efficiency math is untouched.
- `ObservationRunV1`: mechanics OK, zero parks, zero throws, scoring reconciled, count invariant held.
- `StressTestArchetypeRosters`: all 8 buckets, validGames=500/500. Mirror gap 3.4% (small).

**Key observations (record; do not grade):**

*ObservationRunV1 (frozen corpus, 1000 games):*
- PPP combined: 1.072, FG% 50.3% — both within a realistic D1 band.
- Shot mix: Rim 33%, Short 17%, Mid 17%, Long 10%, Three 23%.

*StarVsBalanced — the most informative bucket:*
- Team A (star) wins 60.4%, PPP 1.041 vs 0.987. The star team benefits net.
- Shot mix shift visible: star team Rim 36% / Three 23% vs balanced team Rim 29% / Three 26%. The alpha is commanding a larger share of rim attempts — exactly what the usage-weighted selection pie predicts.
- The two effects that were uncertain before the run (usage concentration raises efficiency by routing to a better scorer; efficiency penalty lowers it) resolved in favor of the star team. Whether that net is calibrated correctly is a separate question; the direction is basketball-coherent.

*Mirror gap (Buckets 7/8 — AthleticVsSkill):* 3.4% — consistent with a side-neutral engine.

**Named finding from this run — FreeThrow attribute authoring:** FT% in the average and weak stress-test buckets (AverageVsAverage ~53%, WeakVsWeak ~47–48%) is far below real basketball (~68–70% D1 average). The cause: the archetype roster generator scales FreeThrow down with the talent tier, which is wrong. FreeThrow is **completely independent of athletic ability and overall skill level** — not merely roughly independent. The only real relationship in the attribute is a weak positive correlation with Outside shooting (shared shooting mechanics), but the best free-throw shooter in the world could be playing D3. No other attribute in the model works this way. The fix: FreeThrow should be drawn from its own independent distribution in the archetype generator, regardless of tier, with at most a mild skew toward high-Outside players. This is a calibration/authoring session in its own right; nothing in the engine changes.

**Open question (journal only — not acted on):** `RunGame` (the dev-only printer, not in the `ok &=` chain) now has an arbitrary fidelity mix: Roll L real (Phase 18), Roll E stub (this session leaves it), everything else stub. Whether it should be consistently all-stub (a clean flat-pie baseline demo) or consistently production-like (matching the observation run) is Emmett's design call.

**Git commit:** `Phase 19: Roll E live in ObservationRunV1 + StressTest (usage→efficiency now fires at game scale)`

## Session 53 — Phase 18: Roll L Real Generator (FreeThrow attribute wired) (2026-06-17)

**Scope:** Replace `RollLStubPieGenerator` with an attribute-driven `RollLGenerator` that reads the shooter's `FreeThrow` rating and uses it directly as the make probability (`FreeThrow / 100.0`). Introduce `IRollLPieGenerator` so the Resolver field is typed to an interface rather than the concrete stub. Update the four real-game construction sites in the harness to use the real generator. All other construction sites keep the stub.

**What shipped (5 files — 2 new + 3 edits):**
- `Generators/IRollLPieGenerator.cs` (NEW) — interface mirroring `IRollMPieGenerator`. Single one-arg method `Pie<FreeThrowOutcome> Generate(PossessionState state)`. Null-slot and unpopulated-slot fallback behavior documented explicitly: both fall back to `config.MakeProbability` (72%), not to a throw. The stub ignores the state parameter entirely; the real generator reads `SelectedSlot` off it.
- `Generators/RollLGenerator.cs` (NEW) — the real, attribute-driven generator. Ctor `(RollLConfig config, GameState game)`. `Generate(state)` walks `game.RosterFor(state.Offense).PlayerAt(state.SelectedSlot.Value)` when `SelectedSlot` is non-null and the slot is populated; uses `player.FreeThrow / 100.0` as the make probability directly. Two fallback paths use `config.MakeProbability`: null slot (bonus foul before Roll E ran, shooter unknown) and unpopulated slot (isolation-test game). `RoadMakePenalty` is a documented seam — NOT read, not applied. `Math.Clamp` applied as a safety net before building the pie. `Player.Validate()` is the upstream guard for invalid authored ratings; the clamp is the last resort.
- `Generators/RollLStubPieGenerator.cs` (edit) — adds `: IRollLPieGenerator`. Changes `Generate()` → `Generate(PossessionState state)`; `state` is accepted but intentionally ignored (documented). No behavior change.
- `Core/Resolver.cs` (edit) — three surgical changes: `_rollLGenerator` field retyped from `RollLStubPieGenerator` to `IRollLPieGenerator`; ctor param likewise; `DriveFreeThrows` call site `_rollLGenerator.Generate()` → `_rollLGenerator.Generate(state)`. `DriveFreeThrows` already received `state` as its first parameter — no signature change needed.
- `Harness/Program.cs` (edit) — four generator swaps; stub signature fix; extended check; null-slot fallback notes. In detail:
  - **Main (~line 43):** Stub declaration removed from before game construction; `new RollLGenerator(cfgL, game)` added after `SeatStartersFromConfig` alongside other real generators (matching the Phase 11/13/14 pattern — `game` must exist first).
  - **RunGame (~line 4083):** `new RollLStubPieGenerator(RollLConfig.Load(configPath))` → `new RollLGenerator(RollLConfig.Load(configPath), game)`.
  - **ObservationRunV1 (~line 4306):** `new RollLStubPieGenerator(cfgL)` → `new RollLGenerator(cfgL, game)`.
  - **StressTestArchetypeRosters (~line 9427):** `new RollLStubPieGenerator(cfgL)` → `new RollLGenerator(cfgL, game)`.
  - **`RollLFreeThrowCheck` part-a stub call (~line 3226):** `new RollLStubPieGenerator(cfgL).Generate()` → `new RollLStubPieGenerator(cfgL).Generate(new PossessionState(...))` — a signature fix, not a generator swap; keeps the stub.
  - **`RollLFreeThrowCheck` part-b (extended):** new section proving the real generator: p72 (FreeThrow=72) → make=0.720000; p85 (FreeThrow=85) → make=0.850000; null-slot fallback → make=config.MakeProbability (0.720000); empty-slot fallback → make=config.MakeProbability. All four confirm within epsilon; `realGenOk` folds into the check's return value.
  - **Null-slot fallback note:** added after the FT% histogram in `ObservationRunV1` and after the FT% Team B lines in each `StressTestArchetypeRosters` bucket: *"FT% reflects authored FreeThrow ratings where SelectedSlot is non-null. Bonus trips before Roll E retain the config.MakeProbability (72%) fallback. This is a named remaining loose end — not a bug."*
  - **All 18 remaining stub sites** (isolation checks constructing empty-roster GameState objects) keep the stub. Confirmed: 22 total construction sites audited; 4 swapped; 18 retained.

**The FreeThrow attribute is unique in the model.** Every other attribute is relative (50 = average). FreeThrow is absolute: the authored value IS the make percentage × 100. A 72-rated shooter makes exactly 72% of free throws. No logistic, no matchup, no context modifier of any kind. This is the cleanest 1:1 in the model.

**`RoadMakePenalty` remains dormant and unread.** The `RollLConfig` field exists as a documented seam (set to 0.0). The real generator does NOT read it — not even conditionally. Home/road free-throw effects are outside Phase 18. The principle: do not introduce a contextual modifier in the same build that establishes FreeThrow as an absolute attribute.

**The null-slot fallback is a named loose end, not a bug.** Bonus free throws that fire before Roll E has selected a shooter arrive with `SelectedSlot = null`. The generator falls back to `config.MakeProbability` (72%) for these trips. This means the reported FT% is a blend of player-attributed ratings (shooting-foul trips and post-Roll-E bonus trips) and the 72% flat fallback (pre-Roll-E bonus trips). The fallback note in the observation run and stress test makes this visible rather than hidden contamination.

**Python Monte Carlo (run before any C#):** all six test ratings (45, 60, 72, 80, 90, 99) confirmed `make = rating / 100.0`, `miss = 1.0 − make`, both in [0.0, 1.0], sum == 1.0. Null-slot and unpopulated-slot fallbacks both produce valid pies. Clamp boundary tests pass.

**Harness results — ALL CHECKS PASSED:**
- `RollLFreeThrowCheck` part (b): p72 0.720000 ✓, p85 0.850000 ✓, null-slot fallback 0.720000 ✓, empty-slot fallback 0.720000 ✓
- All existing part (a) and trip-type checks: unchanged (still use the stub; rates stay at 0.72)
- Observation run FT%: shifted from flat 0.72 toward test roster's authored FreeThrow ratings; fallback note printed
- Stress test: all 8 buckets passed, validGames=500/500 in all buckets; FT% per bucket reflects archetype FreeThrow ratings on attributed trips; fallback note printed in each bucket's cross-bucket summary; mirror gap 1.4% (tight)
- Zero parks, zero throws, mechanics OK across all runs

**FT% readings from the harness (selected):**
- AverageVsAverage: FT% ~53% (average-archetype FreeThrow ratings in the ~50s range)
- EliteVsWeak: Elite team ~68%, Weak team ~47% — clear separation driven by authored ratings
- EliteVsElite: both teams ~64–65%
- ShootingVsAthletic: Shooting team ~60%, Athletic team ~47% (different archetype FreeThrow ratings)
- FT% differentiates meaningfully across all 8 buckets — the attribute is live and working

**NOT changed:** `RollL.cs`, `RollLConfig.cs`, `config.json`, any other engine roll/config/generator.

**Git commit:** `Phase 18: Roll L real generator — FreeThrow attribute wired (rating / 100 direct)`

## Session 52 — Phase 17: Usage → Efficiency Curve (2026-06-16)

**Scope:** Wire the Roll E volume pressure into Roll G and Roll H so that a shooter carrying above an equal share pays a real efficiency cost. Two new per-possession facts stamped (`UsagePressure` at Roll E, `UsageResidualPressure` at Roll G`); Roll H reads both to apply a volume-tax and a residual-penalty term before `BuildRealPie`. FastBreak is fully exempt throughout.

**What shipped:**
- `Core/PossessionState.cs` — two new nullable fields: `UsagePressure` (stamped by Roll E alongside `SelectedSlot`) and `UsageResidualPressure` (stamped by Roll G alongside `ShotType`). Both null until their respective rolls run; both cleared to null by Roll K's `ResetOffense` `with`-expression (leak guard). Detailed XML docs on each field including the leak-guard rationale.
- `Generators/IRollEGenerationProvider.cs` (NEW) — derived interface extending `IRollEPieGenerator` with `GenerateWithPressure(state) → RollEGeneration`. The `RollEGeneration` record carries `(Pie, FinalShares[], Pressures[])`. Resolver field/ctor type widened to this interface so all 20 harness Resolver construction sites compile without change.
- `Generators/IRollGGenerationProvider.cs` (NEW) — derived interface extending `IRollGPieGenerator` with `GenerateWithResidual(state) → RollGGeneration`. The `RollGGeneration` record carries `(Pie, ResidualPressure)`. Resolver field/ctor type widened to this interface.
- `Generators/RollEGenerator.cs` — implements `IRollEGenerationProvider`. Old `Generate` body promoted to `GenerateWithPressure`; pressure computed inline in the same one-pass calculation as the share math. `equalShare = 1.0 / populated`; `pressure[i] = max(0, finalShares[i] − equalShare)`. FastBreak and zero-populated paths both return zero-pressure arrays. `Generate` delegates to `GenerateWithPressure(..).Pie`.
- `Generators/RollEStubPieGenerator.cs` — implements `IRollEGenerationProvider`. Adds `GenerateWithPressure` returning the existing flat pie plus all-zeros pressures (stub has no usage concentration).
- `Rolls/RollE.cs` — `Execute` gains `double[] pressures` parameter (between pie and game). Stamps both `SelectedSlot` and `UsagePressure = pressures[slotNumber - 1]` in one atomic `with`.
- `Generators/RollGGenerator.cs` — implements `IRollGGenerationProvider`. Old `Generate` body promoted to `GenerateWithResidual`. New private `ApplyDietShift(state, shooter, bentNorm[])` method inserted after matchup multiply+renormalize, before building the final pie. `Generate` delegates to `GenerateWithResidual(..).Pie`. Zero-pressure short-circuit: when `UsagePressure` is null or 0.0 the method is a branch-skip — numerically identical to pre-build behavior. Zero-defender fallback path now applies the diet shift (implementer's call: the load is real even when defensive data is absent).
- `Generators/RollGStubPieGenerator.cs` — implements `IRollGGenerationProvider`. Adds `GenerateWithResidual` returning the existing flat pie plus residual 0.0.
- `Rolls/RollG.cs` — `Execute` gains `double residualPressure` parameter (after pie). Stamps both `ShotType` and `UsageResidualPressure` in one atomic `with`.
- `Rolls/RollK.cs` — `ResetOffense` `with`-expression extended with `UsagePressure = null, UsageResidualPressure = null`.
- `Core/Resolver.cs` — `_rollEGenerator` and `_rollGGenerator` field types widened to provider interfaces. Constructor params widened to match. Both Roll E call sites (halfcourt + break path) updated to call `GenerateWithPressure` and pass `genE.Pressures` to `RollE.Execute`. Roll G call site updated to call `GenerateWithResidual` and pass `genG.ResidualPressure` to `RollG.Execute`.
- `Config/RollGConfig.cs` — two new fields: `PressureShiftScale` (requestedShift = pressure × scale; default 0.5) and `PressureShiftCapFraction` (cap on bent-dominant zone mass moved; default 0.8). Invariants on both validated at load.
- `Config/RollHConfig.cs` — two new fields: `PressureVolumeTaxScale` (makePct *= (1 − pressure × scale); default 0.12) and `PressureResidualPenaltyScale` (makePct -= residual × scale; default 2.0). Invariants validated at load.
- `Generators/RollHGenerator.cs` — Phase 17 block inserted after matchup logistic, before `BuildRealPie`. Reads `state.UsagePressure` and `state.UsageResidualPressure` (null → 0.0). Branch-skipped when both are zero. Applies (b) volume-tax and (c) residual-penalty in sequence; clamps to [0, 1].
- `Harness/config.json` — four new keys: `RollG.PressureShiftScale: 0.5`, `RollG.PressureShiftCapFraction: 0.8`, `RollH.PressureVolumeTaxScale: 0.12`, `RollH.PressureResidualPenaltyScale: 2.0`. All calibration placeholders.
- `Harness/Program.cs` — Phase 17 check (`Phase17UsageEfficiencyCheck`) added and called. `RollESpyGenerator` upgraded from `IRollEPieGenerator` to `IRollEGenerationProvider` (adds stub `GenerateWithPressure`). All ~13 direct `RollE.Execute` call sites in legacy phase checks updated to pass `new double[5]` as the new pressures arg. All ~8 direct `RollG.Execute` call sites updated to pass `0.0` as the new residual arg.

**Diet-shift math (Roll G `ApplyDietShift`):**
1. Normalize authored tendencies to [0,1] (mandatory — the 0–99 scale is 100× off without this).
2. `intrinsicCapacity = 1 − authoredNorm[dominantIdx]` — how much the player *can* diversify.
3. `requestedShift = pressure × PressureShiftScale`.
4. `availableMass = bentDomMass × PressureShiftCapFraction` — cap so dominant zone is never emptied.
5. **Zero-destination guard:** if all non-dominant bent zones sum ≤ ε, absorbed = 0 (nowhere to redistribute; full residual — no crash, no silent mis-count).
6. `absorbed = min(requestedShift, intrinsicCapacity, availableMass)`.
7. `residual = requestedShift − absorbed`.
8. Remove `absorbed` from dominant zone, redistribute proportionally to others by their bent-profile weight.
9. Renormalize (floating-point safety).

**Efficiency penalty math (Roll H):**
- (b) Volume-tax: `makePct *= (1 − pressure × PressureVolumeTaxScale)` — small all-shots reduction.
- (c) Residual penalty: `makePct -= residual × PressureResidualPenaltyScale` — larger reduction for load that couldn't shift. Applied to the actual zone taken (Shaq's penalty lands on Rim).
- Clamp to [0, 1] after both terms.

**The specialist/versatile split (validated in Monte Carlo and harness):**
- Specialist (RimTendency 90, others near zero): intrinsicCapacity ≈ 0.10. Requested shift for rail-level pressure (0.32): 0.16. Absorbed ≈ 0.10 (hits capacity). Residual ≈ 0.06. Drop: ~2.5pts vol-tax + ~12.0pts residual = **~14pts**. Mix barely moves.
- Versatile player (spread tendencies): intrinsicCapacity ≈ 0.77. Absorbed ≈ 0.16 (fully absorbed). Residual ≈ 0. Drop: ~2.1pts vol-tax + ~0 = **~2pts**. Mix redistributes away from dominant zone.

**The clamp threshold.** The bent-mass clamp only fires when `bentDom < requestedShift / PressureShiftCapFraction = 0.16 / 0.8 = 0.20`. This requires a defense to have nearly completely neutralized the shooter's dominant zone — extreme disruption. Correct behavior: the clamp is a guard against edge cases, not a routine path.

**Four config dials (all calibration placeholders — edit in config.json to tune):**
| Field | Location | Effect | Default |
|---|---|---|---|
| `PressureShiftScale` | RollGConfig | Diet-shift magnitude | 0.5 |
| `PressureShiftCapFraction` | RollGConfig | Max fraction of dominant zone moved | 0.8 |
| `PressureVolumeTaxScale` | RollHConfig | Vol-tax multiplier | 0.12 |
| `PressureResidualPenaltyScale` | RollHConfig | Residual-penalty multiplier | 2.0 |
Setting `PressureShiftScale = 0` ablates the diet shift entirely (residual always 0, only vol-tax fires). Setting all four to 0 ablates Phase 17 completely while leaving the plumbing intact.

**Bug fixed mid-session (stale file):** A stray `Generators/RollE.cs` file from an earlier session was still in the repo, causing CS0101 duplicate-class errors after the drag-and-drop. Deleted from `Generators/` folder. Legacy direct `RollE.Execute` / `RollG.Execute` call sites in older phase checks were missing the new signature args (also not in the drag-and-drop); fixed by adding `new double[5]` / `0.0` respectively. `RollESpyGenerator` needed upgrading from `IRollEPieGenerator` to `IRollEGenerationProvider`.

**Phase 17 harness results (ALL PASSED):**
- **(a)** Zero-pressure state numerically identical to null state across all five zones ✓
- **(b)** Specialist Rim: base 66.0%, pressured 54.4%, drop **11.7pts**; residual 0.06; attribution: vol-tax 2.5pts + residual-penalty 12.0pts ✓
- **(c)** Versatile Mid: base 40.5%, pressured 38.9%, drop 1.5pts; residual ≈ 0; specialist drop (11.7pts) >> versatile drop (1.5pts) ✓
- **(d)** Below-comfort (pressure=0) unchanged across all zones ✓
- **(e)** Naturally dominant star at natural load (~0.20 pressure): 1.5pt drop confirmed ✓
- **(f)** FastBreak: Roll G residual = 0.0; Roll H make identical to null-scalar state ✓
- **(g)** Fresh state: both fields null; after Roll E stamp: `UsagePressure` non-null; after Roll G stamp: `UsageResidualPressure` non-null; after ResetOffense-style `with`: both null ✓
- All Phases 1–16 unchanged ✓

**Observation run delta (frozen-corpus-v1, 1000 games):**
FG% 50.4% → unchanged (test rosters are below-rail; the five-man roster shares are close to equal, so pressures are small). PPP and shot mix: no material change. The curve is plumbed and proven; the magnitude of the effect at game scale depends on actual usage concentration, which grows once teams have real star/role distinctions. Calibration of the four dials is deferred until realistic rosters exist.

**Git commit:** `Phase 17: usage→efficiency curve (Roll E pressure stamp, Roll G residual, Roll H penalty terms)`

## Session 51 — Roll E: Attribute-Driven Usage Selection (2026-06-16)

**Scope:** Replace Roll E's flat 20% halfcourt selection pie with a real attribute-driven generator so that a better scorer/creator naturally commands a larger share of shot attempts. Transition (FastBreak) pie left exactly as-is. One live dispatch site swapped; all ~20 harness stub-construction sites left untouched.

**What shipped:**
- `Generators/RollEGenerator.cs` (NEW) — attribute-driven `IRollEPieGenerator`. Halfcourt branch reads the five offense players via `GameState.RosterFor(state.Offense)` + `GameState.LineupFor(state.Offense)` + `Roster.PlayerAt(slot)` (Roll B access shape). FastBreak=true returns the existing transition pie byte-for-byte. Empty-roster fallback returns the flat Base* pie.
- `Config/RollEConfig.cs` — four new config fields: `UsageExponent` (tilt strength, >0), `UsageFloor` (guaranteed minimum share, ≥0), `UsageRail` (hard cap, >UsageFloor, ≤1.0), `MinUsageScore` (positivity guard, >0). Config invariants validated on load — fail loud.
- `Harness/config.json` — new RollE fields: `UsageExponent: 2.0`, `UsageFloor: 0.09`, `UsageRail: 0.52`, `MinUsageScore: 1.0`. All calibration placeholders.
- `Harness/Program.cs` — line-37 stub comment; live construction moved after `SeatStartersFromConfig` alongside Roll B/G/H/I/M/F/A: `new RollEGenerator(cfgE, game)`. `ShowSamples` parameter retyped `RollEStubPieGenerator` → `IRollEPieGenerator`. `RollESelectionBatchCheck` reworked: interface-typed, takes `cfgD`, seats a known five-man test roster (alpha + three solids + Rodman-type) in a local `checkGame`, asserts empirical convergence to the generator's own pie, asserts Alpha > 2× Rodman, asserts FastBreak pie exactly equals cfg.Transition* weights. Call site updated to pass `cfgD`.

**Usage score formula (engineering call, audited):**
`score = 0.35 * SelfCreation + 0.30 * (Close + PostMoves) / 2 + 0.35 * (Outside + Mid + Finishing) / 3`
Clamped to `max(score, MinUsageScore)` before the exponent. The (Close+PostMoves)/2 term gives a high-post-moves, high-close center meaningful usage even with modest SelfCreation — Emmett's basketball call. Passing, Playmaking, BallHandling, BasketballIQ explicitly excluded (Roll E is who *takes* the shot, not who *runs* the offense).

**Distribution shape (constrained redistribution — not naive double-renormalize):**
Exponentiated scores normalized to raw shares, then iterative floor application (pins below-floor slots, redistributes free mass proportionally to raw scores until stable), then water-fill rail iteration. Naive renorm was rejected because clamping the rail can push a low slot back under the floor after renormalization.

**Monte Carlo pre-check results (four cases, all assertions passed):**
- Realistic five-man (alpha/3 solids/Rodman): alpha 34.5%, Rodman 9.0% (at floor) ✓
- Five equals: 20.0% each ✓
- Fantasy gap (one elite, four scrubs): elite at rail (0.52), scrubs at 12% each (above floor — correctly, because (1−0.52)/4 = 0.12 > 0.09) ✓
- Post center (Close=88, PostMoves=90, moderate SelfCreation) beats a perimeter scorer with higher SelfCreation: 25.8% vs 24.2% ✓

**Rail feasibility standdown:** when `populatedCount * UsageRail < 1.0` (thin test rosters only — never in a real five-man game), the rail is skipped entirely. Normal five-man game: 5 × 0.52 = 2.6 ≥ 1.0, always active.

**The coupling note (calibration):** `UsageExponent` and `UsageFloor` are coupled — raising the floor lowers the alpha's realistic ceiling even at a fixed exponent, because floors hand teammates guaranteed shots. Calibrate them together, not independently.

**Important label:** Roll E selection share ≠ box-score USG%. Roll E fires only when a possession reaches player selection; turnovers/violations before Roll E peel possessions off, and USG% folds in FTA and weights turnovers differently. Reconciling Roll E share with true USG% is a later calibration job.

**Observation run delta:** aggregate stats (PPP, FG%, pace, etc.) are identical to Session 50. Correct — Roll E changes *who* gets the ball, not *what happens* when they do. That delta arrives with the usage→efficiency curve (the next build). The non-flat selection is plumbed, proven, and waiting.

**Bug fixed mid-session:** `RollESelectionBatchCheck` ordering assertion looked up `SelectionOutcome.Slot4` (the fourth solid, 18.6%) instead of `SelectionOutcome.Slot5` (the Rodman, 9.0%). Off-by-one in the enum key. Fixed; second harness run passed.

**Harness result — ALL CHECKS PASSED (second run):**
- Roll E batch: Slot1 33.951%, Slot2 20.451%, Slot3 18.012%, Slot4 18.593%, Slot5 8.993% — all converge to generator's own pie ✓
- Ordering: Alpha (33.951%) > 2× Rodman (8.993%) ✓
- FastBreak: transition pie exactly equals cfg.Transition* weights ✓
- All ~20 stub-backed checks: unchanged ✓
- Full-game batch closure: `ended + routed-to-stub == BatchSize`, `unrouted == 0` ✓
- All Phase 1–16 checks green ✓

**Git commit:** `Roll E: attribute-driven usage selection (halfcourt); transition unchanged`

## Session 50 — Per-zone shooting counters + shooting-curve calibration (2026-06-16)

**Scope:** Three parts, one conversation. (1) Build: extend the v1 counters with a make/attempt pair per shot zone so the harness reports FG% and attempt share for Rim/Short/Mid/Long/Three separately (Three reuses the existing 3PA/3PM pair). Additive only. (2) Design conversation: with the per-zone data in hand, settle the shooting make-curve calibration. (3) Execute that calibration in-session (Emmett's call — not a separate session): re-fit the five logistic make curves, validate, ship. One harness check (Phase 6f) needed its magnitude threshold relaxed to match the now-flatter curve.

**What shipped (counters):**
- `Core/Resolver.cs` — `RoutingOutcome` gains 8 new `init`-only fields (`RimFga`/`RimFgm`, `ShortFga`/`ShortFgm`, `MidFga`/`MidFgm`, `LongFga`/`LongFgm`), 0 defaults. `Route` bins each resolved shot into its zone at the single `IntoShotResolution` chokepoint (the v1 FGA-tally site), switching on `ShotLocation`. Three keeps `ThreePa`/`ThreePm`.
- `Core/Governor.cs` — `PossessionRecord` gains the same 8 parameters (default 0); `Run` threads them through.
- `Harness/Program.cs` — new `SHOOTING BY ZONE (combined, per game)` section: FG% by zone and attempt share by zone. Two new mechanical checks: zone attempts sum to FGA, zone makes sum to FGM. DEFERRED section trimmed — only press frequency/break rate remains.

**What shipped (calibration — executed in-session):**
- `Config/RollHConfig.cs` — all five per-zone logistic make curves re-fit to the agreed observed-FG% anchors (Floor/Ceiling/K/Midpoint each). Final values: Three 0.1608/0.6328/0.029646/65.8067; Long 0.1934/0.6034/0.034190/59.5793; Mid 0.1042/0.6447/0.021592/42.3369; Short 0.1316/0.7045/0.021592/42.3369; Rim 0.3582/0.9527/0.024666/43.9840. `config.json` does not override these, so editing the class defaults IS the calibration.
- **Carve correction:** the logistic outputs the *clean* make rate (given not blocked/not fouled); the harness reports *observed* FG% AFTER the block/foul carve. Anchors were inverted through each zone's block+foul rates so the observed number lands on target — most visibly at Rim, whose make ceiling is raised to ~0.95 to net ~73% observed after its large carve. Flagged in the file: if Roll H block/foul baselines change, the rim/short make anchors must be re-derived.
- **Long ≥ Three:** Long's rating-99 even anchor nudged 49→51% so a long two stays at or above a three at every rating (the stated gradient).
- `Harness/Program.cs` — Phase 6 (f) make-drop margin relaxed 0.05 → 0.03. On the flattened curve a *skill-only* strong defender (PerimD 90 vs a 50 shooter, even athleticism) lowers the three by ~4.9 points by design; the old 0.05 floor assumed the retired steep curve. Direction (strong defender lowers make) is what the check guards.

**New archive entries:** `docs/observations.md` — Run 3 (pre-calibration baseline, FG% 57.8%) and Run 4 (post-calibration, FG% 50.4%) both present; the before/after pair is the calibration record.

**Harness result — ALL CHECKS PASSED:**
- All v1/v2 + Phase 1–16 checks green ✓
- Zone-attempt bin (`Rim+Short+Mid+Long+Three == FGA`) and zone-make bin (`== FGM`) passed 1,000/1,000 ✓
- Phase 6 (f) reads OK: even 33.5%, strong-defender 28.6% — 4.9-pt drop clears the 3.0 floor ✓

**Result — calibration landed where predicted (Python pre-check → harness, near-exact):**
- Combined FG% 57.8% → **50.4%** (predicted 50.6). A rating-50 roster nets 45.1% (real D1 average); the test rosters read ~50% because their shooting ratings average ~64–67 (above average). The elevation is "above-average rosters," not "hot curve."
- Per-zone FG% (was → now): Rim 67.9→64.5, Short 64.5→48.3, Mid 49.3→42.3, Long 48.5→41.9, Three 49.7→41.7. Gradient now real: Three < Long < Mid < Short < Rim.
- Combined PPP 1.19 → **1.08** (realistic). FT%, shot mix, ORB%, FTr unchanged (calibration touched only the make curves).

**Design conversation — what was settled (full detail in design.md):**
- **50 is absolute average** on the 1–99 scale; a 50 shooter vs a 50 defender cancels to the zone target. Level-flat: one curve all divisions, distributions differ.
- **Old curves were centered right but too steep** (99-three ~62%, 1-three ~6% even-matchup, ~2–3× real spread). Calibration flattened all five (floors up, ceilings down, 50-anchor held). A 16-point rating gap now ≈ 5% make — inside season noise.
- **Athletic > skill for suppressing shooters** — confirmed the engine already encodes this (DEC-5: physical gap exponent 2.7 > skill 2.0). A skill-only strong defender only nudges a good shooter (Outside 78: 46→43% vs an elite-skill/even-athlete defender); real athletic separation drags him down (→39% at a 40-pt athletic gap). The axes cross at a 25-pt gap (= ReferenceScale); below that they are comparable. Lever noted if Emmett ever wants athletic to dominate at moderate gaps too.
- **Era lives in the shot mix, not these curves** (Roll G profile swapped later).
- **The real at-scale target is a healthy strategy space**, which only manifests with 350 unequal teams; small-scale tuning has diminishing returns.

**Validation honesty:** curves fit + Monte-Carlo-traced in the sandbox, then confirmed green on Emmett's harness. The carve inversion and game prediction were re-derived straight from the edited `.cs` to rule out a transcription slip.

**Latent note (not fixed):** the observation "config hash" is over `config.json` only, so Run 3 and Run 4 share a hash despite different make curves (the calibration lives in `.cs` defaults). Flagged for a future "fingerprint the resolved config" pass.

**Git commit:** Emmett stamps — one commit banks the counters, the calibration, and the Phase 6f threshold fix.

## Session 49 — Counter Plumbing v1: shooting + rebounding sentinels (2026-06-16)

**Scope:** Add per-possession shot and rebound counters at the resolver's existing scoring sites, thread them through to `PossessionRecord`, and emit a v2 observation block that reports the shooting splits, shot mix, ORB%, and FTr that the v1 block deferred. No probability weights moved. No outcome routing changed. The engine records more about itself; it does not play differently.

**What shipped:**
- `Core/Resolver.cs` — `RoutingOutcome` gains 10 new `init`-only fields (Fga, Fgm, ThreePa, ThreePm, ShotResolutions, MissFouled, Fta, Ftm, OrbChances, OrbWon) with 0 defaults; every existing positional construction untouched. `Route` initializes 10 matching locals and tallies at five sites: (1) `IntoShotResolution` — FGA/FGM/3PA/3PM/ShotResolutions/MissFouled immediately after `RollH.Execute`; (2) `ResolveFreeThrows` — FTA/FTM from `bonusFtSpins/bonusFtPoints`; (3) `ResolveShootingFreeThrows` — FTA/FTM from `shootingFtSpins/shootingFtPoints`; (4) `ResolveRebound` — ORB chance/won after `RollI.Execute`; (5) `ResolveFTRebound` — ORB chance/won after `RollM.Execute`.
- `Core/Governor.cs` — `PossessionRecord` gains the same 10 parameters (default 0, XML-documented); `Run` zero-initializes them before the NoShot/else branch and extracts them from `outcome.*` in the else branch; passes all 10 to the `PossessionRecord` constructor.
- `Harness/Program.cs` — two new mechanical checks per game in the observation loop: (1) counter reconciliation (`Points == 2*(FGM−3PM) + 3*3PM + FTM`); (2) denominator guard (`FGA + MissFouled == ShotResolutions`) plus five counter-sanity inequalities. Four new sentinel sections: SHOOTING SPLITS (FG%/3P%/FT% per side + combined, histograms), SHOT MIX (3PA rate), ORB%, FTr. DEFERRED section updated: FG%/3P%/FT%/ORB%/FTr removed; press frequency/break rate and full shot mix remain.

**New archive entry:** `docs/observations.md` — Run 2 prepended.

**Harness result — ALL CHECKS PASSED:**
- All v1 mechanical checks still green ✓ (no behavior leaked)
- Counter reconciliation (`Points == 2*(FGM−3PM) + 3*3PM + FTM`) passed 1,000/1,000 games ✓
- Denominator guard (`FGA + MissFouled == ShotResolutions`) passed 1,000/1,000 games ✓
- Counter sanity (FGM ≤ FGA, 3PM ≤ 3PA, etc.) passed 1,000/1,000 games ✓
- All v2 sections appeared with finite, in-range numbers ✓

**First readings — v2 sentinels (recorded, not judged; calibration is later):**
- FG% ~57.8% — well above real D1 (~44–46%). Primary calibration target.
- 3P% ~49.7% — well above real D1 (~33–36%). Same root cause: uncalibrated make pies.
- FT% ~72.1% — matches configured Roll L make rate (72.0%) exactly. Not a calibration problem.
- 3PA rate ~25.3% — somewhat below real D1 (~35–40%). Shot mix calibration target.
- ORB% ~27.8% — close to real D1 (~28–30%). Encouraging.
- FTr ~40.3% — above real D1 (~30–35%).
- All v1 sentinels byte-identical to Run 1 — confirms counters are passive.

**Python Monte Carlo:** 23/23 reconciliation cases passed before C# delivery.

**Key call recorded (Emmett):** ORB% reporting is combined-only (no per-source FG-miss/block/FT breakdown printed). The counters accumulate a single `OrbChances`/`OrbWon` pair across all three sources.

**Git commit:** Emmett stamps.

## Session 48 — Observation Run v1: macro sentinel harness (2026-06-16)

**Scope:** Stand up a repeatable observation harness. Runs N full games against a frozen scenario corpus and emits one self-describing macro-sentinel block. Recorded, not judged. No engine changes, no config changes, no calibration.

**What shipped:**
- `Harness/Program.cs` — `ObservationRunV1` method and helpers (`ObsBucket`, `ObsPrintI`, `ObsPrintD`, `ObsHistI`, `ObsHistD`); wired into `Main` after all existing phase checks; runs 1,000 full games using the real matchup-aware generators (A, B, F, G, H, I, M) and stub generators for all others.

**New file:** `docs/observations.md` — flight recorder archive; Run 1 (frozen-corpus-v1) stamped.

**Harness result — 1,000/1,000 games completed, all mechanics green:**
- Scoring reconciled per game ✓
- Count invariant held (TerminalEnded + Parked + NoShot == total) ✓
- Zero parks — engine chain through Roll M is complete and wired ✓
- No throws ✓
- Loose sanity (PPP ∈ (0,3), pace ∈ (0,200)) ✓

**First readings (observations only — not calibration targets):**
- Pace: mean=133.3 total / ~67 per team — realistic (real D1 is ~65–72/team). Engine lands here naturally without calibration.
- Combined PPP: 1.19 (real D1 ~1.0–1.1). Somewhat high; the more notable number going into calibration.
- Combined PPP: 1.19 (real D1 ~1.0–1.1). Logged.
- APL: 18.0s. Stable; matches prior single-game reading.
- Fouls: ~11.3 Home / ~11.5 Away per game.
- Zero parks confirmed.

**Discipline enforced:** No config values were changed. The ~2× pace reading is logged as an observation. Calibration is a later, gated phase — the identical failure mode that sank prior attempts.

**Git commit:** Emmett stamps.

## Session 47 — Phase 16: press-break fast break (2026-06-16)

**Scope:** When Roll A returns `CleanEntry` under `PressMode.Standard`, stamp `FastBreak=true`, consume the press stamp (`PressMode=None`), skip Roll B, route directly to Roll E. Roll G returns a flat rim-heavy pie when `FastBreak=true`. Dead-ball re-inbounds enforce state hygiene.

**Pre-build audit findings (both decided/resolved):**
- **Issue 1 — Missing `IRollEPieGenerator` interface.** `RollEStubPieGenerator` was a sealed concrete class with no interface; the Resolver held its field as the concrete type, blocking test injection. Resolution: create `Generators/IRollEPieGenerator.cs`, add `: IRollEPieGenerator` to `RollEStubPieGenerator`, retype the Resolver field and ctor param — mirrors the existing `IRollBPieGenerator` pattern exactly. Engineering judgment: right call long-term; the real Roll E generator drops in with zero Resolver changes.
- **Issue 2 — Prompt spy used wrong type `Pie<Slot>`.** The correct type is `Pie<SelectionOutcome>`. Fixed silently in Program.cs.

**What shipped:**
- `Generators/IRollEPieGenerator.cs` — new interface (one method: `Generate(PossessionState state)`)
- `Generators/RollEStubPieGenerator.cs` — adds `: IRollEPieGenerator`
- `Core/Resolver.cs` — field + ctor param retyped to interface; `IntoHalfcourtSet` press-break gate (Standard → skip Roll B, call Roll E directly with `FastBreak=true, PressMode=None`); `ResumeInbound` conditional clear (frontcourt clears both, backcourt preserves); `ResolveSidelineInbound` unconditional clear (dead ball ends all markers)
- `Config/RollGConfig.cs` — five `FastBreak*` auto-properties (sum-to-1.0 load invariant)
- `Generators/RollGGenerator.cs` — FastBreak gate after null-guard, before shooter read; `BuildFastBreakPie()` helper
- `Harness/config.json` — five `FastBreak*` keys under `RollG` (sum=1.00)
- `Harness/Program.cs` — Phase 15 test 7c updated (ResolveSidelineInbound now asserts PressMode=None at Roll A, not Standard); Phase 16 check added (8 sub-tests); three new nested spy classes: `FullStateRollASpyGenerator`, `RollESpyGenerator`, `AlwaysProceedRollBGenerator`; `PressModeSpyGenerator` restored

**Phase 16 harness check — 8 sub-tests:**
1. Standard + IntoHalfcourtSet → Roll E sees FastBreak=true AND PressMode=None
2. None + IntoHalfcourtSet → Roll B fires, Roll E sees FastBreak=false
3. PressMode consumed — second IntoHalfcourtSet (None) routes Roll B, not press-break
4. ResolveSidelineInbound clears both markers (Roll A spy)
5. ResumeInbound frontcourt clears both markers
6. ResumeInbound backcourt preserves PressMode=Standard
7. Roll G FastBreak=true → flat cfg-value pie for both rim-dominant and three-dominant shooters; non-FastBreak pies differ
8. 1000-possession smoke with AwayPressFreq=10.0, unrouted==0

**Python Monte Carlo validation:** All 7 logic gates passed before C# delivery.

**Key design clarifications locked:**
- `IntoHalfcourtSet` is the only gate site — backcourt CleanEntry stamps `FastBreak=true` and `PressMode=None` simultaneously
- `ResumeInbound` semantic: frontcourt dead ball (refs blow whistle on the offensive side of halfcourt) clears both; backcourt foul preserves the live press
- `ResolveSidelineInbound` always clears: any sideline throw-in ends the break context
- `RollGStubPieGenerator` remains FastBreak-blind by design (stub path untouched)
- Phase 15 test 7c corrected: previous assertion was wrong (`PressMode.Standard` at Roll A after a sideline inbound); Phase 16 makes the correct behavior explicit

**Git commit:** `"Phase 16: press-break fast break, state hygiene on re-inbounds"`

## Session 46 — Phase 15: press frequency + Standard mode reframe (Roll A) (2026-06-16)

**Scope:** Reframe the Phase 14 full-court press from a continuous intensity dial into a per-possession frequency decision plus a fixed Standard press mode. The three-gap matchup turnover model (skill + athleticism + size) from Phase 14 is re-pointed, not rebuilt — preserve its shape. Back-end break (Phase 16) explicitly deferred.

**The reframe in one sentence.** The 1–10 dial now means *how often* you press (frequency), not *how hard*. The per-possession press/no-press decision lives above the pure generator, stamped on `PossessionState.PressMode` by the Resolver before `Generate` is called. When it fires, the possession runs in **Standard** mode — a fixed lift plus the three-gap matchup — rather than a dial-blended average.

**What changed from Phase 14.**
- `HomeFullCourtPress` / `AwayFullCourtPress` → `HomePressFrequency` / `AwayPressFrequency` (default 1.0, LOW — most teams don't full-court press as a base strategy).
- `FullCourtPressFor(side)` → `PressProbabilityFor(side)`: linear interpolation from `PressProbabilityAtOne` (0.05) to `PressProbabilityAtTen` (0.80).
- `RollATurnoverCeiling/Floor/…` → `StandardTurnoverCeiling/Floor/…` (same values, renamed).
- `RollASkillWeight / RollAAthleticismWeight / RollASizeWeight` → `StandardSkillWeight / StandardAthleticismWeight / StandardSizeWeight`.
- New props: `PressProbabilityAtOne=0.05`, `PressProbabilityAtTen=0.80`, `StandardLift=0.5`, `StandardGate=0.5`.
- Phase 14's `pressureLift`/`pressureGate` (pUnit, PressureNeutral/Scale normalization) are gone. Standard mode uses `cfg.StandardLift` and `cfg.StandardGate` — fixed config constants, not functions of the dial.

**The press/no-press roll.** In `Resolver.RunPossession`, the Roll A else-branch now stamps PressMode BEFORE calling Generate:
```
var probability = _matchup.PressProbabilityFor(start.Defense);
var mode        = _rng.NextUnitInterval() < probability ? PressMode.Standard : PressMode.None;
start           = start with { PressMode = mode };
```
The Resolver adds `MatchupConfig matchup` to its constructor (between `offensiveFoulGenerator` and `game`). All 9 `new Resolver(...)` sites updated: the main site passes `cfgMatchup`; the other 8 pass `MatchupConfig.Load(configPath)`.

**Standard mode pie — fixed lift + gated matchup.** When `PressMode == Standard`:
- Turnover: `disruptionShift = StandardLift + StandardGate × (skillWeight·skillShift + athWeight·athShift + sizeWeight·sizeShift)`. All three tanh saturation calls still use `FullCourtPressReferenceShift`.
- DefFoul: `StandardLift` only (no gap terms). `StandardDefFoulCeiling / Floor`.
- OffFoul: `StandardLift` only (no gap terms). `StandardOffFoulCeiling / Floor`.
- `JumpBall` pinned flat. `CleanEntry` absorbs complement.

The key change from Phase 14: there is no `pressureGate = max(0, pUnit)` anymore. The gate is replaced by `StandardGate` — a fixed constant. The matchup always contributes in Standard mode (not conditional on press sign); the *whether to press* decision already happened upstream via the frequency roll.

**PressMode enum (new).** `None`, `Standard`, `Desperate`. `Desperate` is declared and reserved; the generator throws `InvalidOperationException` if it ever receives it. The default on `PossessionState` is `PressMode.None` — safe because `RunPossession` is called exactly once per possession and stamps before Generate. The field survives every `with` in the possession chain (verified: `ResumeInbound` and `ResolveSidelineInbound` both use `c.State` which carries `PressMode` through).

**Court-state gate survives unchanged.** `Frontcourt == true` returns `FlatBaseline()` immediately, before the PressMode switch — the press is irrelevant once the offense has crossed half.

**PressMode.None → FlatBaseline immediately** (before any roster read). No change from today's baseline behavior for non-pressed possessions.

**`EntryDisruptionShares` — signature change.** `double fullCourtPress` parameter dropped. The method now reads `cfg.StandardLift` and `cfg.StandardGate` directly. All internal config references updated (`RollA*` → `Standard*`).

**`IRollAPieGenerator.cs`** — stale `MatchupConfig.FullCourtPressFor` doc reference updated to describe `PossessionState.PressMode`.

**Harness result.** ALL CHECKS PASSED. Phase 15 results:
- (1) `PressProbabilityFor` pure function: freq=1→0.05, freq=5.5→0.425, freq=10→0.80, in [0,1], monotone.
- (2) Frequency gates pressed fraction (spy-based): freq=5 expected 0.3833, observed 0.3799, well within 5σ+0.02; logLen=5073, Desperate=0.
- (3) PressMode.None → exact config baseline: all five arms match to float precision.
- (4) Standard pie shape: StandardLift lifts all three arms; skill/ath/size each independently lift TO; size smallest; OffFoul<DefFoul; ceilings hold at worst matchup; CleanEntry>0; five arms sum to 1; JumpBall exactly flat.
- (5) Frontcourt=true gates Standard: high-freq + worst matchup + Standard + Frontcourt=true → exact baseline.
- (6) SelectedSlot-blind: null vs stamped slot → identical pie.
- (7) PressMode threads through re-inbounds: ResumeInbound(Standard) all-Standard, ResumeInbound(None) all-None, ResolveSidelineInbound(Standard) all-Standard.
- (8) Desperate fail-loud: stamp + Generate throws; live RunPossession (1000×) never produces Desperate.
- GovernorLoopCheck green (bands hold — at 5% default press probability, aggregate impact is tiny; new RNG draw per dead-ball possession shifts stream but not distribution).

**Phase 15 supersedes Phase 14's intensity model.** The `EntryDisruptionShares` method now always receives `PressMode.Standard` possessions (called only from the Standard branch in `RollAGenerator`). The halfcourt `PressureNeutral`/`PressureScale` normalization no longer enters Roll A math; the `PressureNeutral=5` anchor point and the dial-to-pUnit conversion are confined to Roll B/F's halfcourt disruption.

**Files changed:** `Core/PressMode.cs` (new), `Core/PossessionState.cs`, `Core/Matchup.cs`, `Core/Resolver.cs`, `Config/MatchupConfig.cs`, `Generators/RollAGenerator.cs`, `Generators/IRollAPieGenerator.cs`, `Harness/Program.cs`, `Harness/config.json`.

**Deferred (explicitly out of scope).**
- **Back-end break / press-break → transition face (Phase 16).** When the offense beats a Standard press it gets a genuine fast break the other way, mitigated by the defense's back-line rim protection. Separate session. No stub/hook carved here.
- **Desperate mode.** Situational end-game press (down late, before intentional fouling). Needs score-and-clock-aware late-game module that doesn't exist. Declared in enum, reserved, never produced.
- **Steal attribution at Roll A turnovers; changed turnover-type mix in Roll C under press; fatigue.**
- **`CoachProfile` migration.** `PressProbabilityFor(side)` reads `HomePressFrequency` / `AwayPressFrequency`; swap to per-team `CoachProfile` fields when that layer arrives (one call site changes).
- **Gap weight and StandardLift/Gate calibration.** All magnitudes are calibration placeholders.

---

## Session 45 — Phase 14: full-court press disruption door (Roll A) (2026-06-15)

**Scope:** Wire the disruption face of full-court press on Roll A (the dead-ball / backcourt entry). At neutral press (5) + even aggregates, Roll A reproduces today's config baseline exactly. Above neutral, the `Turnover`, `DefensiveFoul`, and `OffensiveFoul` slices rise toward their Roll-A-specific ceilings. `JumpBall` is pinned flat. `CleanEntry` absorbs the complement. `Frontcourt = true` returns the flat baseline immediately (press is irrelevant once the offense has crossed half). Press-break → transition face is explicitly deferred.

**What the repo already had on entry.** Sessions 44.5 and 44.6 had landed. A prior partial build had committed `IRollAPieGenerator`, a one-gap `RollAGenerator` (Steals vs. BallHandling only), and the base `MatchupConfig` fields for full-court press — but it was missing three things the confirmed design requires: the three-gap turnover model (skill + athleticism + size), a separate `FullCourtPressReferenceShift` tanh saturation constant, and the three gap weights. This session completes all three.

**The turnover model — three gaps, additively composed.** Unlike Roll B (one skill gap, gated by pressure), Roll A's turnover disruption composes three weighted gap terms:
1. **Skill**: slot-weighted `Steals` (defense) − `BallHandling` (offense) → `GapFn` with SKILL params
2. **Athleticism**: slot-weighted `Athleticism` composite (defense) − offense → `GapFn` with PHYSICAL params
3. **Size**: slot-weighted `LengthRating` composite (defense) − offense → `GapFn` with PHYSICAL params; `RollASizeWeight` is the smallest of the three

`disruptionShift = pressureLift + pressureGate × (skillWeight·skillShift + athWeight·athShift + sizeWeight·sizeShift)`. The gate is `max(0, pUnit)`, so backed off, the team matchup contributes nothing regardless of aggregates.

**Why the saturation constant is separate.** `EntryDisruptionShares` uses `cfg.FullCourtPressReferenceShift` (new, default 1.2) — NOT `cfg.PressureReferenceShift` (halfcourt). The two dials must stay fully independent; coupling the tanh saturation speed would couple their tuning behavior in the calibration pass.

**Both foul slices are press-only.** `DefensiveFoul` (reach-ins) and `OffensiveFoul` (charges / player-control fouls) track how hard the defense presses, not who is on the floor. No gap terms. `OffensiveFoul` ceiling is set low (≈15% of `DefFoul` ceiling) because backcourt charges are rare. Both use `FullCourtPressReferenceShift` for saturation.

**Action-mass normalization.** `actionMass = BaseClean + BaseTurnover + BaseOffensiveFoul + BaseDefensiveFoul = 0.99`. The generator passes base shares normalized over actionMass (`BaseTurnover / actionMass`, etc.) to `EntryDisruptionShares`, then multiplies the returned shares back by actionMass. This is why neutral press + even aggregates reproduces the exact config baseline.

**Court-state gating.** Roll A fires at three moments: initial dead-ball entry (`Frontcourt = false`), `ResumeInbound` (may be either), and `SidelineInbound` (always `Frontcourt = true`). The generator checks `state.Frontcourt` first; if true, it returns `FlatBaseline()` immediately — no roster read, no dial read.

**Separate dials confirmed.** Roll B and Roll F read `HomePressure` / `AwayPressure` (halfcourt). Roll A reads `HomeFullCourtPress` / `AwayFullCourtPress`. A team can press full-court and fall back into a zone — the two dials are independent, confirmed by `FullCourtPressFor(state.Defense)` reading a different field than `PressureFor(state.Defense)`.

**Changes delivered.**
- `Matchup.cs` — `EntryDisruptionShares` updated: signature adds `offenseAthletic, defenseAthletic, offenseLength, defenseLength`; body replaced with three-gap model; all three tanh calls use `cfg.FullCourtPressReferenceShift`.
- `RollAGenerator.cs` — computes six slot-weighted aggregates (handling, stealers, offAthletic, defAthletic, offLength, defLength) and passes all six to `EntryDisruptionShares`.
- `MatchupConfig.cs` — adds `FullCourtPressReferenceShift` (tanh saturation, default 1.2), `RollASkillWeight` (0.50), `RollAAthleticismWeight` (0.35), `RollASizeWeight` (0.15); Load invariants: `FullCourtPressReferenceShift > 0`, all three gap weights `>= 0`.
- `config.json` — adds the four new Matchup keys.
- `Resolver.cs` — `_rollAGenerator` field and ctor param retyped from `StubPieGenerator` to `IRollAPieGenerator`.
- `Program.cs` — `RollAGenerator(cfg, cfgMatchup, game)` constructed after `SeatStartersFromConfig`; `BatchCheck` and `ShowSamples` signatures widened to `IRollAPieGenerator`; `BatchCheck` call passes `new StubPieGenerator(cfg)` (fresh flat baseline, not the live generator with roster state); `Phase14FullCourtPressDoorCheckRollA` added with 11 assertions; call added after Phase 13.

**Harness result.** ALL CHECKS PASSED. Phase 14 results:
- (a) Neutral anchor exact: all five arms match config baseline to float precision.
- (b/c/d) Press raises TO (0.038→0.161), DefFoul (0.014→0.076), OffFoul (0.003→0.012).
- (e) OffFoul < DefFoul at all six press levels (p=1 through 10).
- (f) Cap holds at max press + worst matchup; CleanEntry > 0 (TO at ceiling 0.20, others below ceiling).
- (g) JumpBall exactly flat at all four test cases including Frontcourt=true.
- (h) Five arms sum to 1 at all press levels.
- (i) Frontcourt=true + worst matchup + max press still returns exact baseline.
- (j) Null vs stamped SelectedSlot → identical pie.
- (k) Skill, athleticism, and size each independently lift TO when varied alone at high press; size produces the smallest delta of the three (Δ=0.037311 vs ath=0.037444, skill=0.037493) — the weight ordering is correct.

**Deferred (explicitly out of scope).**
- Press-break → transition face: the reward side of pressing (beat-the-press transition shots), deferred to its own next session. Touches the `FastBreak` / transition machinery, not Roll A's pie.
- Steal attribution at Roll A turnovers.
- Fatigue effects.
- Changed turnover-type mix in Roll C under full-court press.
- `CoachProfile` migration (swap `FullCourtPressFor` to read per-team coach fields at that seam only).

---

## Session 44.6 — remove four unused IContinuationNode params from Resolver ctor (2026-06-15)

**Scope:** Constructor signature change and matching call-site updates only. No behavior change. Harness expected to print "ALL CHECKS PASSED" with byte-for-byte identical rates to the pre-change run.

**What was removed and why.** The `Resolver` constructor accepted four trailing parameters typed `IContinuationNode` — `resumeInbound`, `resolveBlock`, `sidelineInbound`, `transition` — that it never stored or used. As of Contextification #6, the live chain re-runs Roll A on the resume/sideline edges; the resolver holds no inbound stub, making those params vestigial. Phase 14 (Session 45) will touch every `new Resolver(...)` site anyway to retype the Roll A generator param; clearing the dead params now keeps Phase 14's ctor edit purely additive and makes any harness change unambiguously Phase 14's doing.

**Changes:**
- `src/Charm.Engine/Core/Resolver.cs` — removed the four trailing ctor params and the explanatory comment block above them. Final two ctor params are now `GameState game` and `IRng rng`.
- `src/Charm.Harness/Program.cs` — removed the matching four trailing arguments (`new ResumeInboundStub()`, `new BlockRecoveryStub()`, `new SidelineInboundStub()`, `new TransitionStub()`) from all nine `new Resolver(...)` construction sites. Each closing argument (rng, rngR, or `new SystemRng(seed)`) was re-terminated with `);` instead of `,`.

**Retained (not removed):**
- All four stub class definitions in `Stubs.cs` remain (documented as "kept in the corner").
- The three standalone `var sidelineStub = new SidelineInboundStub();` fact-echo helpers in Program.cs remain; these are not Resolver arguments.

**Validation:** Dead param names (`resumeInbound`, `resolveBlock`, `sidelineInbound`) are absent from both changed files; `new TransitionStub()` is absent from Program.cs. Brace balance confirmed on both files. Three standalone `sidelineStub` constructions confirmed present.

---

## Session 44.5 — pre-Phase-14 doc/comment tightening pass (2026-06-15)

**Scope:** Documentation and code comments only. No behavior change, no logic change, no enum change, no pie weights. Harness output expected byte-for-byte identical.

**What was corrected and why.** Two artifacts still described Roll A's pre-Contextification-#6 shape — the seven-arm pie with three violation terminals and a single Foul slice — and would have misled the Phase 14 build, which begins by reading docs:

1. **`docs/design.md` (the "Pie shape: seven slices" block, Roll A section):** A `> ⚠ SUPERSEDED` banner line was added directly above the stale paragraph, pointing to the correction at the bottom of the file. The stale body was NOT rewritten (append-only rule; banner is the confirmed exception). A new correction section was appended at the very end of `design.md` stating the true five-arm shape, where the violations went (Roll C EntryBackcourt context), and how the foul slice split.

2. **`src/Charm.Engine/Rolls/EntryOutcomes.cs` — nine `ContinuationKind` XML doc comments refreshed:**
   - `ResumeInbound`, `ResolveSidelineInbound` — removed "(stubbed)"; now state that Contextification #6 re-runs Roll A on these edges instead of parking.
   - `ResolveRebound` — now states it executes Roll I.
   - `ResolveOffensiveRebound` — now states it executes Roll K.
   - `ResolveFTRebound` — now states it executes Roll M.
   - `ResolveFreeThrows`, `ResolveShootingFreeThrows` — now state they are driven by the Roll L FT-sequence driver (`DriveFreeThrows`).
   - `ResolveBlock` — now marked RETIRED (Contextification #2); resolver throws.
   - `IntoTransition` — now marked RETIRED (Contextification #1); resolver throws.
   Enum member names, values, and declaration order are byte-for-byte unchanged.

**Other stale passages noticed but deferred (not fixed this session):**
- `design.md` lines ~224 and ~239-240: the roll routing table and "True terminals today" paragraph still list the three violation terminals as Roll A exits. These are lower-priority historical context; flagged for a future cleanup pass.

---

## Session 44 — Phase 13: the pressure / disruption door (Roll B, halfcourt initiation) (2026-06-15)

**A design-then-build session.** Two open design questions were settled conversationally
before any code was written. Once confirmed, the build ran as a standard Phase 12-style
retype-the-generator move: the resolver field was retyped to an interface, the stub gained
the interface, the real generator was wired, the harness check was added. All checks green.

**Scope:** Roll B's `DeadBallTurnover` slice and its `Foul` slice are now pressure-and-
matchup-aware. `RollB.Execute` itself is untouched — only the generator changed. The
shot-quality face of pressure is explicitly deferred.

---

**The reframing (CONVENTIONS §6a).** The Phase 12 plan described Roll B and Roll A as
"the same matchup one step earlier in the chain — deliberate fast-follows for their own
sessions." This was stale. The session prompt corrected it: Roll B runs **before** player
selection (Roll E). `PossessionState.SelectedSlot` is null at Roll B — Roll E has not run.
This makes the Phase 12 one-on-one handling-vs-steals contest structurally impossible at
Roll B (no handler to read `BallHandling` from, no slot-matched defender to read `Steals`
from). The session surfaced this as Design Question 1 rather than papering over it.

---

**DQ1 — Is there a per-player matchup at Roll B, or is it pressure-only?**

Emmett settled on **Option B: two-sided team aggregate**. Because no individual player is
selected yet, the matchup uses slot-weighted aggregate scores across all ten on-court
players: the five offensive players' `BallHandling` values (weighted toward guards) vs. the
five defensive players' `Steals` values (same weights). The basketball logic: a pressing
defense with good thieves earns turnovers even before the ball-handler is known; guard-slot
players dominate because they have the most opportunities to handle and steal at halfcourt
initiation. A center with great `Steals` contributes, but with lower weight (8%) because the
game creates fewer opportunities for them at this stage.

Slot weights settled: `[0.35, 0.25, 0.20, 0.12, 0.08]` for slots 1–5, same for both offense
(`BallHandling`) and defense (`Steals`). Stored in `MatchupConfig` as `SlotWeight1`–`SlotWeight5`
with a `SlotWeights` array property, all tunable. `Load` enforces: each ≥ 0, sum = 1.0.

The `Matchup.TeamDisruptionShares` method is the new math entry point. It receives
pre-computed aggregate scores (not `Player` objects), runs the gap through `GapFn` with the
shared skill parameters, and applies the same pressure-gated disruption-shift formula as
`DisruptionShares`. The foul slice is pressure-only (no matchup term) — aggression, not skill.

Roll-B-specific ceilings/floors are added to `MatchupConfig` (`RollBTurnoverCeiling` = 0.10,
`RollBTurnoverFloor` = 0.01, `RollBFoulPressureCeiling` = 0.22, `RollBFoulPressureFloor` = 0.06).
Roll B's baseline foul share (≈12% of the pie) is far higher than Roll F's (≈5%) and its
baseline TO share (≈3%) is lower; using the Phase 12 Roll-F ceilings here would be wrong.
The shared `PressureNeutral`/`PressureScale`/`PressureReferenceShift` knobs are reused — they
describe the pressure dial's normalization, which is a global property.

**DQ2 — What happens to the existing physicality wire?**

Emmett settled on **keep physicality dormant** (Option ii). The `physicality` parameter
stays in the interface and is applied as a secondary nudge on the Foul slice after the
pressure bend, before renormalization — exactly as the stub did. Both live dispatch sites
continue to pass `physicality: 0.0`, so it has no live effect. It is preserved because
physicality and pressure are distinct basketball concepts (how rough/chippy the game is vs.
how aggressively the defense hounds the ball); if the dial becomes live in a future session,
the wire already exists.

Consequence: the interface is **two-arg** `Generate(PossessionState state, double physicality)`,
matching the existing stub's signature. `PhysicalitySignalCheck` and `RollBFoulRate` are
unchanged. `PhysicalityFoulNudge` stays in `RollBConfig` and `config.json`.

---

**The changed calibration anchor.** Same as Phase 12: (neutral pressure 5.0 + even aggregate)
= today's flat Roll B rates. A game running at neutral pressure with equal team aggregates
reproduces the config baseline exactly. Non-neutral pressure or a team mismatch bends the
rates. This is not a bug — pressure is the axis.

**Three-way mass split — JumpBall held exactly flat.** Roll B's action mass =
`BaseProceed + BaseFoul + BaseDeadBallTurnover` (= 0.995). `Proceed` absorbs the complement
of the two bent arms. `JumpBall` is pinned at `BaseJumpBall` (0.005) exactly. Same discipline
as Phase 12. After the pressure bend, the physicality nudge is applied to the raw `Foul`
weight and the whole dict is renormalized — at physicality=0.0 this is a no-op.

**Fallback: empty roster only.** Because Roll B reads no `SelectedSlot` and no per-player
matchup (it reads all five slots via the weighted aggregate), there is no null-slot fallback
and no absent-player fallback. The only fallback triggers when either roster has zero
populated players (isolated test calls). Partial rosters (some slots null) proceed with
weights renormalized over the populated slots. The generator's doc-comment states this
explicitly so a future reader does not assume a missing guard.

**Two dispatch sites in Resolver.cs** (unchanged behavior): the `BallAdvanced` entry path
(line ~192) and the `IntoHalfcourtSet` path (line ~271) both keep `physicality: 0.0`. The
field and ctor param were retyped from `RollBStubPieGenerator` to `IRollBPieGenerator`.

**BatchCheck and PhysicalitySignalCheck** now each pass a fresh `new RollBStubPieGenerator(cfgB)`
rather than the live generator (same cure as Phase 9/11/12 for Roll H, Roll M, Roll F). The
8 Resolver-construction sites in isolated harness checks all pass `new RollBStubPieGenerator(cfgB)`
unchanged — they compile through `IRollBPieGenerator` without any edits.

---

**What landed (8 files — 2 new + 6 edits):**

- **`Generators/IRollBPieGenerator.cs` (NEW)** — two-arg interface
  `Pie<HalfcourtOutcome> Generate(PossessionState state, double physicality)`. Carries the
  physicality parameter since it is kept dormant (DQ2). Mirrors `IRollFPieGenerator`'s shape
  but with the extra arg and an explicit doc-comment on why it's two-arg.

- **`Generators/RollBGenerator.cs` (NEW)** — real, pressure-and-team-aggregate-aware
  generator. Ctor `(RollBConfig cfgB, MatchupConfig matchup, GameState game)` with null
  guards. At generate-time: reads both rosters (5 players each, null-tolerant); falls back to
  flat baseline if either roster has 0 populated players; computes weighted-aggregate
  `BallHandling` (offense) and `Steals` (defense) via `WeightedAggregate` (renormalizes over
  non-null slots); reads pressure via `_matchup.PressureFor(state.Defense)`; calls
  `Matchup.TeamDisruptionShares`; three-way mass split; `JumpBall` pinned flat; overflow
  guard (throws if `finalToShare + finalFoulShare >= 1`); physicality nudge applied after
  pressure bend (dormant at 0.0). Documents: no-slot fallback not needed (Roll B reads no
  `SelectedSlot`), CoachProfile migration path, deferred shot-quality face.

- **`Generators/RollBStubPieGenerator.cs` (edit)** — added `: IRollBPieGenerator`. No
  behavior change. Used by `BatchCheck`, `PhysicalitySignalCheck`, and all 8 Resolver-
  construction sites in isolated harness checks.

- **`Core/Resolver.cs` (edit)** — two surgical type changes: `_rollBGenerator` field and
  ctor param retyped from `RollBStubPieGenerator` to `IRollBPieGenerator`. Both dispatch
  sites unchanged (`physicality: 0.0` kept).

- **`Core/Matchup.cs` (edit)** — `TeamDisruptionShares` appended as a new `public static`
  method. Signature: `(double offenseHandling, double defenseStealers, double pressure,
  double baseTurnoverShare, double baseFoulShare, MatchupConfig cfg) → (turnoverShare, foulShare)`.
  Uses `cfg.RollBTurnoverCeiling/Floor` and `cfg.RollBFoulPressureCeiling/Floor` (not the
  Phase 12 Roll-F keys). The team gap runs through `GapFn` with the shared skill parameters.
  Foul bend is pressure-only. Plain addition throughout (Session 38 lesson).

- **`Config/MatchupConfig.cs` (edit)** — Phase 13 block added: `SlotWeight1`–`SlotWeight5`
  (0.35/0.25/0.20/0.12/0.08), `SlotWeights` array convenience property, `RollBTurnoverCeiling`
  (0.10), `RollBTurnoverFloor` (0.01), `RollBFoulPressureCeiling` (0.22),
  `RollBFoulPressureFloor` (0.06). `Load` extended with invariants: each slot weight ≥ 0;
  sum of weights = 1.0 within 1e-6; floor ≥ 0 for both pairs; ceiling > floor for both pairs.

- **`Harness/Program.cs` (edit)** — changes:
  - Early `var rollBGenerator = new RollBStubPieGenerator(cfgB)` (pre-SeatStartersFromConfig)
    replaced with a comment; live `var rollBGenerator = new RollBGenerator(cfgB, cfgMatchup,
    game)` added after `SeatStartersFromConfig` alongside G/H/I/M/F.
  - `BatchCheck` call site now passes `new RollBStubPieGenerator(cfgB)` (fresh stub).
  - `PhysicalitySignalCheck` call site now passes `new RollBStubPieGenerator(cfgB)` (fresh
    stub — the check tests the stub's wire, and `rollBGenerator` is now the live generator).
  - `BatchCheck` method signature: `RollBStubPieGenerator genB` → `IRollBPieGenerator genB`.
  - `Phase13TeamDisruptionDoorCheckRollB` registered after `Phase12DisruptionDoorCheck`.
  - `Phase13TeamDisruptionDoorCheckRollB` method added with 7 sub-checks mirroring the
    Phase 12 pattern. Key differences from Phase 12's check: `St()` passes `SelectedSlot: null`
    (Roll B reads no selected slot); no `Mk(bh:, st:)` override needed for the aggregate
    (all-50 baseline is even); sub-check (f) proves `SelectedSlot` is ignored (null vs.
    stamped produce identical pies); sub-check (g) flips from "no roster dependence" to
    "matchup matters" (elite defense Steals > average Steals at high pressure proves the
    team aggregate fires).

- **`Harness/config.json` (edit)** — 9 new keys added to `Matchup` section:
  `SlotWeight1`–`SlotWeight5` (0.35/0.25/0.20/0.12/0.08), `RollBTurnoverCeiling` (0.10),
  `RollBTurnoverFloor` (0.01), `RollBFoulPressureCeiling` (0.22), `RollBFoulPressureFloor`
  (0.06). `RollB.PhysicalityFoulNudge` stays (DQ2: physicality kept).

---

**Python pre-check (all 8 cases passed before any C# was written):**
(a) Neutral: all four arms exact baseline. (b) TO monotone rise p=2/5/9: 0.019/0.030/0.077.
(c) Matchup matters at high pressure: off_adv 0.010 < even 0.077 < def_adv 0.100. (d) Matchup
muted at low pressure: gap=0 at p=2 vs gap=0.090 at p=9. (e) Foul pressure-only: monotone
0.087/0.120/0.187; flat across matchup at p=8 (0.17485 in all three cases). (f) Cap holds:
TO share=0.100≤0.10, Foul share=0.198≤0.22, Proceed>0, sum=1. (g) JumpBall=0.005 flat, sum=1
in all 5 cases. (h) Slot weights: slot-1 impact (14.0) > slot-5 impact (3.2), sum=1.

**Harness — ALL CHECKS PASSED.** Phase 13 sub-results:
- (a) Neutral anchor: Proceed=0.84500000, DeadBallTO=0.03000000, Foul=0.12000000,
  JumpBall=0.00500000 — all exact.
- (b) TO: p=2=0.018880, p=5=0.030000, p=9=0.077417 — monotone rise: True.
- (c) Foul: p=2=0.086558, p=5=0.120000, p=9=0.187476 — monotone rise: True.
- (d) TO share=0.100000 ≤ ceiling=0.10, Fo share=0.197988 ≤ ceiling=0.22, Proceed>0.
- (e) All 4 cases: JumpBall=0.00500000, sum=1.0000000000.
- (f) null slot vs stamped slot → identical pie: OK.
- (g) elite-steal def TO=0.099500 > average-steal def TO=0.077417 — defAdv lifts: True.

Existing `BatchCheck`, `PhysicalitySignalCheck`, `Phase12DisruptionDoorCheck`, and the
full 100k chain all stayed green.

**NOT changed (confirmed):** `RollB.cs`, `HalfcourtOutcomes.cs`, `RollBConfig.cs`,
`PossessionState.cs`, `GameState.cs`, `Player.cs`, `RollC.cs`, `RollD.cs`, `DefenderPicker.cs`,
`RollF.cs`, and all Phase 12 Roll F files.

**Walls held / deferred:**
- **Shot-quality face of pressure.** Deferred. No hooks, no stubs.
- **Roll A.** Its own session. Note: Roll A has its own wrinkles (backcourt phase, violation
  terminals, different turnover-type context) — it is NOT a trivial copy of this session.
- **Roll C turnover classification / type mix.** Pressure moves the turnover RATE at Roll B.
  The type mix stays Roll C's job, unchanged.
- **CoachProfile migration.** `MatchupConfig.PressureFor(TeamSide)` is the only read site
  that changes when the coach-settings layer arrives.
- **`PossessionState.DefenderSlot` promotion.** Still deferred. Roll B reads no defender.
- **Player/steal attribution.** Which defender gets credit for the steal, with weight toward
  position and steal rating. Roll C's domain.
- **Slot weights beyond v1.** The 0.35/0.25/0.20/0.12/0.08 split is a calibration placeholder;
  Emmett can tune them in `config.json`.


## Session 43 — Phase 12: the pressure / disruption door (Roll F) (2026-06-15)

**A build (CONVENTIONS §0–§3) — code + harness, all green.** Roll F's generator is now
pressure-and-matchup-aware. The selected handler's chance of coughing the ball up and of drawing
a non-shooting reach-in foul now reflect the defending team's **pressure setting** and the
**handling-vs-steals matchup** — while Roll F itself is untouched. This is the defensive-disruption
twin of the block door: rim protection disrupts the shot; ball pressure disrupts the possession
before the shot gets off.

**Scope: disruption face only.** Pressure has two faces in Emmett's model. This session builds only
the **disruption face**: pressure raises the `Turnover` slice and the `NonShootingFoul` slice.
The **shot-quality face** — beating high pressure yields scrambled-defense rim busts; backed-off
low pressure packs the paint and concedes the perimeter — is explicitly deferred to its own session.
No hooks, no stubs. The split is deliberate cascade-avoidance: one dial bending four things in
opposing directions is exactly the interacting-variable trap that sank the project's two prior
Python attempts. One face, calibrated, before the other.

**The matchup: handling vs. steals, with pressure as the master dial.** Two attributes meet: the
handler's `BallHandling` against his defender's `Steals`. On top sits **pressure** — a per-team
defensive coach dial on a 1–10 scale. Pressure does two jobs on the steal/turnover slice:

1. **Flat, skill-independent lift:** pressing with bad hands still earns steals off a low baseline.
   Even a zero-or-negative matchup produces a positive lift when pressure is above neutral.
2. **Gates how much the matchup matters:** at low pressure even great hands generate almost nothing;
   at high pressure, ball-hawks against weak handlers feast.

"A high-steals defender climbs faster" and "a huge handling gap climbs even faster" are **the same
single lever** — the gap (`defender.Steals − handler.BallHandling`) through the existing convex
`GapFn`. One term captures both. The `pressureGate = max(0, pUnit)` multiplier on the matchup
shift is what gates the matchup contribution: at backed-off pressure the matchup is irrelevant
(gate ≈ 0); at high pressure the gate opens and the gap drives the outcome.

**Foul slice: pressure only, no matchup term.** The non-shooting reach-in foul tracks aggression,
not skill. `foulShift = pressureLift` only — the handling-vs-steals matchup does NOT steepen the
foul climb. You can reach in against anyone if you're playing that aggressively.

**The mass split — JumpBall held flat.** Roll F's four arms: `ShotAttempt`, `Turnover`,
`NonShootingFoul`, `JumpBall`. Pressure bends the first three arms; `JumpBall` is pinned at
`BaseJumpBall` exactly. This is a **three-way reweight** with jump held out — NOT a four-way
renormalization. The `actionMass = BaseShotAttempt + BaseTurnover + BaseNonShootingFoul` defines
the moving mass; `ShotAttempt` absorbs the complement of the other two.

**Changed calibration anchor — the first door to break "even = baseline."** Every prior door held
the invariant "an even matchup reproduces the config baseline." Here that sub-invariant holds only
at **neutral pressure**. The anchor is **(neutral pressure + even matchup) = today's flat rates**.
This is Emmett's basketball call — pressure is the new axis that moves the rates, with the matchup
gated underneath. Documented in `MatchupConfig` and `RollFGenerator`; should not be treated as a
bug in calibration.

**Pressure home — v1 config scalar.** Pressure is a per-team defensive setting, not a player
attribute and not a per-possession fact. For v1, it lives as `HomePressure` / `AwayPressure` in
`MatchupConfig` with a `PressureFor(TeamSide)` helper the generator calls at generate-time.
`CoachProfile` is the eventual owner (stubbed in Phase 9 precisely for this). Migration path: when
the coach-settings layer arrives, these two scalars move to per-team `CoachProfile` fields and
`RollFGenerator` reads them from there. The `PressureFor(...)` call site in the generator is the
only thing that changes. No structural change needed. This was the prompt's recommendation over
half-building the coach-settings layer, which is its own session.

**DefenderPicker fork — second consumer, promotion still deferred.** Roll H already consumes the
slot-matched defender via `DefenderPicker.Pick`. Roll F now reads the same slot-matched defender
(handler's slot number on the defense side) — making Phase 12 the **second door** to consume the
defender, which is the stated trigger for `PossessionState.DefenderSlot` promotion per
`DefenderPicker`'s own doc-comment. However, the pick is still **pure and deterministic** —
`new Slot(state.Defense, state.SelectedSlot.Number)` — so two doors deriving it independently
produce the **same defender** with zero divergence risk. The hazard the promotion guards against
(two doors picking *different* defenders, or a non-deterministic pick) does not exist while the
pick is this pure.

Decision: Roll F derives the defender **locally without calling `DefenderPicker`** — the same
slot-match logic, different call site, same result. Promotion is **still deferred**. The first
door that needs a non-deterministic or mismatch-hunting pick is what forces it. Phase 12 is now
the second consumer, so the promotion bar is technically met — but because the pick is still pure,
promotion remains a "when it needs to be, not before" call. This is recorded in both the journal
and design docs so the next person knows where the bar moved.

**Verdict on the outside-LLM pre-prompt review.** Adopted its structural points; rejected its two
basketball calls:

- **Adopt:** Roll F only (not Roll C); matchup-aware turnover occurrence not classification; the
  interface pattern (`IRollFPieGenerator`, retype Resolver, keep batch check on stub); the
  mass-split discipline (three-way, jump held flat); the fallback rules; the DefenderPicker
  architecture warning (the review's most valuable catch — flagged the second-consumer trigger
  before the build started).
- **Reject:** It proposed broader composites (offense ball-security = BallHandling + Passing +
  BasketballIQ; defensive pressure = Steals + PerimeterDefense + Quickness). Emmett settled
  on **handling vs. steals alone**. It also said the foul slice stays flat (bend only ShotAttempt
  vs. Turnover). Emmett settled that pressure **also raises the foul slice** (the flat aggression
  lift). And it didn't model the pressure dial at all — no flat-lift, no pressure-gating, no
  gradual-low-cap. The whole pressure mechanism was absent from its model.

Net: useful for scaffolding, wrong on the basketball calls. Per working-with-emmett.md §6: Claude
pushed back clearly and directly rather than summarizing or accommodating.

**What landed (8 files — 2 new + 6 edits):**

- **`Generators/IRollFPieGenerator.cs` (NEW)** — the interface. Single one-arg method
  `Pie<PlayerActionOutcome> Generate(PossessionState state)`. Mirrors `IRollGPieGenerator` shape
  (no source enum, no bool). Both stub and real generator implement it; Resolver holds the
  interface.

- **`Generators/RollFGenerator.cs` (NEW)** — the matchup-aware real generator. Ctor takes
  `(RollFConfig cfgF, MatchupConfig matchup, GameState game)` with null guards. At generate-time:
  (1) null `SelectedSlot` → flat baseline (wiring guard — Roll E always runs before Roll F on the
  live path); (2) handler player absent → flat baseline (DEC-6); (3) slot-matched defender absent
  → flat baseline (DEC-6; can't compute one-on-one matchup against a phantom). Defender derived
  locally as `new Slot(state.Defense, selectedSlot.Number)` — NOT via `DefenderPicker.Pick`, per
  the fork decision. Pressure read via `_matchup.PressureFor(state.Defense)`. Calls
  `Matchup.DisruptionShares`; does the three-way mass split; `JumpBall` pinned exactly flat.
  Includes overflow guard (throws if `finalToShare + finalFoulShare ≥ 1`). Documents the deferred
  shot-quality face and the CoachProfile migration path as neutral seams.

  **One compile error caught after initial delivery:** `state.SelectedSlot` is `Slot?`
  (nullable value type `Nullable<Slot>`); the null check at lines 105–107 satisfies the
  programmer's intent but C# doesn't automatically unwrap it. Fix: `var selectedSlot =
  state.SelectedSlot.Value;` after the null guard, then use `selectedSlot` (plain `Slot`) for
  `PlayerAt` and `.Number`. One line; fixed immediately.

- **`Generators/RollFStubPieGenerator.cs` (edit)** — added `: IRollFPieGenerator`. No behavior
  change. Used by `RollFActionBatchCheck` (fresh stub via inline construction) and all 8 Resolver-
  construction sites in isolated harness checks — all compile through the interface unchanged.

- **`Core/Resolver.cs` (edit)** — two surgical type changes: `_rollFGenerator` field retyped from
  `RollFStubPieGenerator` to `IRollFPieGenerator`; ctor param likewise. Dispatch line unchanged
  (already passes `c.State`).

- **`Core/Matchup.cs` (edit)** — one new `public static` method appended at the bottom:
  `DisruptionShares(Player handler, Player defender, double pressure, double baseTurnoverShare,
  double baseFoulShare, MatchupConfig cfg)` returning `(double turnoverShare, double foulShare)`.
  Mirrors `FoulRate`'s structure (the prompt's recommended template). Pressure normalization:
  `pUnit = (pressure − PressureNeutral) / PressureScale`; `pressureLift = pUnit`;
  `pressureGate = max(0, pUnit)`. Turnover: `disruptionShift = pressureLift + pressureGate ×
  matchupShift`; tanh saturation toward `TurnoverCeiling` or `TurnoverFloor`. Foul: `foulShift =
  pressureLift` only (no matchup term); same tanh shape toward `FoulPressureCeiling` /
  `FoulPressureFloor`. Plain addition throughout — tanh is odd and supplies the sign; same
  Session 38 lesson as `BlockWeight` and `OffensiveReboundShare`.

- **`Config/MatchupConfig.cs` (edit)** — Phase 12 block appended: `HomePressure` (5.0),
  `AwayPressure` (5.0), `PressureFor(TeamSide)` helper method, `PressureNeutral` (5.0),
  `PressureScale` (4.0), `PressureReferenceShift` (1.2 — deliberately high for gradual climb),
  `TurnoverCeiling` (0.18 — low by design, "nobody gets 5 steals a game"),
  `TurnoverFloor` (0.02), `FoulPressureCeiling` (0.09), `FoulPressureFloor` (0.01). `Load`
  extended with Phase 12 invariants: `PressureNeutral ∈ [1, 10]`; `PressureScale > 0`;
  `PressureReferenceShift > 0`; `TurnoverFloor ≥ 0`; `TurnoverCeiling > TurnoverFloor`;
  `FoulPressureFloor ≥ 0`; `FoulPressureCeiling > FoulPressureFloor`.

- **`Harness/Program.cs` (edit)** — changes:
  - Early `var rollFGenerator = new RollFStubPieGenerator(cfgF)` (pre-SeatStartersFromConfig)
    **replaced with a comment**; live generator added after `SeatStartersFromConfig` alongside
    G/H/I/M: `var rollFGenerator = new RollFGenerator(cfgF, cfgMatchup, game)`.
  - `RollFActionBatchCheck` call site: passes `new RollFStubPieGenerator(cfgF)` (fresh stub) so
    baseline rates stay flat and matchup-independent — same cure as Phase 9 `RollH` and Phase 11
    `RollM`.
  - `RollFActionBatchCheck` method signature: `RollFStubPieGenerator genF` → `IRollFPieGenerator
    genF`.
  - `Phase12DisruptionDoorCheck` registered after `Phase11FreeThrowReboundDoorCheck`.
  - `Phase12DisruptionDoorCheck` method added. Eight deterministic sub-checks, mirroring
    Phase 10/11 pattern. `Split` helper constructs `RollFGenerator`, calls `Generate`, returns
    dict. `WithAwayPressure(p)` helper loads `MatchupConfig` and sets `AwayPressure` for
    test-specific pressure values (Away = defense in the standard test setup).

- **`Harness/config.json` (edit)** — 9 new keys added to `Matchup` section:
  `HomePressure: 5.0`, `AwayPressure: 5.0`, `PressureNeutral: 5.0`, `PressureScale: 4.0`,
  `PressureReferenceShift: 1.2`, `TurnoverCeiling: 0.18`, `TurnoverFloor: 0.02`,
  `FoulPressureCeiling: 0.09`, `FoulPressureFloor: 0.01`. Both team pressures set to 5.0
  (neutral) so the neutral anchor invariant is provably satisfied at the harness level.

**Python pre-wiring validation (all checks passed before any C# was written):**
Neutral + even → exact baseline (all four arms); pressure raises TO above neutral and lowers
it below neutral; flat lift with bad-hand balance (BH=30, ST=30 at high pressure still lifts TO);
low-pressure delta ≈ 0.000 (matchup muted), high-pressure delta = 0.159 (matchup open); monotone:
BH=30 > BH=50 > BH=80 at high pressure, ST=30 < ST=50 < ST=80; cap holds (0.18 at max lopsided);
foul rises with pressure, flat across matchup (BH/ST don't move it); JumpBall flat and sum = 1 in
all cases; overflow worst case TO + Foul = 0.261 << 1.

**Harness — ALL CHECKS PASSED.** Phase 12 sub-results:
- (a) Neutral anchor: ShotAttempt=0.85500000 (want 0.85500000), Turnover=0.09000000,
  Foul=0.05000000, JumpBall=0.00500000 — all exact.
- (b) TO: low=0.051123, neutral=0.090000, high=0.150790. Bad-hands flat lift: neutral=0.090000,
  high=0.150790 — flatLift=True.
- (c) Low-pressure delta=0.000000; high-pressure delta=0.157977. Gate confirmed.
- (d) BH monotone-fall: 0.179100 > 0.178185 > 0.037619. ST monotone-rise: 0.021123 < 0.150790 <
  0.179100.
- (e) TO=0.179100 ≤ ceiling=0.180000 — capped=True, sane=True (well below 35%).
- (f) Foul rises: 0.027788 < 0.050000 < 0.076983. Flat across matchup: all three cases
  0.07193442 at p=8 — flat=True.
- (g) All 6 cases: JumpBall == BaseJumpBall and sum == 1 — OK.
- (h) Null SelectedSlot → flat baseline: OK. Empty offense roster → flat baseline: OK. Zero
  populated defense → flat baseline: OK.
- `Phase 12 PASSED.`

The existing `RollFActionBatchCheck` stayed green (fresh stub path; four flat baseline rates
unchanged). `RollFHandoffCheck` and the 100k full chain passed unchanged.

**NOT changed (confirmed):** `RollF.cs`, `PlayerActionOutcomes.cs`, `RollFConfig.cs`,
`PossessionState.cs`, `GameState.cs`, `Player.cs`, `RollC.cs`, `RollD.cs`, `DefenderPicker.cs`.

**Walls held / deferred:**

- **Shot-quality face of pressure** (beat-the-press rim busts; packed-paint perimeter
  concession). Deferred. No hooks, no stubs.
- **Roll B, Roll A** (the same matchup one and two steps earlier in the chain). Their own
  fast-follow sessions.
- **Roll C turnover classification / type mix.** Pressure moves the turnover RATE (Roll F);
  the type mix (Roll C) is unchanged.
- **Player/steal attribution** (which defender gets the steal). Deferred attribution pass.
- **Broader coach-settings layer** (tempo, help rules, etc.) beyond the single pressure dial.
- **`PossessionState.DefenderSlot` promotion.** Deferred; the pick is still pure and
  deterministic. Phase 12 is the second consumer but promotion bar has not yet been crossed
  in a way that forces it.
- **Broader composites** (Passing, BasketballIQ, PerimeterDefense, Quickness in the matchup).
  Emmett settled on handling vs. steals alone.

## Session 42 — Phase 11: matchup-aware free-throw rebounding (Roll M, "the FT glass") (2026-06-15)

**A build (CONVENTIONS §0–§3) — code + harness, all green.** Roll M is now matchup-aware.
The FT glass is the twin of the field-goal glass (Roll I, Phase 10): the same two-touchpoint
size model, the same binary mass reweight, the same `Matchup.OffensiveReboundShare` door.
Four structural divergences from the Roll I template are the entire delta; no new math, no new
config, no new player attributes. This was the fast-follow Phase 10 promised.

**What the FT glass is, and why it's more defensive than the field-goal glass.**
Off a free throw, the shooter is behind the line by rule. Everyone else is lined calmly along
the lane in assigned box-out spots. The defense holds the better positions and no one crashes
from the shooter's spot. The model expresses this two ways, both already built by Phase 10:

1. A lower offensive baseline: Roll M's config is `Def 0.735 / Off 0.18`, natural off-share
   ≈ **0.197**, vs Roll I's live-miss ≈ 0.290. The bend operates from this lower baseline.
2. No shooter nerf: Roll M always passes `shooterIdx = -1` to `OffensiveReboundShare`. The
   nerf gate `i == shooterIdx` is never true at -1, so every offensive rebounder contributes
   un-nerfed — exactly right, because there is no crashing shooter.

**The four divergences from Roll I (the entire design delta):**

1. **No source selector (Divergence 1).** Roll I has two baselines (live-miss and block). Roll M
   has exactly ONE (missed final FT). The generator takes one-arg `Generate(PossessionState state)`,
   not two-arg. The interface is `IRollMPieGenerator` (mirrors `IRollGPieGenerator` shape, not
   `IRollIPieGenerator`). One cross-config baseline guard at construction, not two.

2. **No shooter, no nerf (Divergence 2).** Always `shooterIdx = -1`, `zone = ShotLocation.Rim`.
   The generator does NOT read `state.SelectedSlot` or `state.ShotType` for the matchup math.
   A null slot is normal; it must NOT trigger a fallback.

3. **Fallback: empty-roster ONLY (Divergence 3).** The only fallback condition is zero populated
   players on either team. No `SelectedSlot` check, no `ShotType` check. Every bonus FT trip
   reaches Roll M with `SelectedSlot = null`; this is expected, not a fallback trigger. The empty-
   roster path returns the flat baseline pie. A real game always has both teams populated.

4. **Resolver field previously concrete (Divergence 4).** `Resolver._rollMGenerator` was typed
   `RollMStubPieGenerator` (concrete stub). Retyped to `IRollMPieGenerator` (interface); ctor
   param likewise. Dispatch updated: `_rollMGenerator.Generate(c.State)` (was `Generate()`).

**What landed (5 files — 2 new + 3 edits):**

- **`Generators/IRollMPieGenerator.cs` (NEW)** — the interface. Single one-arg method
  `Pie<FreeThrowReboundOutcome> Generate(PossessionState state)`. Documented: one source (no
  `ReboundSource`), no shooter (no nerf), slot-blind by design.

- **`Generators/RollMGenerator.cs` (NEW)** — the matchup-aware real generator. Ctor takes
  `(RollMConfig cfg, MatchupConfig matchup, GameState game)` with null guards plus a
  **single cross-config invariant guard**: at construction, Roll M's natural off-share
  (`baseOff / (baseDef + baseOff) ≈ 0.197`) must lie inside `[ReboundOffShareFloor,
  ReboundOffShareCeiling]`. Confirmed in-band at draft: `0.197 ∈ [0.08, 0.55]`.
  `Generate(state)` reads both rosters (null-tolerant), short-circuits to the flat baseline
  if either team has zero populated players (the ONLY fallback condition — no slot check),
  then calls `Matchup.OffensiveReboundShare(offPlayers, defPlayers, shooterIdx: -1,
  zone: ShotLocation.Rim, baseOffShare, _matchup)`. Splits the `Def+Off` mass by the bent
  share; five flat slivers untouched. Includes the neutral coaching seam note (v1 is
  matchup-only; the crash-glass / get-back insertion point is after `OffensiveReboundShare`
  returns).

- **`Generators/RollMStubPieGenerator.cs` (edit)** — added `: IRollMPieGenerator`. Changed
  `Generate()` → `Generate(PossessionState state)`; `state` is accepted but intentionally
  ignored (documented). No behavior change.

- **`Core/Resolver.cs` (edit)** — three surgical changes: `_rollMGenerator` field retyped to
  `IRollMPieGenerator`; ctor param likewise; dispatch updated to `Generate(c.State)`.

- **`Harness/Program.cs` (edit)** — several changes:
  - Early `var rollMGenerator = new RollMStubPieGenerator(cfgM)` (line ~44, before `cfgMatchup`
    and `game` existed) **deleted**; replaced by `var rollMGenerator = new RollMGenerator(cfgM,
    cfgMatchup, game)` constructed after `SeatStartersFromConfig`, beside the G/H/I generators.
    Same move Phase 10 made for `rollIGenerator`. The `cfgM` naming note: `cfgM` is
    `RollMConfig` in Main; locally-scoped `cfgM` naming `MatchupConfig` inside Phase 6/7/8/9
    checks never collides (different scopes).
  - `RollMReboundBatchCheck` call site: passes `new RollMStubPieGenerator(cfgM)` instead of the
    matchup-aware `rollMGenerator`. The check asserts convergence to the *config* weights —
    it must stay flat. The check's internal `genM.Generate()` → `genM.Generate(state)`.
  - All 8 other `new RollMStubPieGenerator(...)` sites pass the stub to `new Resolver(...)`;
    they compile through `IRollMPieGenerator` (interface) without change — no direct
    `.Generate()` calls there.
  - `Phase11FreeThrowReboundDoorCheck(string configPath)` added and registered after
    `Phase10ReboundDoorCheck`. Seven deterministic sub-checks, mirroring Phase 10:
    (a) neutral all-50, no slot → off-share equals Roll M baseline exactly; five flat slivers
    equal config exactly. (b) size check: offense big (85) vs defense small (35) → off-share
    rises. (c) skill check: equal size, off OffReb=85 vs def DefReb=35 → off-share rises.
    (d) positional weight isolated: equal Str/Height (size check = wash); PostDefense alone
    separates; concentrated OffReb in high-PostDef player beats flat spread. ⚠ Cleaner than
    Phase 10's (d): no shooter slot to pick. (e) no-shooter invariance: null-slot state and
    stamped-slot+zone state produce **byte-identical** off-shares — proves Roll M is slot-blind
    (Divergences 2 and 3). (f) flat slivers unchanged across (b)–(c) cases. (g) FT baseline
    (≈ 0.197) strictly lower than Roll I live-miss baseline (≈ 0.290).

**NOT changed** (confirmed): `RollM.cs`, `FreeThrowReboundOutcomes.cs`, `RollMConfig.cs`,
`Harness/config.json`, `Core/Matchup.cs`, `Config/MatchupConfig.cs`, `Core/Player.cs`.
Roll M reuses the Phase 10 `Matchup` methods verbatim; no new config fields are needed.

**Python pre-wiring validation (all checks, all passed before any C# was written):**
neutral share unchanged (== 0.197 exact); bigger/better offense raises share monotonically;
extreme gaps stay strictly inside `(0.08, 0.55)`; positional weight exactly 1.0 at lineup mean,
bounded `(0.8, 1.2)`; `shooterIdx=-1` produces same share regardless of any slot index (the
no-shooter proof); FT baseline (0.197) strictly below field-goal baseline (0.290).

**Harness — ALL CHECKS PASSED.** Phase 11 sub-results: (a) neutral: exact 0.19672131 vs
baseline 0.19672131; (b) size check: 0.445793 > 0.196721; (c) skill check: 0.401045 > 0.196721;
(d) positional weight: concentrated 0.183464 > flat 0.179108; (e) no-shooter invariance:
null-slot and stamped-slot both produce 0.1967213115 — identical to 10 decimal places;
(f) flat slivers exactly equal config across both tested cases; (g) Roll M 0.196721 < Roll I
0.290323. The existing `RollMReboundBatchCheck` stayed green (flat stub path); all prior Phase 6
through Phase 10 checks unchanged.

**Walls held / deferred:**
- **Per-player rebound attribution** (which slot grabbed the board): Roll M decides only which
  TEAM. No `PossessionState` fact stamped. The attribution pass is separate.
- **Coaching sliders** (crash-glass / get-back): same documented neutral seam as Phase 10.
  `finalOffShare` is the insertion point; v1 is matchup-only.
- **Per-zone FT rebounding**: Roll M has no shot zone at all for a bonus trip. `zone = Rim`
  is a constant (the nerf gate never fires when `shooterIdx = -1`).
- **`Hustle`, athletic/big axis split**: same parks as Phase 10.

## Session 41 — Phase 10: matchup-aware rebounding (Roll I, "the glass") (2026-06-15)

**A build (CONVENTIONS §0–§3) — code + harness, all green.** Roll I is now matchup-aware.
Before this session every rebound resolved from the same flat 66/27 defensive/offensive split
regardless of who was on the floor. Phase 10 bends that split by a two-touchpoint model: the
pre-staging team-size check (which team is bigger/stronger on the floor) and the
positional-weighted skill check (whose rebounders are in position, weighted by post-ness).

**The two-touchpoint model (settled in design, implemented here):**

1. **Pre-staging size check (team-vs-team, external).** Compare each team's mean
   `ReboundPhysical` composite (height + strength). The bigger team tilts the split its way.
   This is a *relative* comparison — a 7-foot stiff helps against a small lineup and hurts
   against giants. Size earns boards on its own, independent of rebounding skill.

2. **Positional-weighted skill shift (intra-team, internal).** Within each lineup, each player
   gets a `PositionalWeight` based on their `Postness` (height + post defense + strength)
   relative to the lineup mean — posts above 1.0, guards below 1.0, exactly 1.0 at the mean.
   Applied to `OffensiveRebounding` (offense) or `DefensiveRebounding` (defense) as a weighted
   mean before comparing team edges. The shooter's offensive contribution is nerfed on
   `Three/Long/Mid` (the shooter is already outside and can't crash easily); no nerf on
   `Rim/Short`.

Both shifts sum additively into a `totalShift`, then bend the offensive off-share toward a
ceiling (offense crashes successfully) or floor (defense locks the glass) via tanh saturation —
the same shape as `BlockWeight` and `FoulRate`. Plain addition; tanh is odd and supplies the
sign (the Session 38 lesson, honored here).

**The binary mass reweight (Divergence 3 from the Phase 9 template).** Roll G renormalizes all
five location slices. Roll I only moves `DefensiveRebound` and `OffensiveRebound` within the
`Def+Off` mass. The five flat slivers (fouls, OOB, jump-ball) stay at their config values
unchanged. The pie still sums to 1 by construction.

**What landed (8 files — 2 new + 6 edits):**

- **`Generators/IRollIPieGenerator.cs` (NEW)** — the interface, mirroring `IRollGPieGenerator`
  and `IRollHPieGenerator`. Two-arg `Generate(PossessionState state, ReboundSource source)` —
  the structural Divergence 1 from Phase 9: Roll I's generator needs both the source (which
  baseline pie) AND the state (rosters, shooter slot, shot zone). Both the stub and the real
  generator implement it; the Resolver field types to the interface.

- **`Generators/RollIGenerator.cs` (NEW)** — the matchup-aware real generator. Ctor takes
  `(RollIConfig, MatchupConfig, GameState)` with null guards plus a **cross-config invariant
  guard**: at construction, both source baselines' off-shares must lie inside
  `[ReboundOffShareFloor, ReboundOffShareCeiling]`. If a future config edit pushes a baseline
  outside the bend band, the tanh direction would invert silently — caught loud at startup.
  Fallback fires before any `SelectedSlot`/`ShotType` read: if either team has zero populated
  players OR `SelectedSlot` is null (batch-check / harness paths with real rosters but no
  shooter stamped), returns the flat baseline pie. A real in-game possession always has both
  rosters populated and a slot stamped. Populated path computes `Matchup.OffensiveReboundShare`,
  splits the mass, leaves the five slivers untouched, and returns the seven-way pie.
  Includes a **neutral coaching seam** note: crash-glass / get-back sliders will bend
  `finalOffShare` further when the strategy layer lands; v1 is matchup-only; the documented
  insertion point sits at identity.

- **`Generators/RollIStubPieGenerator.cs` (edit)** — added `: IRollIPieGenerator`. Changed
  `Generate(ReboundSource source)` → `Generate(PossessionState state, ReboundSource source)`;
  `state` is explicitly documented as ignored. No behavior change.

- **`Core/Matchup.cs` (edit)** — four new Phase 10 public static methods:
  - `ReboundPhysical(Player p, MatchupConfig cfg)` — the pre-staging size composite:
    `ReboundStrengthWeight × Strength + ReboundHeightWeight × Height`. Mirrors `LengthRating`.
  - `Postness(Player p, MatchupConfig cfg)` — the positional composite:
    `PostnessHeight × Height + PostnessPostDefense × PostDefense + PostnessStrength × Strength`.
  - `PositionalWeight(double playerPostness, double lineupMeanPostness, MatchupConfig cfg)` —
    `1.0 + ReboundPositionalSwing × tanh((postness − mean) / ReboundPositionalScale)`. Exactly
    1.0 at the lineup mean; bounded in `(1−swing, 1+swing)` ≈ `(0.8, 1.2)`; monotone.
  - `OffensiveReboundShare(...)` — the door. Stage 1: mean `ReboundPhysical` gap → `GapFn` →
    `sizeShift`. Stage 2: each player's rebounding rating × `PositionalWeight` × shooter nerf
    → weighted mean per team → `GapFn` → `skillShift`. Compose: `totalShift =
    ReboundSizeWeight × sizeShift + ReboundSkillWeight × skillShift`. Tanh saturation toward
    ceiling or floor; plain addition (tanh supplies the sign).

- **`Config/MatchupConfig.cs` (edit)** — Phase 10 block appended: 14 new properties
  (`ReboundStrengthWeight`, `ReboundHeightWeight`, `PostnessHeight`, `PostnessPostDefense`,
  `PostnessStrength`, `ReboundPositionalSwing`, `ReboundPositionalScale`, `ReboundSizeWeight`,
  `ReboundSkillWeight`, `ReboundShooterNerf`, `ReboundOffShareFloor`, `ReboundOffShareCeiling`,
  `ReboundReferenceShift`). All with calibration-placeholder defaults. `Load` invariants added:
  `ReboundSizeWeight + ReboundSkillWeight == 1.0`; `0 ≤ floor < ceiling ≤ 1.0`; reference
  shift > 0; positional scale > 0; `0 ≤ swing < 1.0`; `0 ≤ ShooterNerf ≤ 1.0` (a nerf,
  never a boost or negative).

- **`Core/Resolver.cs` (edit)** — two surgical changes: `_rollIGenerator` field retyped from
  `RollIStubPieGenerator` to `IRollIPieGenerator`; ctor param likewise. Dispatch updated to
  pass `c.State` into `Generate`: `_rollIGenerator.Generate(c.State, c.ReboundSource ??
  ReboundSource.LiveBall)`. The `?? LiveBall` null-coalesce stays in the resolver.

- **`Harness/Program.cs` (edit)** — several changes:
  - Early `var rollIGenerator = new RollIStubPieGenerator(cfgI)` (line ~40, before
    `cfgMatchup` and rosters existed) **deleted**; replaced by
    `var rollIGenerator = new RollIGenerator(cfgI, cfgMatchup, game)` constructed after
    `cfgMatchup` and `SeatStartersFromConfig`, beside `rollGGenerator` and `rollHGenerator`.
    Exact same move Phase 9 made for `rollGGenerator`.
  - Three batch check call sites updated to pass `new RollIStubPieGenerator(cfgI)` instead
    of the matchup-aware `rollIGenerator` — keeps the four baseline rate checks flat and
    independent of matchup (same cure as the Phase 9 `RollHResolutionBatchCheck` fix).
  - All five `genI.Generate(src)` calls updated to `genI.Generate(state, src)` (four batch
    check internals + one observability section).
  - `Phase10ReboundDoorCheck(string configPath)` added and registered after
    `Phase9LocationDoorCheck`. Seven sub-checks, all deterministic (no Monte Carlo batch):
    (a) neutral all-50 → off-share equals baseline exactly; five flat slivers equal config
    exactly. (b) size check: offense big (85) vs defense small (35) → off-share rises.
    (c) skill check: equal size, off OffReb=85 vs def DefReb=35 → off-share rises.
    (d) positional weight isolated: equal height+strength (size check = wash); PostDefense
    alone separates the teams; concentrated OffReb in high-PostDef player beats flat spread.
    (e) shooter nerf: Three (nerf on) yields lower off-share than Rim (nerf off). (f) flat
    slivers unchanged across (b)–(e) cases. (g) block source baseline: at neutral, block
    off-share ≈ 0.390, distinct from live-miss ≈ 0.290.

- **`Harness/config.json` (edit)** — 14 new Phase 10 keys added to the `Matchup` section.
  `RollI` section unchanged.

**Harness — ALL CHECKS PASSED (two runs).** First run threw on `SelectedSlot is null` in
the 100k batch check: the main game has real rosters (from `SeatStartersFromConfig`), so the
empty-roster fallback didn't fire, but the batch check's bare possession state has no shooter
stamped. Fix: extended the fallback to also cover `state.SelectedSlot is null` — any path
without a stamped shooter returns the flat baseline. This is correct: a real in-game possession
always has a slot stamped by Roll E before reaching Roll I. Second run: all green.

**Phase 10 sub-results (from the second run):** (a) neutral: exact 0.29032258 vs baseline
0.29032258; (b) size check: 0.473403 > 0.290323; (c) skill check: 0.440510 > 0.290323;
(d) positional weight: concentrated 0.266434 > flat 0.258584 — positional weight rewards
OffReb in bigs; (e) nerf: Three 0.287977 < Rim 0.290323; (f) all three tested cases: flat
slivers exactly equal config; (g) block off-share 0.390244 = blockBase 0.390244, gap from
live-miss > 0.05.

**Python pre-wiring validation (all 8 checks, all passed before any C# was written):**
neutral share unchanged; bigger/better team monotonically raises share; extreme gaps stay
strictly inside `(0.08, 0.55)`; positional weight exactly 1.0 at lineup mean, bounded
`(0.8, 1.2)`; shooter nerf lowers offensive contribution only on `Three/Long/Mid`; block
baseline distinct from live-miss baseline; both baselines inside `[floor, ceiling]`.

**The execution error (not a prompt failure).** The prompt explicitly said "Do NOT
front-load a `SelectedSlot ?? throw`" and "the fallback is on roster population, not slot."
The first implementation threw on null `SelectedSlot` anyway. The fix was one condition
added to the existing fallback guard.

**Walls held / deferred:**
- **Roll M** (free-throw-board rebounds) — same battle, different baseline, no shooter nerf.
  Fast-follow next session; these exact rails carry it.
- **Per-player rebound attribution** (which slot grabbed the board) — Roll I decides only
  which TEAM. The attribution pass is separate.
- **Coaching sliders** (crash-glass vs. get-back, crash-vs-break-out) — documented neutral
  seam; no code hook needed; strategy layer not yet built.
- **Per-zone rebounding** (long misses off threes favoring guards) — the zone gate for the
  shooter nerf is the only zone read. No per-zone rebound table.
- **`Hustle`** — exists on `Player`, noted as the natural amplifier to fold in later; not
  used in v1.
- **Athletic/big axis split** — the glass uses its own `ReboundPhysical` and `Postness`
  composites; does NOT read `Player.Athleticism`; no entanglement with the deferred
  horizontal/vertical axis refactor.

## Session 40 — Phase 9: the shot location door (2026-06-15)

**A build (CONVENTIONS §0–§3) — code + harness, all green.** Roll G is now matchup-aware.
Before this session every shooter got the same shot-zone distribution regardless of who they
were or who was guarding them. Phase 9 makes Roll G read each player's **authored per-zone
shot tendencies** (five new attributes), bends them by the **defending team's collective
per-zone resistance**, and renormalizes — so the offense's shot mix shifts toward the zones
where the defense is weakest.

This is the first matchup-aware door that reads the **entire defending team** rather than just
the slot-matched defender: shot location is the least one-on-one decision. A great rim protector
suppresses rim attempts even when he isn't the player directly guarding the shooter, because
he'll rotate.

**What landed (14 files — 9 edits + 5 new):**

- **`Core/Player.cs` (edit)** — five new `int` authored attributes (0–99): `RimTendency`,
  `ShortTendency`, `MidTendency`, `LongTendency`, `ThreeTendency`. Represent how much a player
  WANTS to shoot from each zone, independent of how well he converts there (Klay Thompson and
  Steph Curry can have similar three-point skill but very different tendency values). Five new
  `Check(...)` calls in `Validate()` plus a **tendency-sum rule**: if all five are zero the
  config has missing fields; `Player.Validate()` throws loud at load time rather than letting
  Roll G build an invalid pie at runtime.

- **`Config/RosterConfig.cs` (edit)** — five new `int` fields in the `PlayerConfig` DTO and
  five new lines in `ToPlayer()`, mirroring `FoulDrawing`'s Session 39 shape exactly.

- **`Config/MatchupConfig.cs` (edit)** — five new Phase 9 parameters after the Phase 8 block:
  `LocationBlendFirst` (0.55), `LocationBlendSecond` (0.30), `LocationBlendThird` (0.15) —
  the top-3 defender blend weights, enforced to sum to 1.0 in `Load`; `LocationReferenceShift`
  (20.0, mirrors `BlockReferenceShift` / `FoulReferenceShift`); `LocationMaxMultiplier` (2.5,
  the upper asymptote — and 1/this is the lower). All five invariants enforced in `Load`.

- **`Core/CoachProfile.cs` (NEW)** — empty placeholder `sealed record CoachProfile`. Exists so
  the coaching seam can use a semantically-honest type from day one instead of faking it with
  `Player?`. Future coaching session adds fields here and replaces `CoachingPull.Apply`'s body;
  all call sites in `RollGGenerator` stay unchanged.

- **`Core/CoachingPull.cs` (NEW)** — the coaching-malleability seam. v1 is the **identity
  function**: `Apply(shooter, null, null)` returns the shooter's authored tendency values
  unchanged. The seam ships so the future coaching session is a clean append (body replaces,
  signature stays). Three design decisions logged in comments as deferred: malleability is
  per-player (stars conform less), coaching pull can be against the player's best interest, and
  the "system" question (5 independent zone preferences vs coherent identity) is its own design
  call.

- **`Core/Matchup.cs` (edit)** — two new pure-static methods:
  - `DefensiveResistance(zone, defenders, cfg)` — the defending team's per-zone resistance as a
    top-3 weighted blend of the five defenders' `DefenseRating` at that zone, ranked descending.
    Renormalizes blend weights when fewer than 3 defenders are populated. Throws if zero
    defenders (caller must short-circuit first).
  - `LocationMultiplier(zone, shooter, defenders, cfg)` — the per-zone multiplier in the ratio
    form: `exp(log(MaxMultiplier) × tanh(shift / ReferenceShift))`. Bounded in
    `(1/Max, Max)`, exactly 1.0 at zero gap, **never negative**. Public static (mirrors Phase
    7's `BlockWeight` and Phase 8's `FoulRate`) so the harness can test the math directly
    without needing access to a private generator helper.

- **`Generators/IRollGPieGenerator.cs` (NEW)** — interface mirroring `IRollHPieGenerator`.
  Single method `Pie<ShotLocation> Generate(PossessionState state)`. Both stub and real
  generator implement it; Resolver holds the interface.

- **`Generators/RollGStubPieGenerator.cs` (edit)** — single change: `: IRollGPieGenerator`
  added to the class declaration. No behavior modification.

- **`Generators/RollGGenerator.cs` (NEW)** — the matchup-aware Roll G pie generator. Reads the
  shooter's five tendency attributes, routes them through the coaching seam (identity in v1),
  then computes per-zone multipliers via `Matchup.LocationMultiplier` and renormalizes. Three
  fallback paths: (1) no shooter → flat config stub pie; (2) shooter present, zero populated
  defenders → normalized player tendencies with no matchup multiplier; (3) 1–5 defenders →
  top-3 blend. The critical math finding: **uniform gaps cancel in renormalization** — if every
  zone has the same gap, every multiplier is the same, and renormalization restores the original
  tendency mix. Mix shifts only when gaps are *unequal* across zones, not when there is a
  uniform level difference. D3 vs D3 and D1 vs D1 produce similar mixes; the weird stuff
  happens when you mix levels AND the shapes are uneven.

- **`Core/Resolver.cs` (edit)** — two surgical type changes: `_rollGGenerator` field and ctor
  param changed from `RollGStubPieGenerator` to `IRollGPieGenerator`. All else unchanged.

- **`Harness/Program.cs` (edit)** — several changes:
  - `SeatStartersFromConfig(game, configPath)` helper added. Mirrors the seating loop in
    `Phase1RosterCheck`. Must be called after `GameState` construction and before any
    generator that reads `PlayerAt`.
  - Called in `Main` (between `game` creation and generator construction) and in `RunGame`
    (harmless future-proofing; generators there remain stubs for now).
  - Main's early `var rollGGenerator = new RollGStubPieGenerator(cfgG)` removed; replaced by
    `var rollGGenerator = new RollGGenerator(cfgG, cfgMatchup, game)` after `cfgMatchup` and
    the seating call. Variable name `cfgMatchup` preserved (important: `cfgM` already names
    `RollMConfig` in Main).
  - All three `Mk()` helpers (Phase 6, 7, 8) extended with five new optional tendency params.
  - `RunThreePointBatch` player constructions gained `FoulDrawing = 50` (carry-forward miss
    from Phase 8) and the five new tendency fields (defaulting to 50).
  - `RollGLocationBatchCheck` switched to option (ii): constructs its own stub internally so
    the baseline regression stays flat regardless of what Main's live chain does. The `genG`
    parameter was removed; the function now takes `(cfg, cfgG, state)`.
  - `Phase9LocationDoorCheck` — new check (8 sub-checks), wired into `Main` after
    `Phase8FoulDoorCheck`.
  - `RollHResolutionBatchCheck` — Phase 9 required a fix: `SeatStartersFromConfig` now seats
    real players in Main's `game` before `rollHGenerator` is constructed. The check used the
    passed `rollHGenerator` (tied to a now-populated game) and expected values calibrated
    against neutral/stub behavior. Fix: the check now constructs its own isolated
    `RollHGenerator` with an empty local game, so PlayerAt returns null, the generator falls
    back to stub rates, and the calibrated expected values remain valid. The Phase 6/7/8
    matchup checks construct their own isolated setups and were unaffected.

- **`Harness/config.json` (edit)** — `Matchup` section: five new Phase 9 fields. `Rosters`
  section: all 10 players given all five tendency fields, varied by archetype (post bigs: high
  Rim/Short; catch-and-shoot wings: high Three; guards/creators: balanced leaning Three+Mid;
  versatile wings: balanced all five).

**The ratio-form multiplier (v2 fix, critical).** An earlier draft used an additive form
(`1 + tanh(shift) × Range`) with `LocationRange = 1.5`, which could produce negative
multipliers (lower asymptote `1 − 1.5 = −0.5`). The ratio form:
`exp(log(MaxMultiplier) × tanh(shift / ReferenceShift))` is **bounded in
(1/Max, Max), exactly 1.0 at zero gap, and can NEVER go negative.** With `MaxMultiplier = 2.5`
the range is `(0.4, 2.5)`. Python pre-check confirmed: extreme positive (fin=99 vs rimP=1) →
2.491, extreme negative (fin=1 vs rimP=99) → 0.401, zero gap → 1.000 exactly.

**Harness (two runs, both green).** First run failed on `RollHResolutionBatchCheck`:
observed Made rate was 48.4% vs expected 36.5%. Root cause: `SeatStartersFromConfig` now
populates Main's game before `rollHGenerator` is constructed, so the real matchup fired with
actual player attributes. The expected values were calibrated against neutral/stub behavior.
Fix described above. Second run: **ALL CHECKS PASSED.**

**Phase 9 sub-results (all from the second run):** (a) zero-gap exact (50.0000% vs expected
50.0000%); (b) all-weak-rim defense raises rim share 24.993% > 20%; (c) Config B (1 elite + 3
solid) resists more than Config A (1 elite + 4 weak), B rim share 17.6% < A rim share 19.9%;
(d1) D1 finisher vs D3 weak-rim defense: rim rises 65.9% vs 50% tendency baseline; (d2) same-
level matched control: mix stays within 4.6pp of baseline; (d3) uniform D1 vs D3 (everyone
75 vs 45): diff = 0.000% confirming uniform gaps cancel; (e1) zero defenders: 20.0000% exact;
(e2) one elite rim defender: rim 14.2% < 20%; (e3) one elite + two normal: rim 17.9% > 14.2%
(elite diluted — counterintuitive but correct, one elite alone gets 100% blend weight); (f)
coaching seam identity confirmed (30,40,50,60,70); (g) Roll H regression valid; (h) negative-
multiplier guard confirmed (2.491 < 2.5, 0.401 > 0.400).

**The counterintuitive DEC-6 finding (sub-check e).** One elite rim defender in a slot-1-only
roster suppresses rim attempts MORE than the same elite defender plus two average helpers.
This is correct: with one defender, the blend renormalizes his weight to 1.0 (he carries
everything). With three populated defenders, his weight is diluted to 0.55, and the two average
defenders (RimProtection=50) pull the blended resistance down from 99 toward 77. The engine
treats "1 elite, no help" as "the elite is everywhere because there's no one else to cover for."
This is not a bug.

**Walls held / deferred:** shot location only — Roll H's three doors (make, block, foul), OOB
rates, putback contests, and the rebound pie are unchanged. The coaching layer itself ships as
an identity seam (`CoachProfile` empty, `CoachingPull.Apply` returns authored values). Per-zone
blend weights are global for v1. `PossessionState.DefenderSlot` NOT promoted (Phase 9 reads
the defending ROSTER, not a carried slot; slot-matched picker isn't relevant to the whole-team
read). Roll G's `Execute` signature stays `(state, pie, rng)` — only the generator reads
`GameState`.

**Stub count note (surfaced at delivery, not a bug).** The prompt predicted 13 stub sites after
the Main conversion; the actual count is 14. The discrepancy: when `RollGLocationBatchCheck`
was updated to option (ii), it gained its own `new RollGStubPieGenerator(cfgG)` construction
inside the function body — a new site that replaced the one previously passed as a parameter
from line 39 of Main. Net effect: Main's line 39 was removed (−1) and the check function gained
one (+1), holding the count at 14. Behavior is correct: 1 real generator site in Main, 13
stub-only check sites plus 1 baseline-regression site inside `RollGLocationBatchCheck`.

## Session 39 — Phase 8: the foul door (2026-06-15)

**A build (CONVENTIONS §0–§3) — code + harness, all green.** The third matchup door is now wired:
**shooting fouls** are matchup-aware. A foul-magnet (high `FoulDrawing`) going up strong at the rim
against an undisciplined defender draws fouls far more often than a catch-and-shoot wing at the three;
before this session the foul rate was a flat per-zone number regardless of who was shooting or defending.
Phase 8 also fixes a related basketball-wrong flatness: the foul rate was the same at every zone (7%
everywhere), when in reality three-point shooters almost never draw shooting fouls and rim attempts draw
them constantly.

**What landed (9 files):**
- **`Core/Player.cs` (edit)** — one new authored `int` attribute: `FoulDrawing` (0–99), placed near
  `Finishing` and `FreeThrow`. Comment notes the asymmetry: low `FoulDrawing` is NOT a skill, it is
  absence of opportunity — the model encodes this via narrow downward floors, not by reducing the
  attribute's effect. Validation check added.
- **`Config/RosterConfig.cs` (edit)** — `FoulDrawing` added to `PlayerConfig` DTO and `ToPlayer()`
  mapping.
- **`Config/RollHConfig.cs` (edit)** — `BaseMadeAndFouled` and `BaseMissFouled` **retired** (5 touch
  sites gone, stale-ref grep returns zero). Replaced by 10 new per-zone fields: `FoulRim/Short/Mid/
  Long/Three` (foul baselines, replaces the flat ~7% blob with a 20%/10%/5%/3%/1.5% spread) and
  `MafFractionRim/Short/Mid/Long/Three` (the and-1 split — how much of a drawn foul becomes a
  MadeAndFouled vs MissFouled; rim 35%, three 10%). Both exposed via `FoulRate(zone)` and
  `MafFraction(zone)` accessor methods mirroring `BlockWeight(zone)`.
- **`Config/MatchupConfig.cs` (edit)** — 14 new Phase 8 fields: per-zone foul floor (5) and ceiling (5)
  encoding the asymmetry (floors close to baseline, ceilings far above), `FoulReferenceShift` (tanh
  saturation knob, default 20.0), `OffenseFoulWeight` (0.80), `DefenseFoulWeight` (0.20), and
  `AttributeMidpoint` (50.0). `Load` extended with invariants: weights sum to 1.0, floor ≥ 0,
  ceiling > floor, shift > 0, midpoint > 0.
- **`Core/Matchup.cs` (edit)** — `FoulRate(zone, shooter, defender, baseFoulRate, cfg)` added as a
  pure static method. Distinct shape from Phases 6 and 7: offense-dominant asymmetric weights rather
  than a raw attribute gap, both sides expressed as deviations from `AttributeMidpoint` so an average
  player (50) contributes zero. No physical anchor — Emmett's call that foul-drawing's correlation with
  size lives in attribute generation, not in the contest. Reuses `GapFn` with the skill parameters.
  Same tanh saturation shape as Phase 7 (plain addition — no sign conditional, tanh already supplies
  the sign, same Session 38 lesson that caught the Phase 7 bug).
- **`Generators/RollHGenerator.cs` (edit)** — `Generate` now computes `foulRate` via `Matchup.FoulRate`
  (or `_cfg.FoulRate(zone)` for the DEC-6 empty-slot fallback, same shape as block and make doors).
  `BuildRealPie` signature gains a `foulRate` parameter; the carve math changes from one-carve (block)
  to two-carve (block + foul): `nonBlockNonFoul = 1 − block − foul`; `made = makePct × nonBlockNonFoul`;
  `maf = foul × MafFraction(zone)`; `missFouled = foul × (1 − MafFraction)`; `Miss/OOB×2` scale to
  fill the rest. `BuildStubPie` and `BuildPutbackPie` rewired to use per-zone `FoulRate`/`MafFraction`
  (same carve math, no matchup — stub and putback paths have no defender data). Overflow guard added:
  throws if `block + foul ≥ 1`.
- **`Generators/RollHStubPieGenerator.cs` (edit)** — same Phase 8 carve logic applied to the
  matchup-blind stub path.
- **`Harness/Program.cs` (edit)** — `RollHBatchCheck` expected values rewritten to use Phase 8 carve
  math (zone-blended `FoulRate` and `MafFraction` instead of the retired flat fields). Both
  `Phase6MatchupWiringCheck` and `Phase7BlockDoorCheck` Mk() helpers updated with `FoulDrawing`
  parameter. `RollKPutbackPieCheck` expected values updated to Phase 8 carve math (was the sole failure
  on the first harness run — a preexisting flat-Putback* expected array that the Phase 8 carve
  displaced). New `Phase8FoulDoorCheck` (seven sub-checks) added and wired into `Main`.
- **`Harness/config.json` (edit)** — `FoulDrawing` added to all 10 players (varied by archetype: post
  bigs 85–88, drivers 60–68, catch-and-shoot wings 28–30). `RollH` section: `BaseMadeAndFouled` and
  `BaseMissFouled` removed, 10 new foul/MAF fields added. `Matchup` section: 14 new Phase 8 fields
  added.

**The putback failure on the first harness run.** `RollKPutbackPieCheck` compared the putback pie's
slices against the old flat `Putback*` config values (e.g. expected `Made = 0.50` flat). After Phase 8,
`BuildPutbackPie` applies the carve math (block off the top, foul off the top, remainder scaled), so the
slice for `Made` is `PutbackMade × nonBlockNonFoul ≈ 0.365`, not `0.50`. The check's expected array was
a preexisting assumption that Phase 8 invalidated. Fix: compute expected values in the harness using the
same carve formula `BuildPutbackPie` uses. Second run: all green.

**Harness — ALL CHECKS PASSED (second run).** Phase 8 sub-results: (a) shooter FD sweep @Rim monotone
19.2% → 28.3%, below ceiling 35%, top noticeably above baseline 20%; (b) defender Disc sweep @Rim
monotone 20.3% → 19.9%, above floor 17%, only slightly below baseline (small downward range confirmed);
(c) asymmetry up_bend 6.8% vs down_bend 0.09% — up > 3× down, confirming "low FoulDrawing is not an
active skill"; (d) Three spread 1.6% vs Rim spread 9.1% — zone differentiation working; (e) DEC-6
fallback exact baseline, even matchup exact baseline, elite drawer 26.8% > 22%; (f) MAF split at Rim:
14,502 fouled outcomes, 35.00% MAF vs expected 35.00% (exact); (g) regression — elite finisher vs
weak rim protector still produces a valid pie and 76.2% make+and-1. Phase 6 and Phase 7 unchanged.
`RollHBatchCheck` all green with the new per-zone expected values.

**Walls held / deferred:** shooting fouls only — OOB rates, block weight, make %, location, and the
rebound pie remain matchup-blind. Per-player foul tracking (individual foul trouble, the "2-in-1st-half"
cascade) is its own future session requiring a personal foul ledger that doesn't exist yet (team fouls
via `FoulTracker`; individual fouls deferred). `PossessionState.DefenderSlot` NOT promoted — Phase 8 is
the third matchup-aware door but still reuses the single-consumer slot-guards-slot picker (promotion bar
still "a second independent consumer"). No per-zone foul contest weights — Emmett's call: one global
offense-dominant pair, per-zone variation in impact lives in the floors/ceilings. No physical anchor on
the foul contest — Emmett's call: foul-drawing's correlation with size lives in attribute generation.

## Session 38 — Phase 7: the block door (2026-06-15)

**A build (CONVENTIONS §0–§3) — code + harness, all green.** The second matchup door is now wired:
**blocks** are matchup-aware. A shot at the rim against a 7'1" rim protector is blocked more often than
the same shot against a 6'2" guard, and before this session it wasn't — the block weight was flat per
zone from `RollHConfig`. Phase 7 leaves the make door untouched and bends only `BlockWeight`. OOB rates,
foul rates, and every other Roll H weight remain matchup-blind; that's the next session's work.

**What landed (5 files):**
- **`Core/Matchup.cs` (edit)** — two new pure-static methods. `LengthRating(p, cfg)` is the new
  block-specific physical composite — `Height·LengthHeight + Wingspan·LengthWingspan + Vertical·LengthVertical`,
  blend weights in config so the calibration pass can tune the composite without touching code. **It is
  deliberately not the existing `Athleticism` composite** — length is what blocks shots; quickness and
  strength belong to the make door. `BlockWeight(zone, shooter, defender, baseBlockWeight, cfg)` is the
  new core: skill gap (defender blend − shooter zone skill, same attribute reads as Phase 6) and length
  gap (defender length − shooter length) each through `GapFn` with the existing skill/physical params,
  per-zone weighted sum (e.g. 40% skill / 60% length at Rim and Three), tanh-saturated toward a per-zone
  ceiling (defender edge) or floor (shooter edge). All existing methods untouched.
- **`Config/MatchupConfig.cs` (edit)** — per-zone skill/length weight pair (sum to 1.0, enforced),
  per-zone block floor/ceiling (floor ≥ 0, ceiling > floor, enforced), `BlockReferenceShift` saturation
  knob (default 20.0), `LengthHeight/Wingspan/Vertical` blend (sum to 1.0, enforced). Accessors
  (`BlockContestWeights`, `BlockFloor`, `BlockCeiling`) mirror the `BlendWeights` style. `Load` extended
  with the new invariants — Phase 6's load-validation pattern, never `RollHConfig`'s no-validation one.
- **`Generators/RollHGenerator.cs` (edit)** — between defender resolution and the pie build, the block
  weight is computed via `Matchup.BlockWeight` (or `_cfg.BlockWeight(zone)` if the defending slot is
  empty — DEC-6 fallback, same shape as the make door's). `BuildRealPie`'s signature gained a
  `blockWeight` parameter; the carve-then-convert math is unchanged (it already handled any block weight
  in [0, 1) safely). `BuildStubPie` and `BuildPutbackPie` keep reading the flat config baseline — the
  stub path runs only for unpopulated rosters (no matchup data), and the putback contest is a known
  Phase 4 deferral.
- **`Harness/config.json` (edit)** — `Matchup` section extended with the 24 new fields (10 weights,
  10 floor/ceiling, the reference shift, the 3 length blend weights).
- **`Harness/Program.cs` (edit)** — new `Phase7BlockDoorCheck` (six sub-checks, wired into `Main` after
  the Phase 6 call).

**The one design call (spec contradiction caught at pre-check): the tanh sign.** The session prompt's
§2c formula said `result = bw + (shift >= 0 ? bend : -bend)`. The Monte-Carlo pre-check on check (e) —
**shooter-edge symmetry, where block rate must FALL as the shooter improves** — failed: the block rate
*rose* once the shooter's edge grew past about gap 50 instead of asymptoting toward the floor. Trace:
`tanh` is odd, so when `totalShift` is negative, `bend = span · tanh(total/ref)` is already negative; the
spec's `-bend` re-flipped it to positive and bent the result *up* instead of down. The fix is one line:
`return baseBlockWeight + bend`. The spec wrote the conditional flip to mirror the asymmetric span
selection (different headroom toward ceiling vs floor), but didn't account for tanh already supplying the
sign. Surfaced before delivering per CONVENTIONS §0; Emmett approved the fix; check (e) then dropped
monotonically from 15.3% to 5.8% across the shooter sweep.

**Harness — ALL CHECKS PASSED.** Phase 7 sub-results, all green: (a) Rim defender sweep monotone 11.5% →
24.0%, bounded by [4.0%, 30.0%]; (b) Three sweep monotone 0.89% → 3.32%, spread 2.4% vs Rim spread 12.5%
(per-zone weights working — Three is much flatter); (c) at both Rim and Three the length-only delta
exceeds the skill-only delta (Rim: 9.7% > 4.2%; Three: 1.6% > 1.4%), confirming the 60/40 split;
(d) empty slot == baseline == even matchup (12.00% exact), strong defender raises to 21.4%; (e) shooter
edge bends block down monotonically, 15.3% → 5.8%, never crossing the floor; (f) regression — elite
finisher vs weak rim protector still produces a valid pie, 88.1% make rate. Phase 6 unchanged. The full
100k chain still passes — the zone-blended observed block rate (5.59% vs configured 5.68%) sits inside
tolerance even though the per-shot weight now varies (the population of contested shots in the harness
has no shooter, so the stub-pie fallback fires and reads the configured baseline; the matchup path is
only exercised through the populated-roster Phase 7 check, by design).

**Walls held / deferred:** block door only — OOB rates, shooting-foul rates, and the rebound pie all
remain matchup-blind. The putback contest stays a Phase 4 deferral. The asymmetry between Phase 6's
Athleticism composite (5 attributes) and Phase 7's Length composite (3 attributes) is **intentional** and
not unified — they read different physical signals. No `PossessionState.DefenderSlot` (still derived at
generate-time; promotion deferred until a second door needs it — Phase 7 is the second door but reuses
the picker, so the bar still isn't reached). The placeholder magnitudes (per-zone weights, floors,
ceilings, the reference shift) are best-guess scaffolding — calibration owns the numbers.

## Session 37.5 — Hygiene: Roll H pie overflow fix + retired-stub hard-errors (2026-06-15)

**A hygiene pass (no new features) — code + harness, all green.** Three surgical fixes from an
external-LLM code audit, addressed before the blocks door so the next session builds on clean ground.

**What landed (3 files):**
- **`Generators/RollHGenerator.cs` (edit)** — **fix #8, the standout real bug.** `BuildRealPie` computed
  `nonMadeShare = 1 − block − makePct` and scaled the five other non-Made outcomes by it. When
  `makePct + block > 1` (reachable at the rim: ceiling 0.93 + block 0.12 = 1.05), that share went
  negative → negative weights → the `Pie` constructor throws. It had never fired only because no check
  ran the real generator at a high rim rating. The fix is **carve-then-convert**: block is carved off the
  top first (`nonBlock = 1 − block`), and the logistic `makePct` is the conversion rate *given not
  blocked* (`made = makePct × nonBlock`). This matches how the stub has always worked (`BaseMade × (1 −
  block)`), can never overflow for any `makePct` in [0, 1], and changes the make-curve's *meaning*: it
  now reads as "conversion when not blocked," so the calibration pass fits it as `observed-FG% ≈ curve ×
  (1 − block)`. A full rating sweep confirmed 0 invalid pies (was 314 at the rim ceiling). Realized make
  rates shift slightly: Phase 6 (f) 37.7% → 37.4% / 25.5% → 25.3%; Phase 2 gap 46.5% → 46.0% —
  relationships hold; all checks still pass.
- **`Core/Resolver.cs` (edit)** — **fix #4, retired-stub hard-errors.** `ResolveBlock` and
  `IntoTransition` were retired in earlier contextification passes but still returned results via dead stub
  fields (`_resolveBlock.Receive(c)` / `_transition.Receive(c)`). Both cases now `throw new
  InvalidOperationException("... retired ... Nothing should route here.")`. The orphaned `_resolveBlock` /
  `_transition` field declarations and ctor assignments were removed; the ctor *params* are kept (matching
  the existing accept-but-don't-store inbound pattern) so the construction site in `Program.cs` is
  untouched. These cases are unreachable in a correct walk — if either ever fires, it now surfaces a real
  upstream wiring bug rather than silently misrouting.
- **`Harness/Program.cs` (edit)** — **regression guard (g)** added to `Phase6MatchupWiringCheck`. Drives
  an elite finisher (Fin=99) vs a weak rim protector (PostD=10, RimP=10) through the *real* generator at
  the Rim zone — the exact case that previously produced an invalid pie (effective rim ≈ 175, `makePct +
  block ≈ 1.05`). Pre-fix: the `Pie` constructor would throw. Post-fix: a valid pie is built (observed
  make rate 82.2%); the guard proves it.

**Deferred (their own passes):** #7 pie DRY (shared `RollHPieBuilder` — cleanest alongside future Roll H
work) and #5 typed `TerminalReason` enum (23 distinct reason strings, 28 construction sites across 6
files + 2 `Program.cs` lookup tables — deserves a focused pass where a failure points at one thing).

**Harness — ALL CHECKS PASSED.** Numbers match the Session 37 run except for the small carve-then-convert
shifts noted above; block (g) prints "valid pie, no overflow" confirming the fix.

## Session 37 — Phase 6: the matchup wiring (make-door vertical slice) (2026-06-15)

**A build (CONVENTIONS §0–§3) — code + harness, all green.** The first vertical slice of Phase 6:
the **make door** becomes the first place two players' attributes meet. Phases 4–5 *designed* the
matchup; this session wires **one** door of it — the shooter vs the slot-matched defender, the make%
read off a matchup-adjusted effective rating. Every other door (location, turnovers, glass, blocks,
tip) stays matchup-blind. The session opened on a near-complete interrupted build sitting uncommitted
on disk, found and fixed its one gap (a referenced-but-unwritten harness check that would not have
compiled), validated, and shipped.

**What landed (6 files):**
- **`Core/DefenderPicker.cs` (new)** — v1 **slot-guards-slot** (DEC-1): the defender is the same slot
  number on the defense side. Deterministic, single-consumer, **derived at generate-time** (not carried
  on `PossessionState`) — a named, swappable unit so the eventual mismatch-hunting picker is a drop-in.
- **`Core/Matchup.cs` (new)** — the matchup primitive. `OffenseRating` (the zone→skill map, now the
  **single source** — RollHGenerator's old private `RatingFor` was deleted and delegates here),
  `DefenseRating` (the CONF-1 per-zone defensive blend), `GapFn` (the DEC-5 signed power law),
  `EffectiveRating` (baseline + skillShift + physicalShift, additive per DEC-2). Pure, static, no RNG.
- **`Config/MatchupConfig.cs` (new)** — five gap parameters (skill/physical steepness + exponent, a
  shared reference scale) and the CONF-1 blend table as data. `Load` mirrors `RollHConfig.Load` **plus**
  a DEC-5 invariant guard (throws if an exponent ≤ 1 or scale ≤ 0).
- **`Generators/RollHGenerator.cs` (edit)** — the make door resolves the defender via the picker and
  reads make% off `Matchup.EffectiveRating`; **DEC-6 fallback**: an empty defending slot reads the raw
  own-rating (no matchup term, == pre-Phase-6), while the unpopulated-roster case still short-circuits to
  the stub pie upstream. The make-curve (`MakeProbability`) is untouched — a contest just slides the
  shooter along it.
- **`Harness/config.json` (edit)** — one new top-level `Matchup` section; nothing else changed.
- **`Harness/Program.cs` (edit)** — three generator sites threaded to the 3-arg ctor, and a new
  **`Phase6MatchupWiringCheck`** (the §4 calibration evidence).

**The one design call (DEC-5): the gap function is a signed power law.**
`shift = steepness · sign(gap) · (|gap| / scale)^exponent`, exponent > 1 — the only simple family that
satisfies all of axes.md's Phase-4 properties at once: **odd** (an even matchup → zero shift; the
asymmetry of real basketball lives in the make-curve, not here), **flat-bottomed** (exponent > 1 ⇒ zero
slope at the origin, so a marginal edge is imperceptible — this rules out `exp(|g|)−1` and linear, which
have non-zero origin slope), **convex and uncapped** (the make-curve's logistic asymptote is the *only*
payoff bound). **Physical steeper than skill via a larger exponent** — a *tail* property ("size
insurmountable") — while the curve's floor independently delivers "skill never extinguished." The
`referenceScale` is a fixed, legible **unit** (the gap at which a shift equals its steepness), kept so the
steepness knobs stay identifiable. Magnitudes are best-guess placeholders; calibration owns the numbers.

**Harness — ALL CHECKS PASSED** (the full 100k chain plus the new Phase 6 block, matching an independent
Monte-Carlo pre-check to the decimal): defender sweep monotone down (47→21%) with even == baseline (34.3%)
and the big edge compressing toward the floor, not zero; the Mid blend's two sub-attributes move make% by
the identical amount (swap-symmetric, 0.5/0.5); a rim specialist is beatable on the perimeter (Mid 44.5% >
41.2%) but strong at the rim (55.3% < 61.4%); the shooter sweep rises and flattens under the ceiling
(64.1% < 65%); physical is steeper than skill at equal gap (21.34 > 15.36); and the DEC-6 fallback through
the real generator reads raw rating (empty == even, 37.7%) while a strong defender lowers make% (25.5%).
Phase 2 still passes — its high/low gap *widens* to 46.5% under wiring.

**Walls held / deferred:** make door only — location/turnovers/glass/blocks/tip untouched; no
`PossessionState.DefenderSlot` (the picker is derived at generate-time until a second door needs it, with
promotion flagged); no athletic/big axis split (one physical gap on the full Athleticism composite); no
team aggregates / gravity; no magnitude hunt (placeholders throughout). **One placeholder needs Emmett's
basketball call:** the Rim blend split (Post 0.35 / RimProtection 0.65), flagged in the config and the code.

## Session 36 — Phase 5: the roster strength-read (2026-06-14)

**A design session, not a build (CONVENTIONS §4) — no code, no harness.** Phase 5 is how the **five
individual matchups** across a roster (Phase 4's outputs) combine into team strength: the non-linear
coverage math alone — *no weakness beats one peak*, *credible beats elite*, *composition sets the spread*.
Structure and direction only — no magnitudes, still unwired (picker + generators are Phase 6).

**Where it landed:** a new **"Phase 5" section in `docs/axes.md`** (the frame, the cap-vs-bleed
asymmetry, credibility, the two diminishing-returns kinds, the spread, the gravity counterweight,
legibility, the Phase-6 seam, the walls), with the Open "non-linear strength read" item moved to Settled.
The conversational deep-dive is appended to `docs/design.md`. No other files touched.

**The calls that got made:**
- **It EMERGES; it is never computed.** The non-linearity lives in the **sparse five-matchup sum on the
  scoreboard** — no coverage formula, no team-strength scalar (the scalar is never built — tightest
  satisfaction of the no-scalar wall). The radar / profile read is a **derived descriptor** for
  legibility, never consulted by the engine.
- **The crux.** Phase 4's gap response is convex and does NOT saturate, so coverage can't come from it
  flattening. The saturation lives in the **make-curve** (a bounded logistic). So: a **won front is
  capped**, a **lost front bleeds every trip**, and a one-peak roster's one capped peak can't outrun its
  **four bleeding valleys** — *four leaks drown one fountain.* Categorical fronts (rebounds, turnovers)
  sharpen it — they swing whole possessions.
- **"Credible" = the responsive middle of the curve, not a threshold we set.** Top caps (elite→dominant
  wasted), bottom floors-and-flattens (a beaten man still hits a few; losing worse barely moves it but is
  paid every trip). Credible-everywhere beats elite-in-one-spot.
- **Diminishing returns = two ideas, one ours.** Over-stacking a front you win → less and less (same coin
  as the cap, here). The fifth-great-athlete-across-positions kind → spacing / usage crowding, **parked.**
  No separate knob needed.
- **Spread = the shot diet** (Emmett's two refinements): not "swingy" but **lower-odds shots that
  sometimes hit**, competing on volume + winning the other battles; and not two fixed identities but **how
  much you lean on the lower-odds shot with nothing underneath** — a covered three-team is strong, a
  three-or-nothing team is the upset machine. Not a variance dial; not the streak governor.
- **Gravity lift = the parked counterweight.** A superstar bending the defense to free his teammates is
  the real force pushing back on coverage — but it's the **gravity/attention layer**, kept in a separate
  drawer so the two forces fight it out naturally. Flagged loudly, not folded in.

**Walls held:** no team-strength scalar / coverage formula (it emerges), no defender-ID picker or wiring
(Phase 6), no team aggregates (gravity lift + across-position diminishing returns parked), no strategy /
usage / streaks (the spread is the static shot-diet kind only). No magnitudes. Make-curve and Phase-4 gap
function untouched.

## Session 35 — Phase 4: the individual-matchup tilt (2026-06-14)

**A design session, not a build (CONVENTIONS §4) — no code, no harness.** Phase 4 turns **one
attacker vs one defender** into pie tilts: the attribute → axis laddering, the counter-attribute
pairing map, and the per-matchup gap function. Structure and direction only — no magnitudes, still
unwired (picker + generators are Phase 6). Built on the locked two-layer spine from the Session 34
capture; this session resolved the open structure and wrote it down.

**Where it landed:** a new **"Phase 4" section in `docs/axes.md`** (laddering map + pairing map + gap
function, completing the Dependencies block), with the Settled/Open lists updated to move the three
resolved items (laddering, pairing map, gap function) from Open to Settled. The conversational
deep-dive is appended to `docs/design.md`. No other files touched.

**The calls that got made:**
- **Laddering, anti-double-count.** Athletic axis = horizontal separation (speed / quickness / first
  step). Big / size axis = vertical reach + mass (height / wingspan / weight / **strength + vertical**)
  — Emmett's read: does the player's size and reach disrupt the opponent's skill advantage. **Derived
  athleticism stays the full composite as the locked ceiling**, a separate object from the axis. Named
  **defensive ratings are the skill layer only** (technique / timing); their physical tools live in the
  axes — same trait never counted twice. Modifiers (hustle, IQ) ride duels; discipline → foul pies;
  free throw is matchup-immune.
- **Pairing map, per door.** Make %: touch ↔ a perimeter → perimeter+post → post+rim → rim **gradient**
  (Mid = perimeter + post was Emmett's call; "help" dropped out as a team aggregate). Location:
  self-creation / handling ↔ on-ball D, with **self-creation gating shot *type*** (no creation →
  catch-and-shoot + entry looks only). Turnovers: handling ↔ steals. Glass: off-reb ↔ def-reb. Blocks:
  finishing ↔ rim protection (carved off the top). Tip: wingspan. Team-aggregate / interaction items
  parked.
- **Gap function: additive, convex, no separate recovery term.** One convex gap → shift primitive (no
  cap but the curve's asymptote), per-axis physical gaps + per-pairing skill gaps **summed** onto the
  baseline. The recovery behaviors fall out of the structure: **skill lives in the baseline** (never
  zeroed by athleticism), the **make-curve's floor + flattening** compress make%-gaps near the bottom
  while keeping the skilled player above a scrub (the 5'6"-shooter-still-hits-a-few case), and the
  **convex physical nudge** makes a big gap insurmountable and a small gap defer to skill. Per-possession
  floor underneath. **Additive chosen over multiplicative** — one mechanism, gives "skill decides the
  even game" for free. 4th axis = a hard-zero-gap stub in the signature.

**Walls held:** no coverage / strength-read (Phase 5), no defender-ID picker or wiring (Phase 6), no
team aggregates / strategy / tendencies / usage. The out-athleted player's **volume choke** is the
usage lever (deferred) — Phase 4 owns only the per-shot tilt. `MakeProbability` untouched.

## Session 34 — Post-Phase-3 design capture: the matchup deep dive (2026-06-14)

**A design session, not a build (CONVENTIONS §4) — no code, no harness.** A long conversation after
Phase 3 shipped produced a pile of locked design that lived only in chat; this session writes it down
so the Phase 4 prompt is drafted from the record, not from memory. Deliverables: a **correction** in
`docs/axes.md` and a **deep-dive capture** appended to `docs/design.md` (full detail there). Nothing
is wired; every item is gated to a later pass.

**The one correction (axes.md):** Session 33 called skilled "the baseline, NOT a tilt." That is now
amended — skill is the baseline the shift starts from **and** an active **skill-vs-skill contest.**
Make/miss is the shooter's shooting against the *defender's* defending; the effective-rating shift has
**two sources** (physical matchup + skill matchup), neither a separate roll. There is **no simulated
"did he get open" step** — openness is the *physical input* to the shift, not an event (rolling "is he
open" then "does it fall" is the event-sim the project rejects). Skill becomes the **whole signal**
when the physical battle is even — most D1 games. The counter-attribute **pairing map** (which
offensive skill contests which defensive one) is Phase 4.

**What else was locked (captured in design.md, summarized here):**

1. **Per-possession ceiling & floor are emergent.** A single shot tops out at the curve's asymptote
   (no imposed cap); offensive rebounds legitimately push a *possession* higher (distinct events
   compound). The floor (~20–25% even in the worst mismatch) is the residue of events we can't
   simulate and doubles as the cold-streak hard bottom.
2. **Double-count discipline + the two-defender drive.** Spend a gap once; different *matchups* source
   different doors. On-ball defender gates **location**; the help big gates the **finish**.
   Defender-ID resolves **before** location.
3. **The expressed game is relative.** Opportunity widens *attempts*; skill still governs *results*
   (identity preserved). Roles are earned against the competition, not assigned — so tendencies can't
   be fixed per-player numbers.
4. **The competency floor.** College is a selected population; a wide-open college guard hits ~30%+.
   A bad rating = how far he drops as the look tightens. A constraint on future player-generation.
5. **Combination.** Between teams = two one-way battles, **scoreboard as the common currency**, a
   *sparse* round robin (each attack meets its counter). Within a team = **coverage** ("no weakness
   beats one peak"). **Pace is not a dial** — it lives in turnovers + defensive rebounds. Roster
   composition sets **variance**, not just average.
6. **Defender-ID = a weighted roll (Phase 6 build).** Scheme/switch weights; positional pairing v1;
   cross-slot hunting later. **Sequencing locked:** Phase 4 designs the gap *function*, the *picker*
   is built later (Phase 6); do not reorder — function first, picker second.
7. **Variance & streaks.** Per-player hot/cold meters (real 3+ runs only), with **usage as the
   self-correcting thermostat.** Tuned, not automatic; cold side needs a hard floor; **depends on the
   usage layer — don't ship before the governor.** A per-game "shooting weather" draw is the safer
   variance dial (no feedback loop).
8. **Usage ↔ efficiency + the two-pie game.** Offense's usage pie vs defense's attention pie.
   **Attention is the source of openness** (unifies the parked gravity/spacing dial). Superstar =
   flat usage-efficiency curve + outsized gravity. Depth requires adaptive allocation.
9. **Coaching / tactics.** Rigid coaches that top out are content (needs scouting to be legible).
   The tactics rating drives **allocation quality via a simple heuristic — NOT a flat make-boost**
   (that would be the forbidden absolute difficulty dial). Through the model, not around it.
10. **Fatigue / endurance** — its own large future session. Endurance sets drain rate; drains the
    **physical axis first** and flows through the existing matchup machinery; iron-men exist; bigs
    tire faster.
11. **Game-state awareness** — a future modifier layer that **bends existing pies** (threes/pace/
    fouling), not a new system; extends the Session 30 end-of-half seam.
12. **Closed: shot-quality degradation.** Rejected as a separate mechanism — it already lives in the
    shot-mix reweight + the make%-tilt; a within-possession option-1/2/heave sequence would be the
    event-sim we reject. Recorded so it isn't re-raised.

**Deferred on purpose (not resolved — that would be designing ahead of the current pass):** the
attention × matchup combination; the streak's usage governor; and the generation floors.

**Phase 4 scope — resolved (the split is decided).** Canonical phase ladder as of this session:
**3** fingerprints (done) → **4** individual-matchup tilt (laddering + counter-pairings + the gap
function) → **5** roster strength-read (the non-linear coverage math alone) → **6** the build
(defender-ID picker + wiring). The 4th-axis stub seam rides along in Phase 4. (This supersedes the
older "Phase 5 = tendencies" note in the Session 32 wall, left as-is per append-only; newest entry
is canonical.)

**Next:** draft the **Phase 4** prompt (a design pass) — the laddering map (attributes → axes,
anti-double-count), the counter-attribute pairings, and the per-matchup gap function — per
CONVENTIONS §6, gated by the reflection + audit. Coverage / strength-read is now Phase 5, its own
session.

## Session 33 — Phase 3: The pie fingerprints (three tangible axes) (2026-06-14)

**A design session, not a build (CONVENTIONS §4) — no code, no harness; the deliverable is the spec
written into `docs/axes.md`.** Phase 2 proved the player-object → make-curve pipe end to end with the
shooter's own rating. Phase 3 is the first design pass of the *matchup* layer: for each of the three
computable-now axes — athletic, skilled, big — a precise **pie fingerprint** (which pies it tilts,
which way, by what mechanism, and the shape of the gap → tilt response). The experience/cohesion
fingerprint is deliberately left open (it waits on the persistence layer). No magnitudes — those are
the calibration pass.

**What was locked (the reasoning, conversed before any spec text):**

1. **Make/miss is an effective-rating SHIFT, not a curve change.** A matchup tilt slides the
   shooter's *effective rating* along the one per-zone curve everyone shoots on (the Phase 2
   `MakeProbability(zone, rating)` logistic, reused untouched) — never reshaping the curve, moving
   its midpoint/ceiling, or multiplying the output. Categorical pies (location, rebound, turnover,
   transition, tip) have no curve, so a tilt reweights their slices directly.

2. **The zero point is AVERAGE defense, not an open shot.** No modifier = an utterly average
   contest, so a league-average shooter with no modifier shoots league-average. Positive modifier =
   worse-than-average defense (more open) → up the curve; negative = tighter → down. Average sits in
   the *middle* of the scale, not the bottom.

3. **The gap → tilt response is ACCELERATING, not saturating (a deliberate refinement of the
   prompt's starting lean).** A marginal edge is imperceptible; the effect grows *faster* than the
   gap; a true mismatch is cartoonish. The only ceiling is physical reality (a make% cannot exceed
   the curve's asymptote; a frequency cannot exceed 100%) — not an imposed cap, which would
   manufacture parity. Edges on different axes **compound** (each tilts its own pies → all leaning
   one way multiplies into a blowout, correct). Absurd matchups are kept rare by realistic
   scheduling, not by softening the math. The bottom-end "credible beats elite" lean survives; the
   top-end saturation is rejected. The coverage / roster strength-read stays the laddering pass.

4. **Two efficiency doors.** A matchup moves efficiency through the shot *mix* (which zone) and the
   *make%* (whether it falls), independently. A quickness edge swings both; a quickness deficit
   against equal size swings mainly the **mix** — a worse shot diet — even when each shot's make is
   unchanged. The mix has a standing pull toward the rim; the matchup grants access.

5. **Three independent dials set "how open."** The defender matchup (this pass), team fit (gravity /
   spacing / passing aggregate), and role / usage (tendencies) — kept in separate drawers, never
   pre-fused. Only the first is a fingerprint.

6. **The keynote — skilled is the BASELINE, not a tilt.** Once each specific skill is placed, skill
   does not *push* pies the way athletic and big do. It is the level the physical axes tilt around:
   shooting touch (the make/miss baseline) + ball security (the turnover baseline) + the decider when
   the physical battle is even (the vast majority of D1 games). Its creation side — the passing
   make-bump (the alley-oop / backdoor read, baked into the %) — is a hidden **team aggregate**
   beside gravity and spacing, the lifeline by which a smaller, skilled team passes itself open. This
   is the asymmetry made concrete: physical caps skill expression, and not symmetrically.

**The two physical bundles (summary).**
- **Athletic** — make/miss separation on all shot types (both ends); shot location pulled to the rim
  (deficit → pushed out, worse mix); a *usage* choke on the out-athleted player with an emergent
  offense-breaking cascade if forced; live steals manufactured at big gaps; secondary on the glass;
  transition *efficiency* (frequency is mostly a coaching choice).
- **Big** — rim protection down / finish-over up, length weighted to the rim; raises the *block*
  slice (rim-weighted; small finishers get swatted more, block carved before make/miss); strength
  pulls the mix inside; primary rebounder (rebounding skill the gated baseline); wins the *tip* on
  the wingspan gap (accelerating S-curve, the existing `JumpBall.cs` seam).

**Overlaps resolved.** Athletic *horizontal* (separation) vs big *vertical* (over the top / wall off
the rim) — no shared rim tilt; self-creation → the athletic matchup (not a skill bump, since the
coverage-breakdown event cannot be modeled); passing → a team aggregate (not a skill matchup tilt);
rebounding skill → its own ability; tip → size / wingspan.

**Deferred (named, not designed).** Defender identification (the build pass); the per-matchup gap
computation + attribute → axis laddering + coverage / strength-read (the laddering pass); all
magnitudes (the calibration pass); the strategy layer that multiplies the terrain; and the
team-aggregate build (gravity, spacing, the new passing aggregate).

**Deliverable.** `docs/axes.md` — one new section ("The three tangible fingerprints (the precise
bundles)") inserted after the illustrative-sketch section, plus the Settled/Open lists updated (the
three tangible fingerprints moved to Settled; the experience fingerprint stays Open). All prior
settled concept text untouched; no code touched. (Note: the prompt referenced a one-line
reconciliation to `player-model-roadmap.md`, which is not present in the committed tree — no such
file exists to edit.)

## Session 32 — Phase 2: Direct self-rating wiring (Roll H real generator) (2026-06-14)

**The second session of the player-model arc.** Phase 1 gave the slot layer a `Player` to hold and
proved the seam `GameState.RosterFor(side).PlayerAt(slot)` resolves end to end. Phase 2 wires the
first real generator: `RollHGenerator` reads the shooter's own attribute rating and produces a make
probability via a per-zone bounded logistic. No matchup effects, no defender, no gravity, no team
aggregates. Own rating → own pie only. That is the Phase 2 wall.

**Design questions settled before any code (CONVENTIONS §4).**
1. **Zone → attribute mapping.** `ShotLocation` names WHERE the shot comes from; the player
   attribute names the SKILL needed to convert it. Three and Long → `player.Outside`; Mid →
   `player.Mid`; Short → `player.Close`; Rim → `player.Finishing`. `Short` reads `Close` (not
   `Finishing`) because Short is floaters, runners, hooks inside the paint — a skill-bucket, not
   a rim conversion. `Finishing` is explicitly "converting rim attempts." The location/skill split
   is intentional and naming both axes precisely is what makes the mapping legible.
2. **The logistic curve: inflection-above-50.** `makePct = floor + (ceiling - floor) / (1 + exp(-k
   * (rating - midpoint)))`. Slow crawl from rating 1 to 50, steeper gains through the elite range,
   flattening near the ceiling. Five zones, five parameter sets fitted to three anchor points each
   (ratings 1 / 50 / 99). Fitted analytically (Nelder-Mead on the three-anchor residual); result
   confirmed by Python Monte Carlo before writing any C#.
3. **Make weight substitution, not full-pie rebuild.** The logistic result replaces only the `Made`
   weight. All other pie structure — block carve, foul slices, OOB pair, putback path — is preserved
   unchanged from the stub. The five non-Made, non-Blocked base weights are rescaled to fill
   `(1 − block − makePct)` proportionally, so the pie always sums to 1 and the `Pie` constructor's
   validation never fires on a valid rating.
4. **Interface pattern for the resolver field.** `IRollHPieGenerator` with a single
   `Generate(PossessionState, bool)` method. Both the stub and the real generator implement it;
   the Resolver field and ctor param are typed to the interface. This is the long-term pattern —
   every generator that eventually gets a real implementation will follow it. The 14 existing harness
   sites that construct `RollHStubPieGenerator` directly are typed to the concrete class and are
   unaffected; they never go through the Resolver's interface-typed field.
5. **Fail-loud on null slot; fallback on null player.** A null `SelectedSlot` when a possession
   reaches Roll H is a wiring bug and throws. A null player (roster not populated) is a harness
   convenience and silently falls back to stub behavior — this is what allows all 14 existing harness
   checks to pass unchanged.
6. **Putback path unchanged.** Putback shots return the flat putback pie from config. The real
   putback tilt by size/athleticism/rim rating is Phase 4 work.

**The five-zone logistic parameters (fitted and locked in config):**

| Zone | Rating attr | Floor | Ceiling | K | Midpoint | R=1 | R=50 | R=99 |
|---|---|---|---|---|---|---|---|---|
| Three | Outside | 0.03 | 0.65 | 0.057667 | 49.6239 | ~6.5% | ~34% | ~62% |
| Long | Outside | 0.03 | 0.63 | 0.061286 | 45.4063 | ~6.7% | ~37% | ~61% |
| Mid | Mid | 0.05 | 0.67 | 0.059158 | 44.2696 | ~9.5% | ~41% | ~65% |
| Short | Close | 0.08 | 0.83 | 0.057781 | 46.3470 | ~13% | ~49% | ~80% |
| Rim | Finishing | 0.10 | 0.93 | 0.061713 | 42.1330 | ~16% | ~61% | ~91% |

Python Monte Carlo confirmed all values in [0, 1], monotone per zone, and pies summing to exactly 1.

**New files.**
- `Generators/IRollHPieGenerator.cs` — the interface. Single `Generate(PossessionState state, bool
  putback = false)` method. Both `RollHStubPieGenerator` and `RollHGenerator` implement it.
- `Generators/RollHGenerator.cs` — the real generator. Constructor takes `RollHConfig` and
  `GameState`. On `Generate`: reads zone from `state.ShotType` (throws if null), reads slot from
  `state.SelectedSlot` (throws if null — wiring bug), looks up `_game.RosterFor(state.Offense)
  .PlayerAt(slot)` (falls back to stub if null — unpopulated roster). Calls
  `_cfg.MakeProbability(zone, rating)` and builds the pie via `BuildRealPie`. `BuildRealPie` computes
  the five non-Made non-Blocked weights' proportional share, scales them to fill `(1 − block −
  makePct)`, assembles the `Dictionary<ShotResult, double>`, and hands to `new Pie<ShotResult>`.
  `BuildStubPie` and `BuildPutbackPie` mirror the stub exactly for the fallback and putback paths.

**Edited files.**
- `Config/RollHConfig.cs` — added twenty new properties (Floor, Ceiling, K, Midpoint for each of the
  five zones) and the `MakeProbability(ShotLocation zone, double rating)` method that encapsulates the
  logistic formula. This is the single place the formula lives — both the generator and the harness
  check read the same method. Existing properties (`BaseMade`, etc.) and `BlockWeight` unchanged.
- `Generators/RollHStubPieGenerator.cs` — added `: IRollHPieGenerator` to the class declaration.
  No other change; the stub is byte-for-byte identical in behavior.
- `Core/Resolver.cs` — two surgical type changes: `private readonly RollHStubPieGenerator
  _rollHGenerator;` → `IRollHPieGenerator`, and `RollHStubPieGenerator rollHGenerator,` → `IRollHPieGenerator`
  in the ctor signature. The store and the call site (`var pieH = _rollHGenerator.Generate(c.State,
  c.Putback)`) are untouched. All other generator fields and params unchanged.
- `src/Charm.Harness/Program.cs` — three changes:
  1. Main construction site: `var rollHGenerator = new RollHStubPieGenerator(cfgH)` moved to after
     `var game = new GameState(fouls)` and replaced with `var rollHGenerator = new RollHGenerator(cfgH,
     game)` — the real generator needs `GameState`, which is declared after the generators in the
     original code.
  2. `RollHResolutionBatchCheck` param type changed from `RollHStubPieGenerator` to `IRollHPieGenerator`
     (so the main construction site can pass the real generator to it).
  3. New `Phase2AttributeWiringCheck` method appended and wired into the `ok &=` chain after
     `Phase1RosterCheck`. Proves: all logistic values in [0, 1]; monotone per zone; high-Outside (85)
     vs. low-Outside (25) three-point make rates differ by ≥ 10pp; fallback on unpopulated roster does
     not throw.
  The 12 remaining `RollHStubPieGenerator` construction sites in isolated batch checks are untouched
  (they hit the fallback path and produce the flat stub pie unchanged).

**Validation.** Python pre-check: pies sum to 1.0 for all ratings/zones; gap between Outside-85 and
Outside-25 three-point make rates ≈ 40pp (≥ 10pp threshold); fallback path distinct from real path.
Brace balance checked on all new and edited files (all matched). Harness run (Emmett's machine):
**ALL CHECKS PASSED** on the second run (first run: one build error — `pie.Draw(...)` instead of
`pie.Roll(NextUnitInterval())`; fixed in one line). Key Phase 2 output:

```
Three-point make rate — high Outside (85): 60.0%
Three-point make rate — low  Outside (25): 19.5%
Gap: 40.6pp  (threshold ≥ 10%)
Wiring check PASSED — high shooter beats low shooter by required margin.
Fallback OK — unpopulated roster returns stub pie without throwing.
Phase 2 PASSED.
```

All 14 existing Roll H checks produced identical output (the 12 remaining stub sites hit the fallback
path; `RollHResolutionBatchCheck` accepted the interface-typed real generator and ran against the same
flat weights via the fallback).

**Phase 2 wall held.** No defender attribute. No matchup effects. No gravity, no spacing, no team
aggregates. No other generator. No putback tilt. The only attribute the generator reads this session
is the shooter's own zone-relevant rating.

**What stays out (carried to Phase 3+).**
- No defender attribute or matchup effects. Those are Phase 3 (axis math) and Phase 4 (gravity,
  spacing, team aggregates).
- No axis laddering. Phase 3.
- No shot tendencies. Phase 5.
- No putback tilt by size/athleticism. Putback path stays flat (config weights). Phase 4.
- No free-throw rating wiring (Roll L). Separate session.
- No other generator. Roll H only this pass.

**Carried deferrals (unchanged from prior sessions).**
- Attribute-driven generators for all other rolls (the deferred ~90% of the work).
- Score-aware late-game tactics, rushed-shot quality penalty, per-outcome time shaping.

## Session 31 — Phase 1: The player object and Roster seam (2026-06-13)

**The first session of the player-model arc.** The possession engine is functionally complete as a
funnel (Rolls A–M wired, real clock, real score, end-of-half intent). All generators are still stub
pies. This session gives the empty slot layer a player to hold: the `Player` object, the authored
attributes, the derived-attribute computation, the `Roster` that maps slots to players, and the
config-section authoring path. The seam `GameState.RosterFor(side).PlayerAt(slot)` now resolves end
to end. No pie is wired; no generator reads a player. That is Phase 2.

**Design questions settled before any code (CONVENTIONS §4).**
1. **Rating scale: 0–99 integer.** 99 is the ceiling; 100 is impossible. A 99-rated free-throw
   shooter is historically elite, not infallible. The 1:1 calibration note in `attributes.md` (a
   72-rated shooter makes ~72%) is a rough Phase-2 anchor, not a hard formula — the real mapping is
   a bounded logistic, tuned in Phase 6.
2. **Attachment: Option C — a separate `Roster` object.** `Slot` stays pure identity; `Lineup`
   stays a pure identity layer; `GameState` grows `HomeRoster`/`AwayRoster`/`RosterFor(side)`. The
   `Roster` is the bridge between the live game and the almanac: the historical archive holds rosters,
   rosters point at players, players accumulate career stat lines. Building it as a separate object
   rather than inline on `Lineup` or `GameState` is the only option that doesn't create a seam to
   rip out when the dynasty/almanac layer arrives.
3. **Derived-attribute formulas: plain means as placeholders.** Athleticism = mean of Strength +
   Speed + Quickness + FirstStep + Vertical. Transition = mean of Athleticism + Finishing.
   GravityContribution = mean of Close + Mid + Outside + Finishing. SpacingContribution = Outside.
   All provisional; Phase 6 tunes the weights and formula shapes.
4. **Authoring mechanism: config JSON `"Rosters"` section.** The harness gets rated players from a
   new `"Rosters"` section in `config.json` — two arrays of five `PlayerConfig` objects (home/away).
   This is the embryo of the dynasty save format: every future layer that writes or reads a starting
   lineup (the coach screen, the save file, the almanac) points at the same JSON contract.
5. **Name: `Player`.** The live domain object is `Player`. Future distinction: `PlayerSnapshot` or
   `PlayerHistoryEntry` for the almanac's read-only copies if needed.

**The substitution architecture (settled conversationally).** Slot is a stable seat assignment for
the whole game. A substitution swaps WHO fills slot 3, never what slot 3 IS — so a player can
return from the bench into a different slot if the coach wants, and a player can occupy different
slots at different times. The `Roster` handles this via an append-only substitution log:
`SubstitutionEntry(Slot, Player, AtPossession)`. Starters are logged at AtPossession = 1. The
current occupant of a slot is always the last log entry for that slot. The almanac reads the full
log to reconstruct per-player minutes and stat windows per game.

**New files.**
- `Core/Player.cs` — the rated player. 30 authored `int` properties (0–99), grouped by umbrella
  (Offense / Defense / Physical / Intangible). Four computed properties (never stored): `Athleticism`
  (mean of 5 physicals), `Transition` (mean of Athleticism + Finishing), `GravityContribution` (mean
  of 4 scoring ratings), `SpacingContribution` (Outside). `Validate()` returns a list of
  range-violation strings (empty = valid). Includes `Endurance` as a dormant-pending-module authored
  field — seated and proven to exist before the stamina module consumes it, same discipline as Roll
  C's Session-24 expansion.
- `Core/Roster.cs` — one team's slot-to-player map. `SetStarter(slot, player)` populates at game
  start; `Substitute(slot, incoming, atPossession)` appends mid-game. `PlayerAt(Slot)` returns the
  current occupant; `PlayerAt(Slot, atPossession)` returns who was there at a specific possession
  (the almanac's attribution seam). `Log` is the full append-only substitution timeline.
- `Config/RosterConfig.cs` — two classes. `PlayerConfig` is the JSON-deserialisable DTO with all 30
  authored properties plus `Name`; `ToPlayer()` converts it to a `Player`. `RosterConfig` loads the
  `"Rosters"` section from `config.json` (the nested `GetProperty` pattern from `RollHConfig`),
  validates that both arrays carry exactly 5 players, and validates every player's attribute ranges
  before returning.

**Edited files.**
- `Core/GameState.cs` — `HomeRoster` and `AwayRoster` properties (both `Roster`, constructed in the
  ctor alongside `HomeLineup`/`AwayLineup`) and `RosterFor(TeamSide)` (mirrors `LineupFor`). **Zero
  change to the `GameState` constructor signature** — both rosters are constructed internally. All 24
  existing `new GameState(fouls)` sites in the harness compile and run byte-for-byte unchanged.
- `src/Charm.Harness/config.json` — new `"Rosters"` section with two five-player arrays. Ten
  players with realistic, varied attribute profiles (guards with high athleticism and perimeter
  skills; bigs with high strength/size and low lateral quickness). All ratings in the 0–99 range.
- `src/Charm.Harness/Program.cs` — new `Phase1RosterCheck` method (appended at the bottom of the
  class) wired into the `ok &=` dispatch chain. Proves: `RosterConfig.Load` succeeds; all 10 slots
  resolve to non-null players; all 30 authored attributes pass `Validate()`; all four derived values
  are in the 0–99 range; the directional check holds (guards have higher athleticism than bigs in
  the configured fixture); the substitution log has 5 entries per side at possession 1; a bare
  (unpopulated) `GameState` returns null for every slot (existing sites unaffected).

**Validation.** Python pre-check confirmed all 10 players' derived values in range and directionally
correct (Marcus Webb guard ath 73.6 > Javon Okafor big ath 60.6; Darius Eze big ath 58.8 < Kendrick
Shaw guard ath 71.6). Brace balance checked across all new and edited files (all matched). Stale-ref
sweep: no existing file references `RosterFor`, `HomeRoster`, `AwayRoster`, or `PlayerAt` before this
session — zero collision risk. Harness run (Emmett's machine): **ALL CHECKS PASSED** on the first
run. Key Phase 1 output: all 10 slots resolve OK, all Validate() calls return empty, derived values
match the Python pre-check within rounding, directional and substitution log checks both pass, bare
GameState null check passes.

**Phase 1 wall held.** No generator reads a `Player`. No pie is wired. The seam exists and resolves;
nothing on the roll side touches it yet. Rolls A–M, the Governor, and every stub generator are
byte-for-byte unchanged.

**What stays out (Phase 1 wall, carried to Phase 2+).**
- No pie wiring. No generator reads a player attribute yet (Phase 2).
- No axis math. No four-axis laddering, coverage formula, or fingerprints (Phase 3).
- No player tendencies on the `Player` object beyond noting where they will live (Phase 5).
- No team-aggregate computation (team gravity/spacing as a five-man read). Phase 1 computes the
  per-player contribution; the aggregation is Phase 4.
- No franchise/persistence fields (career counters, co-appearance). 4th-axis, off-roadmap.
- No engine behavior change beyond giving the slot a player to hold.

**Carried deferrals (unchanged from prior sessions).**
- Attribute-driven generators (the deferred ~90% of the work) — the whole Phase 2–4 arc.
- Score-aware late-game tactics, rushed-shot quality penalty, second-half tip reset, per-outcome
  time shaping, tempo calibration — all carried from the clock sessions.

## Session 30 — End-of-half intent: the offense holds for the last shot (or doesn't get one off) (2026-06-13)

**The second clock session.** Session 29 put real time on the board but the end of a half was still
naive — a possession starting with almost no time left just capped its elapsed at the remaining time
and resolved normally. This session makes the end of a half behave like basketball: when a possession
starts with less than a full shot clock left, the offense draws a three-way intent and the Governor
forces or overrides the elapsed accordingly.

**Confirmation mode, not rediscovery (CONVENTIONS §0/§6c).** The prompt carried a fully verified §6c
map. S29 dependency confirmed first (`grep "var halfRemaining"` and `grep "double HalfSeconds"` both
hit). All §6c anchors confirmed against the committed tree with zero drift: `int Half);` tail of
`PossessionRecord`; Governor ctor signature; resolver call site `var outcome = _resolver.RunPossession`;
S29 time block `rawElapsed = … ?? DrawPossessionSeconds`; record construction line
`endedOnTerminal, endLabel, … applied, half));`; `noLostOk` count assertion; both harness Governor
construction sites; `"Clock"` section in config.json. No drift.

**The model in one paragraph.** When `halfRemaining >= HoldThresholdSeconds` (default 30s — one full
shot clock) nothing changes; the S29 path runs byte-for-byte. When `halfRemaining < HoldThresholdSeconds`,
the Governor draws a three-way intent from a flat, score-blind, tempo-blind pie:

- **`HoldShootLast` (70%)** — milk the clock and shoot last. The resolver runs normally for its points,
  but the Governor overrides `applied = halfRemaining`, draining the entire remaining clock. The opponent
  gets no return trip.
- **`ShootEarly` (20%)** — take a normal-tempo possession. Elapsed is drawn the S29 way and capped at
  `halfRemaining`, so if time remains the opponent may get a return trip.
- **`NoShot` (10%)** — run out the clock with no attempt. The resolver is NOT called. Zero points;
  `applied = halfRemaining`. A §2a third class: neither `terminalEnded` nor `parked` increments.

**Architectural decision: intent draw in the Governor, resolver untouched.** Same rationale as S29's
"the draw lives in the Governor": the end-of-half decision is a clock-management concern only the
Governor sees. The resolver resolves one possession and knows nothing about the clock or the half.
All changes are Governor-side; the resolver's 8 constructor sites and `RunPossession` are untouched.
No new roll, no new `ContinuationKind`, no generator.

**The § 2a discipline: NoShot is a third possession class.** S29's `noLostOk` asserted
`terminalEnded + parked == records.Count`. A NoShot possession is neither a terminal nor a stub park,
so that assertion was wrong as soon as NoShot could fire. The count assertion was reworked to three
classes: `terminalEnded + parked + noShotCount == records.Count`, where `noShotCount` is derived from
the records directly. The §2b sweep confirmed this was the only site of the old shape; it is now gone.

**Single record construction site preserved (the §0 observation).** The concern going in was whether
NoShot would need its own `new PossessionRecord(...)` call, breaking the "one construction site" rule.
It does not: NoShot synthesizes its variables (`endedOnTerminal = false`, `endLabel = "endOfHalf:NoShot"`,
`consequence`, `pointsThisPossession = 0`, `applied = halfRemaining`) and then converges to the single
existing `records.Add(new PossessionRecord(..., intent))` at the bottom of the loop — the same site
every other path uses. One construction site; three paths feeding it.

**New files.**
- `Rolls/EndOfHalfOutcomes.cs` — `EndOfHalfIntent` enum (`HoldShootLast`, `ShootEarly`, `NoShot`),
  house style mirroring `FoulOutcomes.cs`'s `FoulFlavor`.
- `Config/EndOfHalfConfig.cs` — five properties (`HoldThresholdSeconds`, `HoldShootLast`, `ShootEarly`,
  `NoShot`, `Epsilon`), `Load` pattern mirroring `RollClockConfig.cs`, section `"EndOfHalf"`.

**Governor edits.**
- `PossessionRecord` gains trailing `EndOfHalfIntent? EndOfHalfIntent` (nullable; null on every normal
  possession; non-null only at the end of a half). Added with `<param>` doc in the established voice.
- Governor ctor gains `EndOfHalfConfig endOfHalf` (last param, after `IRng rng`). Stores `_endOfHalf`
  and builds `_endOfHalfPie = new Pie<EndOfHalfIntent>(...)` once in the ctor from config weights — no
  generator needed (this session's pie is context-free; a generator appears when the split becomes
  score-aware, which is deferred).
- `Run` loop restructured: `intent` computed at the top (before the resolver call) from
  `halfRemaining < _endOfHalf.HoldThresholdSeconds ? _endOfHalfPie.Roll(...) : null`. NoShot
  short-circuits the resolver; HoldShootLast and ShootEarly call it normally. The time block is replaced
  by a small `intent`-aware switch producing `applied`: NoShot → `halfRemaining`; HoldShootLast →
  `halfRemaining`; ShootEarly/normal → existing `Math.Min(rawElapsed, halfRemaining)`.
  `halfRemaining -= applied; totalSeconds += applied;` shared after the if/else.

**Harness edits.**
- `Main`: `cfgEndOfHalf = EndOfHalfConfig.Load(configPath)` added after `cfgClock`; `EndOfHalfIntentBatchCheck`
  wired into the `ok &=` chain before `GovernorLoopCheck`; `GovernorLoopCheck` call updated to pass it.
- `GovernorLoopCheck`: signature extended; BOTH Governor construction sites pass `cfgEndOfHalf`
  (GovernorLoopCheck + RunGame); count assertion reworked to three-class; new `noShotCount` / `intentHeld`
  / `intentEarly` counters; end-of-half observability line added (`end-of-half: HoldShootLast=<n>
  ShootEarly=<m> NoShot=<k>`); report count label updated to `terminal+parked+noShot==total`.
- New `EndOfHalfIntentBatchCheck`: builds the same `Pie<EndOfHalfIntent>` from the same config weights
  the Governor uses, rolls 100k times with a fresh seeded `SystemRng`, asserts all three rates within
  `RateTolerance`. Modeled on `RollFActionBatchCheck`.
- `RunGame`: `cfgEndOfHalf` loaded alongside other configs; per-possession print line gains an
  `(held)` / `(early)` / `(no shot)` marker for end-of-half possessions.
- `config.json`: `"EndOfHalf"` section added as a sibling of `"Clock"` with the five default values.

**Validation (CONVENTIONS §2).** Python Monte Carlo confirmed: 100k intent draws all three rates
within 0.005pp tolerance; per-half drain invariant holds across 1000 simulated halves (every half sums
to exactly 1200s regardless of intent mix — HoldShootLast and NoShot drive `halfRemaining` to 0,
ShootEarly caps normally); count math `terminal + parked + noShot == total` holds. Brace balance ok
across all touched files. §2b stale-ref sweep: old `TerminalEnded + result.Parked == records.Count`
shape appears zero times after edits. Harness run (Emmett's machine): **ALL CHECKS PASSED** on the
first run (after fixing a dropped `var rng` / `var seed` line from a str_replace that consumed more
than intended — caught immediately on the first build error, corrected in one targeted fix).

Key harness signals: `EndOfHalfIntentBatchCheck` — HoldShootLast 70.067%, ShootEarly 19.970%,
NoShot 9.963%, all `ok`; `GovernorLoopCheck` — `resolved=134 | terminal-ended=134 | parked=0 |
noShot=0 | terminal+parked+noShot==total -> ok`; `half 1: 1,200s / half 2: 1,200s | each drains
to 1,200 -> ok` (the load-bearing drain invariant); `end-of-half: HoldShootLast=1 ShootEarly=1
NoShot=0` (tiny per-game counts — correct; the rigorous rate proof is the batch check).
The `noShot=0` in the game run is expected: with 134 possessions and a 30s threshold, NoShot (10%
of intent draws) simply did not fire this seed. Every prior rate/routing/score/clock check
byte-for-byte unchanged.

**What stays out.** Score-aware late-game tactics (the leading team milks, the trailing team races;
intentional fouling to stop the clock). Rushed-shot quality penalty (a held buzzer-beater resolves
through the normal roll chain at the same shot quality as any other shot — shot quality is not yet
modelled). Second-half tip/alternating-possession reset. Per-outcome time shaping. Tempo calibration
and the coach pace attribute. The intent weights are explicitly uncalibrated starting knobs for
Emmett to tune by watching games.

**Deferrals (carried).**
- **Score-aware late-game tactics** — the intent becomes a CONTEXT-SELECTED pie reading the margin
  and the time. The flat, score-blind pie this session is the seam it plugs into.
- **Rushed-shot quality penalty** — a held buzzer-beater at a lower make rate than a normal look.
  A shot-quality tilt; deferred until attribute-driven Roll H generator lands.
- **Second-half tip / alternating-possession reset** — the second half continues from the last
  possession's consequence; a real reset (and overtime) is its own session.
- **Per-outcome time shaping** — turnovers reading shorter than makes, etc. Still deferred.
- **Tempo calibration + the coach pace (1–10) attribute** — unchanged from S29.

## Session 29 — Game clock: possessions draw elapsed per shot-clock period; Governor counts down two halves (2026-06-13)

**The first session that puts real time on the board.** The Governor previously drained a flat 18s
placeholder per possession and stopped at a fixed 200-possession cap — a marked seam, exactly like
`score = 0` in Session 28. This session fills it with real machinery: each possession's duration is a
draw from a truncated normal, the resolver counts how many shot-clock periods a possession used, and
the Governor counts that time **down** from two 20-minute halves until the clock runs out.

**Confirmation mode, not rediscovery (CONVENTIONS §0/§6c).** The prompt carried a fully verified §6c
map; the pull confirmed every anchor against the committed tree with zero drift. Session 28 preconditions
verified first (Scoring.cs exists; `public int Points` present in Resolver.cs). All §6c anchors confirmed:
`public int Points { get; init; }` in `RoutingOutcome`; `var points = 0;` in `Route`; the single
`case ContinuationKind.ResolveOffensiveRebound:` site; all three `return new RoutingOutcome` forms;
`SecondsPerPossession` in GovernorConfig; the Governor ctor and `for` loop; both `totalSeconds +=` lines;
the `PossessionRecord` tail; and the GovernorLoopCheck call sites. No drift. No design questions — all locked.

**The load-bearing architectural decision: the draw lives in the Governor, not the resolver.**
The resolver's walk **counts** shot-clock periods and surfaces the count on `RoutingOutcome.ShotClockPeriods`
— a fourth walk tally of the exact `Points` / `FreeThrowSpins` shape (init-only `int`, 0 default, pure
append; every existing construction untouched). The **Governor** owns the actual time draw because it
already owns the time accumulation and the stop rule; the countdown and the per-period sampling belong
there. This keeps the resolver's 8 constructor sites untouched (no new dependency on the resolver).
The walk must supply the count because the Governor cannot see how many 20-second reset periods a
possession used — offensive rebounds happen inside the walk and the Governor never sees them.

**The new continuous primitive: `Core/ClockDraw.cs`.** The engine's first non-pie roll: a truncated
normal. Box-Muller turns two uniform draws into one standard normal; reject-and-resample keeps the result
inside `[floor, ceiling)` — ceiling is **exclusive** so a fast team's tail thins smoothly through 28s and
29s and nothing piles at exactly 30 (exact-30 is owned by the shot-clock-violation terminal, its own
invariant `ElapsedSeconds`). The fallback after 100 attempts clamps to center — a guard so a pathological
config can never spin forever. Follows the `JumpBall.cs` / `Scoring.cs` static-helper house style.

**The tempo config: `Config/RollClockConfig.cs`.** Every tempo number lives here, nothing hardcoded.
Mirrors `GovernorConfig.cs`'s `JsonDocument` → `GetProperty("Clock")` → `JsonSerializer.Deserialize`
pattern exactly. Five properties with defaults: `Center = 17.0`, `StdDev = 4.5`, `Floor = 4.0`,
`FullClockSeconds = 30.0`, `ResetClockSeconds = 20.0`. These are starting knobs, not calibrated values.
Realized APL runs a touch above `Center` because offensive-rebound reset periods add time to ~12% of
possessions; Emmett tunes `Center` down by watching the harness histogram. The **future coach pace (1–10)
attribute** shifts the center passed to `ClockDraw.Sample` — no engine change at that point.

**Resolver edits (the ONLY resolver change): the period counter.**
- `var shotClockPeriods = 1;` beside the other `Route` inits (period 1 = the fresh 30s clock the
  possession opens on).
- `shotClockPeriods++;` as the first statement of `case ContinuationKind.ResolveOffensiveRebound:` —
  every offensive rebound resets the clock to 20 and starts a new period, and that case is hit exactly
  once per offensive rebound (Roll I's `OffensiveRebound` arm → `Continue(ResolveOffensiveRebound)` → Roll
  K). Outcome-blind: the period counts whether Roll K puts it back, resets the offense, or turns it over.
- All three `return new RoutingOutcome` calls gained `, ShotClockPeriods = shotClockPeriods`. No pie,
  no route, no rate changed. Confirmed exactly 3 returns before editing.

**Governor edits: the clock-driven countdown.**
- `GovernorConfig` overhauled: `SecondsPerPossession` **removed** (retired completely — stale-ref
  sweep confirmed zero references remain after the edits). Added `int Halves = 2` and
  `double HalfSeconds = 1200.0`. `PossessionCap` bumped to 400 and repurposed as a safety ceiling
  (the clock is the real stop rule; the guard exists so a game that somehow never drains the clock
  throws rather than spinning).
- `PossessionRecord` gained trailing `double Elapsed` and `int Half` params (with `<param>` docs in the
  established voice).
- Governor ctor gained `RollClockConfig clock` and `IRng rng` (stored as `_clock`, `_rng`). The harness
  passes a **separate seeded** `SystemRng(cfg.Seed + 1)` so the resolver's existing draw stream is
  untouched and every other check stays byte-for-byte identical.
- `Run` replaced the `for (var p = 0; ...)` cap loop with a `while (half <= _cfg.Halves)` countdown.
  State: `half = 1`, `halfRemaining = _cfg.HalfSeconds`, `guard = 0`. The guard increments each
  iteration and throws at `PossessionCap` — the clock-not-draining tripwire.
- Per possession: compute `rawElapsed = outcome.EndedOn?.ElapsedSeconds ?? DrawPossessionSeconds(...)`.
  Invariant terminals (shot-clock violation 30s, backcourt 10s, five-second inbound 0s) use their real
  value; everything else draws. Cap at the half boundary: `applied = Math.Min(rawElapsed, halfRemaining)`;
  `halfRemaining -= applied; totalSeconds += applied` — so each half sums to **exactly** `HalfSeconds`.
  After spawning the next state: `if (halfRemaining <= 0.0) { half++; halfRemaining = _cfg.HalfSeconds; }`.
  Only the clock resets at the half — fouls and arrow carry (no halftime foul reset this session,
  matching the existing persistence checks).
- New private `DrawPossessionSeconds(int shotClockPeriods)`: period 1 draws from `(Center, StdDev, Floor,
  FullClockSeconds)`; each subsequent period (per offensive rebound) draws from the 20s distribution with
  center and sd scaled by `ResetClockSeconds / FullClockSeconds` (≈ 0.667), floor unchanged. The
  `Math.Max(1, periods)` guard handles the 0-default defensively.

**Harness changes.** `RollClockConfig.Load` added to `Main` and threaded to `GovernorLoopCheck`. The
check's title updated ("two 1,200s halves"), `countOk` removed (clock-driven, no fixed cap), `noLostOk`
simplified to `terminal + parked == records.Count`. New clock block: per-half drain check (exact to
0.01s); realized APL assertion `[14, 21]`; possession-count band `[100, 220]`; a 100k-sample tempo
histogram (5-second bins) with truncation proof (every sample `>= Floor` and `< FullClockSeconds`).
All are folded into `clockOk` which joins the `allOk` aggregation. Prior rate/routing/score checks
unchanged — timing adds time to each possession but touches no pie, no route, no rate.

**Validation (CONVENTIONS §2).** Python Monte Carlo mirrored `ClockDraw.Sample` (Box-Muller +
reject-resample, center=17, sd=4.5, floor=4, ceiling<30) over 100k samples: mean=17.01, every sample
in [4, 30), truncation holds, histogram peaks in [15,20) with visible 5–10s and 25–30s tails. Multi-period
check: 2-period mean (28.4s) > 1-period mean (17.0s) as expected. Half-drain simulation: avg game secs
= 2400.0 (exact), avg possessions ~133, APL ~18.0s (in [14,21]). Brace balance ok across all 7 touched
files. Stale-ref sweep: `SecondsPerPossession` appears zero times in any file after edits.
Harness run (Emmett's machine): **ALL CHECKS PASSED** on the first run. New clock lines read
`half 1: 1,200s / half 2: 1,200s | each drains to 1,200 -> ok`, `possessions=135 (~67 per half) |
realized APL=17.8s -> ok`, histogram peaking in [15,20) with thin 5–10s and 25–30s tails, and
`min=4.01 / max=30.00 | truncation holds -> ok`. Prior `score:` and `FG rule` lines still ok.

**Note on `max=30.00` in the harness output.** The histogram prints `max=30.00` with `:F2` formatting,
but the truncation assertion passed — no sample actually reached 30.0. The `:F2` format rounds 29.998
(the highest observed value) to 30.00. Nothing piles at 30; the ceiling-exclusive contract holds.

**What stays out.** No end-of-half hold-for-last-shot behavior (capped and resolved normally). No
late-game tactics. No per-outcome time shaping (all outcomes draw from the same distribution). No halftime
team-foul reset (fouls carry continuously, matching the existing persistence check). No tempo calibration
or coach pace attribute. No new pie, `ContinuationKind`, or roll-signature changes — rolls A–M and every
generator are untouched.

**Deferrals (carried).**
- **End-of-half hold-for-last-shot** — the NEXT session. When a possession starts with < 30s left, the
  offense mostly drains the clock and shoots last; this session just caps. The probabilistic intent branch
  gates on `halfRemaining` in the Governor.
- **Per-outcome time shaping** — turnovers reading shorter than makes, etc. Deferred (creep risk).
- **Halftime team-foul reset** — real basketball resets at the half; this session's fouls stay
  continuous. When added, it reworks the foul-monotonic check.
- **Tempo calibration + coach pace (1–10) attribute** — `Center` is tuned down to hit a target blended
  APL; the pace attribute later shifts `Center` per team. No engine change at that point.
- **Shot-clock-violation-after-offensive-rebound elapsed** — the violation invariant is a flat 30s
  regardless of which clock it occurred on; a violation on a 20s reset is vanishingly rare and left at 30.

## Session 28 — Scoring: the Governor accumulates real points (2026-06-13)

**The first session that puts real numbers on the board.** The Governor already ran an N-possession
game and already wrote to the score every possession — but the value was a literal `0`, a marked
placeholder seam. This session made that value real: the resolver tallies points on its walk, surfaces
the total on `RoutingOutcome`, and the Governor accumulates it into the score. Stub pies still, so the
scorelines are un-basketball-like (Home 107 / Away 103 over 200 possessions) — correct and intended.
This proves the scoring *machinery*; realistic scorelines come when the attribute-driven generators land.

**Confirmation mode, not rediscovery (CONVENTIONS §0/§6c).** The prompt carried a fully verified §6c
map; the pull confirmed every anchor against the committed tree with zero drift. Confirmed: exactly
three `return new RoutingOutcome` statements in `Route` (Terminal + the two retired-stub corners);
`RoutingOutcome` carrying `PutbackAttempts` and `FreeThrowSpins` as the init-only `int`-with-0-default
template; `DriveFreeThrows`'s `out int spinCount` signature and its local `Spin()` counting pattern;
the two FT call sites (bonus fork + shooting fouls); the Governor's `const int pointsThisPossession = 0`
placeholder and its Home/Away split; `PossessionRecord`'s positional shape; and `GovernorLoopCheck`'s
single per-record `for` loop. No design questions remained — all locked.

**Why the tally lives on the walk.** Points have three sources and only the resolver's walk sees all
three at once: (1) a clean made field goal — a `Made` terminal, worth 2/3 by zone; (2) an and-1 basket
— a `MadeAndFouled` shot, which is NOT a terminal (it is a `Continue` into the shooting-FT node), so its
2/3 must be banked at the shooting-FT edge; (3) made free throws — 1 point each, covering and-1, bonus,
and shooting-foul FTs uniformly. The Governor cannot derive points from the final terminal alone — the
and-1 basket and intermediate FT makes are invisible there — exactly the reasoning behind `FreeThrowSpins`.

**The 2/3 rule has one home.** New `Core/Scoring.cs`: a single static `FieldGoalPoints(ShotLocation)`
→ 3 for `Three`, 2 for every other zone (Long is a long TWO — worth 2 despite the name). Same small
`Core/` static-class shape as `JumpBall` and `DefensiveFoulCharge`. A made free throw is always 1 point
and is tallied directly in the FT driver, not here.

**Resolver edits (the walk tally).**
- `RoutingOutcome` gained `public int Points { get; init; }` — a third walk tally of the exact
  `PutbackAttempts` / `FreeThrowSpins` shape (init-only, 0 default, pure append; every existing
  construction untouched).
- A `var points = 0;` counter beside `freeThrowSpins` in `Route`, set on all three `return`s.
- **FG banking at the Terminal return:** `if (t.Reason == "Made") points += Scoring.FieldGoalPoints(t.State.ShotType!.Value);`
  before the return. `ShotType` is non-null on a Made terminal (Roll G stamped it before Roll H resolved).
- **And-1 banking at the shooting-FT edge:** `if (c.State.Result == ShotResult.MadeAndFouled) points += Scoring.FieldGoalPoints(c.State.ShotType!.Value);`
  — the basket counts even though it routed in as a Continue; this edge is hit exactly once per shooting
  foul, with `Result` distinguishing and-1 from a fouled miss (which scores no FG).
- **FT makes:** `DriveFreeThrows` gained `out int ftPoints`; a `ftMakes` counter increments on every
  `Make` inside `Spin()` (intermediate or last — each made FT is 1 point), returned as `ftPoints`. Both
  call sites (bonus fork, shooting fouls) bank it: `points += bonusFtPoints` / `points += shootingFtPoints`.

**Governor edits (accumulation).** The `const int pointsThisPossession = 0;` placeholder became
`var pointsThisPossession = outcome.Points;` — the Home/Away credit lines below are unchanged (they
already credit the offense; only the source value changed). `PossessionRecord` gained a trailing
`int Points` param (credited to `Offense`), threaded at the construction site. The stale class-doc
references to "the placeholder 0" / "the zero score" were updated to describe the real derivation.

**Harness (light validation, Emmett's explicit call).** `GovernorLoopCheck` extended in place — no new
loop. Three accumulators (`homePoints` / `awayPoints` / `talliedPoints`) fold into the existing per-record
`for`, keyed on `r.Offense`. New assertions: accumulation matches (`game.HomeScore == homePoints &&
game.AwayScore == awayPoints`), total matches (`HomeScore + AwayScore == talliedPoints`), and points
actually flow (`talliedPoints > 0`). The "credit offense only, never defense" rule is proven *for free*
by the split — a point credited to the wrong side would land in the wrong accumulator and fail the match.
A trivial no-RNG FG-rule assertion (`FieldGoalPoints(Three)==3`, all others `==2`) folded into the same
check. Both `scoreOk` and `fgRuleOk` added to the `allOk` aggregation.

**Validation (CONVENTIONS §2).** Pre-check: Python Monte Carlo mirrored the point arithmetic over 10k
mixed possessions — clean Made (3/else 2), and-1 (FG + 1/made FT), bonus/shooting FT trips (1/made FT)
— confirming the per-possession tally matches an independent recompute, accumulation equals the summed
tally, and home+away equals total (no leak to defense). Harness run: the two new lines read
`score: Home 107 / Away 103 | accumulates per-possession tally -> ok` and `FG rule (Three=3, others=2) -> ok`;
every prior rate/routing check byte-for-byte unchanged; **ALL CHECKS PASSED** on the first run.

**Out of scope (held).** No clock work (`SecondsPerPossession` / `TotalSeconds` stay flat placeholders).
No config knobs (points are derived, not configured — nothing added to `config.json`). No new
`ContinuationKind` / enum / roll-signature changes; rolls C/H/K/L and every generator untouched. Only
`Resolver.cs`, `Governor.cs`, and the new `Core/Scoring.cs` changed in the engine.

**Deferrals (carried).**
- **Live-ball backcourt steal, high push** — a near-basket steal should push harder than the current
  single 50% steal pie. Needs Emmett's backcourt-steal numbers + a small Roll J split keyed on the
  court stamp the steal terminal already carries.
- **Real, calibrated scoring** — the un-basketball-like stub-pie scorelines become realistic only when
  attribute-driven generators replace the stub pies (the deferred ~90%). No engine change at that point;
  the machinery built this session just reports whatever the pies produce.
- **Clock / time** — a real clock and end-game logic is its own future session.
- **Per-player point attribution** (which slot scored) — the deferred attribution layer reads the
  selected slot off the possession; this session credits the *team* only.


## Session 27 — Offensive-foul flavor tag + backcourt dead-ball spot-flip (2026-06-13)

**Two small surface refinements, no structural gaps.** Both were deferred by #6: the offensive-foul
flavor tag (theater only, unblocks correct per-player attribution later) and the backcourt dead-ball
spot-flip (a team that loses the ball dead before crossing hands the other team the ball already
advanced, skipping the bring-up). Neither changes any rate; neither opens a new stub; both are additive
appends onto what already exists.

**Confirmation mode, not rediscovery (CONVENTIONS §0/§6c).** The prompt carried a verified §6c map;
the pull confirmed all anchors against the committed tree. No drift. The Terminal record confirmed as a
one-liner (no Flavor field yet); three OffensiveFoul emitters confirmed (Roll C line 79, Roll K line
95, Resolver `ResolveOffensiveFoul` line 303); `EntryType` confirmed with only `DeadBallInbound` and
`Transition`; all 13 Roll C dead-ball arms confirmed as plain `DeadBallTo(state.Defense)`. Governor
`NextEntry` threading confirmed at line 164. One pre-build question surfaced: the spot-flip rule
applies to all 13 dead-ball arms in Roll C (not just the original five) — Emmett confirmed, the
principle is ball-was-already-on-that-side regardless of how it was lost.

**Name decision: `BallAdvanced`, not `FrontcourtInbound`.** The draft map used `FrontcourtInbound`;
Emmett renamed it `BallAdvanced` in the design conversation. The name describes what happened (the
other team gets the ball already advanced across) rather than naming a court position. Settled before
any code was written.

**Offensive-foul flavor.**
- New `OffensiveFoulFlavor` enum: `Charge`, `PushOff`, `IllegalScreen`. Theater only — never read for
  routing, never changes any consequence. Lives in `Rolls/OffensiveFoulOutcomes.cs`, same shape and
  doc wording as `FoulFlavor`.
- Two weight sets selected by court-state: **frontcourt** (Charge 30 / PushOff 20 / IllegalScreen 50
  — illegal screens dominate; these are set-play halfcourt fouls) and **backcourt** (Charge 40 /
  PushOff 50 / IllegalScreen 10 — screens don't happen before the ball crosses).
- `Terminal` record expanded from a one-liner to carry `public OffensiveFoulFlavor? Flavor { get;
  init; }` — mirroring `Continue.Flavor` doc wording exactly (theater, never routed, null on every
  non-offensive-foul terminal).
- New `RollOffensiveFoulConfig` and `RollOffensiveFoulStubPieGenerator` — same shape as Roll D's
  flavor pair. The generator reads `state.Frontcourt` to select the mix.
- **Single stamp site (the chokepoint):** `Resolver.cs`'s `case Terminal t:` — the one place all
  three emitters (Roll C, Roll K, `ResolveOffensiveFoul`) converge. When `t.Reason == "OffensiveFoul"`,
  draw the flavor pie and stamp `t = t with { Flavor = flavor }` before returning the routing outcome.
  Roll C and Roll K are untouched; the resolver-stamp approach was the chosen design (per-roll
  alternative was the fallback only if a snag arose).

**Backcourt dead-ball spot-flip.**
- New `EntryType.BallAdvanced` — a dead-ball restart where the ball was already in the backcourt when
  the turnover occurred, so the new offense inbounds from the frontcourt (near their basket) and skips
  Roll A's bring-up entirely. Roll B is the entry node.
- New `PossessionConsequence.BallAdvancedTo(team)` static helper — parallel to `DeadBallTo`, lives in
  `RollResult.cs` alongside the other helpers.
- **`RunPossession` branch:** a `BallAdvanced` entry drops straight into Roll B (generator + execute),
  bypassing Roll A entirely. Sits between the Transition branch and the legacy Roll A branch.
- **Roll C: all 13 dead-ball arms updated** with the spot-flip conditional:
  `state.Frontcourt ? DeadBallTo(state.Defense) : BallAdvancedTo(state.Defense)`. Live arms
  (BadPassIntercepted, LostBallLiveBall) are untouched — they route to Roll J as steals. The timed
  violation arms keep their `ElapsedSeconds` property; only the consequence changes.
- **`ResolveOffensiveFoul` in the Resolver** likewise: a backcourt offensive foul (Frontcourt==false)
  yields `BallAdvancedTo`; a frontcourt one yields `DeadBallTo`.
- **Over-and-back self-handles cleanly:** it is Halfcourt-only (verified EntryBackcourt weight 0.0),
  so it always reads `Frontcourt==true` and always produces `DeadBallTo` — no carve-out needed.
- **Governor unchanged:** already threads `Entry: consequence.NextEntry` onto the spawned possession;
  `BallAdvanced` possession then enters `RunPossession`, hits the new branch, drops to Roll B.
- **Harness:** two existing checks asserted `NextEntry == DeadBallInbound` for all dead-ball Roll C
  arms. Both updated to accept `DeadBallInbound` OR `BallAdvanced`, with no transition context on
  either. Labels updated to reflect the new reality.

**Config.** `OffensiveFoulFlavor` section added to `config.json` (front/back weight sets + Epsilon).
`RollOffensiveFoulConfig.Load` reads it. All 8 harness `Resolver` constructor sites wired with
`RollOffensiveFoulStubPieGenerator`.

**Validation (CONVENTIONS §2).** Pre-check: Python Monte Carlo confirmed both flavor mixes converge
within 0.005pp tolerance (100k draws each); spot-flip conditional produces BallAdvanced only on
backcourt draws and DeadBallInbound only on frontcourt draws (0 misroutes); over-and-back confirmed
self-handling. First harness run flagged two FAIL lines in the existing Roll C checks — the
`DeadBallInbound`-only assertion was stale. Fixed both sites to accept `BallAdvanced`; second run:
**ALL CHECKS PASSED.**

**Deferrals (carried).**
- Live-ball backcourt steal high-push split (Roll J near-basket steal → higher Push than current single
  50% steal pie). Needs Emmett's backcourt-steal pie numbers and a small Roll J split keyed on the
  stamp the steal terminal already carries. The live twin of the dead-ball spot-flip.
- Scoring — the identified next big step. Governor already accumulates `+= 0`; the work is deriving
  points per possession from the made-FG zone and free throws made, tallied on the resolver's walk.
- Re-inbound weights, attribute generators, attribution/stats layer — all carried from prior sessions.


## Session 25 — Contextification #6 (Roll A reshape + live halfcourt losses + closed chain) (2026-06-13)

**The premise this session opens.** #6 completes the contextification arc that #5a set up. #5a SEATED
every no-shot loss type in Roll C — context-gated but dormant, proven only in isolation. #6 turns them
LIVE and closes the loop: it reshapes Roll A to its real outcomes, wires Roll A's loss exit into the
ready Roll C by court-phase context, activates the halfcourt loss set, and makes the two keep-the-ball
inbound edges RE-ENTER Roll A instead of parking — so the possession chain now terminates end to end
with nothing left in a stub. (Numbering note: the #5a design notes forecast this step as "#5b"; it
shipped under the label #6, which is what the code comments and these docs use. The plan bent to what
the code is, per CONVENTIONS §6a.)

**Confirmation mode, not rediscovery (CONVENTIONS §0/§6c).** The prompt carried a verified §6c
reconnaissance map; the pull confirmed it against the committed tree rather than re-deriving the
architecture. The map held throughout — no material drift. Two named gotchas confirmed exactly: (6c-F)
the resolver called `RollC.Execute(..., _rng)` with NO config and held no `_rollCConfig`, so the moment
a Halfcourt violation arm went live it would throw for lack of invariant elapsed; (6c-G) a stale
`EntryType.Transition` doc comment described a retired temp-route. Both fixed. The only divergences
were cosmetic stale comments (the `TurnoverOutcomes` violation members still claimed Roll A held a
twin terminal "until #5b consolidates" — now retired), corrected in passing.

**Decisions settled with Emmett before any code (CONVENTIONS §4).**
1. **The re-inbound re-runs the entry.** A keep-the-ball restart (a below-bonus defensive foul, an
   OOB-retained shot) re-runs Roll A and can be turned over again — it is a real basketball event, not
   a free resumption. For now it draws the SAME placeholder odds as a fresh inbound; making it
   genuinely easier and pressure-driven is deferred to the real generator. The architecture carries the
   distinction (the court-state marker rides through the re-entry), even though the weights don't tilt
   yet.
2. **Offensive foul = a dead-ball loss, nothing more (this session).** A player-control foul (charge /
   illegal screen) hands the ball to the other team with no free throws and no bonus credit; the foul
   is still attributed to the individual player by the future attribution layer. The other team's next
   possession starts in the backcourt. The finer spot-flip (the OTHER team starting in the frontcourt
   after a backcourt turnover) is deferred. Offensive-foul FLAVOR (charge vs. off-arm by the handler vs.
   illegal screen by another player — and it matters because those attribute to DIFFERENT players) is
   deferred: it needs a flavor tag on the LOSS TERMINAL, which terminals can't carry today, so it is
   its own task.
3. **Entry fouls split ≈ 85% defensive / 15% offensive** — entry contact is overwhelmingly a defensive
   reach-in. Placeholder; the split scales with backcourt pressure in the real generator later
   (offensive fouls in the backcourt only with an active press).
4. **Halfcourt turnover types = the blessed 13-way breakdown** (24/18/16/14 mains, 9 offensive foul,
   then 8/2.5/2.5/2/1.5/1.5/0.5/0.5 minors). This single pie governs EVERY halfcourt turnover — Roll
   A's frontcourt re-inbound, Roll B's halfcourt loss, Roll F's player action — because a travel is a
   travel whoever caused it. Emmett's note that the offensive-foul slot will eventually want its own
   flavor pie (handler charge/off-arm vs. screener illegal screen) is logged as the deferred flavor work.
5. **Kept the enum name `CleanEntry`** (no rename to `SuccessfulEntry`) — renames are noisy churn and
   are parked.

**The court-state marker (the new spine).** A single `bool Frontcourt = false` on `PossessionState`,
mirroring `FastBreak`'s "single bit suffices" shape. False = backcourt (still bringing it up: the
10-second count, the backcourt shot-clock, the 5-second inbound are all in play); true = frontcourt
(across and into the set: those backcourt-only losses are gone). It LATCHES to true the instant Roll
A's `CleanEntry` hands off to Roll B and NEVER flips back within the possession (there is no spatial
"return to the backcourt" in the role-based model — over-and-back is a Halfcourt loss, not a court-state
flip). A re-inbound carries whatever court-state is current. The Governor constructs possessions by
name and never sets it, so every fresh inbound defaults to backcourt. This is the origin signal that
lets Roll A pick its loss CONTEXT.

**Roll A reshaped to its real outcomes.** Five outcomes: `CleanEntry`, `Turnover`, `OffensiveFoul`,
`DefensiveFoul`, `JumpBall`. The three former violation terminals (`ShotClockViolation`,
`FiveSecondInbound`, `TenSecondBackcourt`) are RETIRED from `EntryOutcome` — their loss now resolves in
Roll C, reached through the Turnover exit's `EntryBackcourt` context, so the old violation probability
mass folded into `BaseTurnover` (0.06 → 0.08). The old single `Foul` slice split into offensive
(0.0045) and defensive (0.0255). `CleanEntry` latches the court-state; `Turnover` stamps the context
(`state.Frontcourt ? Halfcourt : EntryBackcourt`); `OffensiveFoul` routes to a deterministic terminal;
`DefensiveFoul` routes to Roll D; `JumpBall` to the arrow node. `RollAConfig` dropped the violation
weights and the two violation-elapsed fields (Roll C owns them now); `RollA.Execute` keeps `cfg` on its
signature for call-site parity though it no longer reads it (the violation arms were its only readers).

**The 6c-F wiring (the load-bearing fix).** The resolver gained a `_rollCConfig` field + ctor param
(positioned right after the Roll C generator) and now passes it to `RollC.Execute`. The now-LIVE
Halfcourt `ShotClockViolation` arm (2.5%) and the EntryBackcourt violation arms read their invariant
elapsed (30/0/10) through it and FAIL LOUD without it — so this was the single edit the whole
turn-on depended on. All eight harness `new Resolver(...)` sites gained the matching `cfgC` argument.

**Offensive foul: one node names the loss.** A new `ContinuationKind.ResolveOffensiveFoul` is mapped by
the resolver DETERMINISTICALLY (no pie) straight to a `Terminal("OffensiveFoul", state,
DeadBallTo(defense))` — byte-for-byte the same reason string and consequence Roll C names for an
offensive foul. Kept as a continuation kind (not a Roll A terminal) so the "one node names the loss"
rule holds and the future flavor tag has a single home.

**The chain closes.** `ResumeInbound` (Roll D below-bonus) and `ResolveSidelineInbound` (OOB-retained,
plus the I/J/K/M below-bonus loose-ball-defense / OOB-off-defense edges) no longer park: they re-run
Roll A carrying the current court-state and resolve downstream. The resolver no longer holds the two
inbound stub objects (the fields and their assignments were dropped); the ctor params are retained so
the eight construction sites are unchanged, and the harness builds its OWN stub instances for its
direct fact-echo checks. With the violation terminals moved into Roll C and the two inbound edges
re-entrant, NOTHING in the live chain parks at a stub anymore — every possession ends on a terminal.

**The §2a stateful-accumulation check (CONVENTIONS §2a), front and center.** The re-entrant inbound
loop is exactly the bug class §2a guards: a below-bonus defensive foul re-inbounds, charging another
team foul, and across a shared game the bonus crosses MID-LOOP — so the same `ResumeInbound` edge that
re-ran Roll A early instead routes to `ResolveFreeThrows` later. Both landings are handled, and the
loop converges fast: `CleanEntry`'s ~0.88 weight dominates each Roll A re-run, so the loop length is
geometric (theoretical mean 1/(1−0.0255) ≈ 1.026; Monte-Carlo confirmed mean 1.026, max 4 hops, zero
iteration-guard hits). The harness's shared-game batch crosses the bonus within the first few
possessions, after which almost every defensive foul becomes a free-throw trip rather than a re-inbound
— both terminate.

**Harness regression surface (the ~dozen checks the map predicted).** The chain closing flipped
`BatchCheck`'s split to `ended=100,000, routed-to-stub=0, unrouted=0` (the headline). `RollFHandoffCheck`
lost its `"foul -> Roll D stub"` bucket — a foul now resolves downstream into the shot/turnover/FT
buckets, proven by zero-unrouted. `RollGHandoffCheck` and `RollHHandoffCheck` lost their
`STUB:SidelineInbound` CORE bucket and the zone-/result-ride-through-on-stub assertions (an OOB-retained
shot re-runs Roll A and lands deeper; one-hop fact ride-through is proven authoritatively by
`RollHResolutionBatchCheck`). `RollCBatchCheck` and `RollCContextCheck` now exercise the live 15-way
Halfcourt pie (and `MapTurnover` was extended from 5 reasons to all 15). `ShowSamples`,
`PressureSignalCheck`/`RollCLiveStripRate`, and the eight resolver constructions were threaded with the
config. Crucially, the Roll I/J/K/M handoff checks switch on the returned Continue KIND and resolve
against their OWN local stubs (not the resolver's destination string), so they were UNAFFECTED — the
distinction the §6c map called out exactly.

**Validation (CONVENTIONS §2).** No .NET SDK in Claude's sandbox, so the work was reasoned +
Monte-Carlo-traced (Roll A pie convergence; the re-entrant loop convergence + bonus crossing;
backcourt-vs-frontcourt context selection) plus static checks (brace balance per file, JSON validity,
signature confirmation against the pulled source, a stale-reference sweep). Emmett's harness run is the
gate, exactly as every session. Result: **ALL CHECKS PASSED** — every reshaped Roll A rate, the live
15-way Halfcourt pie, the EntryBackcourt violations stamping 30/0/10, and the closed-chain headline
(`ended=100,000, routed-to-stub=0, unrouted=0`; Governor `parked=0`).

**Deferrals (carried + added).**
- **Offensive-foul flavor** (charge / off-arm by the handler vs. illegal screen by another player):
  needs a flavor tag on the OffensiveFoul loss TERMINAL — terminals carry no flavor field today (only a
  Continue does), so this is its own plumbing task. Attributes to different players, so it is "necessary"
  (Emmett) but not free.
- **The backcourt-turnover spot-flip** (the other team starting in the frontcourt after a backcourt
  turnover): deliberately out of scope; the court-state marker is a single latching bool, not a spatial
  spot model, this session.
- **"Easier + pressure-driven" re-inbound weights:** the re-inbound currently uses fresh-inbound odds;
  the real generator tilts them.
- **The attribute-driven generators** (the deferred ~90% of the work) remain ahead.
- The #5a `Pie.Roll` overflow-fallback FP wrinkle remains parked (≈1e-11 over a 100k batch; a
  pie-mechanism change, out of scope).


## Session 24 — Contextification #5a (Roll C expansion): every no-shot loss type seated in Roll C, context-gated and DORMANT (2026-06-13)

**The premise this session opens.** Fifth of the contextification arc, split in two. #5a makes Roll C
the single canonical home for EVERY way a possession is lost without a shot — all turnover types AND
all violation types — by SEATING them, gated by context, but DORMANT: declared, resolvable, given
zero weight in every live context, with nothing routing to them. They are proven in ISOLATION here.
#5b then reshapes Roll A and wires its loss exit into the ready Roll C, turning them live. The split
is deliberate: it keeps the expansion behavior-neutral and independently provable before anything
depends on it.

**Confirmation mode, not rediscovery (CONVENTIONS §0/§6c).** The prompt carried a verified
reconnaissance map; the pull confirmed it against the tree rather than re-deriving it. Both audited
gotchas held exactly: (1) `Pie` walks ALL enum members and throws on any omission AND validates
sum-to-1, so adding members forces every existing context dict to list them at `0.0` and the config
to carry backing fields — "dormant" means explicitly zeroed, not absent. (2) Roll C's existing arms
set no `ElapsedSeconds`; the new violation arms must stamp invariant elapsed (30/0/10), unlike every
existing turnover arm, so `RollCConfig` gains violation-elapsed fields (dormant copies of Roll A's,
until #5b consolidates).

**Two places the code diverged from the map's wording (code wins, flagged per §0).** (1) "All three
regression checks byte-for-byte" is literally true only for `RollCContextCheck` and
`PressureSignalCheck`, which reference the five existing types by name. `RollCBatchCheck` iterates
`pieC.Slices`, and since `Pie` stores a slice for every member, its output GAINS additive `0.000`
rows for each new type — every existing rate and pass/fail signal unchanged, output merely longer.
The three checks were left untouched (truly byte-for-byte source); the additive zero-rows are
accepted (`ShowSamples`' pie print grows the same way). (2) A latent FP wrinkle, flagged not fixed
(fixing it is an out-of-scope pie-mechanism change): `Pie.Roll`'s overflow fallback returns the LAST
slice, now a zero-weight appended type, so a draw within ~1e-16 of 1.0 in a live context could fall
through to it (→ `RollCBatchCheck`'s unmapped-reason throw). Expected occurrences over a 100k batch
≈ 1e-11 — will not fire in practice. Logged as a deferral.

**The taxonomy seated (settled with Emmett).** ONE expanded `TurnoverOutcome` enum, ONE weighted pie
per context — a possession is lost exactly one way, so a single draw picks the single loss type. Ten
new members appended after `OffensiveFoul` (append order keeps every existing draw byte-for-byte:
zero-weight slices don't advance the cumulative walk, so the same `u` maps to the same outcome —
confirmed by a same-seed 5-member-vs-15-member parity trace). Seven new turnover types (`Travel`,
`DoubleDribble`, `Carry`, `ThreeSecondViolation`, `FiveSecondCloselyGuarded`, `OffensiveGoaltending`,
`BackcourtViolation`) are dead-ball, null elapsed. Three violation types (`ShotClockViolation`,
`FiveSecondInbound`, `TenSecondBackcourt`) are dead-ball but stamp invariant elapsed (30/0/10) — the
only timed arms in Roll C. Defensive goaltending is explicitly NOT here (it AWARDS the basket → a
Roll H make/miss variant, deferred). The enum keeps its name this session; the broader rename to
`PossessionLossOutcome` is a later cosmetic pass.

**The context scheme.** A third `TurnoverContext` value, `EntryBackcourt`, seats the
post-made-basket / backcourt-start phase. Court-phase gates which losses are reachable: Halfcourt
(the settled set — where travel, over-and-back, 3-second etc. will live once #5b turns them on);
Transition (the outlet/push, unchanged); EntryBackcourt (the bring-it-up phase — 5-second inbound,
10-second backcourt, backcourt shot-clock, plus a bad pass / lost ball on the way up). This session
every new type is `0.0` in BOTH live contexts; real (placeholder) weight lives only in EntryBackcourt
and is exercised solely by the isolation check. #5b sets the live Halfcourt weights and implements
the origin-dependent routing (made basket vs. foul past halfcourt) that selects the context.

**The signature wrinkle.** The violation arms need their elapsed from config, but `RollC.Execute`
took no config. Rather than add a required parameter (which would touch every legacy call site and
the resolver), an OPTIONAL `RollCConfig? config = null` was added — mirroring the generator's optional
`context` default. Every legacy caller compiles and behaves byte-for-byte unchanged, because the
violation arms are dormant (0 weight in Halfcourt/Transition, the only contexts the resolver builds)
and so are never reached on the live path. A violation arm reached without a config fails LOUD (not a
silent null-deref), consistent with the engine's fail-at-the-seam rule. Only the isolation check,
which deliberately weights the violations, passes a config.

**Validation (reasoned + Monte-Carlo-traced, pending Emmett's harness run).** The three existing Roll
C checks are unchanged. A new `RollCExpansionCheck` proves the seated set in isolation in two parts:
(1) it drives the `EntryBackcourt` context directly, confirming its seven weighted members are
reachable at their configured rate (selection + resolved rate) and its eight zero-weight members are
unreachable; (2) a directly-built UNIFORM pie over all fifteen types lights up every arm — including
the halfcourt-natural new types that are `0.0` in every live context this session — and asserts each
is a clean terminal with the right consequence (dead-ball to defense, except the two existing live
steals) and the right elapsed (violations 30/0/10, every turnover null), and that NO new type leaks a
steal. It carries its own complete reason map so the regression-net `MapTurnover` stays byte-for-byte.
A Python mirror of the generator + `Pie` cumulative walk confirmed: Halfcourt 30/22/18/20/10 and
Transition 25/15/20/35/05 with the new types at 0%, EntryBackcourt at its configured rates, the
uniform pie within tolerance, and per-draw legacy parity. §2a: the new context is unrouted and
`RollC.Execute` touches no shared state, so the Governor loop is unchanged (`unrouted == 0`). §2b
sweep clean: the new type names and `EntryBackcourt` appear only in Roll C's enum/config/generator/
arms and the isolation check — no feeder references them.

**What #5a closes.** Roll C is now the structural home for every no-shot loss, with the
court-phase context scheme in place — all behavior-neutral and proven in isolation. The arc moves to
#5b (Roll A reshape: collapse to five outcomes, route the loss exit into the ready EntryBackcourt
context, fold the retained inbounds back in, turn the dormant types live), drafted as a separate
audited pass per §6.

## Session 23 — Contextification #4 (Bonus-fork extract): the charge-and-fork in Rolls D / I / J / K / M collapsed into one shared node (2026-06-13)

**The premise this session opens.** Fourth of the five-session contextification arc, and a
different KIND of close than #1–#3: not a new context on an existing roll, but a DE-DUPLICATION.
The same non-shooting-defensive-foul charge-and-fork — charge `state.Defense`, read the bonus,
fork below/in-bonus — was copied into five rolls as each was built ("copied, not reinvented," to
avoid premature abstraction). With five live copies the shape is proven and stable, so it earns
exactly one home. Pure refactor: identical behavior, one definition. Byte-for-byte-identical
output at every one of the five callers was the load-bearing requirement, and the five existing
fork checks are its correctness proof.

**Confirmation mode, not rediscovery (CONVENTIONS §0/§6c).** The prompt carried a verified
reconnaissance map; the pull confirmed it against the tree rather than re-deriving it. The two
audited exceptions both held: (1) Roll D has NO `ResolveFoulOnDefense` helper — its fork is INLINE
in `Execute`, so #4 deleted FOUR helpers (I/J/K/M) and replaced ONE inline fork (D); (2) Roll D is
the only caller that carries a `Flavor` payload, and its below-bonus kind is `ResumeInbound` where
the other four use `ResolveSidelineInbound`. One clarification surfaced on pull, byte-identical not
a divergence: all five already set `Bonus = bonus` on BOTH arms (below-bonus `bonus` is literally
`None`), so the shared node always sets `Bonus = bonus` and reproduces every caller exactly.

**The shared node.** `Core/DefensiveFoulCharge.Resolve(state, game, belowBonusKind, flavor = null)`
— cross-roll infrastructure that reads `GameState.Fouls` and returns a `Continue`, sitting beside
`FoulTracker` and `JumpBall` (the `Core/` static-`Resolve` precedent). It charges the foul, reads
the bonus, and forks: in bonus → `ResolveFreeThrows` (identical for all five); below bonus → the
CALLER-SUPPLIED kind. The optional `flavor` is stamped only when supplied (Roll D passes its rolled
flavor; I/J/K/M pass nothing, so `Flavor = null` ≡ unset). Two things stay caller-owned on purpose
— the below-bonus kind and the flavor — because the five feeders genuinely differ on them; folding
either in would be a behavior change wearing a refactor costume.

**The two caller-owned knobs map to a real basketball distinction (Emmett's framing).** A foul that
came in through Roll A (a pre-shot entry/halfcourt foul) below the bonus just re-runs the inbound
(`ResumeInbound`) — the "ball already came in, fouled back near the other team's goal, repeat the
inbound" case. A foul during live action (rebound scrum, transition push, FT-board scramble) below
the bonus goes to a sideline throw-in (`ResolveSidelineInbound`) — the "frontcourt foul, throw-in
near the basket they're attacking, different weights" case. The role-based engine expresses that
backcourt/frontcourt difference as WHICH inbound, not as a court coordinate; keeping the below-bonus
kind caller-supplied preserves exactly that split. (Court-side-AWARE inbound weighting, and every
foul eventually earning its own weighted descriptor set, are logged future work — this node is the
home both plug into, unchanged when they land.)

**Re-point and delete.** All five callers now make a single `DefensiveFoulCharge.Resolve(...)`
call: I/J/K/M pass `ResolveSidelineInbound` (no flavor); Roll D keeps its flavor roll and tail-calls
with `ResumeInbound` + the flavor. The four private `ResolveFoulOnDefense` helpers (I/J/K/M) are
deleted; Roll D's inline charge-and-fork is replaced by the tail call. `ResolveFoulOnDefense` now
appears nowhere in engine or harness (§2b sweep clean).

**Validation (reasoned + Monte-Carlo-traced, pending Emmett's harness run).** The five existing
fork checks — `RollDFlavorBatchCheck`, `RollDBonusRoutingCheck`, `RollIBonusForkCheck`,
`RollJBonusForkCheck`, `RollKBonusForkCheck`, and Roll M's loose-ball fork — are unchanged and ARE
the refactor's proof: identical routing through the new node = success. A new direct unit check,
`DefensiveFoulChargeCheck`, drives the node across the foul thresholds with BOTH below-bonus kinds
and with/without a flavor, asserting the charge lands on the defense only, the below/in-bonus split,
the Bonus payload on both arms, and the flavor pass-through. A Python trace confirmed byte-for-byte
identical `(kind, bonus, flavor)` for all five callers across the full climb (fouls 1–12, 7/10
thresholds). Per §2a the node is now the SINGLE place fouls cross the bonus, so the Governor loop's
accumulation check is the end-to-end proof (counts climb monotonically, bonus stays crossed,
`unrouted == 0`).

**What #4 closes.** The charge-and-fork has one definition and one place to change when the
free-throw / inbound rules evolve. The arc moves to #5a (seat ALL turnover and violation types in
Roll C, context-gated and DORMANT, validated in isolation) — drafted as a separate audited pass per
CONVENTIONS §6, not as the tail of this build.

**Carried-forward deferrals (unchanged, not this session).** `TransitionSource.Block` push tempo
and a distinct Block `OffensiveReboundSource` into Roll K; `OutOfBoundsOffDefense` own-side inbound
modifiers (land with the Roll A reshape, #5b); steal/foul attribution (the deferred attribution
layer); Roll A's own turnovers becoming steals (comes free at #5b); court-side-aware inbound
weighting and per-foul-type weighted descriptors (new this session, parked for the inbound/foul-roll
buildout).

## Session 22 — Contextification #3 (Steal Feeder): live turnovers enter Roll J as a `Steal` source (2026-06-13)

**The premise this session opens.** Third of the five-session contextification arc. Three
live-turnover arms — Roll C's `BadPassIntercepted` and `LostBallLiveBall`, and Roll K's
`LiveBallTurnover` — already emitted a transition consequence, but a PLACEHOLDER one with no
context ticket (`TransitionTo(defense)`), so the resolver temp-routed the spawned possession
through Roll A (a halfcourt start) instead of Roll J (live transition). This was pre-staged on
purpose for exactly this session. #3 turns that staged routing on and gives the steal its own
Roll J pie. A live theft is the best fast-break trigger in basketball — the defender is already
moving the other way with the offense caught upcourt — so the steal pie runs harder than any
other transition source.

**Promote, not add (the up-front scope decision).** All three callers of the placeholder helper
are steals, so the helper itself was promoted: `PossessionConsequence.TransitionTo` →
`TransitionStealTo`, now carrying `TransitionContext.Steal`. No bare null-context transition
helper is kept in the corner — a transition with a null context is no longer produced by
anything, so a retained bare helper would be dead weight, not a retired stub. One helper, three
callers re-pointed.

**The alarm (the second up-front decision).** Once all three live arms carry `Steal`, NO
null-context transition is emitted anywhere, so a `Transition` entry can never legitimately reach
the resolver's legacy (Roll A) branch. Rather than leave that as a silent assumption, the else
now throws if a `Transition` entry arrives without a recognized source — a loud wiring-bug
tripwire. Cheap insurance: if some future change ever breaks the "every transition stamps a
source" guarantee, the harness fails immediately instead of silently halfcourt-routing a steal.

**The Steal source and its pie.** `TransitionSource.Steal` is the third value (parallel to
`Rebound` / `FreeThrowRebound`), with a `TransitionContext.Steal` static. Roll J's generator gains
a Steal branch returning a third weight set; Roll J's five arms and their routing are UNCHANGED —
the Steal pie reweights the same Settle / Push / Turnover / DefensiveFoul / JumpBall arms. The
resolver's transition-entry guard gained `or TransitionSource.Steal`, so a steal-born possession
routes to Roll J on the steal pie via the same `Generate(ctx)` path the other two sources use.

**The pie intent — runs hardest, with the gaps spread wide (Emmett's call).** Steal Push > Rebound
Push > FreeThrowRebound Push, Settle correspondingly lowest for Steal. Emmett widened the
placeholders to ease later calibration:

| Transition source | Settle | **Push** | Turnover | DefFoul | JumpBall |
|---|---|---|---|---|---|
| Steal *(new)* | 0.40 | **0.50** | 0.06 | 0.035 | 0.005 |
| Rebound *(was 0.65/0.25)* | 0.60 | **0.30** | 0.06 | 0.035 | 0.005 |
| FreeThrowRebound *(was 0.78/0.12)* | 0.82 | **0.08** | 0.05 | 0.04 | 0.01 |

All placeholders, all tunable in `config.json` with no engine change. The real speed/athleticism
favoring ("who got the steal") is the deferred attribute seam; Roll J reads no attributes yet.

**Walls that held.** No new live turnover TYPES — Roll C's classification pie and Roll K's
offensive-rebound pie are untouched; #3 changed only the CONSEQUENCE of arms that were already
live. The DEAD turnovers stay dead (they keep `DeadBallTo` → Roll A). `TransitionSource.Block`
was NOT added (deferred again): a block's defensive rebound shares Roll I's single
`DefensiveRebound` arm, so a Block transition context would force Roll I's ROUTING to read the
`ReboundSource` — crossing the generator-eats-source / roll-eats-pie seam #2 just built. Each steal
arm statically IS a steal, so it has no such problem. Rolls A, I, H, M untouched.

**Validation (reasoned + Monte-Carlo-traced, pending Emmett's harness run).** A sandbox Monte
Carlo mirroring the steal pie converged within the 0.005 tolerance on all five arms, and the §2a
bonus crossing fired both branches on the steal `DefensiveFoul` arm (sideline ~6 before the
defense reaches the 7-foul bonus, FT ~3,500 after). New/updated harness checks: a Steal sub-check
added to the Roll J pie-selection check (selection + rates + the Steal > Rebound > FT Push
ordering); a new `RollJStealBatchCheck` proving the steal pie's five arms route as designed and the
`DefensiveFoul` arm crosses the bonus mid-batch (sideline → FT split); the Governor loop now counts
`stealIntoJ` alongside `reboundIntoJ` (steals join rebounds as Roll J feeders — both must be > 0);
and the Roll C and Roll K batch checks now assert the live arms carry the `Steal` context while the
dead arms carry none. The §2b sweep retired every `TransitionTo` reference in code (only frozen
doc history remains, superseded by this entry).

**What #3 closes, and what it opens.** Closes: the last placeholder transition feed — every
live-ball possession start (rebound, free-throw rebound, steal) now carries a real context into
Roll J. Opens nothing new; the next session is #4 (Bonus-fork extract — the charge-and-fork copied
verbatim in Rolls D / I / J / K / M collapsed into one shared node). Still deferred and logged:
`TransitionSource.Block` push tempo, a distinct Block offensive-rebound source on Roll K, steal
ATTRIBUTION (which defender), and Roll A's own turnovers becoming steals (comes free at #5b).


## Session 21 — Contextification #2 (Block Recovery): Roll H `Blocked` enters the rebound machinery via a `Block` source (2026-06-13)

**The premise this session opens.** Second of the five-session contextification arc.
Roll H's `Blocked` arm dead-ended at `BlockRecoveryStub` (the last live stub on the
field-goal side). A blocked shot *is* a loose-ball scramble — the same battle a missed-shot
rebound already is — so it should resolve through the rebound machinery, not its own node.

**The home is Roll I, not Roll M — and it became an arm-add, not a pure reweight.** The
arc's work order said "Roll M's loose-ball machinery," but Roll M is the **free-throw-board**
resolver; a *field-goal* block belongs to the **field-goal-side** loose-ball resolver, which
is **Roll I**. The catch: Roll I only had four arms, Roll M seven. Rather than route a block
through a four-arm pie that can't express a swat going out of bounds or a tie-up, we **grew
Roll I to Roll M's seven-arm shape** — and made those new arms **live for normal misses too**
(your basketball call: caroms off the rim go out of bounds, rebounders fumble the ball out,
tie-ups happen, on every miss, not just blocks). The block is then a **reweight** of those
seven arms. So this session is a small *arm-add* on Roll I plus a *context* on top, not the
pure reweight the one-line plan implied. The two resolvers (I and M) now share one vocabulary.

**Why the OOB pair is distinct from the rebound arms (the whole point).** A ball that caroms
out of bounds off the offense and a clean defensive rebound both hand the defense the ball —
but they start the defense's *next* possession completely differently: the rebound is a
**live push** (transition, its own weights), the OOB is a **dead-ball inbound** under the far
basket (Roll A, its own weights). Folding OOB into `DefensiveRebound` would erase exactly that
distinction. Same on the other side: an offensive rebound is a live putback/reset (Roll K),
while an OOB off the defender is the offense restarting **dead** from the sideline. Neither is
a turnover (no true possession was established) — they only change *how* the possession starts.
That is why they are seven separate arms, each pointing the next possession at the right
starting context, all routing to nodes that already exist (**no new stub opened**).

**The seven arms and where each routes (live-miss and block share routes; only weights differ):**

| Roll I arm | Routes to | Live / dead | Charge? |
|---|---|---|---|
| `DefensiveRebound` | Terminal → transition to defense (`Rebound` context → Roll J) | live | no |
| `OffensiveRebound` | Continue → `ResolveOffensiveRebound` (Roll K) | live | no |
| `LooseBallFoulOnDefense` | Continue → charge defense + bonus fork (sideline / FTs) | — | **yes (1)** |
| `LooseBallFoulOnOffense` | Terminal → `DeadBallTo(defense)` (Roll A) | dead | no |
| `OutOfBoundsOffOffense` *(new)* | Terminal → `DeadBallTo(defense)` (Roll A) | dead | no |
| `OutOfBoundsOffDefense` *(new)* | Continue → `ResolveSidelineInbound` (offense retains) | dead | no |
| `JumpBall` *(new)* | Continue → `ResolveJumpBall` (arrow node) | live | no |

**The ticket: `ReboundSource { LiveBall, Block }`.** A new optional `ReboundSource?` field on
`Continue`, the `Putback`/`OffensiveReboundSource` precedent: stamped by Roll H's `Blocked`
arm, read by **Roll I's generator** to select the pie, never queried back. A **labeled tag,
not a bool**, so a third loose-ball source appends later without a teardown. **Null reads as
`LiveBall`** — every legacy feeder (Roll H's `Miss`, a missed putback re-entering Roll I)
stamps nothing — so the *selection* is byte-for-byte the legacy path. A block reuses the
`LiveBall` offensive-rebound pie (Roll I stamps no source onward) and reuses the `Rebound`
transition context; a distinct block offensive-rebound source and a `Block` transition push
rate are deferred (see below).

**Edge reuse; the old node retired to the corner.** `Blocked` now emits
`Continue(ResolveRebound) { ReboundSource = Block }` on the **existing** `ResolveRebound`
edge — one edge, a payload selects the pie (the #1 `IntoPlayerSelection` precedent).
`ContinuationKind.ResolveBlock`, the resolver's `ResolveBlock` case, and `BlockRecoveryStub`
are **retired and kept in the corner** (dead but present, swept later — same as #1 left
`IntoTransition`). The seven harness Resolver-ctor injections of `new BlockRecoveryStub()`
are therefore untouched.

**Byte-for-byte was deliberately broken — and that is correct.** Because jump-ball and the
OOB pair are now live on *normal misses*, the live-miss outcome rates shift slightly from the
old four-way split. That is the rebound model getting more honest, not a regression. The new
validation is rate-match against the new seven-arm live-miss pie (the four originals keep
their declaration order, so the new arms are appended last and the pre-existing cumulative
ranges are untouched; only the new slivers change the picture).

**Placeholder weights (yours to tune).** Live-miss: DefensiveRebound .66 / OffensiveRebound
.27 / LooseBallFoulOnDefense .02 / LooseBallFoulOnOffense .01 / OutOfBoundsOffOffense .025 /
OutOfBoundsOffDefense .01 / JumpBall .005. Block (deliberately different, per your read):
.50 / .32 / .03 / .02 / .07 / .05 / .01 — more stays with or squirts off the swatting
defense, a **higher offensive-recovery rate than a clean miss** (a blocked player often beats
his man to his own loose ball), a visible jump-ball sliver. Both sum to 1.

**Validation (no SDK in the sandbox — reasoned + Monte-Carlo).** Brace/paren balance clean on
all eight edited files; the retired-string sweep confirms no live route emits `ResolveBlock`
or lands at `STUB:BlockRecovery` (only the corner remains). Three checks carry the proof:
`RollIReboundBatchCheck` (now seven arms; the OOB-off-defense and below-bonus
loose-ball-defense arms both land on a plain sideline inbound and are separated by the **foul
delta** — the foul arm charged 1, the OOB arm 0, which is also the proof the OOB pair charges
nothing); the new `RollIBlockReboundBatchCheck` (the same seven-arm proof on the block pie,
asserting `DefensiveRebound` carries the `Rebound` context *not* `FreeThrowRebound`, and the
offensive board stamps no source); and the new `RollIBlockContextSelectionCheck` (pie-equality
both ways — Block selects the block weights, LiveBall the live weights, the two differ — plus
a real-resolver route proof that a `Blocked` shot reaches a Roll-I destination and **never**
the retired stub, zero unrouted). Both batches cross the bonus mid-run, exercising §2a. A
Python Monte-Carlo of both pies reproduces every rate within the 0.5pp tolerance, all seven
arms, the §2a crossing, and zero bad charges.

**The §2b sweep bit, as warned.** Retiring `BlockRecovery` touched five assertion sites, not
one: the resolved-bucket OR-list in `RollFHandoffCheck`, the required-destination dicts **and**
classifiers in `RollGHandoffCheck` and `RollHHandoffCheck`, and the `continueResults` array in
`RollHHandoffCheck` (where `ShotResult.Blocked` was asserted to ride straight to a fact stub —
no longer true, since a block now flows *through* Roll I like a miss). All five were swept.

**Deferred (noted for later sessions).** The block-specific **transition push rate** (a
`TransitionSource.Block` flavor — a block-and-go runs differently than a board-and-go) goes to
**#3 (steal feeder)**, which also wires `TransitionSource.Steal`. A **distinct block
offensive-rebound source** (a block recovery may putback differently than a clean board) is a
later Roll K context. The **own-side inbound modifiers** for `OutOfBoundsOffDefense` are the
inbound node's job, landing with the **Roll A reshape (#5b)**.

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
