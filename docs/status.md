# Project Charm — Status Board

The living done/to-do board. **Read this FIRST when planning any session** (CONVENTIONS §6a),
and update it in the docs step of every session (CONVENTIONS §3). Rules:

- Edited **in place**, like design.md — this reflects *now*; journal.md holds history.
- An item leaves **Open** or **Parked** only by **shipping** or by an **explicit ruling**
  (which moves it to Closed-by-ruling) — never by fading out of memory.
- Keep it short. This is a checklist, not a third journal. One line per item, with the
  session/phase that owns the detail.

Last updated: Session 53 (2026-07-12; **Cognition MEASURED — Family H, the sixth and FINAL haul, slot 1. The measurement arc is COMPLETE.** Two isolation walks (BasketballIQ, Discipline, 0→99 step 5, 2,000 games/rung) + a seven-row interaction block — 98,000 games, pure config-and-run (no engine file, no `config.json`, no readout change, no Monte Carlo). The slot-1 anchor held **exactly** (11.6/35.5/21.7% use/1.5 AST/2.6 TO at both 50-rungs and FLAT_50_CONTROL). One block, no slot-5 (Cognition has no physical term — no body×rating compound). The two ratings live on opposite surfaces: BasketballIQ an offensive read on the swept player's own box, Discipline a defensive read on the man he guards (slot-guards-slot diluted). Two wiring divergences caught at the gate, both confirmed whisper-scale by the data: BasketballIQ's assist coefficient (0.15) also feeds `LineupPassingFactor` (team assist *rate*, not just credit — a whisper, team AST +~0.2 full-walk); and the iqFactor read routes to Roll H's C4 passing-converter (a team make bonus), NOT the RollG/RollE attention-share tilt the prompt named (a whisper, ~+0.02pp team FG%). IqMakeSensitivity confirmed live (0.08). Findings: **BasketballIQ is a perimeter make-finisher plus a small assist claim, and the IQ-50 gate splits the two cleanly** — the make bonus is dead flat 0→50 then a perimeter-only hockey-stick (own 3P% 22.2→24.1, +1.8pp; own FG% 35.5→36.6; no rim gain, shot mix flat), while the assist claim is ungated and linear across the whole range (own AST 1.3→1.7, the weakest of the three assist inputs); **the MAESTRO/IQ_ONLY pair proves the split** (HIGH_IQ_MAESTRO AST 2.3 from the passing/playmaking, IQ_ONLY AST just +0.2 from the pure claim, both carrying the same IQ make lift — passing creates the assists, IQ finishes the shots); **Discipline is wired and real but below the instrument's resolution** (own box dead flat, Team B points flat at 52.9 across the whole walk — the read bends the foul rate only on the ~1/5 of Team B shots his man takes, and there is no Team B FTA column; the per-man effect is arithmetic-bounded at roughly −0.02 to +0.08 FTA/game, asymmetric — the hack-happy end moves more than the disciplined end — but unmeasured on this bench, consistent-with not measurement-of, the Family-G on-ball wall again). Cross-wire checks all clean (no below-knee IQ curve, no rim bonus, no Passing-sized AST swing, no Team B foul move under IQ, no own-FG curve under Discipline). Gen-redesign feed: BasketballIQ is a modest perimeter efficiency-and-credit rating (not volume or creation); Discipline is a low-magnitude defender-light foul-avoidance tap. **No new design gaps opened; no bench work of any kind remains.** Full findings: `docs/attribute-meaning.md`, Family H. Three configs shipped in `tools/sweep/`; all three runs green on Emmett's machine; full-suite green = the commit gate. **Next: the SYNTHESIS PASS — read `attribute-meaning.md` end-to-end before any engine work (Emmett's S49 ruling); no family remains.**). Prior: Session 52 (2026-07-12; **Defense MEASURED — Family G, the fifth and largest haul, on TWO slots.** Six isolation walks (PerimeterDefense/PostDefense/RimProtection/Steals/HelpDefense/OffBallDefense, 0→99 step 5, 2,000 games/rung) + a seven-row slot-1 interaction block + a four-row slot-5 interior block — 274,000 games, pure config-and-run (no engine file, no `config.json`, no readout change, no Monte Carlo). Both anchors held **exactly** (slot-1 walks + block A → 11.6/35.5/21.7% use; slot-5 block B → 9.4/34.3/49.8). Defense is the mirror family: the signal lands on **Team B's box** (B.PTS + teamB zone FG%), not the swept player's. Slot ruling: walks + perimeter/steals block on slot 1 (Steals' guard-weighted channel), interior block on slot 5 (real bodies for the body×interior-defense compound). Four wiring findings caught at the gate: OffBallDefense's retired team squeeze was *replaced* by a live-but-unmeasurable per-man denial; PostDefense is one-third of Postness (so its walk reclassifies the player as a big); RimProtection has an undiluted second-chance/putback team channel the map underweighted; the HelpDefense in-zone figure was 1.6pp not 3.7pp (team band ~0.45–0.6pp unchanged). Findings: **RimProtection is the only guard-your-man rating with a real readable team effect** (B.PTS 52.9→51.6, −1.3, via second-chance defense — the slot-guards-slot wall dilutes the on-ball make door to a whisper, leaving Perimeter/PostDefense nearly invisible at team scale); **Steals is claim-not-creation** (own STL 0.1→2.1 but opponent points AND turnovers flat — an attribution dial at neutral pressure, the S49 BallHandling mirror; turnover-forcing side pressure-gated, parked to coaching); **PostDefense mostly reclassifies a player as a big** (REB 7.7→8.7, STL 1.3→0.9, TO 2.6→2.3, opponent scoring −0.4; a synthesis note); **HelpDefense + OffBallDefense are the clean undiluted team-suppression pair** (four off-ball defenders, interior vs perimeter, B.PTS −0.6/−0.8, no personal footprint). Interaction reads: HELP_ANCHOR the cleanest team-suppression benchmark (B.PTS 51.8, −1.1); BALL_HAWK the claim-vs-create proof (2.2 STL, opponent points unchanged); DEFENSIVE_LIABILITY +2.1; ALL_G_ELITE −2.3; RIM_PROTECTOR −2.0 with the body×interior compound visible vs the bodiless walk row (BLK 1.2 vs 0.9); INTERIOR_WALL the biggest single-player suppression of the session (−2.5). Cross-wire checks all clean. Two new parked items (Steals' turnover-forcing side → coaching; PostDefense-as-size → synthesis). Full findings: `docs/attribute-meaning.md`, Family G. Eight configs shipped in `tools/sweep/`; all eight runs green on Emmett's machine; full-suite green = the commit gate. **Next: S53 — Family H, Cognition** (BasketballIQ, Discipline), Emmett's standing order, then the synthesis pass.). Prior: Session 51 (2026-07-12; **Interior offense MEASURED — Family F, the fourth and smallest haul, slot 5.** Two isolation walks (PostMoves, Screening, 0→99 step 5, 2,000 games/rung) + a seven-row interaction block — 98,000 games, pure config-and-run (no engine file, no `config.json`, no readout change, no Monte Carlo). The S48/S50 slot-5 anchor held **exactly** (9.4/34.3/49.8 at both 50-rungs and FLAT_50_CONTROL). Wiring confirmed at the gate: PostMoves has three reads (usage score, denial post-channel, whisper-scale gravity), Screening has one (an all-five aggregate, shooter included, halfcourt-only, all five zones — Phase 44 gate confirmed live). The missing transition/fast-break-share column was reported at the gate, so the Screening dilution was banded (+1.7–2.3pp) not computed. Findings: **Screening is the biggest team-wide single-rating channel on the bench** (team FG% 33.2→35.5, +2.3pp fully stacked, all five zones incl. threes 22.0→24.4; nothing individual — swept usage/STL/BLK dead-flat, his own points rise only through the team lift; rebound ripple down as the make-raiser signature); **PostMoves buys touches that arrive at a generic diet** (usage 12.7→23.9% on a curve steeper than Close's via two stacked ball-getting channels, but the shot mix never tilts inside — no post-hunt wire, the family's first-order design gap, now in the bundle; FG% 35.3→33.8 with no make term; personal TO 1.9→2.7 via involvement); **the tweener-post exists** (TWEENER_POST scored 13.5 PTS on 25.4% usage — a guard-sized post-skill player functions; the parked existence requirement CLOSED affirmative, generation redesign cleared to produce him). Interaction reads: POST_SCORER the dominant line (17.6/42.9%/28% use, tilts inside via Close/Fin not PostMoves), POST_HANDS_NO_FINISH below control (8.7, interior Hack-a-Shaq), SCREEN_BIG pure team value (9.8 personal but +2.2 margin, 10.8 REB). **One new design gap opened** (the no-post-hunt diet gap — see Open); the height-over-defender make term remains the S50 question that most gates the generation redesign. Full findings: `docs/attribute-meaning.md`, Family F. Three configs shipped in `tools/sweep/`; all three runs green on Emmett's machine; full-suite green = the commit gate. **Next: S52 — Family G, Defense** (PerimeterDefense/PostDefense/RimProtection/Steals/HelpDefense/OffBallDefense), Emmett's standing order, then H, then synthesis.). Prior: Session 50 (2026-07-12; **Physical package MEASURED — Families A + C combined, the third and biggest haul, back on slot 5.** Ten isolation walks (Height/Wingspan/Weight/Strength/Speed/Quickness/FirstStep/Vertical/Endurance/Hustle, 0→99 step 5, 2,000 games/rung) + a nine-row body×athleticism interaction block — 438,000 games, pure config-and-run (no engine file, no `config.json`, no readout change, no Monte Carlo). The S48 slot-5 anchor reclaimed and reproduced **exactly** (9.364/34.33/49.75 to the third decimal on all eleven runs). Three draft divergences caught at the gate: Roll A's slot-weighted aggregate reads athleticism AND LengthRating; displacement's physical term is ONE channel reading RAW athleticism (not fatigue-discounted — Endurance skips the diet bend); the denial-blend postness wake-up is invisible under Height on a flat bench (both gaps zero) and expresses only under Strength. Findings: **Weight is proven cosmetic** (21 byte-identical rungs, dial-applied proof via `meta_swept_value`, zero gameplay reads by whole-tree grep); **length creates AND claims blocks** (team blocks up + swept share up under Height/Wingspan/Vertical) **but buys almost no scoring** (Height 99 = +1.35pp FG%, all block-avoidance — no height-over-defender make term exists, the session's headline design question); the **postness suppressions fired as designed** on Height/Strength (TO blame 2.55→2.07; steal share 26%→14% with team steals dead flat — the StealerPicker channel, newly observed on a walk); Strength's interior double-up **lost** to the general suppression; the explosion trio buys ~+1.2pp FG% each (Vertical +2.2pp, the best); **Endurance is a whisper** (0 vs 99 = +0.2pp; MARATHON_MAN ≈ GASSED; aggregate-only — temporal shape needs a time-sliced bench, parked); **Hustle is textbook share-claiming** (boards 6.6→8.7, steals 1.1→1.4, team totals flat — first swept defensive-box mover of the pass). The marquee: **explosion buys points, length buys possessions** — POCKET_ROCKET outscores STIFF_GIANT (10.3/38.1% vs 9.1/34.0%) but the giant wins anyway on possessions; FREAK laps both (12.0/42.9%/11.9 REB, +6.4 margin on zero skill); PHYSICAL_FLOOR_15 is a genuine liability (−3.9). **Four design questions opened for Emmett's ruling** (see Open): the missing height-over-defender make term (gates the height→skill lean sizing — do NOT size the lean before this ruling), the light block channel, wingspan→steals unwired, Weight-cosmetic-or-not. Full findings: `docs/attribute-meaning.md`, Physical package. Eleven configs shipped in `tools/sweep/`; all runs green on Emmett's machine; full-suite green = the commit gate. **Next: S51 — Family F, Interior offense (PostMoves, Screening)**, Emmett's standing order, then G, H, synthesis.). Prior: Session 49 (2026-07-12; **Perimeter-creation family MEASURED — the second haul, on the point guard.** Five isolation walks (BallHandling/Passing/Playmaking/SelfCreation/OffBallMovement, 0→99 step 5, 2,000 games/rung) + an eight-row interaction block — 232,000 games, pure config-and-run (no engine file, no `config.json`, no readout change, no Monte Carlo). Measured on **slot 1** (the guard-heavy position weights are the only slot-sensitive channel; a post-vs-guard rationale offered at check-in was withdrawn as inert in a flat-clone world). Two draft divergences caught at the gate: SelfCreation has **two** engine reads not three (no putback-picker read exists), and BallHandling's Roll F/B mute is a config-scalar-equals-neutral mechanism (not a null-coach fallback), with **Roll A only** as the live channel. Findings: **SelfCreation is the volume king** (steepest usage curve in the engine, 13%→36% use, FG% 38→33 tax, ORB shooter-nerf); **OffBallMovement is getting-open** (usage 20→23% via the denial contest, no make read); **Passing/Playmaking attribute assists correctly and passing-dominant** (0.9→2.1 / 1.1→1.9), but the make-conversion bonus is **openness-gated and invisible on the flat bench** (parked with spacing — the predicted asymmetric FG% dip did not appear, corrected on record); **BallHandling reads INVERTED** — TO 1.6→3.2, the better handler charged MORE (BUTTERFINGERS BH10 = 1.8 TO vs PURE_POINT BH85 = 3.1) because it carries turnover *blame* (committer picker) but not *protection* (halfcourt matchup pressure-gated to zero at neutral). **Two engine reshapers surfaced (the run's real value):** (A) the missing **ball-dominance/initiation layer** — the engine models who shoots but not who holds/initiates the ball, capping passing magnitude and leaving handle-protection nowhere to live; the highest-value design conversation of the pass; (B) the missing **unforced-turnover channel** — a flat base TO rate identical for maestro and butterfingers, no offense-only "lost it yourself" rate. Both logged as design conversations that gate a "correctly valued" verdict on these ratings (do NOT over-size them in the generation redesign). The flat-50 bench is a below-*passing*-average world (S41 `AssistPassMidpoint` 71.31 → ~0.80× assist existence) — assist level is judged on the season page, never the bench, same logic as Family D's what-50-means note. BallHandling is **PARTIALLY measured** — the pressure-dialed test is parked to the coaching layer. Full findings: `docs/attribute-meaning.md` Family E. Six configs shipped in `tools/sweep/`; all runs green on Emmett's machine; full-suite green = the commit gate. **Next: S50 — the next attribute family (Body / Athleticism / Interior offense / Defense), Emmett's order rules.**). Prior: Session 48 (2026-07-11; **Scoring family MEASURED — the ruler's first real haul.** Six isolation walks (Close/Mid/Outside/Finishing/FreeThrow/FoulDrawing, 0→99 step 5, 2,000 games/rung) + a seven-row interaction block — 266,000 games, pure config-and-run: no engine file, no `config.json`, no readout change, no Monte Carlo. Every gate passed: FT% tracks the rating exactly (max dev 0.74pp; rating 0 → a literal 0.0%), all six 50-rungs + FLAT_50_CONTROL identical to three decimals (9.364 PTS / 34.33 FG% / 49.75 FT%) — seven runs, one deterministic answer — Team A ≈ B at control, rebound ripple down under every make rating (including the FT signature: FT=0 elevates boards to 39.3), STL/BLK and Team B flat everywhere. Direct lines pinned: personal 3P% 17.2→44.8 across Outside; team-zone Short 32.7→40.5 (Close), Mid 27.1→34.5, Long 24.8→31.1 + Three 22.8→28.7 (Outside, two zones as wired), Rim 52.8→59.7 (Finishing); FoulDrawing FTA 2.65→4.20 / FTr 0.246→0.406 with FGA falling 10.75→10.34 (the MissFouled denominator mechanism in the raw counts). Three mechanisms traced, all design-not-bugs: **usage follows skill** (~14%→22–23% use share for zone skills; flat for FT/FoulDrawing — the other half of the ratings-drive-efficiency-and-usage ruling, measured for the first time), the **diet tilt is the S36 displacement bend** reading the zone→skill map plus the Rim/Short gates (skilled players hunt their spots; the draft's 'no frequency effect' claim corrected to base-weights-only), and **gravity verified live as a byproduct** — the cross-zone Team-A FG% lift is ordered exactly by the GravityContribution weights (Finishing 0.35 biggest, Outside 0.05 ≈ nothing; Team B flat), explaining ELITE_RIM out-lifting ELITE_SHOOTER on team points (56.0 vs 55.5); magnitude calibration deferred to a real population, spacing untested by design. Hack-a-Shaq emerged: FoulDrawing 90 + FreeThrow 20 scores BELOW control (8.56 vs 9.36). SelfCreation ruled OUT at the gate (Family E — no make-curve role). Full findings: `docs/attribute-meaning.md` Family D. Seven configs shipped in `tools/sweep/`; all runs green on Emmett's machine; full-suite green = the commit gate. **Next: S49 — recommended Family E (perimeter creation)**, Emmett's order rules.). Prior: Session 47 (2026-07-11; **Sweep readout generalized to the full box — the ruler is trusted, every family is now config-and-run.** The `sweep` findings bench (`Program.Checks.AttributeSweep.cs`) went from a **rebounding-only** readout to a **full-box** readout: the swept slot's complete box (PTS/FG/3P/FT/ORB/DRB/REB/AST/STL/BLK/TO/SFL + rates + a `PossessionUse` proxy), both teams' aggregate box, and both teams' zone mix — all read from the eleven previously-unread fields of the **same** trusted `AttributeGame` call the sweep already made, so ownership (blocks/steals/DRB→defender) was already solved and nothing was re-attributed. Harness-only, no engine/config/math touched. The legacy rebound columns were kept **verbatim**; re-running the three S45 configs reproduced the S46 numbers **to the digit** (FLAT 7.69, freak-no-hands 4.86, average-no-hands 1.32, weakling-no-hands 0.67, weakling-elite 8.65, average-elite 12.27, freak-elite 17.54, RebΔ +2.1), and the new swept REB column equals the legacy S5.ORB+S5.DRB total on every rung — proving one reconciled source. Two honest findings surfaced and recorded (flat-50 FT% ~49.8%, flat-50 FG% ~34% — faithful reads, stable across rungs, an FT-curve calibration note for later, not bugs, not this session). Green on Emmett's machine (full suite + stress + three sweeps). **This is the ruler, not a measurement — no new family was swept.** **Next: S48 — Scoring family**, pure config-and-run against this readout (six scoring ratings in isolation + an interaction block, with a direct/secondary/suspicious causal framing so cross-stat movement is interpreted, not treated as a bug). Prior: Session 46 (2026-07-11; **Rebounder-picker body floor SHIPPED** — the S45-diagnosed bug is fixed. Both rebounder pickers (`OffensiveRebounderPicker`, `DefensiveRebounderPicker`) now weight each player `Luck + Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier + BodyPull × max(0, ReboundPhysical − lineupMean) + FloorCeiling × tanh(max(0, ReboundPhysical − FloorReference) / FloorScale)` (ORB side × shooter nerf on the whole weight) — the block-picker's additive body shape. **Luck** (5.0, flat) replaced the retired floor-of-1; **body pull** (0.35, relative) rewards out-sizing your lineup; **body floor** (ceiling 4.0 / scale 40 / reference 22.5, absolute + saturating) rewards raw size vs a fixed reference so a big target claims more random loose balls, tanh-capped so a genuine big doesn't balloon. Result on the `sweep`: a freak with a zero rebounding rating grabs **0.2 → 4.86 boards/game**, and the mushy bottom of the zero-rating height ladder separates cleanly (5'8 ≈1.2 → 6'4 ≈2.2 → 7'3+ ≈4.9; average-no-hands 1.32 vs weakling-no-hands 0.67); elite anchors held (freak-elite 17.54, weakling-elite 8.65 by ruling, average-elite 12.27), controls uniform, team margins unchanged (freak-no-hands +2.1 — pure attribution). The 55/45 team split was NOT touched. Green on Emmett's machine (full suite + stress + three sweeps). **Next: resume the attribute-meaning family sweeps** — aim the bench at Body or Athleticism. Prior: Session 45 (2026-07-11; **Attribute-Meaning layer opened** — the general `sweep` findings bench is BUILT and proven on Rebounding. One harness instrument (`Program.Checks.AttributeSweep.cs`, token `sweep`) that pins a flat all-50 world, walks one rating up 0→99 on one player (or runs named stress rows), runs N seeded real games per rung, and prints rating → real outcome; a generalization of `sizetest`/`athtest`/`deftest` on the S24 lab-bench builder, aimed by a live-path text config. **Rebounding finding:** rebounding is a rating-gated skill the body *amplifies but does not grant* — a freak body with a zero rebounding rating grabs the same ≈0.2 boards/game a tiny weakling does, because the individual rebounder picker makes the body a multiplier on the rating (floored to 1 at rating 0), not a standalone term; blocks are the correct *additive* template. **Ruling: the 55/45 rebound team split stays** (the culprit is the picker, not the blend). Harness-only, engine untouched, three proof runs green on Emmett's machine with all control anchors holding. **Next: the rebounder-picker body-floor fix** (give the pickers additive height/wingspan/strength terms, the `BlockerWeight` shape) — this redirects the stale "aim at the next family" line. Prior: Session 44 (2026-07-09; C# port Phase 2 — the LIVE skill-first generator is built and proven, **standalone**: `src/Charm.Engine/Core/Sampling.cs` (Beta/Gaussian/Exponential on `IRng`; Marsaglia–Tsang k≥1 only) + `PlayerGenPass2Live.cs` (the 40-slot draw loop calling the Phase-1 transforms; `BuildCohort` returns Draws+Result pairs) + the pure `ComputeHeightShape` extraction in the locked transform (re-proven bit-for-bit by Phase 59 every run) + five dormant Player seats (latent/current/runway/arrival/class, outside `Validate()`) + the Phase 60 gate (sampler moments at N=200k against closed forms, all four live Beta pairs; then eight design-invariant bands + determinism on the canonical 46k cohort). Green on Emmett's machine — all moments OK, all bands OK ([B] 0.597, [E] +0.004, [C] 5 giants, [F3] PostMoves 5.89% vs OBD 6.02% inside the −0.5pp band, [G] 25,825 recruitable), Phase 59 still 0 failures / 0.0 deviation, Phases 54/55 unchanged, `ALL CHECKS PASSED`. **Scope reshaped at the S44 draft audit:** enforcer deletion + the season-pool swap + the season re-check are NOT a port — the skill-first cohort is positionless and the divvy is quota-based — so they moved to **Phase 3**, which opens with the positions-from-orientation design conversation. Ruled at the gate: class variation is legal (zero 7'3"+ players is an honest draw). Prior: Session 43 (2026-07-08; C# port Phase 1 — the deterministic MATH ported and proven exact against the S42.2 fixture: 57/57 constants, 306 players / 51,714 checks / 0 failures / 0.0 deviation at absolute 1e-9). Prior: Session 42.2 (replay fixture + reference reader committed); 42.1 (oracle re-locked after three bounded fixes); 42 (skill-first oracle locked as the port spec); 41 (assists 13.7 OK, steals 6.5 OK, rebound instrument audited). Post-S41 ruling: OT-LOW parked under the coaching / late-game-strategy layer.

---

## 1. Built and live

Every roll has a **real generator**, and every meaningful execution path (observation run,
stress test, season, calibration, gen bench) wires all of them. There are **no outstanding
stub generators.** The stub classes that remain exist only for isolated harness regression
baselines and the legacy `game` demo command (see Open).

| Roll | What drives it |
|---|---|
| A — Entry | Press decision + slot-weighted matchup (P15) |
| B — Halfcourt initiation | Pressure + steals-vs-handling matchup (P13) |
| C — Turnover classification | Context ticket (halfcourt / transition / entry-backcourt); flat within context **by ruling** (§2) |
| D — Foul flavor | Flat **by structural necessity** (§2) |
| E — Player selection | Attribute-driven usage hierarchy (P15/19) + defensive attention (P27) |
| F — Player action | Pressure + matchup (P12) |
| G — Shot location | Matchup bend on residualized gaps (P9 + S36 Route B), coaching nudge (P30), usage diet shift (P17), attention amplifier (P28), **matchup displacement** (S36), **fast-break diet bent per shooter + PaceBias tilt** (S38) |
| H — Make/miss | Matchup make/block/foul doors (P6/7/8), IQ term (P50), fatigue-discounted athleticism (P49) |
| I — Rebound | Two-touchpoint matchup model (P10) |
| J — Transition run-or-not | Coach pace + team athleticism gap (P28/30) |
| K — Offensive rebound | Attribute-driven putback tilt, per-zone (P32) |
| L — Free throws | Authored FT rating (P18) |
| M — FT glass | Roll I's model on the FT board population (P11) |
| Offensive-foul flavor | Frontcourt/backcourt context; flat within context **by ruling** (§2) |

**Systems live:** fouls/bonus + jump-ball arrow (**all defensive fouls feed the team total — shooting fouls included as of S40**, so the 7-foul bonus arrives on real schedule); end-of-half intent; OT; fatigue meter +
athleticism discount + fatigue-fence substitutions; Governor + Resolver walk with full
per-possession counters; **court-aware turnover clock** (S37 — turnovers draw a short
court-dependent band, not the shared possession clock; page splits length by court);
**fast-break shot diet** (S38 — the break sets a modern base diet bent per shooter, PaceBias
tilts the three share; page reads the realized transition three share; oracle v1, 14-vector
golden parity, Phase 58);
shooting-foul / steal / rebound / block / assist attribution.

**Layers live:** player generation Pass 1 + skill-derived tendencies (oracle v2, 19-vector
golden parity; **S39 era-profile volume retune — population three diagnostic ~35.5% → ~39.0%**);
divvy Pass 1.5 (national pool + prestige draft); world Pass 1 (347 schools,
32 conferences); season Pass 2 (schedule oracle-fingerprinted, standings, calibration page);
Roll G displacement (oracle v1, 10-vector golden parity, Phase 56); observation / stress /
bench instruments; **attribute-sweep findings bench** (S45, token `sweep` — walks one rating up
its range on one player through the real engine, live-path text config; the attribute-meaning
layer's instrument. **Readout generalized to the FULL box in S47** — swept slot's complete box +
both teams' aggregate box + both teams' zone mix, all from the trusted `AttributeGame` call; the
legacy rebound columns reproduce S46 to the digit; **first family measured with it: Scoring (S48)**. The instrument is now a general ruler: every
future family is a pure config-and-run).
**Player generation Pass 2 — LIVE C# generator, standalone (S44):** samplers + 40-slot draw loop
over the fixture-proven Phase-1 transforms, Phase 60 statistical gate; produces honest positionless
46k cohorts on demand; NOTHING downstream reads it until the Phase-3 bridge (divvy/season still run
Pass 1 with its enforcers).
**Rebounder-picker body attribution — the S46 fix (LIVE, both pickers run every game):** `OffensiveRebounderPicker`
and `DefensiveRebounderPicker` credit the individual rebounder with a luck weight (flat, replaced the
floor-of-1) + a relative body pull (out-size your lineup) + a saturating absolute loose-ball floor (raw
size vs a fixed reference, tanh-capped) — the block-picker's additive shape. A freak body now corrals
boards on its own (0.2 → 4.86 boards/game at zero rating); the zero-rating height ladder rises cleanly.
The team battle (55/45 split) is unchanged. Details in Closed-by-ruling (the S46 shape) + `docs/attribute-meaning.md`.

## 2. Closed by ruling (looks unfinished — is not; do not "fix")

- **Turnover KIND stays flat.** Attributes drive how *often* a team turns it over (Rolls
  A/B/F own frequency), not what *kind* results. (Roll C docstring; the stub-era pressure
  parameter was retired on purpose.)
- **Roll D foul flavor stays flat.** Fires before Roll G — no zone stamped; slot may be
  null — no position context exists at its call time.
- **Offensive-foul flavor stays flat** beyond the frontcourt/backcourt split. Flavor is
  theater; nothing downstream reads it functionally.
- **The and-1 split (MafFraction) is per-zone, not matchup-aware.** Emmett's call.
- **Tendencies are deterministic in ratings** — same final ratings, same diet; the
  per-player style draw was rejected (S34). Volume differences are usage's job.
- **Held-ball losses stay off the turnover line** (S33 R1).
- **The tendency oracle's population-mean diet is a directional diagnostic only** — never
  a gate (S34/35).
- **DisplacementMaxMagnitude = 0 is ablation only** — it does NOT undo Route B; the
  residualized bend is ruled structure, not a dial (S36).
- **Turnover clock is court-aware bands, not a shifted center** (S37). The pace gap
  (65.5 → ~69) closed because turnovers stopped drawing a full possession's clock; the
  tempo Center stayed 17.0. Oracle-confirmed the center barely moves once the bands exist.
- **A possession that has offensive-rebounded is timed as a frontcourt turnover** (S37),
  regardless of the backcourt court-state flag transition / ball-advanced possessions
  carry — you cannot rebound in the backcourt. (`EffectiveTurnoverProfile`.)
- **The fast break sets a modern base diet bent per shooter, not a flat pie** (S38). The
  break dictates a base (Rim 0.57 / Short 0.08 / Mid 0.03 / Long 0.02 / Three 0.30) pulled
  toward the runner's own tendencies with a PaceBias three-share tilt; the null-shooter
  fallback is the flat base, NOT the half-court stub. The break reads RAW tendencies —
  coach shot-selection philosophy is deliberately NOT read on the break (only PaceBias
  tilts it). The `FastBreakMean{Zone}` denominators are pinned to the tendency oracle's
  population diagnostic, not free knobs. Locked oracle wins on any disagreement.
- **Roll K putbacks are excluded from fast-break FGA accounting** (S38). A transition
  possession's putback carries `FastBreak` forward but is rim-forced / putback-pie-resolved,
  so it never touched the fast-break diet; counting it would drag the reported transition
  three-rate below its true value. (`!c.Putback` guard.)
- **Three-point VOLUME is a generation/era-profile lever, not a runtime knob** (S39). The
  era profile's three multiplier + mid/long donors were retuned (Mid 0.50→0.44, Long 0.70→0.63,
  Three 2.10→2.44) to lift the population three diagnostic ~35.5% → ~39.0%, which landed the
  realized league **3PA rate 0.36 → 0.39 (OK)** and eased **aggregate FG% 45.3 → 44.9 (OK)** in
  one pass — a pure *selection* change, no make-curve touch. The `FastBreakMean{Zone}` denominators
  were re-synced to the new diagnostic (0.324/0.147/0.084/0.056/0.390) so the transition bend stays
  calibrated rather than double-counting the higher inclination into the break. Mid-range is the
  primary donor; rim is protected but not invariant. The oracle's multi-level structural gate now
  guards that archetype's identity, not a stale absolute three cap.
- **Shooting fouls count toward the team-foul total** (S40). A shooting foul now increments
  `FoulTracker` — a direct `_game.Fouls.Increment(state.Defense)` at the `ResolveShootingFreeThrows`
  resolution — so it feeds the opponent's bonus like every non-shooting defensive foul already did.
  Before S40 only non-shooting fouls counted, so teams crawled to the 7-foul bonus and the bonus-FTA
  bucket starved (FTA 15.8 / FT-rate 0.27, both LOW). Two increment sites (shooting here + non-shooting
  via `DefensiveFoulCharge`), **one** bonus-fork site (`DefensiveFoulCharge`); the paths are disjoint,
  so no double-charge. The shooter's own trip is never converted to a one-and-one (`oneAndOne: false`,
  never reads `BonusFor`) — the charge only moves future possessions' bonus reads. **Thresholds stay
  7/10** — the gap closed with foul *volume*, not an easier bonus. Proven by `ShootingFoulFeedsBonusCheck`
  (7th-foul boundary exact, no leak, no double). Payoff on the 347-page (seed 20260703, 5205 games):
  FTA 15.8 → 19.3 (t19.5, OK), FT-rate 0.27 → 0.34 (t0.34, OK), bonus share of FTA ~27% → ~42%;
  per-zone FG% all held in band, no shot-math regression.
