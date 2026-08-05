# Session 62 — Discipline Effect B: the per-man NON-SHOOTING foul model (each reach-in gets a name)

Follow **CONVENTIONS.md** for everything: the §0 repo pull, §1 read-the-template, the §2 validation rungs,
the §3 delivery format, the **§4 check-in gate**, and the §6 discipline. This prompt says *what* to build and
audits the wiring; CONVENTIONS says *how*.

**Register:** a build session, and a **foundation piece** (Emmett's frame — the "slow down and get it right"
class). It changes *how a non-shooting foul is born and who it belongs to*, not a knob. Scope walled to the
non-shooting foul: **the reach-in RATE model + the committer ATTRIBUTION + its box-score column. Nothing on the
shooting side; nothing on the make surface; NO foul-out / substitution layer (see §0.5).**

**Design status:** settled conversationally 2026-07-16 and **signed off on the archetype table** (§1); ChatGPT
review folded (this revision). The model shape and the Discipline spread are RULED — **do not re-litigate them.**
Magnitudes are placeholders, page-tuned later, never suite-asserted.

---

## 0. Measurement gate — re-confirm the as-is against live source + a run

Written to an audit already run (2026-07-16 pull + 5,205-game season). The build session **re-confirms**, does
not inherit. Facts the design rests on:

1. **The reach-in RATE is authored at the TEAM level in Roll B and reads NO Discipline.** `RollBGenerator` builds
   `baseFoulShare = BaseFoul / actionMass`, finalized inside `Matchup.TeamDisruptionShares(...)`, which takes a
   **`defensiveFoulNudge`** — a *team Hustle-gap* term (`HustleFoulWeight × HustleGapShift(max(0, −hustleGap), …)`,
   fires only when the defense out-hustles). This is the wire the per-man model replaces.
2. **Six charge sites, one choke point.** `DefensiveFoulCharge.Resolve(state, game, belowBonusKind, flavor?)` is
   called from **Roll D** (`ResumeInbound`, **carries a `flavor`** — the reach-in path) and **Rolls I/J/K/M**
   (`ResolveSidelineInbound`, **no flavor** — loose-ball / rebound-scrum / situational). It has `game` +
   `state.Defense`; the five defenders are reachable there. It charges the team foul and forks on bonus — **it
   names no individual.**
3. **Reach-in vs situational split (measured):** of all non-shooting fouls (**12.09/team-game**), reach-ins
   (Roll D, `flavor != null`) are **82% = 9.92/team-game**; situational (I/J/K/M, `flavor == null`) are **18% =
   2.16/team-game**. The per-man RATE model governs the **9.92 reach-in portion only** (anchor: five average
   defenders → ~9.92/team-game, ~1.98/defender). The situational 2.16 are game-situation-driven — they get a
   committer NAME but their *rate* is not Discipline-built.
4. **Attribution in this engine is emit-event + post-hoc probabilistic draw — NOT atomic accounting.** The
   shooting-foul pipeline is the template: the engine emits a `ShootingFoulEvent` (zone, shooterSlot); the harness
   `DrawFoulingDefender(...)` (separate `foulRng`) picks the committer with a per-zone `MatchedShare` + interior
   tilt (`SignedK`: +0.50 at rim favors interior defenders, −0.50 at three favors perimeter — **CALIBRATION
   PLACEHOLDERS**); the box score credits the `ShFoul`/`SFL` column. **Non-shooting fouls emit NOTHING parseable
   — which is precisely why they are anonymous today.**
   - *Correction to a draft-time claim:* an earlier probe reported shooting fouls "don't lean post (50.7/49.3)."
     That measured the *same-slot* defender at the charge point, NOT `DrawFoulingDefender`'s actual credit, which
     tilts rim fouls toward interior defenders. Shooting fouls as *credited* likely DO lean post via `SignedK`.
     Either way the shooting side is **OUT OF SCOPE** here (its tilt already has a knob); do not touch
     `DefenseFoulWeight` or `DrawFoulingDefender`.

**Gate action:** run the suite green as-is; reproduce the 82/18 reach-in/situational split (`flavor != null` vs
`== null` inside `DefensiveFoulCharge.Resolve`) so the 9.92 anchor is on the record. If reach-ins are NOT ~82%,
STOP and report — the anchor changes.

## 0.5. Scope honesty — this session does NOT build foul-out (source-confirmed)

**There is no per-player personal-foul tracking and no foul-out anywhere in the engine.** `FoulTracker` is
team-only (`FoulsFor`/`Increment`/`BonusFor`, for the bonus); the only per-player foul column is `ShFoul`
(shooting fouls, a post-hoc box-score credit); lineups are FIXED with no substitution layer. So this session
**creates the first individual non-shooting-foul count** (the `NSFL` column) — combined with `SFL` it yields a
per-player foul total for the first time — but it **does NOT build foul-out**: benching a player at 5 fouls needs
the parked substitution layer and a canonical, state-carried per-player foul count (a different architecture from
the post-hoc box-score reconstruction). Foul-out is a future piece; do not scope it in. Log the dependency
(status.md): "individual foul totals now exist in the box score; a future foul-out/substitution layer will carry
a canonical per-player foul count in game state and consume it."

