# Project Charm — Engine Wiring Map

**Snapshot.**
- Repository: `github.com/brothertonhistory-rgb/ProjectCharm`
- Branch: `main`
- **Audited commit: `5050a6d08dd5291c1ea690962e86a203eac8f036`**
- Pulled: `2026-06-25T02:41:10Z` (UTC), via `codeload` zipball

The pin is **verified**, not assumed: `main` HEAD was resolved via `git ls-remote`, and the codeload zipball
*for that exact SHA* was downloaded and recursively diffed against the audited tree — they are byte-identical
(`diff -rq` clean). So the files mapped below are exactly the files at commit `5050a6d…`; no local confirmation
is required. All line numbers are the committed raw-file line numbers at this commit.

**Verification standard.** Every read site **and** every stamped-scalar write site listed as factual was
opened and read this session (CONVENTIONS §2c). Negative findings are phrased as *"no outcome-affecting
reader located in `src/Charm.Engine`"* — scoped and reproducible, not as unbounded "never." Interpretive
phrasing (intent, basketball rationale) is confined to **Notes**, labelled as such; the body states what the
code does. This is a **map only** — no proposed changes; notice-in-passing is quarantined in Part 4.

**Citation convention.** `cs:NNN` is shorthand for the file named in the **same bullet or table row**; where
a row crosses files, the file name is repeated before its line. Helper/derived quantities have their own
definition-site table (Part 2b), so a generator row can cite the helper by name and the table carries the
formula.

---

## At a glance — system families (the 30-second map)

A navigational summary, not a substitute for the detail below. Each row is a subsystem; the full read sites,
directions, and formulas are in Parts 1–3.

| System family | Main authored inputs | Outcome it moves | Where (Roll / helper) |
|---|---|---|---|
| Shot creation / hierarchy | SelfCreation, Close, PostMoves, Outside, Mid, Finishing, HierarchyRank | who shoots / handles (selection share) | Roll E |
| Shot location | the five `*Tendency` fields; zone skill vs. defense | which zone the shot comes from | Roll G (`LocationMultiplier`, `CoachingPull`) |
| Make door | zone skill, defense blend, effective athleticism, gravity/spacing/openness, Screening, Help/OffBall defense, IQ | make% | Roll H (`EffectiveRating` + C1–C8 + IQ) |
| Block / foul door | Height/Wingspan/Vertical (length); FoulDrawing vs. Discipline | block rate / shooting-foul rate | Roll H (`BlockWeight`, `FoulRate`) |
| Pressure / turnovers | BallHandling, Steals, Hustle, athleticism, length | turnover & non-shooting-foul shares | Rolls A / B / F (`*DisruptionShares`) |
| Rebounding | OffensiveRebounding, DefensiveRebounding, Height, Strength, Wingspan, PostDefense (postness), Hustle | offensive vs. defensive board split (+ credit) | Rolls I / M (`OffensiveReboundShare`) + pickers |
| Transition | effective athleticism (+ coach pace) | Push vs. Settle (pace) | Roll J (`MeanEffectiveAthleticism`) |
| Free throws | FreeThrow (make); FoulDrawing/HierarchyRank/BallHandling (who) | FT make% / who shoots | Roll L; `FouledPlayerPicker` |
| Fatigue | Endurance | discounts effective athleticism everywhere physical | `FatigueTracker` ← `Governor` |
| Opening tip | Wingspan | which team starts with the ball | `JumpBall` |
| Attribution (all) | the relevant skill + Hustle + postness/length | which player is credited (not whether it happened) | the `*Picker` family |

---

## How to read this map

- **Source of truth** for the attribute list = the authored fields on `Core/Player.cs`. Every one is
  mapped, plus the derived/aggregate quantities that move a pie.
- **"Read site"** means a place the *engine* consumes the value to move an outcome. Three large
  categories of read are **excluded** as non-outcome and are noted once here so they don't clutter
  each entry:
  - The **clone constructor** `StampPlayerId` (`Program.Harness.Shared.cs:107–148`) copies every field
    — harness plumbing, not a read.
  - The **stress/observation dumps** (`Program.Stress.cs`, `Program.Checks.*`) print or average
    attributes for test output — not engine outcomes.
  - **`Validate()`** (`Player.cs:416–470`) range-checks every field — not a read.
- **The generator/resolver seam holds.** Grep confirms **zero** rated-attribute reads anywhere in
  `Rolls/*.cs` — the resolvers consume pies only; all attribute reads live in `Generators/`, `Core/Matchup.cs`,
  and the `Core/*Picker.cs` attribution helpers. (Verified this session.)
- **Two read layers.** Many attributes reach an outcome **indirectly** through a `Matchup.*` helper
  (e.g. Roll F passes two `Player` objects into `Matchup.DisruptionShares`, which reads `Steals`/`BallHandling`
  inside) or through `FatigueTracker.EffectiveAthleticism` (authored `Athleticism` × fatigue discount).
  Indirect reads are labelled as such with both the helper site and the calling generator.
- **Direction:** *raises* / *lowers* / *weights* the named outcome.
- **Outcome vocabulary:** make% · block rate · shooting-foul rate · turnover share · non-shooting-foul share ·
  selection share (who shoots/handles) · denial (selection suppression) · rebound split (off vs def board) ·
  attribution (which player is credited — no effect on *whether* the event happens) · transition/pace ·
  fatigue · tip winner.

---

# Part 1 — Attribute-first map

## Identity / infrastructure

### `Name` — `Player.cs:52`
- **Kind:** identity · **Status:** no outcome-affecting reader located in `src/Charm.Engine`.
- Read only by the harness for display/validation strings (e.g. `Program.Checks.GameLifecycle.cs:443`).
- **Notes:** the class doc explicitly says it "is not an engine input to any roll" — confirmed.

### `PlayerId` — `Player.cs:59`
- **Kind:** identity key · **Status:** live as a *key*, not as a rated lever.
- **Read sites:**
  - `FatigueTracker.cs:86,108,113,115,155,156,160` — the fatigue meter dictionary is keyed on `PlayerId`
    (`_level[p.PlayerId]`). Load-bearing for fatigue bookkeeping; no pie reads it directly.
  - Harness attribution arrays (`Program.Harness.Shared.cs:197…272`) index per-player stat counters by
    `PlayerId − 1`; validation enforces 1–10 (`Program.Observation.cs:411–414`).
- **Notes:** moves no outcome by itself; it is the handle the fatigue system and the stat layer count against.

