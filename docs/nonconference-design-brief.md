# Design Brief — Non-Conference Scheduling

**Status: RULED, NOT BUILT.** This is the scheduling half of a pair. Its sibling,
`neutral-sites-design-brief.md`, is the *effect* half — what a neutral or pseudo-home game does
to the two teams once it exists — and has been ruled and waiting since S95. Neither can ship
without the other: that brief says plainly that *"the session that builds non-conference
scheduling is the session that makes this buildable."*

**Owner of the rulings:** Emmett, 2026-08-04, in the design conversation following S100.
**Board:** O-92. Non-conference was deleted at S93 and never boarded until now.
**Depends on:** S91's calendar, S92's map and places, S93's conference slate, S94's dating,
S95's road shave, S97/S98's tournaments, S96/S99/S100's career memory.
**Blocks:** O-78 — a national ranking has no honest basis while leagues share no opponents.

> **★ r2 — THE MONEY MODEL IS GONE.** r1 ruled a per-school price and a per-school budget, with
> hosting as the expense. Emmett struck it: *"we're getting too in the weeds with this financial
> budget stuff because that's gonna take a long time to calibrate when we already know exactly
> what kind of results we want from our schedule generator."* Five of r1's thirteen rulings are
> **cut, not superseded in place**. What replaced it is smaller and more direct: a school's
> **class** decides whether it dictates its schedule or adapts to somebody else's, and budget is
> assumed rather than modelled. §11 records what was cut so a later session does not re-propose
> it as though it were unexplored.

---

## 1. Where this stands today, verified against the pull

Non-conference play was **deleted, not amended** at S93 — *"we don't care about a 'season' right
now, we care about games being scheduled."* The 14-regular ring circulant, the conflict queue,
the double-edge repair, the 20-attempt retry and the whole RNG stream that fed them are gone.
Nothing partial survives to be resumed.

A school today plays its league season and nothing else: 14 games in the Ivy, 20 in the Atlantic
Sun, 16 or 18 for most, and **zero for the fourteen Independents**. The only cross-conference
basketball anywhere is S98's 128 tournament games across 88 seated schools.

| Measured off the stock world | |
|---|---|
| Open non-conference dates, whole country | **5,121** |
| Games that implies | **2,560 a season** |
| Power leagues (ACC, SEC, Big East, Big 12, Big Ten, Pac-10) | 73 schools, 14.0 dates each |
| Everyone else | 274 schools, 15.0 dates each |

★ **This roughly doubles the season** — 2,818 conference games become ~5,378. A real run-time
cost for every check that plays a season, and it should be sized in the arc's first session
rather than discovered in the suite.

---

## 2. The rulings — closed, do not re-derive

**R1 — ★ THE TOTAL IS THE ANCHOR; NON-CONFERENCE IS THE REMAINDER.** A school's game count is
roughly fixed and its league season is subtracted from it. The Ivy school with 14 in-league dates
hunts 15-plus non-conference games; the Atlantic Sun school with 20 needs 11. The
*small-conference* schools do most of the shopping.

**R2 — ★ A TOURNAMENT COSTS ONE DATE AND BUYS THREE GAMES.** The exemption is real: a seated
school genuinely plays more basketball. **Seeded reaches 31; stayed home lands near 29.**

**R3 — ★ THE SLATE MUST FILL, FOR EVERYONE. THE HARD LINE.** No school may finish short of games.
This is non-conference's R3, the one thing that can never fail.

**R4 — ★ CLASS DECIDES WHO DICTATES, NOT WHAT THEY WANT.** Emmett: *"they can pretty well dictate
their schedules as they want, and then everybody else will adapt around them because they don't
have the money and influence."* A Marquee school states a request each season and the rest of the
country fills in around it. **This is the most important structural ruling in the document** — it
is why the bottom's November looks the way it does without anyone deciding it should.

**R5 — ★ CLASS IS CONFERENCE TIER BY DEFAULT, WITH PRESTIGE OVERRIDING IN BOTH DIRECTIONS.**
Power-conference budgets are assumed adequate; mid-major and lower are assumed not to be. But
*"the Gonzagas of the world who do supersede lower conference prestige levels might have things
similar"* — and it breaks downward too. **Northwestern (53) schedules like a Big Ten school
because it is in the Big Ten. Gonzaga (85) schedules like a power school because it is Gonzaga.**
Both exceptions are real basketball, not tolerance for error.

**R6 — ★ MONEY IS ASSUMED, NEVER MODELLED.** No price list, no budget number, no ledger, no
accounting anywhere. See §11.