- **The halfcourt turnover mix is 50/50 live/dead** (S41, Emmett's ruling — "to start", iteration
  anticipated). Base live .34→.50 (BadPassIntercepted .265 + LostBallLiveBall .235) and EntryBackcourt
  live .30→.50, dead menus scaled with shape preserved (each context sums to exactly 1.0); Transition
  was already 50/50, untouched. Weights-only, no Roll C code. Landed league **steals 4.5 → 6.5 (t6.2, OK)**;
  the realized `steals/turnovers` box proxy is ~47% (below the configured ~50% because Roll K live-ball
  turnovers are steal-less — the parked dilution, reported not fixed). Total turnover rate held at 13.8.
- **The assist lineup-passing midpoint tracks the generated population, not the scale midpoint** (S41).
  `AssistPassMidpoint` recentered 50 → **71.31** — the eligible-make-weighted mean lineup AssistWeight,
  solved by tanh zero-balance bisection so a league-average lineup earns factor 1.0. The placeholder 50
  assumed attributes center at 50; generated starters average ~71 (measured: mean factor 1.192 over 252k
  eligible makes), which had inflated assists to 17.4. The five `AssistedRate` zone bases were then trimmed
  a **uniform ×0.8909** (.88→.784 / .62→.5524 / .50→.4455 / .43→.3831 / .54→.4811), preserving the ordering;
  per-zone realized assisted shares stay near real-world (three ~78 / rim ~48). Landed **assists 17.4 → 13.7
  (t13.5, OK)** in one pass. The midpoint is a *relative-engine* dial: it follows the population it measures.