### `HierarchyRank` — `Player.cs:74` (default 5)
- **Kind:** individual (usage priority) · **Status:** live.
- **Read sites:**
  - `RollEGenerator.cs:161–165` — selection share. Each player's raw usage score is multiplied by
    `(HierarchyRank / 5)^exponent`, where the exponent is derived from the offensive coach's
    `HeliocentricBias`. **Raises** the shot/usage share of high-rank players; rank 5 = weight 1.0 at any
    bias (the regression anchor). Validates `[1,10]` here.
  - `FouledPlayerPicker.cs:105` — attribution. `nUsage = (HierarchyRank − 1)/9` is the planned-usage
    channel deciding **who draws a foul** (and therefore shoots the FTs). **Weights** toward primary options.

---

## Offense — scoring skill (individual)

These five map location→skill in one place: `Matchup.OffenseRating` (`Matchup.cs:49–57`). That single read
feeds three doors — the make door (`EffectiveRating`, `cs:157`), the block door (`BlockWeight` skill gap,
`cs:221`), and the shot-location door (`LocationMultiplier` capability, `cs:382`). So each scoring skill
**raises make%, lowers the defender's block edge, and pulls shot selection toward its own zone**.

### `Close` — `Player.cs:83`  (Short-zone conversion)
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:54` — `OffenseRating(Short)`. Feeds make/block/selection at the **Short** zone (raises make%,
    weights selection toward Short).
  - `RollEGenerator.cs:131` — selection score term `(Close + PostMoves)/2 × 0.30`. **Raises** who-shoots share.
  - `Player.cs:356` — input to derived `GravityContribution` (0.25 weight). (See Part 2.)

### `Mid` — `Player.cs:85`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:53` — `OffenseRating(Mid)`. Make/block/selection at **Mid**.
  - `RollEGenerator.cs:132` — selection score `(Outside + Mid + Finishing)/3 × 0.35`. **Raises** who-shoots share.
  - `Player.cs:356` (GravityContribution, 0.10) and `Player.cs:401` (SpacingContribution base, 0.25 weight).

### `Outside` — `Player.cs:92`  (threes + long twos)
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:51,52` — `OffenseRating(Three)` and `OffenseRating(Long)`. Make/block/selection at the two
    perimeter zones.
  - `RollEGenerator.cs:132` — selection score (with Mid/Finishing). **Raises** who-shoots share.
  - `Player.cs:356` (GravityContribution, 0.05) and `Player.cs:401–402` (SpacingContribution — base 0.75 weight
    **and** the `OffBallMovement` compound multiplier gate). Outside is the primary spacing driver.

### `Finishing` — `Player.cs:97`  (rim conversion)
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:55` — `OffenseRating(Rim)`. Make/block/selection at **Rim**.
  - `RollEGenerator.cs:132` — selection score. **Raises** who-shoots share.
  - `RollKGenerator.cs:76` — putback offensive composite (`PutbackOffFinishingWeight × Finishing`).
    **Raises** the chance an offensive rebound converts straight to a putback.
  - `Player.cs:356` — GravityContribution (0.35 — the largest of its five coefficients).

### `FreeThrow` — `Player.cs:101`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `RollLGenerator.cs:87` — `makeProbability = FreeThrow / 100.0`, clamped `[0,1]`. **Direct linear 1:1**,
    no logistic, no matchup, no home/road penalty (the road penalty is explicitly dormant, `cs:86`).
    The shooter is whoever `FouledPlayerPicker` selected. **Raises** FT make%.

### `FoulDrawing` — `Player.cs:116`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:284` — shooting-foul door. `OffenseFoulWeight × (FoulDrawing − midpoint)` is the
    offense-dominant term in `FoulRate`. **Raises** the shooting-foul rate on a contested shot (Roll H).
  - `FouledPlayerPicker.cs:104` — attribution. `nFoulDrawing = FoulDrawing/99` is the contact channel for
    **who draws the foul**. **Weights** the fouled-player draw.
- **Notes:** asymmetric by design — low FoulDrawing is "no opportunity," not negative skill; the floor lives
  in `MatchupConfig.FoulFloor`, not in this attribute.

---

## Offense — shot tendencies (individual, the five `*Tendency` fields)

`RimTendency` `Player.cs:134` · `ShortTendency` `Player.cs:139` · `MidTendency` `Player.cs:143` ·
`LongTendency` `Player.cs:147` · `ThreeTendency` `Player.cs:154`.
- **Kind:** individual · **Status:** live. Independent of the matching *skill* (a great shooter can have a low
  tendency).
- **Read sites (all five together):**
  - `CoachingPull.cs:74–78` — the authored tendencies, nudged by the offensive coach's `ShotSelectionBias`
    (inside system pulls Rim/Short up; outside system pulls Long/Three up; Mid neutral). The result is the
    baseline of Roll G's shot-location pie → **sets selection share** (which zone the shot comes from). Called
    from `RollGGenerator.cs:116`.
  - `RollGGenerator.cs:217–229` — the *authored* (un-nudged) tendencies again, used to find the player's
    dominant zone and how much he *can* diversify under usage pressure (the diet shift). **Weights** how a
    forced-usage player's extra volume spills to other zones.
- **Notes:** the per-zone bend that sits between baseline and final pie is `Matchup.LocationMultiplier`
  (`RollGGenerator.cs:138–142`), which reads `OffenseRating`/`DefenseRating`, not the tendencies.

---

## Offense — handling, passing, creation (individual)

### `BallHandling` — `Player.cs:158`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `RollAGenerator.cs:163` — slot-weighted offense aggregate (`offHandling`) → `Matchup.EntryDisruptionShares`.
    **Lowers** the full-court-press turnover share (Roll A, Standard press).
  - `RollBGenerator.cs:120` — slot-weighted aggregate → `Matchup.TeamDisruptionShares`. **Lowers** the
    dead-ball/half-court entry turnover share (Roll B).
  - `Matchup.cs:686` (inside `DisruptionShares`) — the handler's `BallHandling` vs the defender's `Steals` is
    the live-pressure matchup. Called from `RollFGenerator`. **Lowers** the live-ball turnover share (Roll F).
  - `TurnoverCommitterPicker.cs:116` — attribution. `weights[i] = max(1, BallHandling × perimeterMult)`.
    **Raises** the chance this player is named the committer. ⚠ Counterintuitive: here `BallHandling` is a
    *ball-dominance / usage proxy* (the guard with the ball commits most live-ball TOs in raw count), **not**
    a blame metric.
  - `FouledPlayerPicker.cs:106` — `nBallHandling = BallHandling/99`, the handler channel for who draws a foul.