---

## 1. The design (Emmett, settled + table-signed-off 2026-07-16 — DO NOT re-litigate)

**Each defender carries his own reach-in propensity. The team's reach-in RATE is the SUM of the five; the SAME
five numbers weight WHO committed the reach-in.** One set of per-man numbers does both jobs — the "each owns his
own fifth" model in its purest form (Emmett's ruling: a hacker *adds his own fouls on top*, he does not merely
redistribute the team's).

    propensity_i = LuckFloor + Base × discFactor(D_i) × athFactor(A_i) × perimFactor(o_i)

- **Discipline PRIMARY, symmetric about 50 (low D → more):** `discFactor = 1 − DiscSpan × clamp((D−50)/49, −1, +1)`.
- **Underathletic a SMALL secondary (low athleticism → more):** `athFactor = 1 − AthSpan × clamp((A−50)/49, −1, +1)`.
- **Perimeter players a SLIGHT lean (perimeter → more):** `perimFactor = 1 + PerimSpan × (2·orient − 1)`, orient ∈ [0,1] (0 = post, 1 = perimeter), 0.5 = neutral.
- **LuckFloor:** everyone can still draw a ticky-tack whistle — disciplined = *rare*, never immune (propensity always > 0).
- **NO Hustle / team-aggression term.** The "aggressive team fouls more" behavior is **real and stays on the roadmap — it belongs to the COACH / pressure layer** (a dialed-up press *should* foul more), not to a player's Hustle rating. This session RETIRES the `defensiveFoulNudge` from the reach-in rate; the coach layer re-adds a team-pressure foul multiplier later. Log it as **relocated, not deleted** (status.md, coach/pressure layer).

**Anchor:** five average defenders (D50/A50/orient 0.5) reproduce today's **reach-in** rate (~9.92/team-game),
each owning a fifth. **Symmetric:** a lockdown fouls below his share, a hacker above.

**Signed-off spread (RULED — placeholders `DiscSpan 0.35`, `AthSpan 0.12`, `PerimSpan 0.10`, `LuckFloor` per the
anchor):** hacker (D0) +31% vs average, lockdown (D99) −31%, hacker ≈ 2× the lockdown; team stacking linear (five
disciplined ≈ −24%, five hackers ≈ +25%). A hacker's reach-ins + shooting fouls ≈ 4–4.5/game — **playable, fouls
out some nights, a liability not unplayable** (guardrail). The engine's *absolute* foul volume runs high
league-wide — a separate page-calibration knob, NOT this model's concern. **The signed-off spread is the
reach-in spread; the situational picker (§3) must not silently widen the TOTAL player-foul spread — surfaced
numerically at the gate.**

**Units — keep the model definition separate from the calibration report.** `propensity_i` is in raw model units;
the mathematical invariant is **the Roll B reach-in share is proportional to the sum of the five named
propensities** at fixed possession/action-mass inputs. The **9.92/team-game figure is a season-level CALIBRATION
REPORT, not the definition of the propensity** — `Base` is set so five-average lands there at current pace, and
must be re-derivable if pace or upstream action-mass changes. Do not let `Base` become a mystery scalar that
silently embeds pace and Roll-B conversion; name the conversion layer (raw propensity → Roll B foul share →
possessions → observed fouls/game) explicitly in the oracle.

---

## 2. Architecture — the two halves

**Half A — the reach-in RATE (engine, Roll B). THE behavioral change.** Replace the team-Hustle
`defensiveFoulNudge` with a per-man aggregate: the reach-in share is built so the team rate is proportional to
the SUM of the five defenders' propensities, anchored so five-average = today's reach-in rate. Retire
`HustleFoulWeight`'s reach into the foul share; **leave the Hustle *turnover* nudge (`hustlePressureNudge`)
untouched** — only the FOUL nudge is Discipline's to replace. Re-derive the base so the anchor holds after the
Hustle term leaves (§5.4 — absorb the removed nudge's average contribution, don't ignore it). Reads the
DEFENDERS' own ratings only — no offense term, no matchup.

**Half B — committer ATTRIBUTION, mirroring the shooting-foul pipeline (NOT a Continue payload).** The house
pattern is emit-event + post-hoc draw (§0.4). So:
- **ENGINE:** emit a `NonShootingFoulEvent` carrying the **defense side** and a **reach-in/situational flag**
  (derivable from `flavor != null`), **atomically inside `DefensiveFoulCharge.Resolve`, in the same place the
  team foul is incremented** — so no team foul is ever charged without an attributable event on the record. This
  covers all six charge sites at once. The event's flag lets attribution weight reach-in vs situational fouls
  differently (§3).
