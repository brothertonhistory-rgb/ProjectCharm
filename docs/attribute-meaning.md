# Project Charm — Attribute Meaning (the findings layer)

**What this is.** A record of what each rating actually *does* across its 0–99 range, measured
by running the **real possession engine** — not asserted, not hand-calculated. Every number here
was produced by the `sweep` bench (see design.md, "The attribute-sweep findings bench"): a flat
all-50 world, one rating walked up its range on one player, N seeded games per rung, real
outcomes tabulated. This is the currency the generation-tuning questions ("is a 50 in this rating
good? are tall players too skilled?") had no way to answer, because "good" had no definition.

**How to read it.** Each family section records the finding in plain terms, the shape of the
curve (where the rating does nothing / kicks in / saturates), any engine flaw the measurement
exposed, and any ruling or generation-layer note that fell out. Findings are descriptive of the
engine *as it is now*; when a later session changes the engine, the affected finding is
re-measured and this doc is updated in place (journal.md holds the history).

**Method note — measurement, not tuning.** A sweep run never changes an engine constant. A "the
balance should move" conclusion is a finding recorded here for a future build session, not a
change made in the sweep session.

---

## Family B — Rebounding (OffensiveRebounding, DefensiveRebounding) — measured Session 45

The first family measured. Two ratings, and it carried a sharp test: **is rebounding made
useless by the physical attributes, or is it a real skill?** Run as two isolated sweeps
(OffReb 0→99, DefReb 0→99, 5-point steps, everything else pinned at 50) plus an eight-row
interaction block that deliberately moves body and rebounding together. 2,000 games per rung.

### The headline finding

**Rebounding is a rating-gated skill. The body amplifies it but does not grant it.** The swept
center's total boards per game:

| The center | Boards/game |
|---|---|
| 5'6" weakling (body 15), **no** rebounding instinct (rating 0) | 0.2 |
| Average body (50), **no** instinct (0) | 0.2 |
| 7-foot freak (body 99), **no** instinct (0) | **0.2** |
| 5'6" weakling, **elite** instinct (99) | 9.6 |
| Average body, **elite** instinct | 12.9 |
| 7-foot freak, **elite** instinct | 17.3 |
| Average body, average instinct (the flat-50 baseline) | 7.7 |

Two things to read off it:

1. **The rating is the switch.** The top three rows are identical at 0.2 — a 7-foot freak with no
   rebounding rating grabs the same near-nothing a 5'6" weakling does. Body alone gives a player
   almost no *individual* boards. The head-to-head Emmett asked for isn't close: little-guy-with-
   great-hands **9.6** vs freak-body-no-hands **0.2**.
2. **The body is a real amplifier once the rating is present.** Down the elite-instinct rows,
   9.6 → 12.9 → 17.3: a bigger frame converts the same rebounding rating into ~34% more boards
   (average → freak). With no rating there is nothing to amplify (0.2 either way). This is the
   multiplicative "weakest-leg" relationship the design aimed for, working: the rating decides
   whether you rebound at all; the body scales it once you do.

### The curves (isolated sweeps)

Both ratings pay off **continuously and near-linearly across the whole 0–99 range** — no dead
zone at the bottom, no saturation at the top. The swept center's own boards per game:

- **OffensiveRebounding:** 0.06 (at 0) → 2.20 (at 50) → 3.66 (at 99). Team offensive-rebound rate
  moves modestly (27.6% → 28.7%) because the swept player is one of five potential rebounders.
- **DefensiveRebounding:** 0.13 (at 0) → 5.49 (at 50) → 9.30 (at 99). Slightly steeper; team
  defensive-rebound rate 70.9% → 72.6%.

The near-zero floor at rating 0 (0.06 / 0.13, not literally 0) is the picker's floor-of-1 term,
explained below — it is the whole reason the freak-no-hands case is 0.2 and not higher.

### The flaw the finding exposed — FIXED in S46

A rebound resolves in **two engine steps**, and before S46 they behaved oppositely:

1. **Which TEAM gets the board** (`OffensiveReboundShare`): blends team-mean body (45%) and
   team-mean rating (55%). Here the freak's body works exactly right — a freak-body/zero-rating
   center lifts his team's rebound margin to **+2.1**. His body IS real in the team battle. (Unchanged
   by S46 — the 55/45 split was ruled correct and left alone.)
2. **Which of the five PLAYERS is credited** (`OffensiveRebounderPicker` /
   `DefensiveRebounderPicker`): *before S46* each player's pick weight was
   `max(1, Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier)` — the body entered
   **only as a multiplier on the rating**, so a zero-rating freak's product was zero, floored to 1, and
   he drew ≈1/200 of his team's boards. His body helped the team win the board but gave *him* no claim.
   That was the entire 0.2.