### `Passing` — `Player.cs:162`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `AttentionGenerator.cs:206` — collected into the rank-weighted, bottom-heavy passing compound
    (Phase 47; heaviest weight on the *weakest* passer). Feeds `TeamConversionQuality`, which Roll H reads at
    C4. **Raises** the passing-converter make% bonus (a team of good passers compounds). Indirect: stamped → Roll H.
  - `AssistPicker.cs:177` — attribution. `AssistPassingWeight × Passing`. **Weights** who is credited with the assist.

### `Playmaking` — `Player.cs:166`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `AttentionGenerator.cs:197` — `effPlaymaking = (Playmaking/100) × iqFactor`, summed top-down with geometric
    decay (an *unlocker* with diminishing returns — "one ball"). Feeds `TeamConversionQuality` → Roll H C4.
    **Raises** the passing-converter make% bonus (indirect, stamped → Roll H).
  - `AssistPicker.cs:178` — attribution. `AssistPlaymakingWeight × Playmaking`. **Weights** assist credit.

### `SelfCreation` — `Player.cs:171`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `RollEGenerator.cs:130` — selection score, the largest single term (`SelfCreation × 0.35`). **Raises**
    who-shoots share.
  - `Player.cs:352` — input to `GravityContribution`'s perimeter-Access term (with FirstStep, Speed).

### `PostMoves` — `Player.cs:176`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `RollEGenerator.cs:131` — selection score `(Close + PostMoves)/2 × 0.30`. **Raises** who-shoots share.
  - `RollEGenerator.cs:322` — post-denial. `(Strength + PostMoves)/2` is the offense side of the post denial
    gap vs the defender's `PostDefense`. **Raises** the slot's resistance to denial (i.e. protects selection share).
  - `Player.cs:353` — input to `GravityContribution`'s post-Access term (with Strength).

### `OffBallMovement` — `Player.cs:180`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `RollEGenerator.cs:320` — perimeter denial. `perimeterGap = defender.OffBallDefense − OffBallMovement`.
    Higher OffBallMovement **lowers** the gap → **protects** this slot's selection share against denial.
  - `Player.cs:402` — the compound multiplier on `SpacingContribution` (only matters when `Outside` is present).

### `Screening` — `Player.cs:184`
- **Kind:** individual (aggregated) · **Status:** live.
- **Read sites:**
  - `RollHGenerator.cs:264` — C5.5. All five offensive players' `Screening/100` aggregate on an accelerating
    curve (fixed denominator 5). **Raises** make% on **all five zones** (Phase 44 removed the interior gate),
    half-court only, bonus-only. One elite screener is a sliver; five compound.

### `OffensiveRebounding` — `Player.cs:188`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:541` (inside `OffensiveReboundShare`) — the offense's positional-weighted rebounding mean.
    Called by **Roll I** (`RollIGenerator.cs:163`, missed-FG board) and **Roll M** (missed-FT board).
    **Raises** the offensive-rebound share of the Def+Off mass. A shooter nerf (`cs:559`) discounts the
    shooter on Three/Long/Mid.
  - `OffensiveRebounderPicker.cs:140` — attribution. `max(1, OffensiveRebounding × posWeight × wingspanMult ×
    hustleTilt × shooterNerf)`. **Weights** which offensive player is credited with the board.

---

## Defense (individual)

### `PerimeterDefense` — `Player.cs:196`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:69` — `DefenseRating` perimeter weight. Feeds make/block/selection at all zones (the blend
    slides perimeter→interior). **Lowers** shooter make% / **raises** block edge where the zone weights it.
  - `Matchup.cs:931` (inside `BlockerWeight`) — block attribution, leads at Three/Long. **Weights** which
    defender gets the block.

### `PostDefense` — `Player.cs:200`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:70` — `DefenseRating` post weight (make/block/selection).
  - `Matchup.cs:456` (inside `Postness`) — positional composite (with Height, Strength). Postness then drives
    rebound positional weighting, the Roll E denial channel split, and the turnover pickers (see those entries).
  - `Matchup.cs:932` (inside `BlockerWeight`) — block attribution.
  - `RollEGenerator.cs:321` — post denial. Defender's `PostDefense` vs offense `(Strength+PostMoves)/2`.
    **Lowers** the matched offensive slot's selection share (post denial).

### `RimProtection` — `Player.cs:205`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:71` — `DefenseRating` rim weight (make/block/selection).
  - `Matchup.cs:930` (inside `BlockerWeight`) — block attribution, dominant at Rim/Short.
  - `RollKGenerator.cs:92` — putback defensive composite (`PutbackDefRimProtectionWeight × RimProtection`,
    self-weighted so elite protectors dominate non-linearly). **Lowers** the chance an offensive board converts
    to a putback.

### `DefensiveRebounding` — `Player.cs:209`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:546` (inside `OffensiveReboundShare`) — the defense's positional-weighted rebounding mean.
    Called by Roll I and Roll M. **Lowers** the offensive-rebound share (i.e. **raises** the defensive board).
  - `DefensiveRebounderPicker.cs:117` — attribution. `max(1, DefensiveRebounding × posWeight × wingspanMult ×
    hustleTilt)`. **Weights** which defender secures the board.

### `Steals` — `Player.cs:213`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `RollAGenerator.cs:164` — slot-weighted defense aggregate (`defStealers`) → `EntryDisruptionShares`.
    **Raises** the full-court-press turnover share (Roll A).
  - `RollBGenerator.cs:121` — slot-weighted aggregate → `TeamDisruptionShares`. **Raises** the dead-ball entry
    turnover share (Roll B).
  - `Matchup.cs:686` (inside `DisruptionShares`) — defender `Steals` vs handler `BallHandling` on the live path
    (Roll F). **Raises** the live-ball turnover share.
  - `StealerPicker.cs:118` — attribution. `max(1, Steals × perimeterMult × hustleTilt)`, where `perimeterMult`
    is the postness-based guard-favoring multiplier (`Matchup.Postness` → Height/PostDefense/Strength, `cs:110–111`)
    and `hustleTilt` reads `Hustle` (`cs:115–116`). **Weights** which defender is credited with the live-ball steal.

