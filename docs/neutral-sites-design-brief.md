# Design Brief — Neutral Sites and the Proximity Advantage

**Status: RULED, NOT BUILT.** Every design question below is closed except two named
numbers. Nothing here is wired, because nothing in the engine can currently *create* a
neutral game — the session that builds non-conference scheduling is the session that makes
this buildable, and this brief is what it inherits.

**Owner of the rulings:** Emmett, 2026-08-03, in the design conversation following S95.
**Board item:** O-83. **Prerequisite:** a schedule that can say where a game is played.
**Depends on:** S92's map (distances exist, in miles) and S95's road shave (the penalty this
scales). **Blocks:** nothing — the engine is correct today without it.

---

## 1. Why this cannot be built yet, stated plainly

A scheduled game carries a home id and an away id and **nothing else**. There is no site
field and no host field. Every game in the world is a conference game, and **conference
games are never neutral** (ruling 1 below). So there is not one game in the engine today
that this model could run on.

It *can* be written as a pure function and proven against fixtures — that is exactly how
S92's distance function shipped. What it cannot do is be exercised, seen on a page, or
judged by eye. S95 deleted a seam that had sat built-and-unwired since Phase 18
(`RoadMakePenalty`) precisely because an unwired seam eventually becomes a thing that
misleads whoever reads it next. **Emmett's call: capture the design now, build it with
non-conference.**

---

## 2. The rulings (closed — do not re-derive)

1. **★ CONFERENCE GAMES ARE NEVER NEUTRAL.** Neutral and pseudo-home sites exist only in
   non-conference play, multi-team events, and postseason tournaments. This is what protects
   S93's R3 — every team hosts exactly half its league season — from ever being disturbed by
   a site fact.

2. **★ NEUTRAL MEANS NOBODY IS PENALISED.** Both teams at full strength. The effect is
   purely *relative*. Ruled on research, against Claude's stated lean: the literature treats
   neutral as the **midpoint** between home and away rather than as a shared tax (van Bommel
   et al., 100,000+ NCAA games); home is measured as roughly 3–4 points per 100 possessions
   *above* a neutral baseline; and an Atlantic-10 study found game location by itself does
   not create the advantage — crowd atmosphere does. If it is the noise and not the
   building, two teams in a quiet gym are not being taxed by anything.

3. **★ EACH TEAM'S CROWD PRESENCE IS COMPUTED INDEPENDENTLY, NEVER AS A DIFFERENCE OF
   DISTANCES.** This is the load-bearing structural choice. A difference model hands the
   closer team an edge in Seattle for being 700 miles nearer than an East Coast opponent,
   when in truth neither fanbase travels that far. Absolute decay per team gives *"the
   further you get, the more it equalises again"* for free.

4. **★ PROXIMITY IS A STRAIGHT LINE, NOT A CURVE.** A finite pot of proximity draining at a
   constant rate per mile, floored at zero somewhere around 400–500 miles. No acceleration,
   no tail. *(Emmett explicitly withdrew an earlier "the fade accelerates outward" framing in
   favour of this — do not rebuild the curve he replaced.)* The "600 miles is the same as 900
   is the same as 10,000" property falls out of the **floor**, not a curve. Every mile costs
   something from mile one.

5. **★ PRESTIGE IS FAN AMOUNT, AND IT IS THE SECOND INPUT.** Proximity says how far the fans
   must come; prestige says how many exist to come at all. Without it, two schools
   equidistant from a site are a dead heat **by construction**, which is wrong — the bigger
   program wins the building. This is S92's **R4 arriving intact**: the crowd is PRESTIGE and
   DISTANCE, and there is no city size (population, market size and arena capacity are ruled
   out by name — *"a small town college can have an incredible homecourt advantage"*).

6. **★ A PSEUDO-HOME GAME CAN NEVER REACH A TRUE HOME GAME'S VALUE**, however good the
   crowd. They still travelled, still slept in a hotel, still do not know the rims.

---

## 3. ★ A CORRECTION TO O-83, MADE HERE

O-83's third point was written as *"each team's penalty is the road shave reduced by its own
crowd presence at that site."* **That wording is wrong and contradicts ruling 2.** Read
literally it means both teams carry a penalty at a neutral site, reduced only by whatever
crowd each has — so two teams in Seattle would both eat the full road shave, which is exactly
the both-penalised model Emmett ruled out.

What ruling 3 actually means is that **the two presences are computed independently**, and
the *advantage* is the gap between them. Only the team with less crowd pays anything.
Correct statement:

> Compute each team's crowd presence at the site on its own. The team with the smaller
> presence takes a penalty proportional to the gap; the other takes nothing. Two teams with
> no presence both take nothing — that is a neutral game.

O-83 should be amended to this wording when this brief lands.

