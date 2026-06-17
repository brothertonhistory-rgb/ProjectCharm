# Project Charm — Build Prompt: Usage→Efficiency Curve (v6)
### Roll E volume-pressure → Roll G bounded mix shift → Roll H residual discount

**Follow CONVENTIONS.md for all process.** This document is the *what*; CONVENTIONS is the *how* (repo pull, read-before-write, surgical edits, Monte-Carlo pre-check, delivery shape, docs-after-harness, commit ritual). Everything below assumes those rules hold without restating them.

**Register:** execution session. Design is settled (see "Settled design" below). Do **not** reopen basketball decisions. If the code diverges from a claim in this prompt, **stop and flag** — do not silently adapt.

**Scope wall (one sentence):** Compute each shooter's **volume pressure** (how far his shot share sits above an equal load) at Roll E, carry it on `PossessionState`; let Roll G bend the shot-location mix bounded by the shooter's intrinsic versatility AND the actual matchup-shaped pie, **stamp the load it could not absorb** as a residual; and let Roll H apply a small all-shots volume-tax plus a residual penalty — and **nothing else**.

> **This is v5.** Version history of the *design*, so the build understands what's load-bearing:
> - **v1** (red): defined comfort as the player's *raw attribute share* — the floor pressured the wrong player, and naturally dominant scorers paid nothing.
> - **v2**: comfort = equal share (fixes both); introduced the residual mechanism.
> - **v3** (yellow): tried to make Roll H *recompute* the residual from authored tendencies + pressure. **That is impossible** — the residual depends on (a) Roll G's shift-scale config and (b) the matchup-bent pie, which can clamp the shift below the intrinsic amount. Roll H cannot reconstruct a number that depends on the defense's transformation.
> - **v4**: Roll G stamps the actual residual; Roll H reads it. Eliminated the recompute, shared config, and Matchup helper.
> - **v5**: Two further fixes — derived interfaces for the Resolver, zero-destination guard for Roll G.
> - **v6** (this): **Stubs must also implement the richer interfaces.** The harness has 20 Resolver construction sites; 19 pass `new RollEStubPieGenerator(...)` and `new RollGStubPieGenerator(...)`. Widening the Resolver's constructor to the derived interface types without updating the stubs breaks 19 of 20 build checks. Fix: both primary stubs implement `IRollEGenerationProvider` / `IRollGGenerationProvider`, returning their existing pie with zero pressures/residual. Also: `RollEGeneration` and `RollGGeneration` records must be public top-level types, not nested inside the concrete generators. If coming from an earlier version, read **§3F** first.

---

## 1. What this build is, in plain terms

Session 51 made Roll E pick *who* shoots by attributes — an alpha gets ~34% of attempts, a Rodman ~9%. But picking the shooter changed nothing in the box score: every player converts at the same rate no matter how much load he carries. **This build makes the load matter.**

The basketball, exactly as Emmett framed it:
- **More shots than an equal share = harder shots.** A player carrying above an even load gets forced into spots he'd rather not be in, and his efficiency drops.
- **Versatility decides how much it hurts.** A player who can score from many zones (Jordan: spread tendencies) absorbs the extra load by *expanding his shot diet* — efficiency barely moves. A one-zone specialist (Shaq: all rim; Ray Allen: all three) **can't** expand — nowhere to shift — so the load lands as lower efficiency on the shots he was already taking.
- **If a guy can't do a thing, no pressure makes him do it.** Shaq does not start taking eighteen-footers under load; he just gets less efficient at the rim. Ray Allen, who *can* put it on the floor, gets forced into mid-range and drives he'd rather not take.

**The single success test: a forced specialist visibly loses efficiency; a forced versatile player barely does — and the specialist's loss comes from the load he couldn't shift away, not from a tuned dial.**