- **Player-generation Pass 2 is skill-first, no-gate, honest-draw** (S42, oracle locked; **re-locked S42.1**
  after three bounded fixes; the C# port is the next build). The locked oracle
  (`tools/gen_pass2_skillfirst_oracle.py`) is the port spec + calibration reference. Model rulings, hardened
  over five adversarial-review rounds (plus one S42.1 round) and now frozen:
  - **Three independent draws** (skill-quality, athletic-quality, specialization) + orientation; only three
    causal dependencies (orientation→height ceiling, size→athleticism landing, orientation→arrival/runway).
    Quality is body-independent: `corr(q, Height) ≈ 0`.
  - **Chosen-weapon specialization** (one identity weapon, glue skills excluded): broad at low s, spiked at
    high s (top1−top2 gap 34.6 vs 11.3).
  - **Weapon census offsets (S42.1 ruling — Option A):** the weapon is the argmax of
    `base[k] + WEAPON_CENSUS_OFFSET[k]` over the eligible set — the strongest eligibility-CORRECTED
    candidate. The S42 raw argmax had a census artifact (universally-eligible skills won
    disproportionately; "mid-range specialist" was the most common identity at 8.9%, post identity rarer
    than off-ball-defense identity). Offsets shift the argmax comparison only, never card math; the ruled
    default target is a near-flat identity census (~5.9% each; AFTER 5.70–6.02%, PostMoves ≥
    OffBallDefense). Proven by the [F3] paired-counterfactual census (both rules on identical pre-weapon
    states). **Rejected alternative: Option B, a random weighted lottery** (larger semantic change, not
    taken). The [F3] scaffolding (`weapon_raw`, `cf_player`) is oracle-audit-only and does NOT port.
  - **FT idiosyncrasy (S42.1 ruling):** ONE shared per-player draw, `gauss(0, FT_SIGMA=9.0)`, the SAME
    value in the latent-FT and current-FT derivations — a persistent shooter trait, not a second
    development axis. Restores the S29 "oddballs in the tails" ruling: the skilled low-FT hitch (Out≥70 &
    FT<50) runs 2–8 per 46k, the auto-line weak big (H≥71 & Out≤40 & FT>80) 13–22; rare-but-reachable
    passes, hitch-archetype judgment stays deferred to the season layer. [F4] audits are the proof.
  - **Population shape:** 7'3"+ giants ~3/cohort (Gaussian tail, not ~470); stretch bigs exist (~150 at
    S42.1 re-lock, was ~190), point-centers ~0 (shooting orientation-neutral, handle perimeter-locked);
    marquee unicorns rare (Wemby 1/6 sampled worlds, LeBron 5/6 at re-lock, was 3/6) — exact
    once-per-century framing deferred to the season layer.
  - **Baked potential / arrival / runway:** latent skill at birth, arrival suppresses current expression
    (guards developed, posts raw), runway = 21-skill latent−current vector.
  - **The recruiting line is an oracle-only downstream export proxy** (~25.7k recruitable at S42.1
    re-lock, was ~24.8k; six-seed mean 25,792, inside the 20–30k target with R_LINE untouched at 17.0),
    continuous and **orientation-weighted** (`perim_w=1−0.45·o`, `post_w=0.55+0.45·o`) — NOT a role table.
    A body or one weapon may amplify a pathway; neither substitutes for every tool. Rebounding is gated by
    interior skill (a rebound-only tall scrub stays below the line — demonstrated with printed rows in
    [G4] since S42.1, never asserted from prose); interior skill cashes by a height-access curve (~6'2
    inflection) plus a sub-6'0 taper (the whole 5'8"–5'9" tiny-post family clears zero times via the post
    path); Mid is access-gated (a lone midrange isn't a guard). Cross-path exceptions ~1.7%/1.1% (hybrid blur).
  - **Honest draw, no repair:** no redraws, competency repairs, rating floors, or role/position packages —
    the generator stays an honest cohort; the recruiting line is pure downstream selection.

- **The 55/45 rebound team split stays** (S45, Emmett's ruling; **the picker fix that it pointed to shipped in S46**). The `sweep` measurement proved
  rebounding is a genuine size-independent skill under the current blend (`ReboundSizeWeight` 0.45 /
  `ReboundSkillWeight` 0.55): a little-guy-with-great-hands out-rebounds a freak-body-no-hands 9.6 to
  0.2 at the individual grain. The freak-no-hands 0.2 was NOT a split problem — it was the individual
  rebounder picker flooring a zero-rating body to weight 1 (body a multiplier on the rating, not a
  standalone term). The split stayed; the picker was fixed in S46 (below). Full finding in `docs/attribute-meaning.md`.
- **Rebounder-picker body attribution — the S46 shape (Emmett's rulings across two design rounds).** Both
  pickers weight each player `Luck + Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier
  + BodyPull × max(0, ReboundPhysical − lineupMean) + FloorCeiling × tanh(max(0, ReboundPhysical −
  FloorReference) / FloorScale)`; the ORB side multiplies the **whole** weight by the shooter nerf. The
  rulings, all off the sign-off table (archetype table round 1, zero-rating height ladder round 2):
  (1) **`ReboundLuckWeight` = 5.0** — a flat, body-blind claim on random bounces, replacing the retired
  floor-of-1 (an inert player lands ≈0.7–0.9 boards/game, not 0.2). (2) **`ReboundBodyPullWeight` = 0.35,
  one-sided** — the *relative* body pull (out-size your lineup); a below-mean body gets zero, never a
  second penalty. A **signed** term was rejected (it dragged the weakling-elite below its window —
  double-penalty on the skilled small rebounder). (3) **The saturating loose-ball floor** (`ReboundBodyFloorCeiling`
  4.0 / `ReboundBodyFloorScale` 40.0 / `ReboundBodyFloorReference` 22.5) — an *absolute* body channel vs
  a **fixed** reference (not the lineup mean), tanh-saturated, added because the relative pull alone left
  an average body tied with a small one (both at their lineup mean). Gentle setting chosen over wider; a
  5'8 no-rebounder at ≈1.2 boards ruled correct (floor reference stays at the 5'2 extreme). (4) **On the
  offensive glass the shooter nerf multiplies the whole weight** (luck + body included) — the nerf models
  reduced availability after shooting, not a skill-specific penalty. (5) **Weakling-elite settling 9.0 →
  ≈8.65 is accepted** — the structural cost of letting average bodies compete (lifting them lets his
  average-bodied teammates take a sliver); every separating shape pays this, and 8.65 for a 5'6" elite
  rebounder is still elite. The **ORB→putback coupling** is accepted and noted (the picked offensive
  rebounder becomes the Roll K putback shooter — real basketball, a small second-order channel).
  Body composite is `ReboundPhysical` (same as the team battle). Full derivation: `tools/rebounder_body_floor_model.py`.

## 3. Open — next-session candidates

- **Attribute-meaning layer — the `sweep` bench is BUILT (S45), its picker bug FIXED (S46), and its
  readout GENERALIZED to the full box (S47); the remaining families are Open, the ACTIVE next track,
  and now pure config-and-run.** The instrument walks any rating up its range through the real engine
  and reads the full per-player box for both teams + team zone mix (see Built + design.md) — so no
  further instrument work is needed; each family is a light "aim + record" session. **D. Scoring is
  MEASURED (S48)** — six isolation walks + a seven-row interaction block, every gate green; full
  findings (the curves, the traced usage/diet-bend/gravity mechanisms, the open flags, the
  what-50-means note) live in `docs/attribute-meaning.md`, Family D. **E. Perimeter creation is
  MEASURED (S49)** — five isolation walks + an eight-row interaction block on **slot 1 (the point
  guard)**; SelfCreation is the volume king (steepest usage curve in the engine, 13%→36% use),
  OffBallMovement is getting-open (usage without a make read), Passing/Playmaking attribute assists
  correctly and passing-dominant (but the make-conversion bonus is openness-gated and invisible on
  the flat bench, parked with spacing), and **BallHandling reads INVERTED** — it carries turnover
  *blame* (committer picker) but not *protection* (the halfcourt matchup is pressure-gated to zero
  at neutral). **BallHandling is PARTIALLY measured** — see Parked for the pressure-dialed
  prerequisite. Two engine reshapers surfaced (see §3, ball-dominance layer + unforced-turnover
  channel). Full findings in `docs/attribute-meaning.md`, Family E. **A. Body + C. Athleticism are
  MEASURED as one Physical package (S50)** — ten walks + a nine-row interaction block on slot 5 (the
  S48 anchor reclaimed, exact); Weight proven cosmetic, length creates-and-claims blocks but buys
  almost no scoring (no height-over-defender make term — the headline design question), the postness
  suppressions fired as designed, Endurance a whisper, Hustle textbook share-claiming, and the
  marquee reads explosion-buys-points / length-buys-possessions (POCKET_ROCKET outscores STIFF_GIANT;
  FREAK laps both on zero skill). Full findings in `docs/attribute-meaning.md`, Physical package.
  **F. Interior offense is MEASURED (S51)** — two walks (PostMoves, Screening) + a seven-row
  interaction block on slot 5, the smallest haul; **Screening is the biggest team-wide single-rating
  channel on the bench** (+2.3pp team FG% fully stacked, all five zones, nothing individual, usage/
  STL/BLK dead-flat), **PostMoves buys touches that arrive at a generic diet** (usage 12.7→23.9% but
  the shot mix never tilts inside — no post-hunt wire, the family's first-order design gap, now in
  the bundle), and **the tweener-post exists** (a guard-sized post-skilled player scores 13.5 on 25%
  usage — the parked existence requirement CLOSED affirmative; the generation redesign is cleared to
  produce him). Full findings in `docs/attribute-meaning.md`, Family F.
  **G. Defense is MEASURED (S52)** — six walks + two interaction blocks (a seven-row slot-1 block +
  a four-row slot-5 interior block), the largest haul, measured on **two slots** because defense is
  the mirror family (the signal lands on Team B's box, not the swept player's); both anchors held
  exactly. **RimProtection is the only guard-your-man rating with a real readable team effect**
  (B.PTS −1.3 across the walk, via undiluted second-chance/putback defense — the slot-guards-slot
  wall dilutes the on-ball make door to a whisper, leaving Perimeter/PostDefense nearly invisible at
  team scale); **Steals is claim-not-creation** (own STL 0.1→2.1 but opponent points and turnovers
  flat — an attribution dial at neutral pressure, the S49 BallHandling mirror; the turnover-forcing
  side is pressure-gated, parked to the coaching layer); **PostDefense mostly reclassifies a player
  as a big** (one-third of Postness — his rebounds/steals/turnover-blame move, opponent scoring
  barely does; now a synthesis note); **HelpDefense + OffBallDefense are the clean undiluted team-
  suppression pair** (four off-ball defenders, interior vs perimeter, B.PTS ~−0.6/−0.8, no personal
  footprint; OffBallDefense's per-man denial channel is live-but-unmeasurable on flat clones). One
  full defender ≈ −2.3 opponent points, one interior anchor ≈ −2.5; the body×interior-defense
  compound is real (RIM_PROTECTOR with a frame beats the bodiless walk row). Full findings in
  `docs/attribute-meaning.md`, Family G.
  **H. Cognition is MEASURED (S53)** — two walks (BasketballIQ, Discipline) + a seven-row interaction
  block on slot 1, the sixth and smallest haul (anchor exact). **BasketballIQ is a perimeter make-
  finisher plus a small assist claim** — the make bonus is dead flat 0→50 then a perimeter-only
  hockey-stick (own 3P% +1.8pp, no rim gain), while the assist claim is ungated and linear (own AST
  1.3→1.7, the weakest of the three assist inputs); the MAESTRO/IQ_ONLY pair separates the two
  cleanly (passing creates the assists, IQ finishes the shots). **Discipline is wired and real but
  below the instrument's resolution** — own box flat, Team B points flat, the defender-light foul tap
  diluted 5:1 to the man he guards with no Team B FTA column (arithmetic-bounded at ~−0.02 to +0.08
  FTA/game, the Family-G on-ball wall again). Full findings in `docs/attribute-meaning.md`, Family H.
  **THE MEASUREMENT ARC IS COMPLETE** — six families (Scoring S48, Perimeter creation S49, Physical
  package S50, Interior offense S51, Defense S52, Cognition S53), every rating walked through the
  ruler; no family remains. **The next session is the synthesis pass** (Emmett's S49 standing ruling):
  read the completed attribute-meaning doc end-to-end and produce the cross-family reading the
  generation redesign has been waiting on — the height→skill and height→athleticism leans, the
  orientation channel for hybrids — before any engine work is scheduled. The questions it should now
  answer: the S50 height-over-defender make term (the one that most gates the height→skill lean), the
  S51 no-post-hunt diet gap, the S52 PostDefense-as-size coupling, the two S49 reshapers (ball-
  dominance/initiation, unforced turnovers); the pressure-dialed channels (BallHandling, Steals
  turnover-forcing) are coaching-layer, scoped out of synthesis.
  (Emmett's standing order: H done, then synthesis — the arc is finished.)
- **Four physical-package design questions opened by the S50 measurement — Emmett's rulings, logged
  with evidence in `docs/attribute-meaning.md` (Physical package), nothing touched.** Opened by
  Emmett's in-session read ("a 99 guy should be scoring over guys"): (1) **the missing
  height-over-defender make term** — a 99-Height player gains +1.35pp FG%, all block-avoidance;
  shooting over a smaller defender near the rim/post has no wire. **This ruling gates the
  height→skill lean sizing** — do NOT size the generation lean before it. (2) **The block channel is
  light even fully stacked** (FREAK 1.2 blk/g; a full length walk adds ~0.3 team blocks) — magnitude
  call, waits for a real population, flagged. (3) **Wingspan feeds steals nothing** — a wiring
  question. (4) **Weight is cosmetic** — feed Strength-adjacent channels or wait for a body-contact
  layer?
- **The no-post-hunt diet gap opened by the S51 interior-offense measurement — a design conversation,
  logged with evidence in `docs/attribute-meaning.md` (Family F), nothing touched.** PostMoves gets a
  post player the ball (usage 12.7→23.9% across a full walk) but the extra volume arrives at a totally
  generic shot diet — the mix never tilts inside, because the displacement diet bend reads
  `OffenseRating(zone, shooter)` (Outside/Mid/Close/Finishing only) and PostMoves is not in that map.
  The engine cannot express "the post player hunts post shots." The interior analog of the missing
  ball-dominance layer (below): the engine models who gets the ball inside but not that he then wants
  a specific *kind* of shot. Whether a PostMoves→interior-diet wire should exist is Emmett's call; do
  NOT over-size PostMoves in the generation redesign to compensate — its value is entirely downstream
  of the zone skills it feeds touches into (POST_SCORER vs POST_HANDS_NO_FINISH is the whole story).
- **Two engine reshapers surfaced by the S49 perimeter-creation measurement — design conversations,
  not yet scheduled.** Both are the reason BallHandling reads inverted and Passing/Playmaking read
  mellow, and both gate a "correctly valued" verdict on the perimeter-creation ratings (so do NOT
  over-size those ratings in the generation redesign to compensate — fix the engine, not the draws):
  (1) **The ball-dominance / initiation layer (the big one).** The engine models who *shoots* (the
  usage score) but not who *initiates* or *holds* the ball, so a great passer can only claim a bigger
  slice of a small fixed assist pie (PURE_POINT topped out at 2.3 assists) and ball-handling
  protection has nowhere to act. One subsystem unlocks both real assist volume and handle-protects-
  ball; intersects the coaching layer's ball-distribution dials. Recommended as the highest-value
  design conversation of the attribute-meaning pass. (2) **The unforced-turnover channel.** The base
  turnover rate is a flat constant identical for a maestro and a butterfingers; BallHandling only
  enters through a defense-relative matchup (muted at neutral pressure). No offense-only "lost it
  yourself" rate exists. The engine already names the split (dead-ball = self-inflicted; live-ball =
  stripped), so low BallHandling should drive the dead-ball rate directly, offense-side. Attribution
  sub-call for the build: lean unforced blame toward the *weaker* handle (the committer picker
  currently blames the best handler — correct for steals, backwards for dead-balls).
- **A separate assist calibration lever (S49, recorded not acted on).** The lineup-passing swing
  band is ±25% (`AssistPassSwing = 0.25`) — a ceiling on how much team passing skill moves assist
  existence (all-50 → all-99 team ≈ +50%; one player to 99 ≈ +9%). Whether ±25% is the right band is
  a future basketball call. Note the flat-50 bench runs assist existence at ~0.80× because
  `AssistPassMidpoint = 71.31` (S41) sits above the flat-50 mean — the below-pivot discount is the
  documented reason the assist *level* is judged on the season page, never on the bench.
- **FT-curve calibration observation (S47, parked, not owned yet).** The generalized readout showed a
  flat-50 FreeThrow rating makes **~49.8%** on the bench (stable across all rungs — a faithful read of
  a fixed input, not a bug). Whether a 50 FT rating *should* make ~50% vs a more college-like ~70% is a
  make-curve calibration question; recorded here so it isn't lost. **S48 charted the full line:**
  FT% ≈ rating exactly (max deviation 0.74pp over 21 rungs), and rating 0 makes a **literal 0.0%**
  over 2,000 games — a true zero exists, unlike the floored make curves (a 0-rated three still
  converts ~17%). The exact shape is on record for the eventual ruling; a calibration pass owns it.
- **S48 micro-flag (small, open).** At identical FoulDrawing 90, the FT-90 interaction row drew
  4.08 FTA/g vs the FT-20 row's 3.77 — ~8%, too large for noise at 2,000 games/row, mechanism
  untraced (candidate: missed-FT rebound/possession composition). Recorded in
  `attribute-meaning.md`; chase only if a later family moves it.
- **Season calibration page — three-point thread CLOSED at S39 (seed 20260703, stock world,
  5205 games).** The full three-point arc is now done across three sessions: S38 fast-break diet
  (transition three 5% → 34.5%), S38.1 make-curve (all five per-zone FG% on target), **S39 volume
  via the era-profile retune — 3PA rate 0.36 → 0.39 (OK, on target) and aggregate FG% 45.3 → 44.9
  (OK, back in band), together, in one pass.** No make-curve touch; a pure population-selection change.
  Per-zone FG% all held OK (rim 61.2 / short 42.8 / mid 39.3 / long 36.1 / three 33.9); fast-break
  page line 34.9% transition three, sane (Means re-synced). FG% sits at the high edge of its band
  (44.9 vs 44.0) — a mix result, noted not chased.
  **Remaining open page gaps (S41 347-page run, seed 20260703) — three-point CLOSED at S39, FTA/FT-rate CLOSED at S40, assists + steals CLOSED at S41:**
  **HIGH** — turnovers 13.8 (t12.5) / TO% 19.6 (mostly a pace echo, its own read); blocks 4.1 (t3.5).
  **LOW** — rebounds 30.7 (t34.5) incl. offensive 8.8 (now an **understood definition gap**, not a mystery —
  S41 audited the credited↔public reconciliation and it degraded to diagnostic-only; see Parked, team-rebound
  line). Also a small routing drift: FG% 45.0 sits a hair over its +1.0 band edge (more live
  turnovers → more transition → slightly more efficient shots; noted, not chased). **The front-runner is now
  the turnovers-HIGH pace echo or the blocks-HIGH attribution.** Assists-HIGH (17.4) and steals-LOW
  (4.5) were prior front-runners and are **closed by S41** (assists 13.7 OK via the midpoint recenter + uniform
  trim; steals 6.5 OK via the 50/50 turnover mix). OT-LOW moved to Parked (diagnosed; needs the coaching layer).
  Pick the gap before drafting; the next-session prompt is its own audited pass.
- **C# port of the Pass-2 oracle — Phases 1+2 BUILT (S43/S44); Phase 3 (the divvy/season bridge) is the active next build.**
  **Phase 1 (S43, DONE):** the deterministic MATH in `src/Charm.Engine/Core/PlayerGenPass2.cs`, proven bit-for-bit
  against the committed S42.2 fixture by the Phase 59 gate (57/57 constants, 306 players / 51,714 checks / 0
  failures / 0.0 deviation at absolute 1e-9); re-proven on every harness run. **Phase 2 (S44, DONE):** the LIVE
  generator, standalone — `Sampling.cs` (Beta via Marsaglia–Tsang two-gamma k≥1-only, Gaussian via the untruncated
  ClockDraw Box-Muller core, Exponential via inverse-CDF), `PlayerGenPass2Live.cs` (the 40-slot draw loop in fixture
  order; ONE height-noise draw either branch; `LivePlayer` Draws+Result pairs; `BuildCohort`), the pure
  `ComputeHeightShape` extraction (Phase-59-protected), five dormant Player seats outside `Validate()`, and the
  Phase 60 gate: sampler moments (N=200k, closed forms, all four live Beta pairs) then eight design-invariant BANDS +
  determinism on the canonical 46k cohort — bands, never the oracle's seed numbers (different RNG stream by design);
  the 7'3"+ band is 0–40 (zero legal by ruling), the [F3] census band is PostMoves ≥ OBD − 0.5pp (near-parity by
  design; strict ≥ false-reds ~30% of honest cohorts). Green on Emmett's machine; Phases 54/55 untouched.
  **Phase 3 (OPEN, the next build) — the bridge, reshaped at the S44 draft audit:** the skill-first cohort is
  positionless (no position, role, leg, or quota) and the divvy is quota-based (80G/60W/60B, coverage roles,
  opening-five shape), so the swap requires a **positions-from-orientation apportionment design that does not
  exist yet**. Phase 3 opens with that oracle-side design conversation BEFORE any code, then: swap the season
  talent pool to the Pass-2 cohort, delete `GenEnforceFloors`/`GenEnforceLegHealth` (both call sites —
  `Program.Divvy.cs:239-240` and `Program.Gen.cs:883-884`; the honest-draw ruling made executable), and re-check
  the season page against the S39–S41 calibration (assist midpoint, steal mix, rebound instrument all measured on
  the Pass-1 population — expect movement, re-measure before re-tuning). Preserve the frozen contracts (see
  Closed-by-ruling, S42 + S42.1). The one unexercised clamp edge (Height == 99) still rides. The Phase-3 prompt
  is its own audited pass.
- **Generation-layer bridge — now crossed for Pass 2.** S39 was the first population-selection change (era
  profile); S42 locked the full Pass-2 skill-first oracle on the same proven oracle→archetype-table→
  golden-parity workflow. The C# port (above) is the build that lands it in the engine.
- **Turnover-band calibration** — the court-aware bands shipped with **placeholder**
  centers/spreads (backcourt ~5s, frontcourt ~14.5s); tune them off the season page's new
  turnover-length-by-court split. Open question flagged S37: single-period transition /
  ball-advanced turnovers currently draw the short backcourt band — fine for a bring-up
  strip, arguably too short for an already-across possession; decide once the split's size
  and mean are on the page. (S37)
- **Bridge #3 — shooting-foul rate dial (PARKED, no longer urgent)** — the S40 bonus fix closed
  FTA to target with foul *volume*, so a shooting-foul-rate bump is not needed for FTA. If ever
  opened it carries three items: (1) a modest per-zone shooting-foul-rate bump **with** the make-curve
  re-derive (standing condition — if Roll H block/foul baselines move, rim/short midpoints re-derive);
  (2) the `FoulDrawing` half is **ANSWERED by the S48 sweep** — it enters only the foul-draw contest vs the defender's Discipline (FTA 2.65→4.20/g and FTr 0.246→0.406 across its walk; recorded FG%/3P% rise only compositionally as fouled misses leave the denominators); the where-shooting-fouls-are-called-per-zone piece stays open;
  (3) **late-game intentional fouling** — a trailing team hacking to extend the game, entirely unbuilt,
  a real separate FTA source. (S40)
- **Curve-steepness design conversation** — before any K moves; carries the finding that
  diminishing returns no longer exist inside the authored 0–99 range. (S32)
- **Displacement magnitude tuning** — only via the oracle-first flow (approve new oracle
  calibration → regenerate fixture → sync C# defaults + config → parity stays green). (S36)
- **`game` demo command** still stub-wired (self-documented) — upgrade to real generators
  or retire; micro-session or rides a session that touches Program.Game.cs anyway.

## 4. Parked — waiting on a named prerequisite

- **Steals' turnover-forcing side → the coaching layer** (parked S52, 2026-07-12). Steals was measured
  on the S52 bench at NEUTRAL pressure. Its one live *team-turnover* channel (Roll A, guard-weighted,
  pressure-independent) barely moves opponent turnovers, and the Roll B/F disruption channels are
  pressure-gated toward zero at neutral — the exact mirror of BallHandling's S49 finding. So the bench
  showed steal *attribution* rising cleanly (own STL 0.1→2.1) while opponent points and turnovers
  stayed flat: on this bench Steals is a **claim** dial, not a **create** dial. The turnover-forcing
  side needs defensive pressure dialed up (same `CoachProfile.Pressure` prerequisite as BallHandling).
  Until then Steals is measured as an attribution dial only.
- **The PostDefense-as-size coupling → the synthesis pass** (parked S52, 2026-07-12). PostDefense is
  one-third of `Matchup.Postness` (Height/PostDefense/Strength, equal thirds), so walking it 0→99
  reclassifies the swept player as a big — his rebounds rise, steal-share and turnover-blame fall —
  while opponent scoring barely moves (B.PTS −0.4). A "great post defender" rating currently reads
  more as "is a big" than "stops post scoring." Whether the generation redesign wants that coupling
  loosened is a synthesis-pass decision, not a bench fix.
- **Endurance's temporal shape → a time-sliced bench** (parked S50, 2026-07-12). The S50 whole-game
  aggregate readout made the *magnitude* verdict (a whisper: 0 vs 99 = +0.2pp FG%; MARATHON_MAN ≈
  GASSED) but has no period/time-slice columns, so *when* in the game fatigue lands is unmeasurable
  on this instrument. Source inspection confirms Endurance acts only through the fatigue meter
  (drain/recovery), live on every possession. Validating the late-game shape needs a time-sliced
  readout; whether the whisper is *correct* also depends on substitutions existing (nobody sits on
  the sweep bench).
- **Wingspan's JumpBall tip → a first-possession counter** (parked S50, 2026-07-12). The opening tip
  is won on max team wingspan (source-confirmed, `JumpBall.cs`), but no first-possession column
  exists and one tip/game is hopelessly confounded with Wingspan's own block/rebound effects on
  full-game totals. Wired-but-unmeasured until the readout grows the counter.
- **BallHandling's pressure-dialed test → the coaching layer** (parked S49, 2026-07-12). BallHandling
  was measured on the S49 bench at NEUTRAL pressure, which gates two of its three turnover-rate
  channels to zero (the Roll F halfcourt-individual matchup and the Roll B halfcourt-entry aggregate);
  only Roll A (fixed `StandardGate`, pressure-independent) reads it live. So the bench showed
  attribution rising while team turnover *rate* barely moved — BallHandling reads as a net negative
  (blame without protection). Its complete meaning needs a run with defensive pressure dialed up,
  which lights up Roll B and Roll F — and pressure is a coaching-personality knob (`CoachProfile` has
  no `Pressure` property yet; `CoachProfileFor` is a comment-only migration path). Until then
  BallHandling is **partially measured**. This is distinct from — but related to — the unforced-
  turnover channel reshaper (§3): the pressure test completes the *forced* side; the reshaper builds
  the *unforced* side.
- **OT-LOW → the coaching / late-game-strategy layer** (parked 2026-07-06). Overtime runs ~2.8% vs
  the 4–8% target. **Diagnosed, not a mystery:** the end-of-game intent is a flat, *score-blind* pie
  (HoldShootLast 0.70 / ShootEarly / NoShot) triggered by time-left only — the Governor's own comment
  calls it "score-blind... a future score-aware layer." So none of the real behaviors that funnel close
  games into buzzer-ties exist: a trailing team never fouls to extend, it even milks the clock 70% of the
  time *while losing* (actively killing its own comeback), and a tied team doesn't reliably hold for the
  last shot. These are coaching-personality knobs (aggressive coach fouls/pushes; conservative coach bleeds
  clock), so the fix belongs to the coaching layer, not a standalone dial — bolting a score-aware layer on
  now would be rebuilt when coaching profiles land. This layer is also the parked intentional-fouling FTA
  source (bridge #3 item 3). Before building, size the prize with a cheap page diagnostic: "games within one
  possession (≤3) at ~1:00 left → fraction reaching OT" vs the real conversion.
- **Reconciled team-rebound line** (S41) — the credited-rebound LOW is an understood definition gap:
  public 34.5 includes uncredited team rebounds. S41's C0 audit proved the candidate dead-ball endings
  (OOB-off-offense, jump-ball arrows, loose-ball-foul-on-offense, MissOutOfBoundsLost) are individually
  rebound-opportunity-only but **cannot be reconciled page-only** — `JumpBallArrow` labels carry no
  rebound-origin provenance (jump balls feed from Rolls A/B/F/I/J/K/M). A true team-rebound line needs
  **rebound-provenance instrumentation** (a counter stamping which held-ball/OOB endings arose from a
  Roll I/M rebound scramble). Until then the page prints the candidates as a NOT-reconciled diagnostic only.
- **Player-generation Pass 2 — oracle LOCKED (S42/S42.1); C# port Phases 1+2 BUILT (S43/S44); Phase 3 (the bridge) is the active Open build** (see §3).
  The skill-first generation model is frozen in `tools/gen_pass2_skillfirst_oracle.py` and the S42
  Closed-by-ruling entry; the deterministic math is now ported and fixture-parity-proven (S43). One design
  note still rides Phase 2, not the oracle: **weakest-leg multiplicative development** belongs to the
  development/season layer, not generation. (Formal notes in memory + journal.) The **tweener-post existence
  requirement** (guard/wing-sized players whose primary package is post play) is **CLOSED affirmative by the
  S51 measurement** — the bench's TWEENER_POST row (Height 15, PostMoves 90, Close 75) scored 13.5 PTS on
  25.4% usage: the engine already lets a guard-sized post-skill player function, so the generation redesign
  is cleared to produce him. (Full row in `docs/attribute-meaning.md`, Family F.)
- **Age/class population structure → the season layer** (parked 2026-07-07, S42.1). The oracle's age/class
  labels are a **placeholder projection of arrival** ("guards arrive developed" is currently implemented as
  "guards arrive old"; ready 18-year-olds ~0.03%). The season layer owns the real population structure, the
  one-class-vs-standing-pool question (deferred at S29), and the **ready-freshman existence requirement** —
  the ready freshman is a defining creature of modern college basketball and must exist there. Arrival is
  the ruled mechanism and ports as spec; the age/class labels are decoration on it and do NOT port as spec.
- **Roll G lineup-context bend** — teammate spacing/gravity as a *selection* effect
  (gravity/spacing attributes carried on Player, unread). Needs its own design conversation.
- **`Outside == 0` buzzer heave** at pie time — the only heave residual left after the
  universal capable floor (S35). Tiny.
- **Personality/timidity on the usage dial; strategy layers.** (S36)
- **Per-player attribution for held balls and Roll K turnovers** — records carry no
  slot/committer; aggregate-only today. (S33)
- **Press frequency / break rate game-level sentinels** — counter plumbing. (Named in the
  observation output's deferred block.)
- **Length-in-make% defender term** — a parked candidate wire, judged on its own merits,
  never a rescue knob. (S17/S40-era)
- **Per-zone location-blend weights** (top-3 blend varies by zone). (P9)
- **Corner vs above-the-break three split.** (P9)
- **Reference-card source pinning** — per line, when a tuning session needs a bullseye. (S31)
- **Multi-seed measurement** — blend seeds if per-zone drift proves material. (S31/32)
- **EqualShare centralization** — one shared constant across C1/C3/selection-tilt/Roll G. (P28)
- **Opening-five shape / lineup logic** — future lineup-or-coaching layer, not the divvy.
- **FT unattributed bonus-trip fallback** (pre-Roll-E trips use config MakeProbability 72%)
  — named loose end, ~0 volume on populated rosters.
- **Displacement "advantaged" observation bin** — empty on the even sentinel corpus by
  construction; revisit on a varied-population season page. (S36)
- **Code hygiene, parked:** WeightedAggregate duplication (A/B); harness `Mk`
  fixture-builder consolidation; RollEStubPieGenerator's internal double-build.
- **Long-term watches (design before the relevant layer ships, not now):** save-file
  schema versioning; end-to-end RNG/determinism review before the full season layer;
  the Player data layer at 21k+ actives; moddability. (working-with-emmett §7)
