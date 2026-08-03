# Design Brief — Multi-Team Events

**Status: DESIGNED, NOT BUILT. Blocked on the memory layer.** The rulings below are Emmett's,
from the design conversation of 2026-08-03. Several open questions are named in §7 and are
*deliberately* open — do not close them by inference.

**Prerequisite:** season-to-season memory (the four-year interval rule cannot be enforced
without it). **Pairs with:** the neutral-sites brief — MTEs are where most neutral games in
the engine will come from. **Followed by:** the guarantee market and the bulletin board.

---

## 1. What an MTE is, and why every school wants one

A multi-team event is a small tournament, mostly played over two to four days in November,
that carries an **NCAA exemption**: the games inside it do not count against the regular-season
limit the same way ordinary games do.

**The current rules, which reconcile exactly:**

| combination | scheduled | event | total |
|---|---|---|---|
| MTE with three games | 28 | 3 | **31** |
| MTE with two games | 29 | 2 | **31** |
| no MTE | 29 | 0 | **29** |

★ **The exemption is worth two extra games.** That is the entire incentive, and it explains
the participation rate: in the 2025-26 real-world data, **20 of 31 conferences were completely
booked**, and the unattached list ran to roughly twenty schools nationally. **An MTE is not a
prize. It is near-universal.** What is scarce is not being *in* one — it is being in a *good*
one.

**Other standing rules, all from the current NCAA text:**

- Only one team per conference may be contracted to an event in a given year. ★ **This binds
  at the BRACKET, not the event** — the Charleston Classic legally ran two ACC schools in
  2025-26 because they sat in separate, non-meeting brackets.
- A team may play in the same event only once every four years (waived for a host from Hawaii
  or Alaska — the Chaminade case, ruled **way down the line** and out of scope).
- Every team in an event plays the **same number of games**. Field size and game count are
  therefore one decision, not two.
- Only D1 teams may participate, unless the host school is not D1, in which case the host may.

*(The Players Era Festival — eighteen teams, NIL-driven, bespoke rules — is explicitly NOT
modelled. Ruled out by name so a later session does not try.)*

---

## 2. The three formats — all real, all needed

★ **This is the finding that most changes the shape of the build.** An MTE is not necessarily a
neutral event. The 2025-26 field contains three genuinely different animals, and the engine
needs all three.

**1. The destination bracket.** Four or eight teams, two or three games, every game on a
neutral floor somewhere nobody lives: Maui, Battle 4 Atlantis, the Cayman Islands Classic, the
Paradise Jam, Las Vegas. These are the events schools compete to get into.

**2. The regional bracket.** Same structure, unglamorous location, cheaper field. The Coconut
Hoops Royal Palm Division in Fort Myers — Belmont, Saint Francis, Toledo, Troy. Four solid
low-and-mid-majors, two games, a neutral floor in Florida. There are many of these and they
are the workhorse tier.

**3. ★ The campus event.** The host plays every game at home; the visitors play each other.
Duke invites Howard and Niagara: Duke plays both at home, and **Howard–Niagara is played in
Cameron Indoor as a true neutral game.** Works with three teams (two games) or four (three
games). Kentucky, UConn, Villanova and SMU all ran one in 2025-26.

