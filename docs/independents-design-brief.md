# Independents, and contract origination — design brief (S105)

**Status:** design conversation 2026-08-06. Rulings below are closed. **Four numbers are OPEN
and need Emmett's ruling before a prompt is drafted** (§6). Nothing is built yet.

---

## 1. The hole

Fourteen schools in the stock world have `conference.Games == 0`. The request builder gives them
`0, 0, 0, 0` — **zero games**. Every other school in the country gets its request filled exactly
(1,934 pairs, 0 unrepaired, 222 schools at 29 games and 108 at 31). Independents get nothing.

They are also the bottom of the country: **thirteen of fourteen are Selling class**, prestige 0-38,
and they are scattered — median 1,141 miles apart, up to 2,441. NJIT and Cal State Bakersfield are
not a conference.

## 2. Emmett's rulings (2026-08-06 — closed, do not re-derive)

**R35 — An Independent plays November through March, front-loaded.** The majority of its games fall
in November and December, when nobody else is in league play. *"They would be going to a lot of road
games most likely in Nov and Dec."*

**R36 — From January they play each other and fill gaps.** Once other leagues are locked up, an
Independent plays other Independents and fills the open weekday/weekend dates that conference
schools have. Measured: a 14-game league uses 8 of ~13 conference-season weeks, a 16-game league 9,
an 18-game league 10 — so roughly **1,200 open school-weeks exist** league-wide. Supply is not the
constraint.

**R37 — How many Independents there are changes the shape, and that is correct.** With many, they
play the geographically convenient ones only. With four or five, they each play at least once.
Nothing forces a fixed pattern.

**R38 — C-36 STAYS SHUT. No D2 opponents this session**, and an Independent may therefore **come up
SHORT**. *"They just buy those games or maybe end up with 26 or 27 games or something for now."*
★ This is a real departure: every other school's request is filled exactly. An Independent's
shortfall is the honest degenerate state, **printed on the page, never papered over**, and it
improves when the D2 layer arrives.

**R39 — Home-and-homes are how a bottom school gets a home game**, and they are signed by **any two
schools that fit**, not Independents only. *"An independent school in Cali would easily make a home
and home with a low level Big West school."*

## 3. Why origination is the real session

S103 built the entire contract system — persistence, forced and optional legs, windows, capacity
gating, the charge chain, two death conditions — and it is green. What was never built is anything
that **signs** one. `Program.Season.Contracts.cs` says so in its own header:

> *"NOTHING IN THE ENGINE SIGNS A CONTRACT — fixture-authored contracts prove the honouring; the
> negotiation layer is a future, coach-adjacent session."*

**Zero contracts have ever been signed in Project Charm.** The stock world authors none.

★ **A home-and-home is the ONLY pairing shape that is self-balancing.** A bought game consumes a
road request and a home request; the country already wants **352 more road games than it offers**,
and a Selling school asks for 1 home and 10.6 road. Two road-hungry schools cannot help each other
by buying. A home-and-home gives each side one home and one road, so it feeds two hungry schools at
once and **relieves the national gap rather than widening it**.

**Ruling accepted (Claude's recommendation, Emmett's call):** sign on **NEED, not temperament**. A
school short of home games agrees with a nearby peer who is also short. This does not pre-empt the
coach layer — it gives the coach something to *modulate* later rather than requiring willingness to
be invented from nothing. Only the **home-and-home** shape is signed this session; 2-for-1,
five-in-eight and neutral series stay unsigned until there is a reason for them.

## 4. The shape of an Independent's season (R40, R41 — from two real schedules)

Emmett supplied North Carolina Central and Seattle from the same season. They settle the numbers.

| | games | home | away | neutral | non-D1 home | same-season home-and-homes |
|---|---|---|---|---|---|---|
| NC Central (bottom) | 29 | 11 | 16 | 2 | **4** | 3 |
| Seattle (top) | 31 | 13 | 16 | 2 | **0** | **7** |

**R40 — ROAD IS THE CONSTANT; HOME SCALES WITH PRESTIGE.** Sixteen road games and two neutral in
both, at opposite ends of the prestige range. What changes is the home slate: **~13 at the top,
~7 at the bottom** once the non-D1 games are removed. This inverts an earlier proposal in this
brief (4 home, 24 road), which was wrong and is deleted rather than annotated.

★ The two neutral games are a two-game event in late November in both schedules — already modelled
by the MTE/showcase pool. Nothing new is needed for them.

**R41 — ★ THE SAME-SEASON HOME-AND-HOME IS THE MECHANISM, NOT A GARNISH.** Seattle plays SEVEN of
them — Northridge, Eastern Washington, Sacramento State, UC Davis, Idaho, Portland State, Utah
Valley — which is fourteen of its thirty-one games. Its partners are exactly the "equal low-majors
looking for one or two home games" of R39: low Big West and Big Sky schools, plus two fellow
independents. NC Central plays three, two of them against fellow independents (Savannah State,
Longwood) — R36 in the raw.

★ **THE ENGINE CANNOT DO THIS.** `ContractSeasonStep` exercises ONE leg per season (`.First()` on
the outstanding legs), so a home-and-home currently means one game this year and one next. Both
real schedules need **both legs inside one winter**. Two legs in one season is therefore a change
to the S103 window machine and is **the central obstacle of this session**, not a detail.

It is also what rescues R38: a same-season home-and-home balances NOW rather than over a career, so
an Independent's first season is no longer the lean one. The earlier note in this brief saying
otherwise is superseded.

**The D2 dependency scales INVERSELY with prestige.** Seattle needed no non-D1 opponents at all;
NC Central needed four, all at home. So C-36 staying shut (R38) costs far less than feared — it
bites only the bottom of the country, and there it costs about four home games. A bottom
Independent lands near **7 home** and short of 29 total until the D2 layer arrives.

### Still open for Emmett (small, and ruled against an archetype table during the build design)

- **What counts as "short of home games"** — the threshold that makes a school willing to sign.
- **How many home-and-homes one school may hold.** Possibly self-answering: a school signs as many
  as it needs to reach its home quota (Seattle 7, NC Central 3 — need-driven and self-limiting).

## 5. What this session does NOT do

**Dates.** Everything in R35/R36 is date-shaped and **cannot be expressed yet** — non-conference
games carry a site but no day. Front-loading, January gap-filling and "a few times during other
leagues' conference season" all land in the **calendar session**, which is the next real blocker.
This session gives Independents a *request* and *pairings*; the calendar gives them a *year*.

**The other contract shapes. The coach layer. The D2 layer (C-36). The recurrence curve.**

★ **The one-leg-per-season limit is IN scope** (R41) — without it the whole design fails. Widening
it is confined to the home-and-home shape; nothing else about the S103 window machine moves.

## 6. The acceptance instrument

Page-only, never suite-asserted: an Independent's year printed the way any school's is (opponent,
prestige, site, miles, bucket), plus the count each actually reached and the shortfall. Read across
several seasons — the first is expected to be the worst.