**R7 — ★ WHAT A MARQUEE SCHOOL WANTS IS DYNAMIC, AND ITS TWO INPUTS DO NOT EXIST YET.** Emmett: a
coach rebuilding with freshmen wants *"an easy schedule with a lot of cupcake home games"*; three
years later with juniors and seniors who have started dozens of games together he takes them on
the road, or books two or three neutral sites *"to prep them for the postseason."* So the request
is driven by **roster maturity** and **coach temperament** — and the engine has neither. See §5.

**R8 — ★ SHIP WITH CONSTANTS AND REWIRE LATER** (Emmett, explicitly). The constants are named
inputs at **one seam**, never numbers scattered through the scheduler, so the later swap is a
substitution rather than a hunt.

**R9 — ★ A TRUE NEUTRAL SHOWCASE NEEDS TWO BIG NAMES, BOTH OF THEM.** For a one-off in New York
or somewhere far from either campus, *"it has to be a fairly big matchup, teams that are 75/80+
most likely, or part of an MTE."* **One big name does not carry it — "Duke vs a 40 prestige team
won't cut it."** Supply: 55 schools at 75+, 42 at 80+. Scarce on purpose; a showcase is an event,
not a way to fill a date.

**R10 — ★ DISTANCE AND MATCHUP QUALITY ARE ONE CURVE, NOT TWO RULES.** *"The further away from
one campus site, the higher the threshold the prestige needs to be."* Near campus, anybody will
do. Further out, the matchup must be better to justify it. At the far end you need two 75/80+
teams — **which is R9. The showcase is the end of the same line, not a separate object.**

**R11 — ★ TWO SHAPES OF OFF-CAMPUS GAME, AND THE OPPONENT DECIDES WHICH.** *"Duke might play a
mid-major in Charlotte for a semi-home game; they wouldn't play that same game semi-away."*
- **Relocated home game** — weak opponent, always near the big school, **no return leg**. A buy
  game that happens to be off campus.
- **Alternating series** — peer opponent, neither will take a true road game, so they play near
  one campus and swap the following year. Chicago this year, Oklahoma City next.

**R12 — ★ OFF CAMPUS IS OFF CAMPUS.** A campus inside a big metro does **not** disqualify that
school from a showcase there. *"It's decidedly not on Northwestern's campus, so it's the same
thing."* Claude proposed the opposite and it was struck. The ruled crowd model already agrees —
its proximity line runs to a floor around 450 miles, so Evanston at 15 miles and Stillwater at 65
both sit on essentially a full pot.

**R13 — ★ THE INDEPENDENTS ARE FOURTEEN SCHOOLS THAT SHARE A PROBLEM, NOT A LEAGUE.** *"They have
no guarantee to play one another."* Heavy non-conference up front, each other during everyone
else's conference season, and gaps found inside conference play. They also **fill in gaps for
conference teams** — that is what makes them useful to everybody else. Already toggleable: the
stock world carries all fourteen at `games = 0`, tier `low`, so a world without Independents
simply has nobody in that conference.

---

## 3. The classes

Class is a **spectrum, not four buckets** — a school at 79 and a school at 80 must not play
completely different Novembers. The rows below are the shapes to interpolate between, and they
are the *aggregate the country lands on*, not a specification handed to each school (R4).

| Class | Rule | Count | Home | True road | Neutral / tournament |
|---|---|---|---|---|---|
| **Marquee** | power tier, or 80+ anywhere | **83** (73 power, 10 on prestige) | 7–10 | **0–2** — a rival or a return leg only | tournament, plus a showcase or two |
| **Solid** | 55–79 | 42 | 5–7 | 3–5 | a tournament some years |
| **Working** | 25–54 | 133 | 3–5 | 6–8 | rarely |
| **Selling** | under 25 | 75 | 0–2, mostly non-D1 | **10+** | never |
| **Independent** | `games = 0` | 14 | see §4 | | |

**★ THE ACCEPTANCE MEASURE: the average power-conference school plays 0–2 true road games.**
Page-only, never suite-asserted. But it is the number that says whether this is right — if the
first build hands Duke four road trips, something is wrong no matter how green the suite is.

### The evidence behind the rows

| | Home | True road | Neutral | Semi |
|---|---|---|---|---|
| Texas Tech | 7 | **1** | 5 | — |
| Wake Forest | 9 | **0** | 2 | 2 |
| Missouri | 9 | 2 | 2 | — |
| Drake | 7 | 1 | 3 | — |
| Arkansas Pine Bluff | **2** | **11** | 0 | — |

