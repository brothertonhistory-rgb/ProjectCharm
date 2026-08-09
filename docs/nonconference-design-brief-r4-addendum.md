# Non-conference design brief — r4 addendum

## THE SCHEDULE IS TWO WEIGHTED DRAWS

Ruled in conversation **2026-08-05**, after S102 shipped and its output was read.

> ★ **PROVENANCE — READ THIS FIRST.** This document was written on 2026-08-05 and
> **never landed in the repo.** The brief on disk stayed at r3, R18–R21 appeared nowhere,
> and every session from S103 to S106 was built against r3 — which is why the shipped
> matcher still runs R8's fixed bucket mixes that this addendum withdraws. Recovered and
> re-committed **2026-08-08** at Emmett's instruction, after he re-described the design
> from memory and asked why it had never been implemented.
>
> **The rulings below are Emmett's, quoted where the record preserves his words.** The
> connecting prose is a reconstruction, not the original text. Every measured number has
> been **re-measured against the committed stock world on 2026-08-08** rather than carried
> over — the S81.3 rule applies to a recovered document as much as to a stale one.
> **Emmett should read this back**; a reconstruction is not the original.

This addendum **supersedes** the fixed bucket mixes (R8's constants) and the "reach grab"
that was boarded for arc session 3. Both are **withdrawn**: they become the two axes below,
and the reach is simply the tail of the distance table.

Every non-conference request is a draw on two independent axes:

| axis | what it asks | keyed on | owner later |
|---|---|---|---|
| **quality** | how hard an opponent do I want | my prestige gap against **my own league's median** | coach temperament |
| **distance** | how far will I bring one in | my **conference tier** (standing in for the cheque book) | fixed by budget |

★ **NEITHER AXIS IS ABSOLUTE.** Quality is relative to your own conference, distance is
relative to your own shelf. Nothing here collapses to a rating.

★ **AND NEITHER AXIS IS A TARGET.** These are odds, not quotas. The point is that a hard
game is *possible*, not that it is aimed at. *(Emmett, 2026-08-08, correcting a proposal
to aim power schools at a 40–60 prestige band: "the target isn't 40-60, it's that it should
be possible.")*

---

## R18 — EVERY NON-POWER SCHOOL IS A BUY GAME

**Conference tier, not prestige, decides whether an opponent is bought or met.** To a
power-conference program, everyone outside the power conferences is a buy game — *"Utah
State is getting paid by OSU to go play in Stillwater. Same with North Texas."* Utah
State's prestige of 83 does not make it a marquee opponent; it makes it a good team taking
a cheque.

★ **AND THIS IS WHY IT MUST BE TIER.** Prestige will be dynamic — *"Utah State in forty
years might be a horrible program."* A model keyed to today's prestige silently changes
shape as programs rise and fall. Conference tier moves at realignment, not every season, so
a forty-year career keeps its structure while the programs inside it churn.

Stock world (re-measured 2026-08-08): **73 power, 64 high-mid, 65 low-mid, 145 low**
(131 conference members plus the 14 Independents, who file under `low`). A power school's
buy shelf is ~260 schools, not the 75 a prestige-under-25 filter produced.

★ **WHAT THE OLD FILTER WAS HIDING.** Oklahoma State's nearest buy-game options are Oral
Roberts (64 mi), Tulsa (64), Wichita State (107), North Texas (203), Missouri State (222) —
none a low-major, all excluded. Wyoming's third-nearest is **Utah State at 324 miles,
prestige 83**.

**Peers** — the schools a power program *meets* rather than buys — are power-conference
schools, plus anyone clearing roughly prestige 80 regardless of conference (Utah State
included).

---

## R19 — THE DISTANCE AXIS: RANKED SHELF, NOT MILES

**Bands are positions on each school's own list, never a mileage.** *"Two hundred and fifty
miles is just a placeholder because different schools will have far different numbers.
There's not very many schools within two hundred and fifty miles of the University of
Wyoming."*

The distance matrix already exists — S102 computes all 55,000 pairs every run — so slicing
a school's own shelf is free.

**Band sizes (nearest-first): 5 / 15 / 40 / 100 / the rest.** ★ Sized by COUNT at the top,
not by fifths.

**The weights concentrate toward home as the wallet thins.** *"The smaller of school you
are, the more concentrated those weights will be toward your home location. If Vermont has
enough money for, say, four buy games, there really isn't nearly the odds that they're
gonna bring in Portland State. The odds of, say, the University of Connecticut bringing
Portland State is low, but certainly higher, because their budget is completely different."*

★ **This is C-40 finally expressible.** "The tilt strengthens down the classes" has been on
the board since S101 and S102 could not honour it, because a strict nearest-first sort has
no strength to vary. A weight curve does.

★ **And it puts the causal direction the right way round.** In a buy game the host pays the
guarantee and the travel, so how far an opponent comes in is a fact about **the host's**
wallet, not the visitor's. The shipped engine has this backwards.

### R19a — THE BAND EDGE IS TAPERED, NOT A CLIFF *(Emmett's ruling)*

Measured on the pre-taper design: Missouri State at 222 miles is fifth on Oklahoma State's
shelf and comes 10.8 times in twenty years; SMU at 233 is sixth and comes 3.0. **Eleven
miles, three and a half times the frequency.** Emmett: *"It needs to be tapered if at all
possible."* Weight tapers across ranks inside each band — same band totals, no cliff.

### R19b — PRESTIGE FLATTENS THE CURVE; THE BIGGEST SCHOOLS HAVE THE FURTHEST REACH *(Emmett's ruling)*

*"The higher the prestige of the school is, probably flattens out those odds even more. The
biggest schools that have the furthest range."* Tier sets the base curve; prestige flattens
it further at the top. This is also the partial answer to the Gonzaga problem below.

### The distance anchors

- Continuous per-mile decay, **halving roughly every 140 miles**.
- Calibrated against **Oklahoma State–Tulsa recurring about 10 times in 20 years**, with
  real variance — *"some spans maybe 13, some 7, no hard fast rule."*
- Recurrence curve: smooth decay from roughly **65% at zero miles to a flat floor past 700
  miles**. **No ceiling, no memory, no artificial prevention** — recurrence is a roll, and
  small samples do what small samples do.

---

## R20 — THE QUALITY AXIS: YOUR GAP AGAINST YOUR OWN LEAGUE'S MEDIAN

**How hard a school wants to schedule is a function of how far above or below its own
conference's median it sits** — never an absolute rating.

*"The higher a team is prestige wise relative to the rest of the conference makes them want
to schedule harder. The lower a team is relative to the average of the conference makes
them wanna schedule it easier if at all possible."*

★ **What the shipped engine gets exactly backwards.** Northwestern is Marquee class by
conference floor at prestige 53, so it asks for the same mix as Duke. In reality Northwestern
schedules *softer* than Duke — it is the one facing eighteen games against a league median of
73, while Duke sits twenty-three points clear of its own. Those underwater programs are
precisely the ones spending November trying to get to 9-2 before the roof falls in.

**The country's gaps, re-measured 2026-08-08** (333 conference members):

| percentile | gap | | named schools | gap |
|---|---|---|---|---|
| 2nd | −27 | | Gonzaga (WCC) | **+41** |
| 10th | −19 | | Memphis (C-USA) | +32 |
| 25th | −9 | | Utah State (WAC) | +30 |
| 50th | **+0** | | Kansas (Big 12) | +25 |
| 75th | +8 | | Duke (ACC) | +23 |
| 90th | +17 | | Oklahoma State (Big 12) | +15 |
| 98th | +31 | | Drake (MVC) | −13 |
| min / max | −32 / +41 | | Northwestern (Big Ten) | −20 |
| at +25 or better | 14 of 333 | | Rutgers (Big East) | −30 |
| | | | Oregon State (Pac-10) | **−32** |

### R20a — A GRADIENT, NOT BUCKETS *(Emmett's ruling: "I love gradients whenever possible")*

There are **no quality classes.** A request draws a **target opponent quality relative to
the host** — how far below me I am aiming — and every candidate is weighted by how near it
lands to that target.

★ **It is the same weight function twice.** Distance decays with miles; quality decays with
prestige distance from the target. No Good/Solid/Easy, no boundary questions, no
"is it inclusive," no spill rule when a class has no legal candidate.

### R20b — THE COACH OWNS THIS AXIS LATER, AND THE TAIL IS THE POINT

*"If a coach wants the easiest possible schedule, they should have a ninety percent or maybe
higher chance to pull easy games in the basket. But there's still a small chance they might
get a kind of a tough game."*

★ **The 10% is load-bearing.** It is what stops every soft-schedule coach in the country
producing the identical slate, and it is what lets supply ration itself — *"since teams
can't get exactly what they want, that might be a way to divvy it out a little better."*
**The odds do the rationing, never a hard quota.**

Until the coach system exists, the gap alone drives the axis.

---

## R21 — THE BOTTOM DOES NOT SCHEDULE; IT ANSWERS THE PHONE

*"As for the lowest of the lows like Arkansas Pine Bluff, in my mind, the math of all these
other schools — the high majors, the mid majors, etcetera — needing home games is going to
stop up the vast majority of these lowest level schools' schedules before they even get a
chance to schedule their own."*

Pine Bluff has a **budget problem, not a scheduling philosophy.** This **confirms** S102's
top-down fill order rather than changing it: the quality axis only really bites for schools
that host meaningfully, and everyone else answers the phone.

---

## R22 — THE NEUTRAL ALLOWANCE RISES WITH THE GAP

*"Schools like Gonzaga, whose prestige is way higher than the average of their conference,
are not gonna be able to bring in very many high level home games at all. So they would
need to play a lot of neutral sites to compensate for their unique situation. Gonzaga is
gonna be very aggressive in non-conference scheduling because their conference schedule is
so weak, and the best way to get those games is high level tournaments and seeking high
level opponents at neutral sites."*

The gap does more work than R20 alone gave it. It sets how hard you want to schedule **and
where those games can physically happen**:

- **Big positive gap** — you need quality and cannot host it. Your good games are showcases
  and high-level tournaments. **The neutral allowance rises with the gap.**
- **Around zero** — a normal mix, some at home.
- **Deep negative** — you want winnable home games and have no use for a showcase at all.

Shape: a gradient, roughly `clamp(1 + gap/15, 0..5)` — Gonzaga averaging 3 with odds to roll
higher. Today the allowance is a flat constant per class, which is why Gonzaga and Duke both
get two.

★ **Emmett on the extreme:** *"+41 is extreme and really isn't likely to happen much I'd
wager in a dynamic prestige save file over the course of 30 years."* The stock world's
+41 is a starting-state artifact, not a permanent fixture — 14 of 333 schools sit at +25
or better.

★ **Gap-driven EVENT SEATING is a separate piece of work** from the matching. Seats already
band on prestige, so an 85 qualifies for the top brackets — Gonzaga simply was not drawn.
Whether a big positive gap should make a school *actively hunt* a seat is a change to the
seating, queued behind the shelf session, not inside it.

---

## What this withdraws

- **R8's fixed bucket mixes** (the per-class 5-easy / 2-working / 1-decent shapes). Live in
  the shipped code today; the cause of Northwestern asking for Duke's slate.
- **The "reach grab"** boarded for arc session 3 — it is simply the tail of the distance
  table.

## Still open before a build prompt can be drafted

1. ★ **THE QUALITY CURVE HAS NO NUMBERS.** R20 says which direction a school leans and by
   how much it is underwater; nothing yet says what Oregon State at −32 actually draws
   versus Kansas at +25. **This is the archetype table Emmett rules on** — named schools,
   their gap, and the odds each draws across opponent quality — and it is the last thing
   standing between here and a prompt.
2. **The distance weight tables** — do the four tiers separate hard enough?
3. **Gonzaga is where the tier proxy breaks.** The WCC is low-mid, so Gonzaga draws the
   concentrated curve and would play Eastern Washington seventeen years in twenty. Gonzaga
   has money; its conference does not. Tier is right for ~340 schools and wrong for about
   five. R19b (prestige flattens the curve) partially answers this; whether it answers it
   *enough* is unruled. The real fix is a budget model, and that is a long way off.