---

## 4. The model

```
presence(team, site) = max(0, 1 − miles(team.home, site) / D) × fanAmount(team.prestige)

gap        = |presence(A) − presence(B)|
penalty    = RoadShave × CrowdShare × gap        ← applied to the LOWER-presence team only
                                                   the other team takes 0
```

A **hosted** game is a separate branch and is unchanged by any of this: the visitor takes the
full `RoadShave`, the host takes nothing.

Ruling 6 holds automatically as long as `CrowdShare < 1`: the largest possible gap is 1, so
the largest possible pseudo-home penalty is `RoadShave × CrowdShare`, which is strictly less
than a true road game's `RoadShave`.

### ★ The shave must become fractional

`RoadShave` is **3**. If a pseudo-home penalty can only be a whole number, this entire model
collapses into three buckets with hard edges, and "every mile costs something" becomes
decorative.

The fix does not need a bigger dial: **spread a fractional shave across the 23 skill
ratings.** A penalty of 1.5 means roughly half a man's skills drop 2 and half drop 1. Across
23 ratings that reads as a genuine 1.5, it is fully deterministic, and it gives real
resolution — 60 miles is distinguishable from 90 if anyone wants it to be. **This is a
prerequisite change to S95's applicator, not a nice-to-have**, and the allocation rule must
be deterministic and order-stable (the same player at the same penalty always loses the same
ratings) or two identical seasons will not reproduce.

---

## 5. The two open numbers

Both are live when the build session opens. Neither is guessed at here; the tables in §6
exist so they can be ruled by reading rather than by argument.

**`D` — where the proximity pot hits zero.** Emmett's band: **400–500 miles**. The tables
below use **450** as the working value.

**`CrowdShare` — how much of home court is the crowd rather than the building.** Unruled.
This is the cap on a pseudo-home game, expressed as the fraction of the road shave that a
perfect crowd can erase. Candidates: **1/3** (the gym and the routine are most of it),
**1/2**, or **2/3** (the whistle and the noise are most of it, and those travel).

A third, smaller question hides inside ruling 5: **what maps prestige to fan amount?** The
tables use `prestige / 100` — a 97-prestige school brings 97% of the pot it could, a
60-prestige school 60%. That is the simplest thing that satisfies ruling 5 and it may be too
generous to low-majors; a curve that punishes the bottom harder is defensible. Named so it
is ruled rather than inherited.

---

## 6. The archetype tables

**Every distance below is real** — computed from the committed world file's coordinates with
the engine's own great-circle model, not invented. Prestige is each school's authored
`currentPrestige`. Working values: `D = 450`, `CrowdShare = 2/3`, `RoadShave = 3`.

### Oklahoma State (87) vs Boston College (77) — the case that generated the model

| site | OkSt miles | presence | BC miles | presence | penalty | who pays |
|---|---|---|---|---|---|---|
| Oklahoma City | 53 | 0.77 | 1,485 | 0.00 | **1.54** | Boston College |
| Tulsa | 64 | 0.75 | 1,385 | 0.00 | **1.50** | Boston College |
| Dallas | 233 | 0.42 | 1,540 | 0.00 | **0.84** | Boston College |
| Kansas City | 245 | 0.40 | 1,239 | 0.00 | **0.80** | Boston College |
| Denver | 495 | 0.00 | 1,757 | 0.00 | **0.00** | nobody |
| Seattle | 1,511 | 0.00 | 2,479 | 0.00 | **0.00** | nobody |
| New York | 1,289 | 0.00 | 180 | 0.46 | **0.93** | Oklahoma State |

Reading it: **Oklahoma City and Tulsa are worth about half a home game** and are effectively
tied (Stillwater sits between them — 53 miles and 64). **Dallas is worth about half of that
again**, which is the fade Emmett asked for: they would rather be in Tulsa or OKC, and it is
not close. **Denver is already nothing** at 495 miles. **Seattle is nothing**, which is the
whole point — Oklahoma State is 968 miles nearer than Boston College and it buys them
precisely zero, because neither fanbase is going. And the model runs both ways: **New York is
Boston College's building**, not a neutral one.

### Gonzaga (85) vs Oklahoma State (87) — the Seattle test

| site | Gonzaga miles | presence | OkSt miles | presence | penalty | who pays |
|---|---|---|---|---|---|---|
| Seattle | 229 | 0.42 | 1,511 | 0.00 | **0.84** | Oklahoma State |
| Las Vegas | 801 | 0.00 | 1,010 | 0.00 | **0.00** | nobody |
| Lahaina | 2,858 | 0.00 | 3,710 | 0.00 | **0.00** | nobody |

Seattle is a real Gonzaga edge and a genuine neutral for everybody else. **Maui is neutral for
the entire country**, which is what an exotic MTE should be.

