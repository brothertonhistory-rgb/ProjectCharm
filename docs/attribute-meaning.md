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

### The flaw the finding exposed (diagnosed, not yet fixed)

A rebound resolves in **two engine steps**, and they behave oppositely:

1. **Which TEAM gets the board** (`OffensiveReboundShare`): blends team-mean body (45%) and
   team-mean rating (55%). Here the freak's body works exactly right — a freak-body/zero-rating
   center lifts his team's rebound margin to **+2.1**. His body IS real in the team battle.
2. **Which of the five PLAYERS is credited** (`OffensiveRebounderPicker` /
   `DefensiveRebounderPicker`): each player's pick weight is
   `max(1, Rating × PositionalWeight × WingspanMultiplier × HustleMultiplier)`. The body enters
   **only as a multiplier on the rating.** A zero-rating freak's product is zero, floored to 1, so
   he draws ≈1/200 of his team's boards. His body helped the team win the board but gives *him* no
   claim to it. That is the entire 0.2.

So the physical tools pull weight in the team battle but the **individual selector floors the
no-rating body out**. This is the one and only per-player selector where a body attribute that
should confer standalone individual credit is wired multiplicatively.

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
  genuine skill regardless of size. The culprit is the picker's body-as-multiplier, not the team
  blend. (Recorded in status.md, Closed-by-ruling.)
- **The fix is single and well-scoped:** make the two rebounder pickers look like the block picker —
  give height, wingspan, and strength their own **additive** standalone terms so a freak body has
  individual rebounding pull independent of his rating. This is the S45 "obvious next session."
  Target archetype table for sign-off: freak-no-hands 0.2 → a sane 3–4, elite instinct holds, and
  weakling-no-hands stays ≈0.
- **Generation-layer corollary (logged, not acted on):** because the engine gives the body almost
  no *passive individual* rebounding floor, a tall player's board count lives or dies on his
  rebounding rating. So the eventual generation redesign must give tall players a rebounding-rating
  floor — otherwise a big with a low rebounding draw would rebound like a guard. (Note that a body
  floor added to the *picker*, above, changes the passive-floor picture and should be re-measured
  before the generation floor is sized.)