Pine Bluff has **zero Division-I home games** — eleven straight road paydays, then two non-D1
opponents at home. Wake takes no true road game at all.

★ **A correction on the record.** Claude read this table and concluded peer home-and-homes had
vanished. **They have not — they have moved off campus** (R11). Oklahoma State/Northwestern and
Wake's two Semi-Away games are the same thing: a series relocated so neither team eats a road
trip. Claude also built the r1 money model partly on Missouri buying opponents ranked 232–360
while Tech bought 95–268. Under a class model both are Marquee and that difference is **no longer
explained by anything.** It may have been one season of one school over-read. If it matters, it
returns as *opponent quality within the class*, driven by prestige — a far smaller thing than a
budget.

---

## 4. The Independents, read from three real schedules

North Carolina Central, Savannah State and Chicago State, checked against the stock world's own
fourteen.

**They play each other constantly, and nobody schedules it.** NC Central played Texas-Pan
American **three times**, plus Chicago State twice, Longwood twice, Savannah State twice. Chicago
State got **twelve** games out of fellow Independents. That is a third of a season — and it is
emergent, exactly as R13 says: in January everyone else is in conference play. With fourteen
schools each needing roughly ten dates in that window, pairs recur two and three times by
arithmetic alone. **No pairing guarantee should be built.**

★ **PROXIMITY IS THE ONE GUESS THE DATA DOES NOT SUPPORT.** Emmett expected *"probably only the
ones in decent proximity."* Chicago State went to Utah Valley, Houston Baptist, NJIT and
Texas-Pan American — Utah, Texas, New Jersey, Texas. Savannah State played Utah Valley twice. The
likely reason: a conference school picks the near option because it *has* options; an Independent
in January takes the game. Distance cannot constrain what has no alternative.

**Non-D1 opponents are a staple, not a curiosity.** Savannah State hosted four — Brewton-Parker,
Talladega, Allen, Carver Bible. Chicago State hosted St. Xavier and Olivet Nazarene. It is how an
Independent gets a home game at all, and it is the same mechanism behind Pine Bluff's only two.
**A non-D1 opponent is a real object this arc needs** and the world does not have one.

**They fill conference-season gaps, deep in.** NC Central at NC State on Feb 3 and Kansas State on
Feb 17; Chicago State at Northwestern on Feb 4.

---

## 5. ★ THE TWO MISSING INPUTS (R7)

**Coaches exist but no school owns one.** `CoachProfile` is real and wired — `HeliocentricBias`,
`ShotSelectionBias`, `PaceBias`, all 1–10 with 5.0 neutral. But it is set on a **game** via
`SetCoach`, and only ever inside checks. Nobody at Missouri has a persistent temperament.

**Roster maturity does not exist at all.** O-72: `Player.PlayerClass` is generated and then
discarded — `GenMapToPlayer` never assigns it, so every season player carries an empty string.
Worse, the board records it as *"a PLACEHOLDER label decorating `Arrival`"* with the real
population-structure question unowned. **The engine cannot tell a team of freshmen from a team of
seniors.**

Per R8 the request ships with constants. Two readings of "constant" were considered:

- A single shared value — every Marquee school asks for the same November. All 83 identical,
  distinguished only by which cupcakes were still available. Lifeless, and hard to judge by
  reading a season.
- **A fixed spread** — Marquee schools draw from a distribution, deterministic from the seed.
  Some take two road games, some none. The country looks varied immediately, the variation means
  nothing until R7's inputs land, and **the later rewire tilts the draw instead of replacing the
  machinery.**

Claude recommended the spread; Emmett did not rule on it directly. Listed in §10.

---

## 6. ★ THE ARITHMETIC THAT CONSTRAINS EVERYTHING

**Games always clear. Home dates never do.** Every unfilled date is two schools who both need a
game, so matching is never the scarce thing. But every game has exactly one host, so nationally
the non-conference home share is *precisely* 50% minus whatever is neutral. **Wake hosting 9 of 13
is only possible because Pine Bluff hosts 2.**

Consequence for R4: when a request cannot be met, it will yield on **venue and opponent quality**,
essentially never on *"did I get a game."*

**Supply is comfortable.** If every power school hosts ten, that is 730 visits spread over the
other 274 schools at **2.7 road trips each** out of fifteen dates. The top wants to stay home and
the bottom needs the road — opposite wants, which is why it works.

---

## 7. The site fact, and the seam that exists

Confirmed against the pull. From the neutral brief's contract:

