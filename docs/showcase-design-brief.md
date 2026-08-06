# Showcases and the living event pool — design brief r2

**Arc:** non-conference scheduling (O-92), session 4 per the reordered arc (contracts brief §0).
All rulings Emmett's, 2026-08-06, unless marked. *(r2: the ChatGPT review round folded — the
radius terminal policy ruled, the Selling allowance reversal, four standby showcases added, the
four-year rule's inheritance corrected against live source, draw generalized to every event.)*

---

## 0. WHAT THIS SESSION IS

The event pool learns a second kind. A **showcase** is a living authored event exactly like a
tournament — a name, a home city, seat requirements, a persistence dial — but it is **not a
bracket**: it invites **four schools out and stages two stand-alone games in one day**. Nobody
advances, nobody places, nobody wins the event. Emmett: *"much like the MTEs, I'm imagining
'existing' showcases, like the Jimmy V, that might play 2 games in one day, and it's not a
bracketed event at all, but they invite 4 schools out... Living events that come in and out of
college basketball."*

★ **ALMOST ALL OF THE MACHINERY EXISTS, and the inheritance is verified against source, not
assumed.** Activation and every seat draw are keyed on the event's own id — adding showcases
cannot move a tournament's draw, by construction. Per-seat relaxation provenance is already
stored as words. An event that cannot fill already seats SHORT and a short event does not play.
The four-year rule is already **per specific event** — `(this event, this school)` is the
stored exclusion — so the Jimmy V blocks a Jimmy V return and never a Champions Classic
invitation; nothing about it changes. What is genuinely new: the two-game non-bracket shape,
the per-kind participation walls, the neutral-first charge, the draw, and the authored slate.

---

## 1. R25 — ONE OF EACH, NEVER TWO OF EITHER

**A school may play one tournament AND one showcase in the same season** — Duke plays the Maui
and then comes out for the Jimmy V. The existing "at most one event per season" becomes **two
walls, one per kind**. The four-year rule is untouched: per event, exactly as built.

★ **One-of-each makes a double-booking possible that could never happen before**: a school's
tournament window and its showcase day can now collide. Eligibility must exclude a school whose
other seated event overlaps the showcase's day, and the seating order across kinds decides who
saw the school first — §9.2's audit item, ruled deterministic, never accidental.

---

## 2. R26 — A SHOWCASE COSTS ONE OF YOUR GAMES, CHARGED DOWN THE S103 CHAIN

A tournament seat makes the season genuinely bigger (31 games; R2, settled). **A showcase is
different: "it just counts as one of your games."** Season totals unchanged — 31 with a
tournament, 29 without, regardless of showcases.

★ **THE CHARGE IS THE S103 FIXED-PAIRING SEAM, NEUTRAL FIRST.** A showcase game is a fixed
pairing (both schools named, the site neutral) that charges the school's request **neutral →
road → home**, joins the matcher's used set before any phase runs, and appears in the pairing
log. The seam exists; nothing new is invented.

★ **THE SELLING ALLOWANCE STAYS 0 — r1's 0 → 1 is REVERSED (Claude's correction, Emmett
agreed).** r1 changed the allowance on the premise that an invited school needs a neutral game
to spend; the S103 charge chain had already made that false. An invited bottom school **pays
with a road game** — exactly right: it gave up a road trip to attend a sponsored event — and no
Selling school ever goes shopping for a neutral game on its own, which r1's change would have
made all sixty of them do every season. Eligibility for an invitation is NOT "holds a neutral
request"; it is having an open game to give, which the capacity arithmetic already expresses.

★ **THE MATCHER'S NEUTRAL PHASE HANDLES THE LEFTOVERS** — neutral requests no showcase
claimed. ★ **AND THE LEFTOVERS ARE TYPICALLY THE SEMI-HOME SHAPE, recorded for the sites
session, not built here** (Emmett): *"A city 300 miles away from either school doesn't just
have them come play randomly unless it's part of a sponsored event. But the big town 40 miles
away would."* A true far neutral exists only because a sponsored event put it there. This
half-writes the parked semi-home ruling — the sites session inherits it.

---

## 3. R27 — THE EVENT DECIDES WHO PLAYS WHOM

**Seats 1–2 are the headliner and play each other; seats 3–4 are the undercard and play each
other.** The matchup is the product. The event's ambition lives in its authored seat bands, so
R9 ("two big names, BOTH of them") is enforced by the event's own seats. Emmett: *"different
showcases have different requirements and they will be filled."* ★ The two games carry their
role from the STORED seat numbers, never from list position or from prestige-sorting results —
an upset must not relabel which game was authored as the nightcap.

---

## 4. R28 — THE FALLBACK: BAND, THEN RADIUS IN AUTHORED STEPS, THEN SHORT

