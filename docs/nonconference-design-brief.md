# Design Brief — Non-Conference Scheduling (r3)

**Status: ARC SESSION 1 SHIPPED (S101); SESSIONS 2–4 RULED AND WAITING.** This is the scheduling
half of a pair. Its sibling, `neutral-sites-design-brief.md`, is the *effect* half — what a
neutral or pseudo-home game does to the two teams once it exists — and has been ruled and waiting
since S95. It becomes buildable at arc session 3.

**Owner of the rulings:** Emmett — 2026-08-04 (the design conversation following S100) and
2026-08-05 (the S101 session, including the geographic tilt).
**Board:** O-92 (the arc); rulings recorded as C-36..C-40.
**Depends on:** S91's calendar, S92's map and places, S93's conference slate, S94's dating,
S95's road shave, S97/S98's tournaments, S96/S99/S100's career memory.
**Blocks:** O-78 — a national ranking has no honest basis while leagues share no opponents.

> **★ r3 — SESSION 1 IS REAL AND FOUR RULINGS LANDED.** S101 shipped classes and requests
> (Phase 92; page-only). New rulings folded below: **R14** non-D1 deferred to D2/D3, **R15**
> bottom hosts bottom, **R16** no forward debt on off-campus series, **R17** the geographic
> tilt. R5 is amended (the floor holds at EVERY tier). §8's opens 3 and 4 are closed. A1 is
> settled (31 is literal — the flat-29 reading produces six impossible Big East slates). §1's
> unreproducible date figures are replaced by S101's measured game balance.

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