### `HelpDefense` — `Player.cs:220`
- **Kind:** individual (aggregated) · **Status:** live.
- **Read sites:**
  - `RollEGenerator.cs:367` — interior compression. All five defenders' `HelpDefense/100` aggregate (fixed
    denom 5) compresses **above-equal-share** offensive selection slots (models the lane collapsing).
    **Lowers** the selection share of focal interior options.
  - `RollHGenerator.cs:311` — C6. The four *off-ball* defenders' `HelpDefense/100` aggregate (accelerating
    curve, fixed denom 4, matched defender excluded). **Lowers** make% at **Rim/Short fully, Mid partially**
    (`HelpDefenseMidMultiplier`), zero on Long/Three. Half-court only.

### `OffBallDefense` — `Player.cs:227`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `RollEGenerator.cs:320` — per-man perimeter denial vs the matched offensive `OffBallMovement`. **Lowers**
    the matched slot's selection share (perimeter denial). (Phase 46 replaced an older team-wide compression
    with this per-man form.)
  - `RollHGenerator.cs:348` — C7. The four off-ball defenders' `OffBallDefense/100` aggregate (accelerating,
    fixed denom 4, matched defender excluded). **Lowers** make% at **Long/Three fully, Mid partially**
    (`OffBallDefenseMidMultiplier`), zero on Rim/Short. Half-court only. Symmetric counterpart to C6.

---

## Physical (individual)

### `Height` — `Player.cs:235`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:180` (inside `LengthRating`) — block-door length composite (with Wingspan, Vertical) → bends
    block rate (Roll H) and the Roll A entry size gap. **Raises** the defender's block edge.
  - `Matchup.cs:410` (inside `ReboundPhysical`) — team-size composite (with Strength, Wingspan) → the
    team-vs-team size shift of the rebound split (Roll I/M).
  - `Matchup.cs:455` (inside `Postness`) — positional composite (drives rebound weighting, denial split,
    turnover pickers).
  - `Matchup.cs:933` (inside `BlockerWeight`) — block attribution, dominant at Rim/Short.
  - `RollKGenerator.cs:74,93` — putback offense and defense composites. **Weights** putback conversion both ways.

### `Wingspan` — `Player.cs:239`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Matchup.cs:181` (inside `LengthRating`) — block-door length composite. **Raises** block edge.
  - `Matchup.cs:411` (inside `ReboundPhysical`) — team-size rebound shift.
  - `Matchup.cs:934` (inside `BlockerWeight`) — block attribution, meaningful at every zone.
  - `DefensiveRebounderPicker.cs:72` and `OffensiveRebounderPicker.cs:90` (via
    `Matchup.ReboundWingspanMultiplier`, `cs:435–440`) — within-lineup rebound attribution tilt (±10% at the
    default swing). **Weights** which player gets the board.
  - `JumpBall.cs:70–71` — the highest-Wingspan player represents each team on the tip; the wingspan gap shifts
    the win probability (clamped 0.10–0.90, `cs:81–86`). **Raises** the chance of winning the opening/OT tip.

### `Weight` — `Player.cs:242`
- **Kind:** individual · **Status:** **DORMANT** — authored and validated, **zero engine readers** (grep
  confirmed; the only `.Weight` reads in the engine are pie-slice weights, an unrelated member).
- **Future module:** none named. The field's doc cites "contact absorption, post leverage, screening," but each
  of those is currently carried by a *different* attribute (Strength for rebound/post physicality via
  `ReboundPhysical`/`Postness`; Screening as its own field; Length uses Height/Wingspan/Vertical). No consumer
  exists or is named.

### `Strength` — `Player.cs:246`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Player.cs:309` — input to derived `Athleticism` (one of five). (See Part 2.)
  - `Player.cs:353` — input to `GravityContribution`'s post-Access (with PostMoves).
  - `Matchup.cs:409` (inside `ReboundPhysical`) — team-size rebound shift.
  - `Matchup.cs:457` (inside `Postness`) — positional composite.
  - `RollKGenerator.cs:73,94` — putback offense and defense composites.
  - `RollEGenerator.cs:322` — post denial offense side `(Strength + PostMoves)/2`.
  - `TurnoverInteriorPicker.cs:114` — attribution. `max(1, Strength × interiorMult)` selects the committer of
    an **interior** turnover (3-sec violation, offensive goaltending, offensive foul), weighted toward posts.

### `Speed` — `Player.cs:249`
- **Kind:** individual · **Status:** live **only through derived composites** — no direct engine read.
- **Read sites:** `Player.cs:309` (Athleticism, one of five) and `Player.cs:352` (GravityContribution
  perimeter-Access, with FirstStep + SelfCreation). No standalone `.Speed` read anywhere in the engine.

### `Quickness` — `Player.cs:253`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Player.cs:309` — Athleticism.
  - `AttentionGenerator.cs:200` — the perimeter activation route `(Quickness + FirstStep)/2` for the passing
    converter. Feeds `TeamConversionQuality` → Roll H C4. **Raises** the passing-converter make% bonus
    (indirect, stamped → Roll H). Doc flags this route as level-agnostic/flat for now (matchup-relative version
    deferred).

### `FirstStep` — `Player.cs:256`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Player.cs:309` — Athleticism.
  - `Player.cs:352` — GravityContribution perimeter-Access.
  - `AttentionGenerator.cs:200` — perimeter activation route (with Quickness). → Roll H C4 (indirect).

### `Vertical` — `Player.cs:260`
- **Kind:** individual · **Status:** live.
- **Read sites:**
  - `Player.cs:309` — Athleticism.
  - `Matchup.cs:182` (inside `LengthRating`) — block-door length composite. **Raises** block edge.
  - `Matchup.cs:935` (inside `BlockerWeight`) — block attribution.