**(a) Best fit otherwise** (Emmett): when nobody legal fits a seat's band, the seat relaxes and
takes the strongest school available — the existing ladder, with per-seat provenance saying
which seats needed it. A down year fills the Jimmy V with the best names on offer.

**(b) ★ EVERY EVENT CARRIES A DRAW — a general event property, not a showcase field** (folded
from review): `National`, or a radius from the home city. **Every existing tournament defaults
to National** — Maui flies everyone; nothing about tournaments changes. Showcases author theirs
per the slate. This is C-40 walking into the event pool: distance yields as prestige rises.
Oklahoma State–Vanderbilt in Miami makes *a little* sense; Southern Miss and Davidson do not
meet in Phoenix outside a tournament.

**(c) ★ THE RELAXATION ORDER, RULED: THE BAND GIVES FIRST; THE RADIUS THEN WIDENS IN AUTHORED
STEPS AND NEVER GOES NATIONAL; A SEAT STILL EMPTY SEATS SHORT.** (Emmett: *"the radius has to
end."*) Harbor Classic looks 300 miles, then 500, then 900 — and if Brooklyn's basketball
neighborhood truly cannot staff it, the event goes dark that year exactly as a short tournament
already does not play. Locality survives; a bad year is a visible dark year, never Arizona in
Brooklyn. A local showcase invites a weaker neighbour before it ever widens — the band exhausts
inside each radius step before the next step opens.

**(d) ★ TOO FEW IS ANSWERED BY DEPTH, NOT BY BREAKING (c)** (Emmett: *"there needs to be
replacement showcases ready to take its place"*). The pool is authored DEEPER than the country
needs at every rung — the standby events are the replacements, already living in the world —
so a dark year at one showcase is covered by a sibling. Sixteen authored showcases average
**~11.6 active per season** (the oracle measures this; prose never asserts it).

★ **TRUE BIRTH AND DEATH OF EVENTS IS DEFERRED AND NAMED** — the persistence dial flickers, it
does not kill: a 0.55 event is dark some years and back others, forever. Events that
permanently fold and new ones founded in their place mean the world's event pool changing
across a career — a real feature, homed with the world-generation toggle (§8), not smuggled in
here.

---

## 5. R29 — THE STOCK SLATE: SIXTEEN LIVING SHOWCASES

Seats ×2 each; tiers assigned so the shared pick order works across both kinds; days sit before
December 7, where conference play opens. Persistence is the flicker dial (0.98 = an
institution; 0.50 = here some years, gone others). *(r2: four standby showcases added at the
bottom rungs per R28(d); Emmett's churn instruction — "ones that exist for a few years then
don't, or last for 35 years, all sorts of things" — is served by the persistence spread now and
by birth/death later.)*

| # | Showcase | City | Day | Tier | Headliner ×2 | Undercard ×2 | Persist | Draw |
|---|---|---|---|---|---|---|---|---|
| 1 | Champions Classic | Indianapolis | Nov 12 | 1 | 84–99 | 80–94 | 0.98 | National |
| 2 | Jimmy V Classic | New York | Dec 3 | 1 | 80–95 | 68–85 | 0.96 | National |
| 3 | CBS Sports Classic | Atlanta | Dec 5 | 2 | 78–92 | 65–82 | 0.90 | National |
| 4 | Hoophall Showcase | Chicago | Nov 29 | 2 | 74–90 | 62–80 | 0.88 | National |
| 5 | Lone Star Shootout | Dallas | Dec 4 | 3 | 70–88 | 58–76 | 0.82 | 900 mi |
| 6 | Sunset Showdown | Los Angeles | Nov 22 | 3 | 70–88 | 55–75 | 0.80 | 900 mi |
| 7 | Crossroads Classic | Indianapolis | Dec 6 | 4 | 60–80 | 45–68 | 0.75 | 500 mi |
| 8 | Holiday Festival | New York | Dec 1 | 4 | 68–84 | 55–75 | 0.72 | 500 mi |
| 9 | Music City Showcase | Nashville | Nov 21 | 5 | 55–75 | 42–62 | 0.70 | 400 mi |
| 10 | Queen City Clash | Charlotte | Nov 30 | 5 | 52–72 | 40–60 | 0.65 | 400 mi |
| 11 | Gateway Classic | St. Louis | Nov 28 | 6 | 48–68 | 35–55 | 0.62 | 400 mi |
| 12 | Heartland Showcase | Kansas City | Nov 20 | 6 | 45–65 | 32–52 | 0.60 | 350 mi |
| 13 | River City Rumble | Louisville | Nov 25 | 6 | 44–64 | 32–52 | 0.58 | 350 mi |
| 14 | Harbor Classic | Brooklyn | Nov 19 | 7 | 38–58 | 25–45 | 0.55 | 300 mi |
| 15 | Steel City Showcase | Pittsburgh | Dec 2 | 7 | 36–56 | 24–44 | 0.55 | 300 mi |
| 16 | Delta Classic | Memphis | Nov 26 | 7 | 35–55 | 22–42 | 0.50 | 300 mi |

Radius steps for every radius-drawn event: **authored radius → +200 mi → +400 mi → short.**
All sixteen cities exist in the world's places table (verified). Exact calendar legality and
the supply arithmetic (active count per season, seats by band vs candidate supply, fallback and
radius-step incidence, four-year pressure) are the **oracle's to measure** — no number in this
table is a fixture claim.

---

## 6. CLAUDE-LANE DEFAULTS, FLAGGED RATHER THAN ASKED

- **A showcase completes as two plain results — no placement table, no champion synthesized.**
  Whether the record can say so within the existing status words is a §9 source audit, not a
  mandate: do not invent a ceremonial status, and do not misuse a bracket-shaped one either.
- **A showcase is one day**; the window arithmetic (3 days for an 8-bracket, 2 for a 4)
  extends rather than bends.
- **Eligibility and reservation are two operations in a fixed order**: build each event's
  candidate pool (kind wall, four-year, overlap, draw, open-game capacity) → seat by the
  existing keyed draw within the filtered pool (filter first, never draw-then-reject) →
  reservation charges through the S103 seam. The exact sequence is the prompt's to pin
  against live source.

---

## 7. STOCK-WORLD VISIBILITY — THE POINT OF THE SESSION

Unlike S103, this session is **deliberately visible**: the sixteen showcases are authored into
the stock world and the season page shows them seating real fields — dormant distinguished
from short, short distinguished from played, per-seat relaxation notes, headliner and undercard
labelled from their stored seats. The honest cost, stated in advance: the November landscape
genuinely changes — seatings, requests and the matching all move, and every golden whose basis
includes them is **regenerated from its oracle, never patched**. The wall that holds: **the
conference schedule and its results do not move an inch**, and existing tournament fields do
not move either (id-keyed draws). Which fingerprints move and which must not is the build
prompt's §6b audit to enumerate exactly, against live source.

---

## 8. OUT OF SCOPE — NAMED SO IT IS NOT ABSORBED

- **★ THE LIVING-POOL SESSION (Emmett, deferred and named): the world-generation toggle (how
  many tournaments and showcases a world carries) AND true event birth/death** — events that
  permanently fold, new ones founded with generated names and cities, the pool changing across
  a career. One future session owns both; on the board so neither fades.
- **Sites and nights for everything else** — the semi-home ruling (half-written by R26's
  leftover note), the crowd model, R9–R12's full wiring. Arc session 6.
- **The shelf and the odds** — arc session 5; C-41's inversion knowingly still standing.
- **Anything behavioural** — no school declines an invitation, no organizer negotiates.
- **A general event-distance framework beyond the one Draw property** — the review's warning,
  adopted: showcases get what showcases need; tournaments inherit `National` and nothing else.

---

## 9. OPEN — FOR THE BUILD PROMPT, NOT FOR EMMETT

Claude's to specify and audit against live source at draft time:

1. The authored shape: the kind word beside `fieldSize`; the 8-or-4 loader wall gains the
   showcase form; the **bracket-assumption audit table** (every field/behaviour: shared /
   tournament-only / showcase replacement) — a showcase must never be inferred as a four-team
   bracket anywhere downstream.
2. Seating order across the two kinds in one pool (shared tier order today — verify nothing in
   the pick order assumes a bracket), and the deterministic rule for the newly-possible
   tournament/showcase date collision on one school.
3. The exact eligibility → seat → reserve → charge sequence through the S103 seam, including
   interaction with contract neutral legs charging the same chain in the same season.
4. The record shape for a two-game event (results without placement; stable game roles from
   stored seats; the S98 replaced-never-rebuilt write proven on a showcase round-trip; rerun
   creates no duplicates) and whether the existing status words truthfully cover it.
5. The Draw property against the S92 map — quantized like every other distance, the exact
   boundary comparison pinned, missing geography failing per the existing policy, radius steps
   exhausting the band at each step before widening.
6. Which goldens regenerate (matching, any results-bearing zero-path basis) and which
   fingerprints must NOT move (conference, dated; existing tournament fields) — enumerated
   exactly, with the recapture procedure named and non-interference asserted, not assumed.
7. What the page prints: each showcase's two games with role labels, per-seat relaxation and
   radius-step notes, dormant vs short vs played kept visually distinct, following the
   existing event page's shape.