- **HARNESS:** a new post-hoc draw (mirror `DrawFoulingDefender`) picks the committer per non-shooting foul
  event, weighted by the per-man propensities (recomputed from the defensive roster's ratings, as
  `DrawFoulingDefender` already recomputes foul weights), with the luck floor guaranteeing every defender a
  nonzero draw. Credit a new `NSFL` box-score column. Post-hoc probabilistic — consistent with `SFL`/rebounds.

**Attribution atomicity invariant.** Every non-shooting foul emits exactly one committer-attributable event, in
the same resolver step as the team-foul increment and bonus decision — team increment, bonus, and the emitted
attribution event represent one foul and cannot diverge. The post-hoc draw then credits exactly one defender's
`NSFL` once per event, regardless of bonus outcome. Attribution must NOT depend on an optional later consumer
that a branch could skip; the emitted event (always present with the increment) is what guarantees no anonymous
non-shooting foul.

---

## 3. Open design details — resolve with Emmett at the check-in gate, do NOT pre-decide

1. **What is "athleticism" for the reach-in secondary?** Recommend a footspeed/reaction composite (**Quickness +
   FirstStep**, the defender's own — "slow, late-reacting guy reaches"), read absolutely (not a matchup — "beaten
   by *this* man" was deferred as too matchup-heavy). Confirm the exact ratings and blend.
2. **What is "orientation" for the perimeter lean?** Recommend `Matchup.Postness` mapped to orient ∈ [0,1] (post
   → 0, perimeter → 1), centered so the lineup's average orientation is neutral. Confirm.
3. **★ Situational (I/J/K/M) attribution — a THREE-way choice, ruled against a number (ChatGPT catch).** The
   unchanged situational pool (18%) still needs a committer name, but the reach-in propensity's **perimeter lean
   is basketball-backwards for a rebound scrum** (scrums lean POST). Three candidates for the situational-only
   picker weights: **(a) full reach-in propensity** (Discipline + ath + perimeter), **(b) Discipline-only**
   (nonzero Discipline-weighted, no ath/perimeter modifiers), **(c) flat**. **Recommend (b) Discipline-only** —
   preserves the broad meaning of Discipline without pretending the reach-in archetype (with its perimeter lean)
   explains rebound scrums. **Gate requirement (hard):** before Emmett rules, report the **D0/D50/D99 archetype
   totals under all three candidates** — reach-ins, situational fouls, total non-shooting fouls, shooting fouls,
   and total personal fouls — so the effect on the TOTAL player-foul spread is visible. The signed-off reach-in
   spread does NOT automatically authorize an expanded total-foul spread or a perimeter lean in scrum events.
4. **Re-anchoring `Base` after the Hustle nudge leaves.** Five-average must land at ~9.92 reach-ins/team-game
   once the Hustle foul term is gone. Measure the average `defensiveFoulNudge` contribution first (§5.4) so the
   re-anchor absorbs it. Reported in session harness output, **never suite-asserted** (page-only).

---

## 4. The build, once the gate clears

**Oracle-first:** `tools/nonshooting_foul_oracle.py` proves the per-man propensity math + the anchor + the
archetype table + the **units/conversion layer (raw propensity → expected Roll B reach-in share → simulated
reach-ins/team-game)** BEFORE any C#. Golden fixture (`tools/nonshooting_foul_golden.json`) binds the C# named
propensity static at 1e-12; **neutral/kill rows bit-exact on the HELPER, not on the whole Roll B path** (see
feature-off below). Emmett signs off the table (spread already ruled; the oracle locks it).

- **Named propensity static** (engine) — the sum-drivable per-man propensity; the golden binds to it. Read the
  nearest sibling for the named-static + config-knob shape (the S61 lesson: make-chain knobs lived in RollHConfig,
  not MatchupConfig — home these where their siblings live; confirm against source).
- **Reach-in rate rewire** — `RollBGenerator` + `Matchup.TeamDisruptionShares` (foul-share half): remove
  `defensiveFoulNudge`, feed the per-man aggregate (share ∝ Σ propensity), re-anchor.
- **`NonShootingFoulEvent`** (engine) — emitted atomically in `DefensiveFoulCharge.Resolve`; carries defense side
  + reach-in/situational flag. Mirror `ShootingFoulEvent`.
- **Harness committer draw + `NSFL` column** — mirror `DrawFoulingDefender`; weight by propensities; apply the
  §3-ruled situational weights per the event flag.
- **Config knobs** (new, placeholders, Load-guarded both sides): `ReachInDiscSpan`, `ReachInAthSpan`,
  `ReachInPerimSpan`, `ReachInLuckFloor`, plus the base/scale the re-anchor needs.

**Feature-off semantics (NOT "identity" — ChatGPT catch).** A zero reach-in-model scale makes the per-man model
**rating-neutral** (no Discipline/ath/orientation response; five flat per-man propensities sum to the anchored
rate) **while retaining the new per-man aggregate architecture and anchor.** It is **NOT** bit-identical
pre-Session-62 behavior and does **NOT** restore the retired Hustle foul nudge. Golden bit-identity applies to
the propensity helper's neutral rows only, NOT to the whole Roll B path.

**Phase 68 harness check** proves: golden parity on the propensity (1e-12, neutral/kill helper rows bit-exact);
**anchor** (five average → the measured reach-in rate within tolerance — REPORTED, not asserted); **per-man
symmetry** (lockdown below share, hacker above, symmetric about 50); **★ the SUM property — the most load-bearing
assertion** (team reach-in share ∝ Σ of five propensities; a hacker RAISES the team total, not just his share);
**stackable-linear** (five disciplined ≈ 5× one delta, NOT >5×); **the draw ∝ propensity** (disciplined picked
least, hacker most, floor lets anyone be picked); **every non-shooting foul across all six sites emits an
attributable event** (no anonymous charge remains) and **credits exactly one defender's `NSFL` once per event
regardless of bonus**; the **playable floor** (a 0-Discipline starter's total foul rate REPORTED for Emmett's
calibration eye). Magnitude is **reported, never suite-locked** (page-only).

---

## 5. Adversarial preamble — load-bearing assumptions to disprove (attacked at draft time where possible)

1. **"The reach-in rate reads no Discipline, only the team Hustle nudge."** Source: `RollBGenerator` foul-share
   block + `Matchup.TeamDisruptionShares` (2026-07-16 pull, `defensiveFoulNudge` the only foul term). *Alternate:*
   a Discipline read hidden inside `TeamDisruptionShares`' foul-share math. **Re-grep before rewiring** — a second
   Discipline read would double-count.
2. **"All six non-shooting charge sites funnel through `DefensiveFoulCharge.Resolve`, which names no one; the
   shooting incrementer (Resolver.cs:856) is the only other `Fouls.Increment` and is out of scope."** *Alternate:*
   a seventh non-shooting `Fouls.Increment` that bypasses Resolve — it would escape the emitted event and stay
   anonymous. **Grep every `Fouls.Increment` before claiming full coverage.**
3. **"The `flavor != null` discriminator cleanly separates reach-in (Roll D) from situational (I/J/K/M)."**
   Source: RollD passes a `flavor`; I/J/K/M pass none. *Alternate:* a Roll D path passing `flavor == null`, or an
   I/J/K/M path passing one — misclassifying the rate-governed portion and the situational-picker flag. **Confirm
   each of the six sites' flavor argument.**
4. **"Retiring the Hustle foul nudge + re-anchoring leaves the average team's reach-in rate ~unchanged."** The
   Hustle foul term fires only when the defense out-hustles (`max(0, −hustleGap)`) — 0 at equal Hustle, positive
   across a real season. *Alternate:* it contributes materially, so removing it and holding `Base` drops the rate.
   **Measure the average `defensiveFoulNudge` contribution at the gate** so the re-anchor absorbs it.
5. **"Attribution is emit-event + post-hoc draw (like `ShootingFoulEvent` → `DrawFoulingDefender` → `SFL`), and a
   Continue payload is NOT the attribution mechanism."** Source: §0.4. *Alternate:* a box-score path that would
   require the committer carried on `Continue` through all six branches (fragile — a dropped payload = anonymous
   foul). **The emitted event, atomic with the increment, is the guarantee; confirm the event survives to the
   box-score parse the way `ShootingFoulEvent` does.**

---

## 6. Delivery, docs, next

Per CONVENTIONS §3: code first → Emmett's harness green → docs → commit. Oracle + golden through the house
pattern. Docs pass updates journal (prepend), design.md (a new "Session 62" non-shooting foul section — the
per-man reach-in rate + the emit-event/post-hoc-draw committer attribution; update the S61 foul-door note that
non-shooting fouls now read Discipline and carry a name), status.md (Discipline Effect B → Built; the
Hustle-foul-nudge relocation to the coach layer recorded; the foul-out/substitution dependency logged; the
shooting-foul post-lean re-affirmed as parked — with the note that `DrawFoulingDefender`'s `SignedK` already
tilts it), attribute-meaning.md (the Discipline "fouls committed" half now wired per-man). Every dial page-tuned
later, never suite-asserted.

**Run the generated inventory / file listing at session start** — survey the surface before forming a hypothesis
(the S60.2 lesson).