### `Endurance` — `Player.cs:266`
- **Kind:** individual · **Status:** **live** (⚠ the field doc still says "Dormant-pending-module … no
  generator reads it until the stamina module is built" — that comment is stale; see notice-in-passing).
- **Read sites:**
  - `FatigueTracker.cs:111` — `DrainFactor(Endurance)` scales the per-possession fatigue accrual step. Higher
    Endurance → smaller steps → reaches the cliff later. **Lowers** fatigue accrual.
  - `FatigueTracker.cs:158` — `RecoveryFactor(Endurance)` scales off-floor / halftime recovery. Higher Endurance
    → more recovery. **Raises** recovery.
- **Effect:** via the fatigue meter, Endurance preserves a player's **effective athleticism** (Part 2), which in
  turn touches the make door, denial, transition, entry, and putback — but only when the meter is non-zero. In the
  engine game loop the meter rises once per possession (`Governor.cs:427` calls `Accrue`) and is reset at the half
  (`Governor.cs:481` calls `ApplyHalftimeRecovery`). The per-elapsed-second off-floor `Recover` (`FatigueTracker.cs:129`)
  has **no engine caller** — only the harness exercises it in isolation — so without a substitution path the meter
  effectively rises within a half and clears at the half.

---

## Intangible (individual / modifier)

### `Hustle` — `Player.cs:276`
- **Kind:** modifier (effort family), team-aggregate where it moves an outcome · **Status:** live.
- **Read sites:**
  - `Matchup.cs:106` (inside `TeamMeanHustle`, fixed denom 5) — feeds the team Hustle gap used in:
    `OffensiveReboundShare` (`cs:584`, **raises** off-board share when offense out-hustles, Roll I/M);
    `DisruptionShares`/`TeamDisruptionShares` nudges (**raises** turnover share, and a defense-only foul cost,
    Roll F/B/A); and `RollHGenerator.cs:397` — C8, fast-break only, **lowers** make% on a run-out when the
    defense out-hustles.
  - `DefensiveRebounderPicker.cs:115`, `OffensiveRebounderPicker.cs:138`, `StealerPicker.cs:116` — per-player
    `tanh`-shaped tilt (centered at 50). **Weights** rebound and steal **attribution** toward higher-effort players.
- **Notes:** by design Hustle never produces an outcome alone — it amplifies boards, steals, and disruption.

### `BasketballIQ` — `Player.cs:282`
- **Kind:** modifier (decision family) · **Status:** live.
- **Read sites:**
  - `RollHGenerator.cs:443` — the IQ make-door bonus. `iqProgress = clamp((IQ−50)/49, 0, 1)`;
    `bump = settledMakePct × IqMakeSensitivity × ZoneWeight × iqProgress`. **Raises** make% proportionally —
    a good look gets a real bump, a poor look a rounding error. Zone weights: Three/Long 1.0, Mid 0.7, Short 0.3,
    **Rim 0.0**; **zero at or below IQ 50**. It is the *last* make-door term, on top of every tangible adjustment.
  - `AttentionGenerator.cs:196` — `iqFactor` amplifies `effPlaymaking` in the passing converter. → Roll H C4
    (indirect). **Raises** the converter bonus.
  - `AssistPicker.cs:179` — attribution. `AssistIqWeight × BasketballIQ`. **Weights** assist credit.
- **Notes:** absolute (not relative to the defender), and — given the engine has no separate "openness" stage —
  it is written onto the outcome surface, kept honest by per-zone weights and the proportional (ceiling-relative) form.

### `Discipline` — `Player.cs:287`
- **Kind:** individual (defender modifier) · **Status:** live.
- **Read sites:**
  - `Matchup.cs:285` — shooting-foul door, the defender-light term `− DefenseFoulWeight × (Discipline −
    midpoint)`. Higher Discipline **lowers** the shooting-foul rate the defender concedes (Roll H).
- **Notes:** the only `Discipline` read in the engine. Foul-out is a raw count threshold (`FoulTracker`), not a
  Discipline read — confirmed `FoulTracker.cs` has no attribute reads.

---

# Part 2 — Derived & team-aggregate quantities

### `Athleticism` (derived) — `Player.cs:308`  = mean(Strength, Speed, Quickness, FirstStep, Vertical)
- **Kind:** derived · **Status:** live, almost always **fatigue-discounted** before use.
- **The fatigue wrapper:** `FatigueTracker.EffectiveAthleticism` (`cs:89`) = `Athleticism × (1 − drop ×
  level/Ceiling)`, with a steeper drop on defense than offense. A fresh player returns authored athleticism exactly.
- **Five consumer sites of effective athleticism (the physical axis everywhere):**
  1. `RollHGenerator.cs:108–110` → `Matchup.EffectiveRating` physical shift (`Matchup.cs:162`) — **raises/lowers
     make%** by the athletic gap.
  2. `RollEGenerator.cs:329–330` — denial physical channel (`athGap`). **Weights** selection denial.
  3. `RollJGenerator.cs:186` — `MeanEffectiveAthleticism` per team; gap `(offense − defense)` **raises Push /
     lowers Settle** (transition/pace).
  4. `RollAGenerator.cs:165–166` — slot-weighted entry aggregate → `EntryDisruptionShares` athletic term.
     **Weights** the full-court-press turnover share.
  5. `RollKGenerator.cs:75` — putback offensive composite (`PutbackOffAthleticismWeight`). **Weights** putback conversion.
- **Note on the raw path:** `Matchup.cs:138` (the 4-arg `EffectiveRating` overload) reads raw `Athleticism`, but
  that overload is used by the analytic make-curve sweep / no-defender harness path — the live Roll H uses the
  6-arg fatigue overload above.

### `GravityContribution` (derived) — `Player.cs:348`
- = `0.35×Finishing + 0.25×Close + 0.25×Access + 0.10×Mid + 0.05×Outside`, where `Access` blends a perimeter
  route (FirstStep, SelfCreation, Speed) and a post route (PostMoves, Strength). Clamped `[0,100]`.
- **Kind:** derived (per-player input to a team aggregate) · **Status:** live.
- **Read sites:**
  - `AttentionGenerator.cs:91` — per-player gravity feeding the **team gravity level** (sigmoid-gated top threat)
    and the attention shares.
  - `AttentionGenerator.cs:202` — `postRoute` in the passing-converter activation.
- **Effect:** team gravity → stamped `TeamGravityLevel` → Roll H **C2** (make% imbalance penalty). Also drives
  attention shares → `ShooterAttentionShare` → Roll H **C1/C3** and Roll G diet amplification.

### `SpacingContribution` (derived) — `Player.cs:397`
- = `(0.75×Outside + 0.25×Mid) × (1 + (OffBallMovement/100)×(Outside/100)×0.30)`. Clamped `[0,100]`.
- **Kind:** derived · **Status:** live.
- **Read site:** `AttentionGenerator.cs:92` — per-player spacing feeding the **team spacing field** (mean of the
  four non-primary-gravity players).
- **Effect:** stamped `TeamSpacingLevel` → Roll H **C2**, and combines with gravity into `TeamBaseOpenness` →
  Roll H **C1** and the **C4** opportunity gate.

### Team-aggregate stamped scalars (computed in `AttentionGenerator`, consumed downstream)
These are not authored fields; they are the connective tissue between the offense-skill attributes above and
Roll H's make%. Stamped on `PossessionState` at selection time and read by Roll H (and Roll G):

| Stamped scalar | Built from (cited reads) | Consumed at | Outcome |
|---|---|---|---|
| `TeamGravityLevel` | GravityContribution `AttentionGenerator.cs:91` | Roll H C2 `RollHGenerator.cs:123,153` | make% (imbalance penalty) |
| `TeamSpacingLevel` | SpacingContribution `AttentionGenerator.cs:92` | Roll H C2 `RollHGenerator.cs:122,153` | make% |
| `TeamBaseOpenness` | gravity × spacing `AttentionGenerator.cs:161` | Roll H C1 `RollHGenerator.cs:121,139` + C4 gate `RollHGenerator.cs:228` | make% |
| `TeamConversionQuality` | `AttentionGenerator.cs`: Passing `:206`, Playmaking `:197`, IQ `:196`, Quickness/FirstStep `:200`, Gravity `:202` | Roll H C4 `RollHGenerator.cs:226` | make% (passing converter) |
| `ShooterAttentionShare` | gravity + usage shares `AttentionGenerator.cs:105` | Roll H C1/C3 `RollHGenerator.cs:120` + Roll G diet `RollGGenerator.cs:240` | make% + selection diet |

> `UsagePressure` / `UsageResidualPressure` (read at `RollHGenerator.cs:182–183` for C3 and
> `RollGGenerator.cs:210` for the diet shift) are stamped at selection time and **now traced** (Part 5):
> `UsageResidualPressure` = the Roll G diet-shift residual (`RollG.cs:60`); `UsagePressure` = the Roll E
> per-slot focal-concentration value (`RollE.cs:65`).

> **Write path for the five team scalars above, confirmed end to end:** `AttentionGenerator` returns an
> `AttentionResult` record (`AttentionGenerator.cs:258`, type at `cs:337`); `RollE.cs:66–70` copies its fields
> onto the possession state (`ShooterAttentionShare`, `TeamBaseOpenness`, `TeamGravityLevel`,
> `TeamSpacingLevel`, `TeamConversionQuality`); Roll H / Roll G read them. No inference remains in this chain.

---

# Part 2b — Matchup & disruption helper quantities (first-class)

Charm rarely bends a pie from a raw field directly — it bends from a small set of **named reusable
intermediates**. Part 1 cites these inline; this table gives each its own formula / definition site / consumers
/ outcome, so a reader sees the actual lever. (Athleticism, GravityContribution, SpacingContribution,
EffectiveAthleticism, and the five stamped team scalars are already first-class in Part 2 and not repeated.)

| Quantity (def site) | Built from | Consumers | Pie / term moved | Direction |
|---|---|---|---|---|
| `OffenseRating` `Matchup.cs:49–57` | the shooter's zone skill: Outside (Three/Long), Mid, Close (Short), Finishing (Rim) | `EffectiveRating` `cs:157`, `BlockWeight` `cs:221`, `LocationMultiplier` `cs:382`, Roll H no-defender path `RollHGenerator.cs:107` | make% baseline, block skill gap, shot-location capability | higher = raises make% / weights selection to zone / lowers defender block edge |
| `DefenseRating(zone)` `Matchup.cs:66–72` | PerimeterDefense + PostDefense + RimProtection, zone-blended | `EffectiveRating` `cs:158`, `BlockWeight` `cs:221`, `DefensiveResistance` `cs:331` | make% baseline, block skill gap, zone resistance | higher = lowers shooter make% / raises block edge / raises resistance |
| `EffectiveRating` `Matchup.cs:136–166` | `OffenseRating` baseline + skill-gap shift + **effective-athleticism** physical shift | Roll H make door `RollHGenerator.cs:108` | make% (the value fed to the make curve) | higher = raises make% |
| `LengthRating` `Matchup.cs:179–182` | Height + Wingspan + Vertical | `BlockWeight` `cs:226`, Roll A entry aggregates `RollAGenerator.cs:167–168` | block rate, full-court-press turnover share | defender longer = raises block rate / press TOs |
| `BlockWeight` `Matchup.cs:216–246` | `DefenseRating` − `OffenseRating` (skill) + `LengthRating` gap (length), tanh-bent | Roll H block door `RollHGenerator.cs:454` | block share on the shot pie | defender edge = raises block share |
| `FoulRate` `Matchup.cs:278–300` | FoulDrawing (offense-dominant) − Discipline (defense-light), tanh-bent | Roll H foul door `RollHGenerator.cs:460` | shooting-foul share | shooter edge = raises shooting-foul share |
| `DefensiveResistance` `Matchup.cs:324–350` | top-3 defenders' `DefenseRating`, weighted | `LocationMultiplier` `cs:381` | per-zone shot-location bend | higher = pushes selection out of that zone |
| `LocationMultiplier` `Matchup.cs:376–389` | `OffenseRating` − `DefensiveResistance`, ratio-bounded | Roll G shot pie `RollGGenerator.cs:138–142` | which zone the shot comes from | shooter edge in a zone = raises that zone's selection |
| `ReboundPhysical` `Matchup.cs:408–411` | Strength + Height + Wingspan | `OffensiveReboundShare` size shift `cs:532` | team-vs-team rebound size shift | bigger team = raises its board share |
| `Postness` `Matchup.cs:454–457` | Height + PostDefense + Strength | `OffensiveReboundShare` `cs:541,546`, Roll E denial split `RollEGenerator.cs:312`, the steal/turnover pickers, both rebounder pickers | positional weighting in boards, denial channel split, picker tilts | higher = "more post-like" weighting |
| `PositionalWeight` `Matchup.cs:473–475` | a player's `Postness` vs lineup-mean postness | `OffensiveReboundShare`, rebounder pickers | within-lineup board weighting (≈0.8–1.2) | above-mean post = larger board weight |
| `ReboundWingspanMultiplier` `Matchup.cs:435–440` | player Wingspan vs lineup-mean wingspan | `DefensiveRebounderPicker.cs:68`, `OffensiveRebounderPicker.cs:68` | rebound **attribution** tilt (≈0.9–1.1) | longer arms = larger board credit |
| `TeamMeanHustle` / `HustleGap` `Matchup.cs:100–118` | mean Hustle (fixed denom 5), off−def | `OffensiveReboundShare` `cs:584`, `DisruptionShares`/`TeamDisruptionShares` nudges, Roll H C8 `RollHGenerator.cs:397` | rebound share, turnover/foul share, fast-break make% | out-hustling side gains boards/TOs; defense out-hustle lowers run-out make% |
| `OffensiveReboundShare` `Matchup.cs:516–600` | `ReboundPhysical` size + `Postness`-weighted (OffensiveRebounding vs DefensiveRebounding) skill + Hustle, tanh-bent | Roll I `RollIGenerator.cs:163`, Roll M | offensive vs defensive board split | offense edge = raises offensive board |
| `DisruptionShares` `Matchup.cs:669–721` | handler BallHandling vs defender Steals, pressure-gated, + Hustle nudges | Roll F `RollFGenerator.cs:180` | live-ball turnover & non-shooting-foul shares | defender/pressure/hustle edge = raises TOs/fouls |
| `TeamDisruptionShares` `Matchup.cs:769–810` | team BallHandling vs team Steals aggregates, pressure-gated, + Hustle | Roll B `RollBGenerator.cs` | dead-ball entry turnover & foul shares | same direction, team-level |
| `EntryDisruptionShares` `Matchup.cs:867–914` | team handling/steals + athleticism + length aggregates, Standard press | Roll A `RollAGenerator.cs:185` | full-court-press turnover / def-foul / off-foul shares | defense edge = raises press TOs/fouls |
| `BlockerWeight` `Matchup.cs:929–936` | RimProtection + PerimeterDefense + PostDefense + Height + Wingspan + Vertical, zone-coef | `BlockerPicker.cs:108` | which defender is **credited** with a block | higher = larger block credit |
| `MeanEffectiveAthleticism` `RollJGenerator.cs:175–191` | mean effective athleticism of the active five (one side) | Roll J athletic gap `cs:137–139` | transition Push vs Settle | offense > defense = raises Push (pace) |
| `EffectiveAthleticism` `FatigueTracker.cs:84–90` | authored `Athleticism` × fatigue discount (steeper on defense) | the five sites in Part 2 (make door, denial, transition, entry, putback) | physical axis everywhere | fatigue = lowers effective athleticism |

**Roll E internal intermediates** (not reusable helpers, but they move the selection pie — listed for
completeness): the **per-man denial multiplier** (`RollEGenerator.cs:343–346`, from the OffBallDefense/
OffBallMovement perimeter gap and PostDefense/Strength/PostMoves post gap, athleticism-discounted) **lowers**
a slot's selection share; the **interior HelpDefense compression** (`cs:369–385`, the five-defender HelpDefense
aggregate) **lowers** above-equal-share interior focal points.