**The S46 fix (the block-picker's additive shape) gave the body two standalone channels.** The pick
weight is now `Luck + Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier
+ BodyPull × max(0, ReboundPhysical − lineupMean)
+ FloorCeiling × tanh(max(0, ReboundPhysical − FloorReference) / FloorScale)` (the ORB side × the
shooter nerf on the whole weight). **Luck** (5.0) is every slot's equal claim on random bounces — it
replaced the retired floor-of-1, so an inert player collects the garbage boards a body-blind floor
should give (weakling-no-hands ≈0.7, average-no-hands ≈1.3). **Body pull** (0.35, *relative*) rewards
out-sizing your own lineup; **body floor** (ceiling 4.0 / scale 40 / reference 22.5, *absolute* and
*saturating*) rewards raw size against a fixed reference — a big target loose balls find regardless of
teammates — tanh-capped so a genuine big doesn't balloon. The absolute floor was added (S46b) because
the relative pull alone left an average body tied with a small one (both sit at their lineup mean and
earn nothing from a relative term); the floor un-flattens the mushy bottom of the zero-rating height
ladder into a clean rise (5'8 ≈1.2 → 6'0 ≈1.6 → 6'4 ≈2.2 → 7'3+ ≈4.9).

**The head-to-head, after S46 (swept center total boards/game, `sweep` interaction, 2,000 games/rung):**

| The center | S45 (before) | S46 (after) |
|---|---|---|
| 7-foot freak, **no** instinct (rating 0) | 0.2 | **4.86** |
| average body, **no** instinct | 0.2 | 1.32 |
| 5'6" weakling, **no** instinct | 0.2 | 0.67 |
| 7-foot freak, **elite** instinct | 17.3 | 17.54 |
| average body, **elite** instinct | 12.9 | 12.27 |
| 5'6" weakling, **elite** instinct | 9.6 | 8.65 |

The freak's body now earns him boards on its own (0.2 → 4.86); a bigger body separates cleanly from a
smaller one even at zero rating; and the elite anchors held (the weakling-elite slipped 9.6 → 8.65 by
ruling — the structural cost of letting average bodies compete). Team margins are unchanged (freak-
no-hands still +2.1) — the fix was pure individual attribution, not team-total movement. This was the
one and only per-player selector where a body attribute that should confer standalone credit was wired
multiplicatively; the cross-selector audit below confirmed no other picker needs it.

### The cross-selector audit (all seven pickers, S45)

The floor-of-1 shape (`max(1, base × multipliers)`) repeats, but the shape is only *wrong* when the
thing being multiplied is a body attribute that should stand on its own. That is true for exactly
one stat.

| Counting stat | Should a pure freak body earn this individually? | Body given standalone pull in the selector? | Verdict |
|---|---|---|---|
| **Rebounds** (O + D) | **Yes** — a giant corrals boards at the rim | **No** — body only multiplies the rating; zero rating floors out | **Broken** |
| **Blocks** | Yes — length + hops swat shots | **Yes** — Height, Wingspan, Vertical are *added in* on their own | Correct (**the template**) |
| Steals | No — hands and anticipation, not size | n/a — base is the steal rating, no body term denied | Correct |
| Assists | No — passing, not size | n/a — passing-driven | Correct |
| Turnovers (lost ball) | No — handling; a *blame* stat | n/a | Correct |
| Interior turnovers | Body already *is* the driver (Strength-based) | Yes — Strength is the base | Correct |

**Blocks are the correct template.** `BlockerWeight` is additive:
`BlkHeight·Height + BlkWingspan·Wingspan + BlkVertical·Vertical + BlkRimProtection·RimProtection + …`
— so a 7-foot freak with a zero shot-blocking rating still swats shots on body alone. Blocks and
rebounds are the two stats where a big body should earn standalone individual credit, and the engine
wired them **oppositely**. The other selectors look structurally identical but aren't a problem,
because steals/assists/turnovers *shouldn't* hand a giant free credit for being tall.

### Rulings and notes that fell out

- **Ruling: the 55/45 rebound team split stays.** It delivers the design goal — rebounding is a
  genuine skill regardless of size. The culprit was the picker's body-as-multiplier, not the team
  blend. (Recorded in status.md, Closed-by-ruling.)
- **The fix shipped in S46 (single and well-scoped):** the two rebounder pickers now carry the block
  picker's additive body shape plus a luck weight and a saturating loose-ball floor (see the FIXED
  section above). Validated on the `sweep` bench — freak-no-hands 0.2 → 4.86, elite anchors held,
  average-no-hands (1.32) cleanly above weakling-no-hands (0.67). The signed sign-off was against the
  archetype table (round 1) and the full zero-rating height ladder (round 2, the S46b floor).
- **Generation-layer corollary — RE-EVALUATE after S46 (the passive-floor picture changed).** The
  S45 note said a tall player's board count lives or dies on his rebounding rating, so the generation
  redesign should give tall players a rebounding-rating floor. **S46 changed this:** a body now confers
  standalone individual rebounding pull independent of rating (the additive pull + the saturating floor),
  so a big with a low rebounding draw no longer rebounds like a guard on body alone. Re-measure the
  passive-floor picture on the live pickers **before** sizing any generation-layer rebounding floor —
  it may now be smaller than the S45 note assumed, or unnecessary.