### Kansas (97) vs Wichita State (60) — prestige doing the work

| site | Kansas miles | presence | WSU miles | presence | penalty | who pays |
|---|---|---|---|---|---|---|
| Kansas City | 38 | 0.89 | 179 | 0.36 | **1.06** | Wichita State |
| Tulsa | 199 | 0.54 | 133 | 0.42 | **0.24** | Wichita State |
| Denver | 522 | 0.00 | 435 | 0.02 | **0.04** | Kansas |

★ **This is the table that proves ruling 5 earns its place.** In Tulsa, Wichita State is
*closer* — 133 miles against 199 — and still loses the building, because Kansas is a 97 and
they are a 60. That is right, and a distance-only model gets it backwards.

### Duke (98) vs Kansas (97) — two bluebloods

| site | Duke miles | presence | Kansas miles | presence | penalty | who pays |
|---|---|---|---|---|---|---|
| Indianapolis | 474 | 0.00 | 489 | 0.00 | **0.00** | nobody |
| Kansas City | 882 | 0.00 | 38 | 0.89 | **1.79** | Duke |
| New York | 423 | 0.06 | 1,135 | 0.00 | **0.12** | Kansas |

Indianapolis is a true neutral for both — the Final Four behaving as a Final Four should.
Kansas City is nearly a Kansas home game, and 1.79 of a possible 2.00 is the largest number
this model produces anywhere in the tables.

### Sensitivity — what the two open numbers actually change

**`D`** (OkSt vs BC, CrowdShare 2/3):

| D | Oklahoma City | Dallas | Kansas City | Denver |
|---|---|---|---|---|
| 350 | 1.48 | 0.59 | 0.53 | 0.00 |
| **450** | **1.54** | **0.84** | **0.80** | **0.00** |
| 550 | 1.58 | 1.01 | 0.97 | 0.18 |

`D` barely touches the near sites and almost entirely controls **the middle band** — Dallas
and Kansas City move by a factor of two across the band. The question `D` really answers is
*"is a 230-mile drive worth much?"*

**`CrowdShare`** (OkSt vs BC, D 450):

| CrowdShare | Oklahoma City | Dallas | max possible (of 3) |
|---|---|---|---|
| 1/3 | 0.76 | 0.42 | 0.99 |
| 1/2 | 1.15 | 0.63 | 1.50 |
| **2/3** | **1.54** | **0.84** | **2.01** |

`CrowdShare` scales everything uniformly. It is the answer to *"how much of home court is the
crowd?"* and nothing else.

---

## 7. What this needs from a schedule — the contract non-conference must satisfy

Written as a checklist because the non-conference design conversation should be held with
these in view.

1. **A game must be able to name a site.** A place id, from the same authored table S92 built.
2. **A game must be able to say nobody hosts it.** S92 already ruled this shape — `GameHost`
   is a tagged value, `Nobody` is a named case, never a null standing in for absence.
3. **`hasHost` already exists as the seam.** S95's `ApplyRoadShave` and
   `PrepareSeasonGameSides` take an explicit `hasHost` flag which the season loop currently
   passes as a literal `true`. When a game owns its site fact, **that fact becomes this
   argument and nothing else in the applicator moves.**
4. **Phase 86's B8 expectation moves** from `schedule.Count` to the count of games whose host
   fact is true. Named in the check's own comment already.
5. **The season page's home-court denominator narrows** the same way — today it is every
   completed schedule game, honestly, because every entry today really is hosted.
6. **Phase 83's A9 will finally need rewriting.** It forbids the season-path files from
   naming a geography type. S95 dodged it by keeping the host fact out; a session that gives
   games a site cannot dodge it, and that is legitimate rather than a failure.

---

## 8. What this model deliberately does NOT do

- **No city size, ever.** Ruled out by name at S92 and re-affirmed here.
- **No travel fatigue.** A team that flew five hours is not tired in this model; it simply
  has no crowd. Fatigue is the engine's own module and this does not reach into it.
- **No altitude, no time zones, no back-to-backs.** Each is a real basketball effect and each
  is a separate ruling nobody has made.
- **No arena, no capacity, no attendance number.** A place is a city (S92 R1).
- **No effect on conference play whatsoever.** Ruling 1.

---

## 9. The consequence to measure, not to pre-solve

Prestige will drive recruiting *and* the crowd, so a blue blood gets the better players and
the friendlier neutral building, compounding in the same direction every year. Emmett:
*"prestige is awesome to have in every way."* This is the sport working as it should, not a
defect.

The single thing worth checking once neutral games exist: **can a mid-major ever win in
Maui?** That is a question answered by one run, not by argument — and it is the reason this
model should ship *with* the games rather than before them.