---

# Part 3 — Reverse index (Roll/helper → attributes)

A completeness cross-check against Part 1, split into **direct reads** (the generator reads the authored field
itself) and **indirect reads** (the field reaches the outcome only through a `Matchup.*` helper, `FatigueTracker`,
or a stamped scalar). The split is mechanical: it prevents reading a Roll's attribute list as "directly accessed."

- **Roll A** (backcourt entry / full-court press, Standard) → turnover / def-foul / off-foul shares.
  - *Direct:* `BallHandling`, `Steals` (`RollAGenerator.cs:163–164`).
  - *Indirect:* effective `Athleticism` via Fatigue (`RollAGenerator.cs:165–166`); `Height`/`Wingspan`/`Vertical`
    via `LengthRating` (`RollAGenerator.cs:167–168`) → all into `EntryDisruptionShares`.
- **Roll B** (dead-ball / half-court entry under press) → turnover / foul shares.
  - *Direct:* `BallHandling`, `Steals` (`RollBGenerator.cs:120–121`).
  - *Indirect:* `Hustle` via `TeamMeanHustle` → `TeamDisruptionShares`.
- **Roll C**: **no attribute reads** — pure config/routing pie. (⚠ memory's "Steals feeds Roll C's live-strip
  arms" is **not** in the live code; flagged in Part 5.)
- **Roll D**: **no attribute reads** — config/routing pie (`ShotType` null at every call site per its own doc).
- **Roll E** (selection — the heaviest reader) → selection share + denial; also stamps the team scalars.
  - *Direct:* `SelfCreation`, `Close`, `PostMoves`, `Outside`, `Mid`, `Finishing` (usage score);
    `HierarchyRank` (hierarchy weight); `OffBallDefense`, `OffBallMovement`, `PostDefense`, `Strength`,
    `PostMoves` (per-man denial gaps); `HelpDefense` (interior compression).
  - *Indirect:* effective `Athleticism` via Fatigue (denial physical channel); `Postness`
    (→ Height/PostDefense/Strength) via Matchup; the offense-skill set via `AttentionGenerator` → stamped scalars.
- **Roll F** (live-ball halfcourt pressure on the handler) → live turnover / non-shooting-foul shares.
  - *Direct:* none.
  - *Indirect:* `BallHandling`, `Steals` via `Matchup.DisruptionShares`; `Hustle` via `HustleGap` nudges
    (`RollFGenerator.cs:161–182`).
- **Roll G** (shot location) → which zone the shot comes from.
  - *Direct:* the five `*Tendency` fields (in the diet shift, `RollGGenerator.cs:217–229`).
  - *Indirect:* the five `*Tendency` fields again via `CoachingPull` (coach-nudged baseline);
    `OffenseRating`/`DefenseRating` via `LocationMultiplier` (call site `RollGGenerator.cs:138–142`); stamped
    `ShooterAttentionShare`/`UsagePressure`.
- **Roll H** (shot result — make / block / foul) → make% / block rate / shooting-foul rate.
  - *Direct:* `Screening` (C5.5), `HelpDefense` (C6), `OffBallDefense` (C7), `BasketballIQ` (IQ bonus).
  - *Indirect:* `Outside`/`Mid`/`Close`/`Finishing` + `PerimeterDefense`/`PostDefense`/`RimProtection` +
    effective `Athleticism` via `EffectiveRating` (make door); `Height`/`Wingspan`/`Vertical` via `BlockWeight`;
    `FoulDrawing` + `Discipline` via `FoulRate`; `Hustle` via `HustleGap` (C8); all five stamped team scalars
    (C1/C2/C3/C4) from Roll E.
- **Roll I** (missed-FG rebound split) → offensive vs defensive board.
  - *Direct:* none.
  - *Indirect:* `OffensiveRebounding`, `DefensiveRebounding`, `Height`, `Strength`, `Wingspan`,
    `PostDefense` (via `Postness`), `Hustle` — all via `Matchup.OffensiveReboundShare` (call site `RollIGenerator.cs:163`).
- **Roll J** (transition push/settle) → Push vs Settle (pace).
  - *Direct:* none.
  - *Indirect:* effective `Athleticism` (the five physicals) via `MeanEffectiveAthleticism` (`RollJGenerator.cs:186`);
    coach `PaceBias` (not a player attribute).
- **Roll K** (putback after offensive board) → putback-conversion share.
  - *Direct:* `Strength`, `Height`, `Finishing` (offense); `RimProtection`, `Height`, `Strength` (defense).
  - *Indirect:* effective `Athleticism` via Fatigue (offense composite, `RollKGenerator.cs:75`).
- **Roll L** (free throw) → FT make%.
  - *Direct:* `FreeThrow` (`RollLGenerator.cs:87`). *Indirect:* none.
- **Roll M** (missed-FT rebound split) → board split. Same direct/indirect profile as Roll I (all indirect via
  `OffensiveReboundShare`), no shooter nerf.
- **RollOffensiveFoulGenerator**: **no attribute reads** — config/routing pie.

**Attribution pickers (which player is credited — no effect on whether the event happens):**
- `DefenderPicker` — **positional only** (same slot number, defense side); no attribute read.
- `BlockerPicker` — `RimProtection`, `PerimeterDefense`, `PostDefense`, `Height`, `Wingspan`, `Vertical` via
  `BlockerWeight`. → block credit.
- `AssistPicker` — `Passing`, `Playmaking`, `BasketballIQ`. → assist credit.
- `StealerPicker` — `Steals`, postness (Height/PostDefense/Strength, guard-favoring), `Hustle`. → steal credit.
- `DefensiveRebounderPicker` — `DefensiveRebounding`, `Wingspan`, `Hustle`, `Postness`(Height/PostDefense/Strength). → DRB credit.
- `OffensiveRebounderPicker` — `OffensiveRebounding`, `Wingspan`, `Hustle`, `Postness`. → ORB credit.
- `FouledPlayerPicker` — `FoulDrawing`, `HierarchyRank`, `BallHandling`. → who draws a foul (→ shoots FTs).
- `TurnoverCommitterPicker` — `BallHandling` (usage proxy) + postness. → live-TO committer.
- `TurnoverInteriorPicker` — `Strength` + postness. → interior-TO committer.

**Attributes with no outcome reader (dormant or identity-only):** `Weight` (dormant), `Name`,
`PlayerId` (key only). **Attributes live only through composites:** `Speed` (Athleticism + Gravity).

---

# Part 4 — Notice in passing (NOT changes; stale-doc observations)

Logged for a later doc pass, per the map-only discipline. These are docstring/comment staleness only; the
executable code paths are unaffected.

1. `Player.cs:262–266` — `Endurance` doc says "Dormant-pending-module … no generator reads it." It **is** read
   by `FatigueTracker` (drain + recovery). Stale.
2. `Player.cs:26–27` (class summary) — lists "Dormant-pending-module attributes (Endurance, Gravity, Spacing)."
   All three are **live** now (Endurance via `FatigueTracker`; `GravityContribution`/`SpacingContribution` via
   `AttentionGenerator`, Phase 27). Stale.
3. `Player.cs:30–33` — "Phase 1 wall: NO pie wiring. This object is data only. No generator reads it yet."
   Wildly stale — a frozen Phase-1 historical note; nearly every field is now read.
4. `Player.cs:242` — `Weight` doc cites "contact absorption, post leverage, screening," none of which it actually
   feeds (those run through Strength / Screening / Length). It is a genuinely unused field with no named consumer.

---

# Part 5 — Tracing notes (resolved this pass) and open items

**Resolved this pass** (previously inferred, now read directly — no inference remains):

- **`UsageResidualPressure` / `UsagePressure` source.** `UsageResidualPressure` = the Roll G diet-shift
  residual (`RollG.cs:60`, the `requested − absorbed` value from `RollGGenerator.ApplyDietShift`);
  `UsagePressure` = the Roll E per-slot focal-concentration value (`RollE.cs:65` = `pressures[slot−1]`, from the
  above-equal-share logic in `RollEGenerator`). Consumed at Roll H C3 (`RollHGenerator.cs:182–183`) and Roll G's
  diet shift (`RollGGenerator.cs:210`). Driven by the same selection-score fields (SelfCreation/Close/PostMoves/
  Outside/Mid/Finishing/HierarchyRank) that set the concentration, so they reach make% (C3) and the shot diet indirectly.
- **Stamped-scalar write path.** Confirmed end to end: `AttentionGenerator` returns `AttentionResult`
  (`AttentionGenerator.cs:258`, record type at `AttentionGenerator.cs:337`); `RollE.cs:66–70` copies its fields
  onto the possession state; Roll H / Roll G read them. The `PossessionState` field names are the ones listed in
  the Part 2 scalar table.
- **`OffensiveRebounderPicker` postness term.** Read directly in `OffensiveRebounderPicker.cs`: the
  `Matchup.Postness` call at `:30`, `Matchup.PositionalWeight` at `:67`, and `weights[i] = max(1,
  OffensiveRebounding × posWeight × wingspanMult × hustleTilt × shooterNerf)` at `:81`. Parallel to the defensive
  picker, now confirmed rather than inferred.

- **Commit SHA — verified pin (resolved).** Resolved via `git ls-remote`, then *confirmed* by downloading the
  codeload zipball pinned to that exact SHA and recursively diffing it against the audited tree — byte-identical
  (`diff -rq` clean). The audited files are provably the files at `5050a6d08dd5291c1ea690962e86a203eac8f036`; no
  local confirmation is needed.

**Open items** (genuine, not resolvable from source alone):

- **Memory vs. code — Roll C.** Working memory notes "`Steals` feeds Roll C's live-strip arms" and "Roll B's
  strip." Live `RollCGenerator` has **zero** attribute reads (config/routing only — confirmed). Whether that
  wiring is future/aspirational or was retired is the open question; the live tree does not contain it.

*Line numbers are from commit `5050a6d…` recorded at the top. Reasoned + source-read; offered for independent
audit against the same commit.*