★ **The campus format is why the site fact cannot be deferred.** A campus MTE contains a
neutral game *inside* a home arena — a game at a school's place that nobody hosts. That is
S92's `GameHost.Nobody` doing exactly the work it was designed for, and it means the third
site category (neutral, at a school's own place) is not an edge case but a routine output.

★ **And it is the escape hatch.** *"If Duke wants to avoid a tough tournament that year, they
just host their own small level MTE."* The campus event is the floor of the market: cheap,
local, and it never runs out, because it is three schools and a gym. **Any school that does
not get into a bracket organises one of these**, which is why participation can be near-total
without the good events being any less scarce.

---

## 3. The money — and it runs the opposite way from the first guess

★ **THE TOURNAMENT HAS THE BUDGET AND BUYS THE FIELD.** Not schools paying promoters for slots.
An event has money, schools have prices, and the event spends its budget acquiring teams.

*"No tournament can afford to bring in the 8 best colleges in basketball."* That single
sentence produces the whole tier structure without anyone authoring it:

- A tournament's budget **is** its tier. Maui outbids Charleston; Charleston outbids the
  Coconut Hoops.
- A top event can afford **one flagship and a body of solid programs** — never eight elite
  schools, because the money does not exist.
- Events draft **top-down**: the richest picks first, the next picks from what remains, and so
  on down to the regional brackets. Everyone unpicked hosts a campus event.

★ **The real Maui participation record is the evidence for all of it.** Across twenty seasons:
North Carolina appears six times, Kansas and Arizona five, Duke three. Nobody appears every
year except the permanent host. And the tail of the same list contains **Southern Utah, Coppin
State, Mount St. Mary's and Delaware State** — a top event fills the *bottom* of its bracket
cheap, because it spent at the top. A tier-eligibility model would never produce Coppin State
in Maui; a budget model produces it naturally.

**A school cannot refuse.** If the money is there, it goes. Ruled — negotiation and declined
invitations are not modelled.

**Price is prestige-driven.** Duke costs a great deal because Duke is the draw; Southern Utah
costs little. *(Working population: 347 schools, prestige 0–98, median 43; 11 schools at 90+,
42 at 80+, 166 at 40 or below.)*

---

## 4. Where this sits against the other markets

Two markets exist in the non-conference design and they are **different mechanisms that happen
to share a currency**:

| | who pays | who is paid | what is bought |
|---|---|---|---|
| **MTE** | the tournament | the school | a team in the field |
| **Guarantee** | the home school | the visitor | a road opponent |

★ **They touch at the floor.** Duke's campus MTE pays Howard and Niagara to show up — which
draws from the same pool of low-major sellers the guarantee market wants in November. That is
the first place the two systems interact and it is where a clearing problem, if one exists,
would appear.

**The school-side trade-off that makes this worth building** *(Emmett's Saint Mary's case)*:
spend a large share of the budget to get into a destination bracket and eat cheap buy-games
for the rest of November — or host a cheap campus event and keep the money for better
individual games. **A binary with a real cost**, not a spectrum.
---

## 5. ★ Why this is blocked on the memory layer

The four-year interval needs three seasons of history. Without it, **the richest event buys
the same flagship every season forever** — Maui's field would be identical every year, which
is the same "nothing ever rotates" failure the conference schedule already has and which S95
made expensive.

Emmett: *"there should be some mechanism to prevent teams from participating in the same
tournaments without intervals in between."*

This is the **third** thing to arrive at the same blocker, and that is what settles the arc:

1. **Conference single-meeting games never flip** — you host Kansas every year and travel to
   Texas every year. 526 pairs across fourteen leagues, each now worth about three points.
2. **Home-and-homes** are a two-season promise and cannot be expressed.
3. **The MTE four-year rule.**

Building MTEs first means building a field generator that produces an identical Maui every
season and then going back to fix it. **The memory layer goes first.**

What it must carry for this brief: *which event each school played in, for the last several
seasons.* Nothing else — not rosters, not results, not development.

---

## 6. What a build session inherits

**Authored per event** (a data file, the same way `conf.csv` authors leagues):

- name, format (destination / regional / campus), field size, games per team
- budget
- whether a permanent host seat exists *(the Chaminade case — out of scope, but the field
  should exist so it is not a schema change later)*
- bracket structure where an event splits (Charleston, the Sunshine Slam, the ESPN Events
  Invitational, the Acrisure Series pods)

**Derived per school:** a price, from prestige.

**The assembly algorithm:** events drafted in budget order, each spending down on the best
field it can afford, subject to one-team-per-conference-per-bracket and the four-year
interval; every school not picked assembles a campus event.

**Session boundary (Emmett's cut):** session one **creates the events and fills them with
teams**. Its output is a field list. Session two **puts them on the schedule** — sites, dates,
the neutral fact, and the proximity model running. Session one therefore needs no calendar and
no site fact, and every check is about a field being legal and plausible.

---

## 7. Open — do NOT close these by inference

1. **Is a school's price one number across both markets?** What it costs to bring Duke into
   your gym and what it costs to put Duke in your field are plausibly the same question, but
   this was raised and never ruled.
2. **The prestige → price curve.** Linear? Steep at the top? The Maui record suggests steep —
   one flagship consumes a large share of a top budget.
3. **How is a tournament's budget authored?** Per-event constant, or derived from something?
4. **Is the four-year interval exactly four**, and is it per-event (ruled: yes, the same event)
   or does a softer national interval also apply?
5. **Non-exempt events exist** — the Big 5 Classic, the Don Haskins Sun Bowl Invitational. They
   look like MTEs and carry no exemption. Modelled or ignored?
6. **How many events, and of what mix?** The real 2025-26 landscape had a handful of
   destination brackets, many regional ones, and a long tail of campus events. Whether that
   distribution is authored or targeted is unruled.
7. **What happens to a school whose event does not fill?** Campus events are the floor and
   never run out, so this may be unreachable — but it should be proven unreachable rather than
   assumed.

---

## 8. Explicitly out of scope

- **The Players Era Festival** and NIL-driven formats — ruled out by name.
- **The Chaminade permanent-host exemption** — *"WAYYY down the line."*
- **Non-D1 and reclassifying participants.** The historical Maui list contains Bob Jones, York-
  Pennsylvania and Washington & Lee; the current rule bars non-D1 teams except a non-D1 host,
  so that list predates the rule. The engine has no non-D1 schools yet regardless.
- **Declining an invitation.** Ruled: a school cannot refuse.
- **Tournament results mattering** — seeding, champions, bracket advancement as a rating input.
  A field is a field; what happens inside it is just basketball.

---

## 9. The arc this sits in

1. **Memory layer** — a season reads prior seasons' schedule facts. Narrow: who hosted whom,
   who played in which event. Nothing that opens the career door. Immediately pays for itself
   by flipping conference single-meeting games.
2. **MTEs — fields.** This brief.
3. **MTEs — placement.** Sites, November dates, the neutral fact, and the proximity model
   live (see the neutral-sites brief).
4. **The guarantee market and the bulletin board** — the rest of non-conference.

★ **Why non-conference matters at all, in Emmett's words:** *"I do want realistic nonconference
schedules when we're testing these seasons to start getting more realistic overall records."*
Today every record is pure conference play, so a 20-game league and a 14-game league produce
records that cannot be compared, and no two leagues share an opponent even indirectly. Non-
conference is roughly 40% of every season — about 5,300 games nationally, nearly twice the size
of the conference slate. It is the bigger half of the schedule, not a garnish.
