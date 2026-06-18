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