Non-conference play was **deleted, not amended** at S93. **S101 opened the real arc**: every
school now carries a class and a request — home, neutral and road counts of ordinary
non-conference games — printed on the season page. No game is scheduled yet; a school still
plays only its league season (plus S98's 128 tournament games), and the fourteen Independents
play nothing.

| Measured on the stock world, S101's page | |
|---|---|
| Classes | 83 Marquee / 70 Solid / 120 Working / 60 Selling / 14 Independent |
| Seated in an event (play 31 games; everyone else 29) | 108 |
| Home games the country wants to host | **1,666** |
| Neutral games requested | 223 |
| Road games the country wants to play | **2,024** |
| ★ **The gap** | **+358 — ~179 games must be hosted by schools that wanted the road** |

> **★ r3 — the r2 figures "5,121 open dates / 2,560 games" are WITHDRAWN.** They could not be
> reproduced against the calendar (conference play opens 12-07 to 01-11 depending on the
> league) and the method was never recorded — the S81.3/S100 lesson: a plausible inherited
> measurement is the most dangerous artifact in a brief. The balance above is the arc's real
> sizing, measured live on the page every season. The run-time worry also died at S101: playing
> the whole country takes ~26 seconds and the check suite never plays the stock season for
> basketball, so roughly doubling the season costs ~half a minute on one command.

★ **Under R15, the +358 gap lands on the 60 Selling schools at ~3 extra home games each** —
which takes Selling from 1.0 home toward 4.0, outside its own §3 row. That tension is the class
curve question (§8.1), deliberately left for Emmett to rule off the page.

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

**R5 — ★ CLASS IS PRESTIGE EVERY SEASON, WITH THE CONFERENCE TIER AS A FLOOR AT EVERY TIER**
*(amended at r3 — Emmett, 2026-08-04/05, now C-39).* Class = the higher of the conference tier's
floor and the prestige band, read from `CurrentPrestige` each season. *"Even the absolute worst
power conference team gets the easy home games in non con if they want them"* — and the same one
tier down: the 21-prestige Atlantic 10 school schedules like a Solid program, not like Pine
Bluff. Prestige lifts in both directions' spirit — **Northwestern (53) schedules Big Ten because
it is in the Big Ten; Gonzaga (85) schedules Marquee because it is Gonzaga** — and the floor
never drops. Corroboration from the evidence table itself: Drake (50, MVC) reads 7 home / 1 road
/ 3 neutral, a Solid November the floor produces and a prestige-only rule would not.

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

**R14 — ★ NON-D1 OPPONENTS ARE NOT BUILT NOW** *(r3; Emmett, 2026-08-04; C-36).* They arrive
with the D2/D3 layer, where the lowest teams pay local schools to visit. Until then the bottom's
home games come from R15. §4's evidence (Savannah State hosting four non-D1 schools; Pine
Bluff's only two home games) is the record of what this will eventually model — do not conjure
a generic pool in the meantime.

**R15 — ★ WHEN THE COUNTRY RUNS OUT OF GYMS, THE BOTTOM HOSTS THE BOTTOM** *(r3; Emmett,
2026-08-04; C-37).* Two schools that both wanted the road pair off and one eats the home date —
never the middle absorbing the shortfall, never a short slate. This is R3's terminal filler:
the +358 gap has a designated landing place, so R3 can be a hard line rather than a hope, and a
Selling school yields on venue, never on "did I get a game" (§6's prediction, confirmed).

**R16 — ★ OFF-CAMPUS SERIES CARRY NO FORWARD DEBT** *(r3; Emmett, 2026-08-04; C-38).* A one-off
neutral game is complete when played; a two-year series (Detroit this year, Charlotte next) is
agreed as two games up front. Which shape a series takes is a draw, not a rule — *"sometimes it
just goes two years."* Nothing is ever owed into a season that might not be able to pay it (the
trap S100 dug conference play out of). **2-for-1 / 3-for-1 trades** (Oklahoma State twice home
per once at Tulsa) are future multi-year structure, parked — and they are the honest third
source of a Marquee school's 0–2 true road games, alongside a rival and a return leg.

**R17 — ★ THE GEOGRAPHIC TILT** *(r3; Emmett, 2026-08-05; C-40; governs arc session 2).*
Distance is a cost on every pairing and the tilt is a **preference, not a wall**: near beats far
at equal value, so Oklahoma State lands Oral Roberts or Tulsa most seasons with no series memory
needed — recurrence falls out of proximity for free. Texas schools pick up a low-level Texas
school most years. The tilt **strengthens down the classes** — a power school flies, a small
school buses; Maine has fifty candidates before it ever crosses the country — and **yields when
options run out**: §4's Independent evidence (Chicago State to Utah in January) is the exception
proving it, because distance cannot constrain what has no alternative. Not 100%, a general
trend. S92's map and distance math make this input free.

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

Per R8 the request ships with constants. ★ *r3:* the single-value-vs-fixed-spread question this
section used to carry is **closed by R5 as amended** — prestige is already varied and already
moves season to season, so the 83 Marquee requests differ without any drawn spread, and R7's
future inputs (maturity, temperament) arrive as a **tilt on an already-varied request**, which
is exactly the rewire R8 wanted. No RNG reaches the request; S101 asserts that.

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
   unruled. ★ *r3:* the season to read now exists — the S101 page, whose balance line
   (**+358, ~3 extra home games per Selling school under R15**) is the number to rule against.
2. **The distance/quality curve of R10.** Two anchors: ~65 miles takes anybody, "hundreds of
   miles" needs two 75/80+ teams. The shape between is unruled. R17 now supplies the pairing
   side of the same principle; this open is the site side.

★ *r3:* opens 3 (non-D1 opponents) and 4 (constant-as-spread) are **closed** — by R14 and by
R5-as-amended respectively.

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

- **A1.** ★ *r3 — SETTLED, and the cautious reading was wrong.* "Seeded reaches 31" is
  **literal**: a seated school's season is 31 actual games and the three event games come out of
  the higher total. The flat-29-minus-3 reading hands six Big East schools (18 conference games,
  seated) an impossible slate — more home games requested than open games held. Asserted forever
  by Phase 92 C6.
- **A2.** The class boundaries in §3 (80 / 55 / 25) and their counts. Emmett named "75/80+" for
  showcases only. ★ *r3:* shipped as S101's constants (R8 seam) — standing, but still Claude's
  numbers, and the §8.1 ruling may move them.
- **A3.** That a pseudo-home site is drawn from the **tagged** place list rather than from all 315
  by distance.
- **A4.** That non-conference reuses the shared eight-season career window for alternating an R11
  series rather than getting its own depth. ★ *r3:* R16 narrows this — a two-year series is
  agreed up front, so no long memory is needed for the alternation itself; the window question
  survives only for avoiding immediate rematches, if that is even wanted.
- **A5.** That the plan in R4 is a per-school target of counts and quality, not a list of named
  opponents. ★ *r3:* S101 shipped the counts half; quality-within-class is session 2's.

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

1. **Classes and requests** — ★ **SHIPPED at S101** (Phase 92). The balance proved the country
   does not balance (+358), the run-time cost proved negligible (~half a minute), and the class
   curve now has a page to be ruled from.
2. **The matching** — who plays whom, who hosts, and the yield order when a request cannot be met.
   Where R3 becomes a hard line, R4 becomes an order, R17's tilt prices every pairing, and R15
   fills the last gyms. All of its design inputs are ruled except §8's two opens.
3. **Sites and nights** — games gain a place, the neutral brief's model is wired at last, and dates
   land around conference play and the tournament windows. R9–R12 live here.
4. **The Independents** — their November and their gap-filling role (R13). ★ *r3:* the non-D1
   pool is out of the arc entirely (R14 — it arrives with D2/D3).