- A game must **name a site** — a `placeId` from S92's authored table.
- A game must say **nobody hosts it** — `GameHost` is a tagged value with `Nobody` as a named
  case, never a null.
- `ApplyRoadShave` and `PrepareSeasonGameSides` already take an explicit `hasHost` flag and
  `Program.Season.cs` passes `sg.HasHost`. **When a game owns its site fact, that fact becomes
  this argument and nothing else in the applicator moves.**
- Phase 86's B8 and the season page's home-court denominator narrow from every schedule game to
  the games actually hosted.
- Phase 83's A9 forbids season-path files from naming a geography type. A session giving games a
  site cannot dodge it — legitimate, not a failure.

### ★ Tags, not population

A place carries `placeId, name, subdivision, country, lat, long, tags`. **There is no population
field and there must not be one** — the neutral brief rules out city size, market size and arena
capacity by name, because *"a small town college can have an incredible homecourt advantage."*

22 of 315 places are already tagged (13 `domestic`, 9 `exotic`) from S97's tournament venues, and
**Oklahoma City is one of them**. The two jobs stay separate:

- **Choosing the site** — market matters. Nobody stages a showcase in Stillwater.
- **What the crowd does once there** — proximity and prestige only.

A tag does the first without letting a size number near the second. A population field would put
one a single step away from a model that ruled size out, and a later session would wire it in.

---

## 8. Open — for Emmett to rule

1. **The class curve.** §3's rows are shapes to interpolate between; the interpolation is
   unruled. Probably best settled by reading a generated season, not in advance.
2. **The distance/quality curve of R10.** Two anchors: ~65 miles takes anybody, "hundreds of
   miles" needs two 75/80+ teams. The shape between is unruled.
3. **Non-D1 opponents.** §4 shows they are structural, and the world has no such object. Are they
   schools with no conference, a generic pool, or something the scheduler conjures?
4. **The constant-as-spread recommendation** (§5).

---

## 9. What this deliberately does NOT do

- **No money, in any form** (R6, §11).
- **No city size, ever** — inherited from S92 and the neutral brief, preserved by §7.
- **No effect on conference play.** R3 stands untouched; conference games are never neutral.
- **No pairing guarantee among Independents** (R13, §4).
- **No travel fatigue, altitude, time zones or back-to-backs.** Each is real and each is a
  separate ruling nobody has made.

---

## 10. ★ Assumptions Claude made rather than heard — strike freely

- **A1.** R2's arithmetic — that a tournament costing one date and buying three is what makes "31
  for some teams" true. Emmett said the games come out of the allowance; the one-date reading is
  Claude's.
- **A2.** The class boundaries in §3 (80 / 55 / 25) and their counts. Emmett named "75/80+" for
  showcases only.
- **A3.** That a pseudo-home site is drawn from the **tagged** place list rather than from all 315
  by distance.
- **A4.** That non-conference reuses the shared eight-season career window for alternating an R11
  series rather than getting its own depth.
- **A5.** That the plan in R4 is a per-school target of counts and quality, not a list of named
  opponents.
- **A6.** The spread reading of R8 (§5).

---

## 11. ★ CUT AT r2 — do not re-propose without a new ruling

r1 ruled all of the following. All are **withdrawn**, recorded so a later session recognises them
as decided rather than unexplored:

- A **price** per school — what it costs to bring them in, ceiling ~100K.
- A **budget** per school — Duke ~2M, Arkansas Pine Bluff ~15K.
- **Hosting as the expense**, with the budget capping home dates.
- Price as a **property of the school, not the matchup**.
- The budget as **living**, recomputed each season from current prestige.

The reason is not that the model was wrong — it explained the evidence well, and Emmett's
inversion of hosting-as-expense was better than what Claude proposed. It is that **it buys nothing
the class model does not already buy, and costs a long calibration to get there** when the target
November is already known. The one thing genuinely lost is Missouri-versus-Texas Tech (§3).

---

## 12. Session shape

An **arc, not a session**:

1. **Classes and requests** — every school's class and target November. No games scheduled.
   Provable alone: the country's total home demand against total road supply. **Size the run-time
   cost of doubling the season here.**
2. **The matching** — who plays whom, who hosts, and the yield order when a request cannot be met.
   Where R3 becomes a hard line and R4 becomes an order.
3. **Sites and nights** — games gain a place, the neutral brief's model is wired at last, and dates
   land around conference play and the tournament windows. R9–R12 live here.
4. **The Independents and the non-D1 pool**, if they have not fallen out of 1–3 naturally.