### The mechanism in one line
**Pressure asks for a shot-diet expansion. Roll G grants as much as the player's versatility AND the actual matchup pie allow. Whatever it can't grant — the residual — Roll G stamps on the possession, and Roll H turns into an efficiency penalty.** Versatility is the valve; the matchup is the second valve (a defense that disrupts the player's concentration leaves more load stuck).

### Layer discipline
This is **Layer 1** of three. **Volume** is the only signal this build models. Deferred layers stack later:
- **Gravity / attention (Layer 2, DEFERRED):** how hard the defense keys on a player — distinct from how much he shoots (a high-volume player can draw ordinary attention; a low-volume elite shooter can be face-guarded). A hounded *low-volume* shooter loses efficiency; an ignored *low-volume* shooter gains it (the below-equal-share lift). None of this is in this build; at low volume, pressure is zero and efficiency does not move. When it lands it contributes its own **named component** to the same downstream difficulty calculation — NOT presumed merged into `UsagePressure`.
- **Spacing (Layer 3, DEFERRED):** teammate shooting opening/closing the shooter's lanes by zone.

Because gravity/spacing compound on top, **magnitudes here are deliberately modest.** Build the *pipe* — a difficulty calculation this build feeds with a volume component — not just this build's water through it.

---

## 2. Settled design (DECIDED — do not relitigate)

### 2a. Comfort = an equal share. Pressure = volume above it.
For five on-court players, **comfort = 0.20** (`1.0 / lineupSize`). The neutral anchor: at an equal share nothing unusual is happening — the same way (later) equal defensive attention means pure one-on-one with no help coming.

**`pressure = max(0, finalShare − comfortShare)`**, where `comfortShare = 1.0 / populatedCount` and `finalShare` is the slot's Session-51 post-floor/rail pie weight. Per populated slot, every possession, **no state carried**; when the ball flips teams nothing persists.

Consequences that MUST hold (verify each in the Monte Carlo):
- **Five equal players → each at 0.20 → zero pressure for all.** Clean regression; nothing moves.
- **A naturally dominant scorer pays a cost.** A star whose attributes command 0.40 is at `0.40 − 0.20 = 0.20` pressure — real load, real versatility-scaled cost. (v1 bug fixed.)
- **The floor does NOT create star pressure.** Flooring a weak teammate takes mass *off* the others (the alpha's share goes slightly *down*). The alpha's pressure comes from his *absolute* share being above 0.20, which dwarfs that tiny reduction. The floored teammate sits *below* 0.20 → pressure zero (under-worked, not over-worked). (v1 sign bug fixed.)
- **`rawShare` plays no role in this build's pressure.** (Still computed inside Roll E for the pie; just not the comfort anchor. The "coach force-feeds above natural allocation" `finalShare − rawShare` term is a separate, later coaching-layer term.)

### 2b. Below comfort = flat, no effect (this build)
`finalShare < comfortShare` → pressure 0 → no shift, no efficiency change. The "ignored low-volume shooter gets easy looks" lift is the **attention/gravity** axis (Layer 2), deferred. Do NOT model a below-comfort bonus.

### 2c. Versatility is intrinsic; absorption is bounded twice
Versatility = the **spread of the shooter's five authored zone tendencies** (`RimTendency`, `ShortTendency`, `MidTendency`, `LongTendency`, `ThreeTendency`). NO new "Versatility" attribute.

Under pressure the shooter's location pie (already matchup-bent by Phase 9) is pushed **away from its dominant zone toward the others**. How much actually moves is bounded by **two** things:
1. **Intrinsic capacity** — the shooter's authored non-dominant tendency mass (how much he *can* diversify, a property of the player).
2. **The actual bent pie** — you cannot move more mass off a zone than that zone currently holds (a defense that has flattened/suppressed his concentration leaves less to redistribute).

Whatever the requested shift demanded but these two bounds would not allow is the **residual** — the load that stayed stuck. (v3 tried to derive the residual from intrinsic capacity alone so Roll H could recompute it; that fails because bound #2 depends on the bent pie. v4 has Roll G compute the *actual* residual and carry it — §2d, §3.)

### 2d. The residual is computed by Roll G and CARRIED to Roll H
```
requestedShift   = f(pressure)                                  // diet expansion the load demands
absorbedShift    = min(requestedShift,                          // bound 1: intrinsic capacity
                       intrinsicCapacity,
                       availableBentDominantMass)                // bound 2: what the bent pie can give up
residualPressure = requestedShift − absorbedShift               // the load that stayed stuck
```
- **Versatile player, ordinary defense:** high intrinsic capacity, ample bent mass → absorbs nearly all → `residualPressure ≈ 0` → tiny efficiency hit (just the volume-tax, §2e).
- **Shaq (one zone):** intrinsic capacity ≈ 0 → absorbs nothing → `residualPressure ≈ requestedShift` → the load lands as a make-rate penalty **on his preferred zone**.
- **Versatile player, disruptive defense:** capacity is high but the bent pie has flattened his concentration → bound #2 bites → more residual than against a weak defense. (This is the matchup-responsiveness §1 flags — adopted deliberately.)

**Roll G stamps `residualPressure` onto `PossessionState` (§3B) at the same moment it stamps `ShotType`.** Roll H reads it. Roll H does NOT recompute it. This makes the residual *exact* to the possession's real shot diet and *observable* in the harness.

### 2e. Roll H applies two things
1. **Volume-tax term (SMALL, ALL shots under pressure).** A modest make-rate reduction scaled by `state.UsagePressure` (the Roll E volume pressure), representing the baseline difficulty of sustaining a larger load. Applied regardless of zone. The term a versatile player feels almost exclusively. (Named "volume-tax," NOT "attention" — this build has no defender-attention calculation; attention is the deferred gravity input, §2g. Keeping the names distinct prevents a collision when that layer lands.)
2. **Residual term (LARGE).** A make-rate reduction scaled by `state.UsageResidualPressure` (the Roll G stamped remainder), applied to whatever shot is actually taken — **including the preferred zone** (Shaq's penalty lands on rim). The term a specialist feels.

Both reduce the clean make rate `makePct` before the pie is built; `BuildRealPie` then carves block/foul and fills the miss slices as today, so a lower `makePct` correctly raises misses.

### 2f. Magnitude targets (for the Monte Carlo)
Athletically-neutral shooters (so existing Roll H matchup math contributes nothing), at the rail (`finalShare ≈ 0.52` → pressure ≈ `0.32`):
- **Specialist** (one dominant tendency, four near-zero): efficiency drops **~10–15 pts**, almost all from the **residual** landing on his preferred zone (he can't shift, so the load can't drain), with the small volume-tax underneath.
- **Versatile** (five comparable tendencies, ordinary defense): drops **~2–4 pts**, almost entirely volume-tax (`residualPressure ≈ 0`).
- **Five-equal roster:** zero pressure, zero drift, every make rate identical to today.
- **Specialist drop ≫ versatile drop** is the ordering that must hold.

### 2g. DEFERRED — understood, parked, NOT in this build (do NOT build hooks/stubs)
- **Gravity / attention (Layer 2).** Separate "how hard is the defense keying on him" axis; where the below-comfort lift lives; contributes a named component to the same difficulty calc later, NOT merged into `UsagePressure`. No teammate-attribute read anywhere in this build.
- **Spacing (Layer 3).**
- **Coach concentrate-vs-spread.** A `finalShare − rawShare` term added in the coaching layer; this build is the attribute-only baseline it bends. No coach hooks.
- **Floor/rail YIELD at extremes (Wemby-in-Class-2B).** Needs a global joint-feasibility pass and cannot occur under D1 distributions. **Leave Session-51's floor/rail exactly as they are. No yield thresholds.** Revisit when sub-D1 levels are built.
- **Transition pressure.** On a break, Roll E returns the transition pie and Roll G returns the flat rim-heavy pie; pressure does not apply. See §3E.

---

## 3. The architecture — VERIFY every claim against the pulled tree before writing

> All line numbers are from the audited pull and **must be re-confirmed** against the freshly pulled source. Pointers, not gospel.

### A. Two stamping seams, mirror each other

Neither generator interface returns the scalar it computes (`IRollEPieGenerator.Generate` → `Pie<SelectionOutcome>`; `IRollGPieGenerator.Generate` → `Pie<ShotLocation>`). Both the volume pressure (Roll E) and the residual (Roll G) are properties of the selected shot, so both land on `PossessionState` via the roll's `Execute`, exactly how `SelectedSlot` and `ShotType` already do.

**Roll E seam (volume pressure).** Confirmed Session-51 structure: `RollEGenerator.Generate` builds `rawScores → expScores → shares` (raw, pre-floor/rail) → `ApplyFloorAndRail(...)` → final shares → pie. One pass already yields raw AND final. Collapse to one concrete method (avoids the v1 double-compute / paired-method fragility):
```
public readonly record struct RollEGeneration(
    Pie<SelectionOutcome> Pie,
    double[] FinalShares,
    double[] Pressures);   // Pressures[i] = max(0, FinalShares[i] − 1.0/populatedCount); 0.0 for null/empty/FastBreak slots
public RollEGeneration GenerateWithPressure(PossessionState state) { … }   // the real one-pass computation
public Pie<SelectionOutcome> Generate(PossessionState state) => GenerateWithPressure(state).Pie;   // interface impl
```
`RollE.Execute` rolls the pie, gets `slotNumber = (int)outcome + 1`, and stamps both facts on one `with`:
```
RollResult Execute(PossessionState state, Pie<SelectionOutcome> pie, double[] pressures, GameState game, IRng rng)
// …
var selectedState = state with { SelectedSlot = slot, UsagePressure = pressures[slotNumber - 1] };
```
Confirm the current `RollE.Execute(state, pie, game, rng)` signature; update it + both resolver call sites (break path ~line 412, halfcourt ~line 454) to call `GenerateWithPressure` and pass `.Pie` + `.Pressures`. Roll E stays ignorant of generator types (it gets a `double[]`, not the generator).

**Roll G seam (residual) — NEW, mirrors the above.** Confirm `RollG.Execute` currently `(state, pie, rng)` and stamps `ShotType`. Add a concrete method on the generator returning the residual alongside the pie:
```
public readonly record struct RollGGeneration(
    Pie<ShotLocation> Pie,
    double ResidualPressure);   // 0.0 when no pressure, fully absorbed, or FastBreak
public RollGGeneration GenerateWithResidual(PossessionState state) { … }
public Pie<ShotLocation> Generate(PossessionState state) => GenerateWithResidual(state).Pie;   // interface impl
```
`RollG.Execute` takes the residual and stamps it with `ShotType`:
```
RollResult Execute(PossessionState state, Pie<ShotLocation> pie, double residualPressure, IRng rng)
// …
var locatedState = state with { ShotType = zone, UsageResidualPressure = residualPressure };
```
Update the resolver's Roll G call site (~line 565) to call `GenerateWithResidual` and pass `.Pie` + `.ResidualPressure`. The interface `Generate` stays unchanged (stub/tests keep using it).

> **This is the v4 structural fix.** Roll G is the only place that knows the bent pie, so it is the only place that can compute the true residual — therefore it computes and stamps it, and Roll H reads it. No recompute, no shared/cross-roll config, no Matchup helper for the residual. Reason both seams out loud in delivery; present the two `Execute` signature changes as (reversible) calls.

### B. Two new fields on `PossessionState`
Append two nullable doubles in the optional-parameter tail (after `Result`; all callers construct by name or `with`, so position is free):
```
double? UsagePressure        = null,   // stamped by Roll E
double? UsageResidualPressure = null   // stamped by Roll G
```
Document both in the existing XML-comment style (the `SelectedSlot`/`ShotType`/`Result` register):
- **`UsagePressure`** — the FOURTH per-possession fact, stamped by Roll E alongside `SelectedSlot`. The selected shooter's **volume pressure**, `max(0, finalShare − equalShare)`. Null until Roll E runs; on a possession that reaches Roll E but carries no load (FastBreak, or a slot at/below the equal share) it is **`0.0`, not null** (Roll E ran and established zero). The player's OWN volume load; gravity/spacing later add their own named components, not merged here.
- **`UsageResidualPressure`** — the FIFTH per-possession fact, stamped by Roll G alongside `ShotType`. The load Roll G could not absorb into a wider shot diet this possession. Null until Roll G runs; `0.0` when fully absorbed, when there was no pressure, or on a FastBreak. Read ONLY by Roll H's residual term.

**Leak guard (confirmed location):** `ResetOffense` wipes the shot facts in **`RollK.cs` (line ~120)**:
```
state with { SelectedSlot = null, ShotType = null, Result = null, FastBreak = false }
```
**Add BOTH `UsagePressure = null` and `UsageResidualPressure = null` to that exact `with`.** A reset offense recomputes selection and location fresh, so neither stale value may ride through. (Edit is in Roll K, not the resolver — confirm.)

### C. Roll G generator — bounded shift + residual computation
In `RollGGenerator.GenerateWithResidual`, the real path is: `SelectedSlot` lookup → (FastBreak short-circuit) → shooter null? stub → read defenders → `CoachingPull.Apply` (identity) → per-zone `Matchup.LocationMultiplier` → multiply tendencies → **renormalize**. **Insert the pressure shift AFTER the matchup multiply, BEFORE the final renormalize**, then return the residual it produced.

- Read `pressure = state.UsagePressure ?? 0.0`. **If `0.0`: pie numerically unchanged from today (exact — the shift code is branch-skipped), and `ResidualPressure = 0.0`.** Regression guard.
- If `> 0.0`, compute against the **bent** profile (§4a): `requestedShift = f(pressure)`; `intrinsicCapacity` from the shooter's normalized authored non-dominant tendency mass; `bentDom` = the bent profile's max zone; **compute `destinationMass` = sum of all non-dominant bent zone weights; if `destinationMass ≤ Epsilon`, treat `absorbedShift = 0.0` (full `requestedShift` becomes residual — there is nowhere to redistribute; never divide by zero); otherwise:** `absorbedShift = min(requestedShift, intrinsicCapacity, bentDom_mass × PressureShiftCapFraction)`; move `absorbedShift` off `bentDom`, redistribute across the other zones proportional to their bent weight; renormalize. Return `residualPressure = requestedShift − absorbedShift`.
- Must: identity + zero residual at zero pressure; near-nil shift and `residual ≈ requestedShift` for a one-zone profile; smooth shift and `residual ≈ 0` for a spread profile against ordinary defense; the shift never drives any zone negative or the total to zero; sum-to-1.
- **FastBreak:** `if (state.FastBreak) return new RollGGeneration(BuildFastBreakPie(), 0.0);` — short-circuit before any shift; residual 0.0.
- **Fallbacks:** zero-defender pure-tendency short-circuit MAY take the shift (real shooter under load — implementer's call, flag it) and returns its residual; the unpopulated-shooter stub path takes NO shift and returns residual 0.0 (no player).

### D. Roll H generator — volume-tax + residual penalty (both read from state)
In `RollHGenerator.Generate`, after `makePct` is computed (matchup-adjusted logistic) and BEFORE `BuildRealPie`:
- Read `pressure = state.UsagePressure ?? 0.0` and `residual = state.UsageResidualPressure ?? 0.0`. **If both `0.0`: numerically unchanged from today** (branch-skipped). Regression guard.
- Otherwise apply both to `makePct`:
  1. **Volume-tax:** reduce `makePct` scaled by `pressure` (multiplicative or additive — §4b). Small.
  2. **Residual:** reduce `makePct` scaled by `residual`, applied to the realized zone `state.ShotType` regardless of whether it's the preferred zone.
- Clamp the result to `[0, 1]` (a heavily-pressured specialist approaches but cannot cross 0). Flag the clamp.
- **No recompute, no Matchup usage-helper, no Roll G config read.** Roll H consumes the two stamped scalars and its own two penalty scales only.
- **Putback** (`putback == true`) and **FastBreak**: NO discount. The putback short-circuit returns first (confirm) — leave it. On a break both scalars are `0.0` anyway; assert exemption regardless.
- **Empty-defender (DEC-6):** the discounts read only the stamped scalars + `makePct`, never the defender — so they still apply (the make door's DEC-6 raw-rating read happens first, then the discounts). **Unpopulated-shooter stub path:** NO discount. Flag both.

### E. Transition / FastBreak: NO behavior change, assert exemption
On a break: Roll E's `GenerateWithPressure` returns all-zero `Pressures` → `UsagePressure = 0.0`; Roll G short-circuits to the flat fast-break pie and returns residual `0.0` → `UsageResidualPressure = 0.0`; Roll H sees both `0.0` → no discount. Confirm Roll H has no separate FastBreak branch (it relies on the two scalars being `0.0` — sufficient). The harness must PROVE a break shows zero shift and zero discount (§5f).

### F. Interface typing — derived interfaces + stubs updated (v5/v6 fix, MUST resolve before writing)
**Confirmed against source:** `Resolver._rollEGenerator` is typed `IRollEPieGenerator`; `Resolver._rollGGenerator` is typed `IRollGPieGenerator`. Neither interface has `GenerateWithPressure` or `GenerateWithResidual`. Calling those methods on the Resolver's fields would **not compile**. Do NOT fix this with a downcast — that breaks substitution.

**Confirmed harness scope:** there are **20 `new Resolver(...)` construction sites** in `Program.cs`. 19 of them pass `new RollEStubPieGenerator(...)` and `new RollGStubPieGenerator(...)`. If those stubs do not implement the richer interfaces, 19 of 20 checks fail to compile.

**DECIDED fix (v5/v6): minimal derived interfaces + stubs updated to implement them.**

**(1) Two new interfaces:**
```csharp
public interface IRollEGenerationProvider : IRollEPieGenerator
{
    RollEGeneration GenerateWithPressure(PossessionState state);
}
public interface IRollGGenerationProvider : IRollGPieGenerator
{
    RollGGeneration GenerateWithResidual(PossessionState state);
}
```

**(2) Concrete generators implement the richer interfaces:** `RollEGenerator : IRollEGenerationProvider`; `RollGGenerator : IRollGGenerationProvider`.

**(3) PRIMARY STUBS also implement the richer interfaces (v6 fix):**
```csharp
// RollEStubPieGenerator
public sealed class RollEStubPieGenerator : IRollEGenerationProvider
{
    public RollEGeneration GenerateWithPressure(PossessionState state)
    {
        var pie = Generate(state);                     // existing logic unchanged
        var finalShares = ExtractSharesFromPie(pie);   // read weights from the pie
        return new RollEGeneration(pie, finalShares, new double[5]);   // pressures always zero
    }
    public Pie<SelectionOutcome> Generate(PossessionState state) { /* existing body unchanged */ }
}

// RollGStubPieGenerator
public sealed class RollGStubPieGenerator : IRollGGenerationProvider
{
    public RollGGeneration GenerateWithResidual(PossessionState state)
        => new(Generate(state), 0.0);                  // residual always zero
    public Pie<ShotLocation> Generate(PossessionState state) { /* existing body unchanged */ }
}
```
Stubs return their existing pies unchanged; pressures/residual are always zero. Old test code that calls `.Generate(state)` still works. Resolver construction with stubs still compiles. Isolated callers that type the parameter as `IRollEPieGenerator` still work (the stub satisfies both interfaces through inheritance).

**(4) Resolver: widen two field/ctor types only:**
`IRollEPieGenerator → IRollEGenerationProvider` for `_rollEGenerator`; `IRollGPieGenerator → IRollGGenerationProvider` for `_rollGGenerator`. No other Resolver change.

**(5) Record type location — public top-level (v5 clarification):** `RollEGeneration` and `RollGGeneration` must be **public top-level `readonly record struct` types**, not nested inside the concrete generator classes. If nested, the interface signatures would require qualified names like `RollEGenerator.RollEGeneration`, coupling the interface to one implementation. Place each record in its own file in `Generators/`, or at minimum as a top-level type in the same file as its interface. Confirm this at draft time.

**(6) Verify ALL 20 Resolver construction sites compile:** grep `new Resolver(` and confirm every Roll E and Roll G argument implements `IRollEGenerationProvider` / `IRollGGenerationProvider` respectively. The primary stubs now satisfy this. If any OTHER stub (not `RollEStubPieGenerator` / `RollGStubPieGenerator`) appears in a Resolver construction, it must also be updated — but confirm against source.

---

## 4. The formulas (IMPLEMENTER'S CALL — audited, Monte-Carlo mandatory)

Engineering, not basketball, but the crux. Reason it out loud and **Monte-Carlo all of it before any C#** (CONVENTIONS §2).

### 4a. The bounded shift + residual (Roll G only)
Tendencies normalized to [0,1] (sum 1) before any of this — **mandatory** (authored 0–99; an un-normalized scale is 100× off). State it at the call site.
- **Intrinsic capacity** (authored): `intrinsicCapacity = 1 − a[authoredDom]`, where `a[]` are the normalized authored tendencies. A one-zone player ≈ 0; a spread player large. (Authored, so the defense cannot change how adaptable he *is*.)
- **Requested shift:** `requestedShift = f(pressure)`, saturating so even max pressure cannot empty the dominant zone (Ray Allen still shoots mostly threes — just fewer). E.g. `requestedShift = PressureShiftScale × pressure`, capped at a fraction.
- **Available bent mass:** `bentDom_mass × PressureShiftCapFraction`, where `bentDom` is the bent profile's max zone — you can't move more off it than it holds (× a cap so it can't be fully emptied).
- **Zero-destination guard (v5 — mandatory):** before declaring any mass absorbed, compute `destinationMass = sum of all non-dominant bent zone weights`. If `destinationMass ≤ Epsilon`, set `absorbedShift = 0.0` — the full `requestedShift` becomes residual. Never divide by zero; never count a shift as absorbed if there is nowhere to place it. This can occur under extreme matchup multipliers, unusual test values, or future configuration, and it must not crash or silently mis-count.
- **Absorbed:** `absorbedShift = (destinationMass > Epsilon) ? min(requestedShift, intrinsicCapacity, bentDom_mass × PressureShiftCapFraction) : 0.0`.
- **Residual:** `residualPressure = requestedShift − absorbedShift` — the number Roll G stamps and Roll H consumes.

Roll G applies `absorbedShift` to the bent profile (off `bentDom`, redistributed across the others proportional to bent weight). The residual carries forward; nothing recomputes it.

### 4b. The two Roll H reductions
- **Volume-tax:** `makePct *= (1 − pressure × PressureVolumeTaxScale)` (or additive). Small. Tuned so a versatile player at the rail loses ~2–4 pts.
- **Residual:** `makePct −= residual × PressureResidualPenaltyScale`. Tuned so a specialist loses the bulk of ~10–15 pts here. Then clamp `makePct` to `[0,1]`.

### 4c. Config fields (review point — each consumer owns its dials; NO shared config)
Carrying the residual means **each roll uses only its own config** — Roll H never needs Roll G's shift scales. Confirm each touched config's loader style (Session-51 `RollEConfig` manual `GetProperty`; `RollGConfig`/`RollHConfig` may use `JsonSerializer.Deserialize` — match each file). Each field a calibration placeholder, fail-loud invariant on load:
- `PressureShiftScale` (**RollGConfig**) — requested-shift strength. Invariant `≥ 0` (0 = no shift, ablation-friendly).
- `PressureShiftCapFraction` (**RollGConfig**) — fraction of the bent-dominant mass that may be moved. Invariant `> 0`, `≤ 1`.
- `PressureVolumeTaxScale` (**RollHConfig**) — the small all-shots reduction. Invariant `≥ 0`.
- `PressureResidualPenaltyScale` (**RollHConfig**) — the residual reduction. Invariant `≥ 0`.
- **No RollEConfig fields** (comfort is `1.0/populatedCount`, computed; yields deferred). **No shared/cross-roll config, no MatchupConfig usage fields.**

### 4d. Monte Carlo cases to demonstrate (report ALL seven)
1. **Five-equal, no pressure:** every slot `finalShare = 0.20`, pressure 0. Roll G pie, residual 0, and Roll H `makePct` identical to baseline. (Zero-drift regression.)
2. **Star at 0.40 (natural dominance):** pressure `0.20` > 0 → real cost; show the shift (smooth if versatile) and the `makePct` drop. (v1 "dominant scorer pays nothing" bug gone.)
3. **Floor binds on a Rodman:** alpha `finalShare` goes *down* slightly; Rodman below 0.20 with pressure 0; alpha's pressure is absolute-share-driven, not floor-driven. (v1 sign bug gone.)
4. **Forced specialist at the rail** (one dominant *authored* tendency, four near-zero, athletically neutral): `intrinsicCapacity ≈ 0` → `absorbedShift ≈ 0`, `residualPressure ≈ requestedShift`, `makePct` drops ~10–15 pts concentrated on the preferred zone; mix barely moves.
5. **Forced versatile player at the rail, ordinary defense** (five comparable *authored* tendencies, athletically neutral): large capacity, ample bent mass → `absorbedShift ≈ requestedShift`, `residualPressure ≈ 0`, `makePct` drops ~2–4 pts; mix shifts smoothly. Assert specialist-drop ≫ versatile-drop.
6. **Below comfort:** `finalShare < 0.20` → pressure 0 → no shift, no penalty, residual 0. (Below-comfort flat; lift is deferred gravity.)
7. **Bent-mass clamp (the v4 reason — make it testable):** a versatile shooter whose bent profile has been flattened so the bent-dominant mass is *smaller* than `requestedShift`. Show `absorbedShift` clamped by `bentDom_mass × PressureShiftCapFraction` (below the intrinsic amount), so `residualPressure` is **larger** than the intrinsic-only calc would give — i.e. a disruptive defense raises the residual. This is the case v3's recompute could not reproduce.

---

## 5. The harness checks — additive; the zero-pressure regression is load-bearing

Add a **Phase 17** block (mirror the Phase 9/10/11 matchup-door checks — seated test rosters, directional assertions). Prove:
- **(a) Zero-pressure regression (CRITICAL).** Five-equal roster (every slot 0.20 → pressure 0): Roll G location pie, stamped residual (0), and Roll H make rates **numerically identical** to the pre-build values for that seeded roster (exact, since the pressure code is branch-skipped). Any perturbation → FAIL.
- **(b) Specialist efficiency drop.** Forced specialist at high `finalShare`: make rate drops materially vs. no-pressure; mix barely shifts; drop concentrated in the preferred zone (residual landing there).
- **(c) Versatile player absorbs.** Forced versatile player (ordinary defense) at the same `finalShare`: make rate barely moves; mix shifts smoothly. **Assert specialist-drop > versatile-drop** (the thesis).
- **(d) Below-comfort no-op.** `finalShare < 0.20` slot: make rate and mix unchanged.
- **(e) Natural-dominance cost.** A star at a naturally high `finalShare` (no coach, no floor games) shows a real make-rate drop — the curve fires on absolute load.
- **(f) FastBreak exemption.** A break possession: zero Roll G shift, zero Roll H discount; `UsagePressure == 0.0` and `UsageResidualPressure == 0.0`.
- **(g) Pressure + residual plumbing.** `UsagePressure`: null before Roll E, non-null (0.0 on a zero-load/FastBreak selection) after Roll E. `UsageResidualPressure`: **null before Roll G; non-null after Roll G under pressure; `0.0` when fully absorbed or on a FastBreak; positive for a forced specialist.** Both **null after a Roll K `ResetOffense` re-entry** (the leak guard).

**Existing checks that MUST stay green (verify, don't assume):** every Phase 1–16 check, the Session-51 Roll E selection batch, and full-game closure (`ended + routed-to-stub == BatchSize`, `unrouted == 0`).

**The observation run WILL move this time** — and on the frozen corpus it may move by **more than a point or two**, because the alphas there sit above 0.20 and now pay a volume cost. That is the system working, not a bug. **Record the before/after in `observations.md` as a new Run; report the FG%/PPP delta plainly.** If the drop is implausibly large (e.g. combined FG% below ~46% on these above-average rosters), flag it as over-tuned and recommend lowering the scales — do not silently ship a hot calibration. Routing/pace/foul/closure numbers must stay steady (pressure changes shot *quality*, never routing).

**Attribution note (v5 observability discipline):** the harness check for a forced specialist (Phase 17b) should report what fraction of his total make-rate decline came from (a) the existing Roll H matchup adjustment, (b) the volume-tax, and (c) the residual penalty. Accidental double-punishment is most visible here: if matchup already suppresses his make rate and the residual piles on top at full strength, the combination can exceed the intent. Report the three contributions separately so over-tuning is caught early without having to re-derive it later.

---

## 6. What to watch in the harness output

- **Phase 17 (a):** zero-pressure roster → Roll G pie, residual 0, Roll H makes identical to baseline → `ok`. The make-or-break regression line.
- **Phase 17 (b)/(c):** specialist make-rate drop materially larger than versatile; specialist mix static with the drop in his preferred zone; versatile mix shifted with a tiny drop → `ok`. The ordering assertion is the thesis.
- **Phase 17 (d):** below-comfort slot unchanged → `ok`.
- **Phase 17 (e):** naturally dominant star shows a real drop → `ok`.
- **Phase 17 (f):** FastBreak — zero shift, zero discount, both scalars `0.0` → `ok`.
- **Phase 17 (g):** `UsagePressure` non-null post-Roll-E; `UsageResidualPressure` null pre-Roll-G, positive for a forced specialist, `0.0` when absorbed; both null after `ResetOffense` → `ok`.
- **Every Phase 1–16 check + Session-51 Roll E batch:** unchanged, still `ok`.
- **Full-game closure:** `ended + routed-to-stub == BatchSize`, `unrouted == 0` — unchanged.
- **Observation run:** frozen-corpus FG%/PPP drift DOWN (alphas paying volume cost); report magnitude; flag if implausibly large. Routing/pace/fouls steady.
- **Build compiles:** signatures touched are `RollE.Execute` (+`double[] pressures`), `RollG.Execute` (+`double residualPressure`), `PossessionState` (+2 fields), `RollK.cs` reset `with` (+2 nulls), and the three resolver call sites (two Roll E paths, one Roll G). Roll H + all three interfaces UNCHANGED.

---

## 7. Delivery order (CONVENTIONS §3)

1. **Code first:** `PossessionState.cs` (two new fields), `Generators/IRollEGenerationProvider.cs` (NEW — derived interface + `RollEGeneration` public top-level record), `Generators/IRollGGenerationProvider.cs` (NEW — derived interface + `RollGGeneration` public top-level record), `RollEGenerator.cs` (implements `IRollEGenerationProvider`; `GenerateWithPressure` + delegates `Generate`), `RollEStubPieGenerator.cs` (now implements `IRollEGenerationProvider`; `GenerateWithPressure` returns existing pie + zero pressures), `RollE.cs` (+`pressures` param, stamp `UsagePressure`), `RollGGenerator.cs` (implements `IRollGGenerationProvider`; `GenerateWithResidual` + bounded shift + zero-destination guard + delegates `Generate`), `RollGStubPieGenerator.cs` (now implements `IRollGGenerationProvider`; `GenerateWithResidual` returns existing pie + residual 0.0), `RollG.cs` (+`residualPressure` param, stamp `UsageResidualPressure`), `RollK.cs` (reset clears both fields), `Resolver.cs` (two field/ctor types widened to provider interfaces; three call sites updated), `RollGConfig.cs` (+2 fields + load), `RollHGenerator.cs` (volume-tax + residual penalty from state), `RollHConfig.cs` (+2 fields + load), `config.json` (+4 fields), `Program.cs` (Phase 17). Each a complete, overwrite-ready file with a folder map. Surgical `str_replace` in the sandbox; deliver the resulting whole file. (No `Matchup.cs` change.)
2. Emmett runs the harness.
3. **Docs only after green:** `journal.md` (prepend), `design.md` (append), `observations.md` (prepend the new Run). Complete overwrite-ready files, read-merge-deliver.
4. Commit ritual one step at a time. Suggested message: `Usage->efficiency: volume pressure (Roll E) + carried residual (Roll G) -> Roll H volume-tax + residual penalty`.

---

## 8. Pre-write checklist (the implementer confirms ALL before writing a line of C#)

- [ ] Repo pulled fresh (§0); every file below opened, not remembered.
- [ ] **ALL 20 `new Resolver(...)` construction sites grepped** (confirmed 20 in harness) — every Roll E and Roll G argument implements the richer provider interfaces. The two primary stubs now satisfy this; if any other stub appears in a Resolver construction, update it too.
- [ ] `RollEGeneration` and `RollGGeneration` confirmed as **public top-level `readonly record struct`** types (not nested inside generators); each in `Generators/` alongside its interface file, or otherwise at a top-level accessible namespace.
- [ ] `Resolver.cs` two field/ctor types widened (`IRollEPieGenerator → IRollEGenerationProvider`; `IRollGPieGenerator → IRollGGenerationProvider`); three call sites updated.
- [ ] `IRollEGenerationProvider.cs` and `IRollGGenerationProvider.cs` — new files; nearest existing interface siblings opened and pattern-matched.
- [ ] `RollEGenerator.cs` read — pipeline confirmed; `GenerateWithPressure` one-pass; implements `IRollEGenerationProvider`; `Generate` delegates.
- [ ] `RollEStubPieGenerator.cs` read — existing `Generate` body confirmed unchanged; `GenerateWithPressure` added returning existing pie + zero pressures array; now implements `IRollEGenerationProvider`.
- [ ] `RollE.cs` signature `(state, pie, game, rng)` confirmed; +`double[] pressures`; `SelectedSlot`+`UsagePressure` stamped on one `with`; `slotNumber = (int)outcome + 1` confirmed.
- [ ] `RollGGenerator.cs` read — matchup-multiply→renormalize located; `GenerateWithResidual` planned; zero-destination guard confirmed; FastBreak + fallback short-circuits return residual 0.0; implements `IRollGGenerationProvider`; `Generate` delegates.
- [ ] `RollGStubPieGenerator.cs` read — existing `Generate` body confirmed unchanged; `GenerateWithResidual` added returning existing pie + residual 0.0; now implements `IRollGGenerationProvider`.
- [ ] `RollG.cs` signature `(state, pie, rng)` confirmed; +`double residualPressure`; `ShotType`+`UsageResidualPressure` stamped on one `with`.
- [ ] `RollK.cs` `ResetOffense` `with` (line ~120) confirmed; **both** `UsagePressure = null` and `UsageResidualPressure = null` added there.
- [ ] `PossessionState.cs` optional-parameter tail + XML-doc style confirmed; both fields appended; `0.0`-vs-null conventions documented.
- [ ] `RollHGenerator.cs` read — `makePct` site located; volume-tax + residual penalty inserted; NO recompute, NO Matchup helper, NO Roll G config read; `[0,1]` clamp; putback + DEC-6 + unpopulated exempt/decided.
- [ ] `Player.cs` tendency names confirmed verbatim; normalization to [0,1] mandatory.
- [ ] Each touched config's loader style confirmed; four new fields (2 RollGConfig, 2 RollHConfig) + invariants; NO RollEConfig fields, NO shared config.
- [ ] Monte Carlo for all SEVEN §4d cases BEFORE C# + zero-destination edge case.
- [ ] Roll G shift verified: identity + zero residual at zero pressure; near-nil shift for one-zone; smooth for spread; zero-destination guard fires; bent-mass clamp raises residual; sum-to-1; never negative.
- [ ] Roll H discounts verified: identity at zero; clamp holds; volume-tax from `UsagePressure`; residual from `UsageResidualPressure`; preferred zone affected.
- [ ] Brace balance per touched file checked.
