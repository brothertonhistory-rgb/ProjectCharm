# Project Charm — Status Board

The living done/to-do board. **Read this FIRST when planning any session** (CONVENTIONS §6a),
and update it in the docs step of every session (CONVENTIONS §3). Rules:

- Edited **in place**, like design.md — this reflects *now*; journal.md holds history.
- An item leaves **Open** or **Parked** only by **shipping** or by an **explicit ruling**
  (which moves it to Closed-by-ruling) — never by fading out of memory.
- Keep it short. This is a checklist, not a third journal. One line per item, with the
  session/phase that owns the detail. The S73 migration ledger (journal S73) maps every
  pre-rebuild item to its home here.

Last updated: **Session 94** (2026-08-03; verified on Emmett's machine — ALL CHECKS PASSED, Phase 85 PASS at 20
assertions, structural fingerprint `6f79d663…` UNMOVED, dated fingerprint `7515df7d…` in EXACT golden parity with the
locked oracle, 63 December games, the season page's basketball identical to S93 to the digit. **EVERY GAME IN THE
COUNTRY NOW HAS A NIGHT.** Three authored numbers per league — games, weeks, and days-before-Selection-Sunday its
tournament opens (`none` first-class) — and everything derives: the wall, a window of complete Mon–Sun playing weeks
counted backward from the last full playing week before the wall (the real 2026 Big East finished Sat Mar 7 against a
Tue Mar 10 wall — rest days into the tournament, never a partial week), Christmas skipped, exact weekly totals heavier-
latest, dates filling in the league's own authored night priority. ★ THE RULING THAT DEFINED THE SESSION: **LOOSE OVER
TIGHT**, delivered as EVIDENCE — two real Big East schedules overturned four load-bearing rules of the tight draft at
once (the week is Mon–Sun not rolling; December is the front of the window, not an isolated borrowed night; December
counts are unequal and correct; off days equalise in SLOTS, `2·weeks − games`). ★ THE SCHEDULER IS A ROTATION, NOT A
SEARCH: blind backtracking drowned on the first nine-team league; circle-method rounds with extras interleaved half a
rotation out date the whole country in 0.68 s with ZERO same-quarter rematch collisions — the spacing is structural.
★ A TWO-TEAM LEAGUE CANNOT BE DATED (every game a back-to-back rematch of the only opponent) — refused by name at
static validation; the Duo fixture is the standing example. World schema v4, v3 refused by name ("it cannot say WHEN
its own season is"), the world fingerprint's THIRD move invoked deliberately. Two predictions missed and owned: December
is 63 games in TEN leagues (the Atlantic Sun's real window plus the ruled-fine Dec 28–31 openers), and the fixture
fingerprint moved because the prompt's own C1 contradicted its own §7. Eight prompt revisions r8→r15, each smaller —
including one refusal DELETED BY PROOF (the weekly-capacity condition is a theorem, unreachable once nights validate).
Opens **O-81**. The S94=host-memory / S95=dates order on this board was reordered by Emmett; host memory (O-79) is the
next scheduler third.)

*(Previous board entry, S93 — 2026-08-02; verified on Emmett's machine — ALL CHECKS PASSED, Phase 84 PASS at 26
assertions, schedule fingerprint `6f79d663…`, 2,818 games, credit identity 3,996,770/399,677 = 10.0 dropped 0, census
4,511/4,511. **THE SEASON IS NOW THE CONFERENCE SLATE AND NOTHING ELSE.** Every league plays the number of games its own
`conf.csv` row has carried since the file landed — 14 for the Ivy, 16 for thirteen leagues, 18 for fourteen, 20 for the
Atlantic Sun — against the opponents the shape demands, in gyms chosen on purpose. ★ THE RULING THAT DEFINED THE BUILD
(Emmett, mid-session): *"we don't care about a 'season' right now, we care about games being scheduled."* Non-conference
play is **DELETED, not amended**: the 14-regular ring circulant, the conflict queue, the double-edge repair, the
20-attempt retry and the whole RNG stream that fed them are gone, and non-conference scheduling starts from nothing in
its own session. ★ THE FOURTEEN INDEPENDENT SCHOOLS PLAY ZERO GAMES — `Games = 0` is R14 and it is now live in the stock
world. ★ THE SCHEDULE CONSUMES NO RANDOMNESS AT ALL, so the same world builds the identical slate at every seed; asserted
deliberately, so the day someone wires a scheduler RNG the check goes red. ★ THE INVARIANT THAT WOULD HAVE PASSED WHILE
THE SEMANTICS WERE WRONG: all 347 schools ALREADY sat at exactly 8 home / 8 away in conference play, at eight seeds, in
all 32 leagues — the old Eulerian walk got it right by accident of even degrees, so "every team is exactly even" passes
on the PRE-S93 code and proves nothing. Phase 84's A9 is what discriminates: pre-fixed venues, which a Eulerian cannot
honour — sampled hosts all honoured with everyone still even, an over-commit refused BEFORE the flow, and a
quota-consistent-but-impossible set refused BY the flow. Schema v3; v2 refused by name and the WORLD FINGERPRINT MOVED a
second time, invoked deliberately. Two gate findings reshaped the session: the board's "next approved candidate" said
HOME COURT and the recorded reason inverted; and the rivalries turned out to be already authored under another name —
`TravelPart`'s 38 mutual in-conference pairs — which Emmett ruled stay put as travel partners. Opens **O-78**, **O-79**,
**O-80**.)*

*(Previous board entry, S92 — 2026-08-02; verified on Emmett's machine — ALL CHECKS PASSED, Phase 83 PASS at
66 assertions, and the full 5,205-game season page unmoved: PPP 0.9692, possessions 737,952, men who played
105,830, forced replacements 8,512, foul-outs 0.844, fouls 17.95, census 4,511/4,511, fingerprint `93d8c853…`
— every machine-of-record figure S89 recorded reproduced to the digit. **THE ENGINE NOW KNOWS WHERE THINGS
ARE.** 310 places, great-circle miles between any two, and the fact of who is hosting; world schema v2, with
v1 refused by name and no migration path. ★ THE RULING THAT SHAPED THE BUILD: **hosting is a SEPARATE FACT
from location** — where the game is, and whose gym it is. That one separation makes a neutral site stop being
a category (it is a game nobody hosts) and makes the five Philadelphia schools sharing one point cost
nothing. **NOTHING CONSUMES THE MAP** — no game is placed, no crowd is modelled, no home-court advantage
exists. ★ THE WORLD FINGERPRINT MOVED (`fa823da9…` → `9351889c…`), breaking the binding of any pre-S92
history file: ruled acceptable, asserted deliberately rather than discovered, free today because no career
exists outside this repo. Two rulings at the check-in gate: **Brooklyn stays** (its stated no-campus
criterion is false — St. Francis-NY is a mile away — but Barclays really hosts, and hosting is the criterion
that matters) and **St. Peter's moves to Jersey City** (the source data had it in New Brunswick on Rutgers'
exact coordinates, 25 miles off; nothing read a coordinate, so it had never mattered). Opens **O-77**
(`teams.csv` may hold more mis-located schools; St. Peter's was found by accident, and nothing has ever
audited the column). ★ TWO CHECKS FAILED IN THE SANDBOX AND WERE RIGHT TO: a contiguous-US bounding box
turned out to contain the Bahamas, so the exotic-list check was rebuilt as a two-armed negative control that
constructs the US-shaped validator and requires it to reject all eight; and a v1 file was dying with
"'places' array is required at root" because the parser demanded the place table before anything checked the
version. ★ ONE PREDICTION OF THIRTEEN MISSED, and in the harmless direction: the triangle-inequality
violation came back 0.000E+000 on Windows against a predicted 1.8E-012 — the arcsine lands exactly there and
one bit short on Linux. It should have been flagged as platform-dependent alongside the two deviation figures
that were..))

*(Previous board entry, S91 — 2026-08-02; verified on Emmett's machine — ALL CHECKS PASSED, Phase 82 PASS at
53 assertions, and the full 5,205-game season page byte-identical to its pre-S91 self, fingerprint `93d8c853…`.
**THE ENGINE NOW KNOWS WHAT DAY IT IS.** Any year 0001–9999 with correct weekdays and leap years from one code
path, nothing authored and nothing shipped as data, plus the season spine: November 1 as a FLOOR, Selection
Sunday as the third Sunday in March, and the ten D1 tournament dates derived from that single anchor. ★ THE
RULING THAT SURVIVED THREE PROMPT REVISIONS: legality is ONE CONTINUOUS SPAN, Nov 1 through championship
Monday, every day inside it legal — the ten D1 dates are REFERENCE DATA that permit and forbid nothing, and
Selection Sunday is a legal playing day. r1 made a date intrinsically "conference tournament"; r2/r3 made D1's
bracket the definition of when basketball is legal; both are one competition's schedule mistaken for a property
of the date. NO GAMES ARE PLACED ON IT — the scheduler is its own session. Opens **O-75** (the observation run
and stress test cannot fail the suite: both are `void` and wired in after the verdict is computed) and **O-76**
(no way to run the checks without simming 5,000 games). ★ ONE CHECK-IN FINDING WAS WRONG IN THE DIRECTION OF
CORRECTING THE PROMPT: Claude reported "the stock world has ZERO independents; the prompt's claim is false."
There are **14**, modelled as a conference named `Independent` — Claude tested for an empty conference field
instead of reading conference names, and the word was in plain text on the season page it had already read. The
prompt's reasoning was still a non-sequitur, so the claim was true for a reason its author had not given and
the correction was false for a reason Claude had not checked.))

*(Previous board entry, S90 — 2026-08-02; verified on Emmett's machine — ALL CHECKS PASSED, Phase 81 PASS at
41 assertions, season page byte-identical with zero new lines. **Closes O-31**: per-game retention, forever,
at full detail. Ships history schema v2 and folds the offensive foul into the season record (the printed
column stays O-67). Opens **O-74** (hierarchy rank is a constant 5 league-wide). ★ THREE CLAIMS THAT FOUR
PROMPT REVISIONS CARRIED WERE FALSE AGAINST SOURCE and were caught before the format hardened: the row was
missing a counter the engine already attributes; the world fingerprint is a labelled string, so its label
check can only live at the writer, never the reader; and the "compact JSON is 5–8× the bytes" argument that
justified a custom binary format is wrong by most of an order of magnitude — measured, it is a wash, and the
format now stands on exact size arithmetic and index-target predictability instead. A fourth, "the measured
105,830 rows", was a seat-occupancy count presented as a row count; the true count was measured at the end
and happens to be exactly 105,830, so the bound was tight and the claim was still wrong when it was made.))

*(Previous board entry, S90 chores commit — 2026-08-02; DOCUMENTATION ONLY, no source changed. Closes **O-71**: S88's transition-defence subsystem finally has a journal entry and a design section, backfilled from the locked oracle, the source headers and Phase 79. The eight rulings survive verbatim because S88 wrote them into the oracle header; the design conversation does not survive and was not reconstructed. Opens **O-72** (class year is generated and dropped before the season) and **O-73** (the development ceiling — latent card, runway, arrival — is computed for all 4,511 men every world build and discarded one line before the season sees it; Emmett ruled it out of the S90 archive as not-history, which settles retention but not the engine gap). ★ ONE CHORE ON THE S90 PROMPT WAS PHANTOM: an "O-number renumber" rode through four prompt revisions unverified and does not exist — there are no duplicate O-numbers and O-25 is simply a number never assigned. The real collision in the record was S88's PHASE number, fixed at S89.1. A chore claim inherited across revisions got none of the source audit every code claim around it received.)

*(Previous board entry, Session 89.1 — 2026-08-01; CORRECTION SESSION. `git add -A` at the end of S89 swept in five UNTRACKED files and revealed that **S88 is a complete, working session**: a per-man transition-defence model wired into the live engine (`RollHGenerator`'s break make and block doors, `Resolver`'s defender draw — ~98,000 shots a season), oracle-locked, retiring the four `HustleTransitionDefense*` dials for twenty `Transition*` ones. Emmett's `config.json` was CORRECT and S89 reverted it, breaking a working session; restored. S89's account of S88 is wrong in four places and is corrected in the S89.1 journal entry — the S89 entry itself is left untouched, because the journal is immutable history. ★ A REAL DEFECT S89 INTRODUCED: a phase-number collision. S88 declared `Phase79TransitionDefenseCheck`, S89 registered `Phase79IdentityCheck` as 79, and **S88's phase had no caller anywhere — it had never run once**. Registered here at 79 by date; identity moves to 80. First-ever run: 40 of 43, all three failures one cause — S88's two golden fixtures were never added to the harness project. Both added; suite now green with 43 + 71 assertions that had never both run. ★ O-69 STRUCK: Phase 71 is not broken. Handed a real mismatch it named all four orphan keys and all twenty absent ones in exactly C-25's language. S89 never opened the red suite to see which phase failed.)

*(Previous board entry, S89: PERMANENT IDENTITY AND THE HISTORY FILE. Every person, season and game now carries a number issued once and never issued again — high-water counters in a named save file, `--history <path>`, with no default path. Identities live in a NEW PROJECT, `src/Charm.History`, and the project boundary IS the seam: the raw `long` is `internal` with no `InternalsVisibleTo`, so no calibration or domain file can write "person 4001 is older than person 4000". No ordering, no conversion, no arithmetic — only equality and hashing. **No basketball moved**: the full season page with and without a history differs by exactly THREE banner lines on Emmett's machine. Phase 79 adds 71 assertions, all green first time, two of which discriminate (the reflected type surface; isolation with a real negative control that moves one live per-player field). The check-in found the world already HAD a canonical writer — the fingerprint's projection was split out of `WriteWorld` rather than written twice — and found a landmine in Phase 55's determinism replay, which used generated record equality and would have gone red the first time it saw history mode. ★ THE SESSION ALSO FOUND S88: `config.json` carried a STAGED, uncommitted edit — twenty `Transition*` dials with no code behind any of them, four `HustleTransitionDefense*` keys removed whose engine defaults match the deleted values character for character. Behaviourally inert, saved out, reverted, opened as O-68 — and **Phase 71 went green through all of it**, which is O-69. Four honest misses recorded, all the same mistake: sandbox numbers asserted against Emmett's Windows machine, including the Gate 1 fixture itself (O-70). Suite `ALL CHECKS PASSED`, season verified on Emmett's machine.)

*(Previous board entry, S86: THE COACH BECAME A GATE. Roll J's run-or-not balance stopped being a flat number nudged by two additive lifts and became a score the players build against a bar the coach sets: the ball-winner's legs and his outlet pass as two OVERLAPPING escape routes, his four teammates' speed against all five defenders' speed getting back, and the offensive coach's pace setting the height that opportunity must clear. Both old lifts RETIRED rather than supplemented — pace was a nudge and is now the gate, so keeping both would let pace pay twice, and `Speed` lives inside `Player.Athleticism`, so keeping the five-way composite gap beside the new speed race would pay fast teams twice. League push 33.55% -> 35.70%, but the mean is not the finding: **per-offensive-team push now runs min 18.89% / median 36.16% / max 48.15%, a 29.26pp band, on rosters alone**, because every bar in the country is still the neutral 0.475 (O-57). Read against the entries-conceded band (12pp): who GETS transition chances is far more uniform than who RUNS on them. ★ THE RULING THAT CHANGED THE BUILD: the free-throw board was measured BEFORE it was wired and pulled out of scope — base Push 0.08 against a swing of 0.22 pinned a plodding rebounder to exactly 0.0% at grind AND neutral pace while everyone with legs jumped to ~28%, a source with no middle, and one of the two zero rows was an *average* rebounder against a fast defense. The locked oracle independently confirmed the call: its tables and its 45-case golden fixture carry only the live board and the two steals, so it never modelled the free-throw board at all and needed no edit. ★ THE CHECK-IN CAUGHT TWO SUITE CHECKS THAT WOULD HAVE GONE RED WITH NOTHING WRONG (a tempo-direction check and a tired-legs check, both stamping which team had the ball but never WHO); the same defect would have silently left the exploratory transition ladder reading dead flat — the S59.2 flat-baseline trap. Golden parity worst |delta| 4.92E-013 against a deliberately non-bitwise 1e-6 bar (S81.3 lesson). Nine of nine suite and eighteen of eighteen season predictions landed exactly; sandbox byte-identical to Emmett's machine. Three honest misses recorded, including one wrong claim in the build prompt that survived the §6b audit: entry rate DID move (41.61% -> 41.56%) because changing a pie shifts the RNG stream. Opened O-61, O-62, O-63. Suite `ALL CHECKS PASSED`, season verified on Emmett's machine.)

## Current baseline

**S94 dated every game and moved NO basketball**: the page below reproduces to the digit with one added banner line
(`Dated: season 2026-2027, 63 December games, dated fingerprint 7515df7d…`).

**★ S93 REPLACED THE SCHEDULE, SO THE PAGE BELOW IS NOT COMPARABLE LINE FOR LINE.** The season is now **2,818
conference games** instead of 5,205, every school plays its own league's number instead of 30, and fourteen schools play
none — so any TOTAL (possessions, games played, minutes leaders, qualifier counts) has moved for a structural reason and
not a basketball one. **Verified on Emmett's machine at S93** (seed 20260720, schedule fingerprint `6f79d663…`): PPP
**0.9675**, fouls/team/game **18.00** (6.36 shooting / 11.64 non-shooting), offensive fouls 1.55, foul-outs 0.831,
possessions **399,677**, credit identity 3,996,770/399,677 = 10.0 dropped 0, men who played a possession 57,213, census
4,511/4,511, qualifiers `>=100 3002 | >=250 2309 | >=500 432 | >=900 0`. **Per-possession rates are the comparable
lines and they held**: PPP 0.9692 → 0.9675, pace unchanged, turnovers and rebounds unmoved. **Blocks moved 4.1 → 4.6 and
steals rose with them, with nothing in the shot-blocking machinery touched** — power-conference bigs now face only
power-conference offenses and the low-majors only each other, so the size mismatches are distributed differently than
when the leagues were mixed. It is the opponent pool, not the engine. **Short seasons also make every rate leaderboard
noisier**: the best passer reads 35.0% AST%, finally in the elite range, on 55 assists over 14 games; five men shot 100%
from the line. Read the top of a rate board as luck until the season is long again.

**The S78 page is the arc's recorded reference — and it is PROVISIONAL pending a calibration session**
(seed 20260720, world `stock-d1`, schedule fingerprint `93d8c853…` unchanged): points 68.5, FG% 42.9,
3P% 34.9, FT% 69.9, PPP 0.9651, TO% 22.2, pace 71.0, rebounds credited 31.4, fouls **17.95/team/game
(6.31 shooting / 11.64 non-shooting)**, blocks 4.1, steals 7.5, assists **13.1 (S84; was 9.9 — the only line S84 moved)**, usage max/p90/median
36.9% / 18.0% / 6.2%, top-five share of floor time 69.7%, cross-position occupancy 24.44%, census
clean (4,511/4,511 drafted; 347/347 exact rosters; 347/347 protected coverage). Every calibration
session diffs against that page, never against memory.

**★★ S89 CORRECTION — THE RECORDED BASELINE NUMBERS BELOW WERE TAKEN FROM A SANDBOX RUN, NOT FROM EMMETT'S MACHINE.** S89 ran the identical committed tree on Emmett's machine and got **PPP 0.9692** (not 0.9710), **foul-outs 0.844** (not 0.840), **737,952 possessions** (not 738,211), **105,830 men who played** (not 105,823), **8,512 forced replacements** (not 8,507). `Math.Pow` is not bit-portable, and over three-quarters of a million possessions the last-bit differences cascade into a handful of different shot outcomes. **Emmett's machine is the verification of record (CONVENTIONS §2), so HIS figures are the baseline** and the sandbox figures below are wrong. The numbers that ARE machine-independent and unchanged: schedule fingerprint `93d8c853…`, fouls/team/game **17.95**, offensive fouls **1.54**, census 4,511/4,511, qualifiers `>=100 3470`. **Standing rule for every future session: a recorded reference figure must come from Emmett's run, and the session that records it should say which machine produced it.** See O-70.

**★ S87 moved PPP 0.9719 → 0.9710 and NOTHING else outside the foul columns.** *(sandbox figures — see the correction above)* Team fouls, turnovers,
rebounds and assists are unchanged to the digit; the whole delta is tellable as *someone fouled out and a
different man played*. Credit identity `7382110 / 738211 = 10.0`, dropped 0; every reconciliation at
residual 0. **The per-player SFL/NSF columns will not match any pre-S87 run and cannot be expected to** —
those were drawn post-hoc, in a different order, over a rebuilt lineup. Three new page lines, all
page-only: `offensive fouls/team/game 1.54` (charged to the MAN only — deliberately NOT folded into the
17.95, which is the bonus-relevant number), `foul-outs per team-game 0.840`, and the personal-foul spread
over the 105,823 men who played a possession — **0 21.2% · 1 24.6% · 2 21.3% · 3 15.5% · 4 9.1% · 5+ 8.3%**.
The escape hatch fired zero times; forced replacements 8,507 against 8,507 trips played while
disqualified (exactly one each — the known finish-the-trip gap, measured rather than estimated).

**S84 added a second reference line to the page, and it is the drift alarm for the assist door:**
`lineup passing factor applied: league mean 0.9994 over 237989 assist-eligible makes` /
`by team (n=347): min 0.8583 p10 0.9257 median 0.9991 p90 1.0678 max 1.1474`. Read BOTH — the league
mean going off 1.000 means the generator moved under the midpoint; the team band collapsing means the
swing has gone inert. Neither can detect the other's failure. *(Previous reference: the S77 page — points
72.4, FG% 45.8, 3P% 35.9, FT% 70.5, PPP 1.0176, TO% 21.6, pace 71.1, fouls 20.23 = 6.47/13.76.)*

**Why it moved at S78, and why NOTHING was chased.** Seventeen calibration verdicts read HIGH or LOW.
`PrintCalibrationReadout` is a `void` print — page-only, never asserted — and no dial was touched.
The page moved because the POPULATION was corrected: `Discipline` went ~17 → ~53 and S62 wired it
into reach-in foul propensity, so fouls fell 20.23 → 17.95, FTA fell with them, and free points went
with that. Defense also strengthened league-wide, which outran Roll H's IQ make-bonus finally
switching on (`clamp((IQ − 50)/49)` — dead at the old league mean of ~17).

**S77 changed no simulated number.** The page grew from 493 lines to 627 by appending the stat section;
the 493 pre-existing lines are **byte-identical** to S76.1, proven by line diff, not by hash. Only the
fingerprint moved (`38ec0e9f…` → `96eb2c3a…`). New on the page: 4,511 player-seasons, 1,018 of whom never
took the floor; qualifiers `>=100 3470 | >=250 2810 | >=500 2082 | >=900 439`; league per-game medians
points 4.4, rebounds 2.1, assists 0.6, minutes 16.1.

**Why it moved from S76 (S76.1, one line).** No dial, no engine path, nothing simulated. `AttributeGame`'s
per-slot shooting block still carried the literal `>= 20` guard S75 replaced everywhere else, so stamped ids
21–26 logged no FGA/FGM/3PA/3PM/FTA/FTM at all — 56,714 shot attempts a season, ~10% of the league, every
road game. Attribution is post-hoc, so **492 of 493 page lines are byte-identical**; only the box-sourced
usage line moved, 42.0/19.5/5.0 → 38.8/18.1/6.3. Journal S76.1 carries the measurement and the reason it
survived two sessions.

**Why S76 moved from S72 (deliberate, S76).** No dial was touched. The minutes allocator changed WHO IS ON
THE FLOOR, so every rate shifted slightly: PPP 1.0246 → 1.0176, points 72.8 → 72.4, FG% 45.9 → 45.8,
3P% 36.2 → 35.9, TO% 21.3 → 21.6. **This is the point of the session** — the S72 numbers described a league
where five men played 35 minutes, so they were never the right calibration target. Engine state: all Rolls
A–M real; the world drafts the Pass-3 two-plane budget cohort (S70 bridge); `PressureVolumeTaxScale` 0.30 is
the one calibrated dial (S72); the settings file and the config classes are name-parity-locked by Phase 71
(S74) — `config.json` SHA-256 `5094367e…`.

## Shipped since the last board update

- **★ S93 — THE CONFERENCE SLATE: EVERY LEAGUE PLAYS ITS OWN SEASON.** `data/conf.csv` has carried a
  `Games` column for all 32 leagues since the day the file landed and the converter read the column and
  threw it away; the season hardcoded sixteen for everybody and dealt fourteen random non-conference
  opponents on top. Now the authored number is the only number. A league's `Games` and `Skip` define the
  shape — `p = n−1−k` opponents played, `r` of them at `q+1` meetings, the rest at `q`, `k` skipped — and
  **rivalries are placed by construction, never searched for**: a rivalry buys a place in the shape (top
  meeting count, never the one skipped), never a number of games, and where every pair already meets the
  same number of times it provably changes nothing. **R3 is a hard line** — every team hosts exactly half
  its league season, by construction for even meeting counts and by an integral flow for the odd residual.
  Four verdicts kept strictly apart (`InvalidConfiguration` / `InfeasibleUnderConstraints` /
  `SearchBudgetExhausted` / `UnsupportedConferenceSize`); the search is **exhaustive to n = 20**, which is
  what licenses the word *infeasible*, and above 20 it refuses without searching. Stock: **2,818 games**,
  derived from the world and asserted once as a golden. New fixture world `fixture-schedule` (32 schools,
  five leagues) because the tiny fixture has zero unbalanced games and could not exercise any of this.
  The host-memory venue seam (`FixedResidualHost`) is **built and empty** (S94 shipped dates instead; the seam waits for the next scheduler third). Phase 84, 26 assertions.
  Opens **O-78**, **O-79**, **O-80**.

- **★ S92 — GEOGRAPHY: THE ENGINE HAS A MAP.** Every school had carried a real latitude and longitude since
  the world layer shipped and **nothing computed anything with them** — there was no distance function
  anywhere. Now there are **310 places** (293 campus cities plus R2's two authored lists: nine domestic host
  cities, eight exotic), great-circle miles between any two (spherical haversine, mean radius 3958.7613), and
  `GameSite` = a place plus a host that is **`Nobody` or `School(id)`, an explicit tagged value and never a
  nullable int**. A school no longer carries `city`, `state`, `lat` or `long` — it carries one `placeId`, so
  it has exactly one answer for where it is. World schema **v2**; v1 refused by name from one guard shared by
  the parser and the validator, with the re-conversion command in the message, and **no migration code** on
  purpose. `dotnet run -- geography <world.json>` prints the map. **Nothing consumes it**: no game is placed,
  no crowd is modelled, no home-court advantage exists at the end of this session. Phase 83, 66 assertions,
  green first time on Emmett's machine; season page unmoved.
  - ★ **The tolerance is EVIDENCED at run time, not asserted** — platform variance 1.09E-011 mi ≪ tolerance
    1.00E-006 mi ≪ wrong-formula error 10.1 mi, with the left number *measured* by perturbing every library
    trig call by 4 ULP in every combination. ★ **Near-antipodal is the one place that ordering breaks** (a
    last-bit wobble moves the answer 1.7E-004 mi), so that probe asserts properties — finite, clamped, never
    more than half the way round — and never a mileage. A golden number there would have been S81.3 again.
  - ★ **Three invariants that would have passed while the semantics were wrong**, each answered: a distance
    function tested only on nearby American cities passes with flat-earth arithmetic (so the golden is
    dominated by long pairs and a planar negative control must FAIL them, scoped to the long rows by name); a
    place table passes every structural test while the authored entries are silently dropped (so all seventeen
    are asserted individually, by name); and a golden mileage table from an online calculator fails a CORRECT
    implementation for the right-looking reason (so the golden pins the **model** — spherical haversine, that
    exact radius, computed outside .NET from the exact serialized coordinates).
  - **The data trap:** 13 city names appear in two states (Durham NC vs Durham NH would have put Duke in New
    Hampshire) and five cities disagreed with themselves on coordinates. The collapse rule — lowest school id
    in the city wins — ran **once, by hand, into `data/places.csv`** and is never executed at load. No school
    moved more than 1.38 miles. The converter **refuses by name** any school whose csv city/state disagrees
    with its resolved place: a resolving id is not sufficient.
  - **Permanent contract decisions** (three prompt revisions, all twenty required changes on this): `PlaceId`
    IS the identity and `(name, subdivision, country)` is only a uniqueness constraint; ids are **authored,
    never generated**, never reused, never compacted; `country` is ISO 3166-1 alpha-2 **strictly**, so PR and
    VI take their own codes rather than being filed under US; `tags` is authored data nothing reads, canonical
    sorted array; coordinate serialisation pins the **mechanism** (the existing `Utf8JsonWriter` path), because
    `1`, `1.0` and `1E+00` all round-trip and all hash differently.
  - Honest misses: the purity scan tripped on its own documentation (searched for "config", found it in the
    header line promising no config — the same fix Phase 82 had already recorded and this session did not
    carry across); and the stock world was never copied beside the binary, costing one suite run.

- **★ S91 — THE CALENDAR: THE ENGINE HAS A CLOCK.** Before this, a season was 5,205 games in an arbitrary
  order and no game had a date. Now any year from 0001 to 9999 comes out with correct weekdays and correct
  leap years from one code path — **nothing authored, nothing shipped as data**, so a custom world starting
  in 1850 gets the real 1850 calendar (R1). Proleptic Gregorian, named as a convention rather than assumed.
  On top of it sits the season spine, all of it derived from two anchors: **November 1 is a FLOOR, not a
  start line**, and Selection Sunday is the third Sunday in March, from which the two Thu–Sun weekends, the
  Final Four Saturday and the championship Monday at +22 all follow — verified across all 9,998 supported
  seasons with zero weekday violations. **Legality is ONE CONTINUOUS SPAN** and season membership is the
  same span under one name; the ten D1 dates are reference data that gate nothing, because the NIT, D2, D3
  and JUCO play the days D1 rests. Periods overlap by construction (R9) and none is registered. Phase 82,
  53 assertions, three negative controls including a rebuild of the rejected gated-legality design. **No
  games are placed on it** — the scheduler is its own session, deliberately, because a calendar built
  alongside a scheduler bends around whatever the scheduler needed that day.

- **★ O-31 CLOSED — EVERY GAME EVERY MAN PLAYS IS RETAINED, FOREVER (S90).** A career bound to a history
  file now writes one permanent log per season: a roster section (name, school, seat, position, archetype,
  recruiting rank and all 38 ratings, **including the 661 men who never played a minute**) and one row per
  man per game, 21 counters each. Career highs, full college stats and the game-by-game log all derive from
  those rows; the S76 per-RANK minutes ladder is now re-derivable by identity, which season totals could not
  do. Stock season: 4,511 entries, 5,205 blocks, **105,830 rows, 21,162,128 bytes**; ~807 MiB for a forty-year
  career. Season page byte-identical, zero new lines. Phase 81, 41 assertions. Also ships history schema v2
  (`historyId`, born-v2, one-way migration for pre-S90 careers) and **adds the offensive foul to the season
  record** — the engine had named the man since S87 and the season line had been dropping him.

- **★ O-71 CLOSED — S88's JOURNAL AND DESIGN ENTRIES ARE WRITTEN (S90 chores commit).** The
  transition-defence subsystem now has a session entry at its chronological slot in `journal.md`
  (between S89 and S87) and a `design.md` section beside its S85/S86 transition siblings. The eight
  rulings survive verbatim because S88 wrote them into the oracle header; **the design conversation
  that produced them does not survive and was not reconstructed** — that limit is stated in the entry
  rather than papered over, and it is the honest cost of shipping without docs. No source changed.

- **★ S88 — WHO GOT BACK: THE LAST TEAM-SCALAR IN THE ENGINE IS GONE.** *(Shipped at S88, committed
  accidentally at S89, verified at S89.1 — its suite phase had never run until then.)* A fast break
  was one team average against another: the defence's mean Hustle shaved a couple of points off the
  make, nobody guarded anybody, and the engine could not tell a break against an elite rim protector
  from one against a shooting guard. Now each of the five carries a GOT-BACK number — his legs, and
  how deep the man he is guarding starts — doing three jobs: WHO defends this break, HOW SET he is
  when he arrives, and HOW MANY got back (the dominant channel on conversion). Oracle-locked, ported
  constant for constant. **R2: depth is set by the man you are guarding, never by your own body** —
  read off the OPPOSING lineup, so a defence is not stranded under the rim against a team that goes
  small; A9 builds the own-body mis-wire and proves the check rejects it (five identical defenders
  collapse to one number where the real wire gives 1.5495 / 1.4575 / 1.2803 / 1.0383 / 0.9186).
  **R6: Hustle rides inside the legs term**, no channel of its own — keeping both would pay a fast,
  high-effort team twice (the S86 double-count). Anchor exact: five average men on an average
  offence give 1.000000000000000, and break make/block reproduce their configured bases to the
  digit. Phase 79, 43 assertions, no basketball target among them. See O-71 — it still has no
  journal or design entry.

- **★ S89 — PERMANENT IDENTITY AND THE HISTORY FILE. The first persistent layer that outlives a run.**
  `PersonId` / `SeasonId` / `GameId` are readonly record structs hiding a `long` in a **new project**,
  `src/Charm.History`, referenced by the harness and referencing nothing. The raw accessor is `internal`
  and the assembly grants **no `InternalsVisibleTo`** — that is the seam, and it is a compiler guarantee
  rather than a convention, because the whole harness (every calibration file, every check) is one
  assembly where `internal` would seal nothing. No ordering, no conversion, no arithmetic; equality and
  hashing only. Zero is not a person — issuance starts at 1, so `default` is invalid, enforced at
  boundaries by `IdentityGuard`. The file holds **three counters and a world binding and nothing else**,
  schema v1, pinned byte-for-byte by `tools/history_v1_golden.json`; the loader is as strict as the world
  loader with sixteen classified refusal reasons, and **a parse failure is never treated as "no file"**.
  Allocation is **high-water, never a free list** (the counters ARE the proof; a list that knows which
  numbers are free can be wrong, and a wrong list means two men share a career), **reserve → persist →
  issue** in that order, one batch per kind, checked half-open arithmetic that rejects a whole oversized
  reservation with the file untouched. The lock is a **sidecar** `.lock` file taken before existence is
  even checked, released before the long simulation. `--history <path>` is **named with no default** — a
  hidden file appearing beside the binary is how a throwaway run burns 4,511 numbers out of a real career.
  Transport is a frozen, validated `PoolId -> PersonId` bijection built at the one construction site and
  handed to the accumulator once; a lookup **throws**, never skips. `PoolId` untouched (it is
  position-encoded; renumbering it reclassifies the league). `WriteWorld` split into `CanonicalWorldBytes`
  + a thin writer so the fingerprint hashes the SAME canonical form the converter emits. **Phase 79**
  adds 71 assertions, no basketball target among them; the two that discriminate are the reflected type
  surface (banned operations proven *unwritable*, `GetHashCode` exempt by name) and isolation with a real
  negative control (one live per-player field moved by a single shot, comparison required to go red).
  **Phase 55's determinism replay was fixed first** — it used generated record equality and would have
  gone red with nothing wrong the moment it saw history mode. Verified on Emmett's machine: full 5,205-game
  season, legacy vs history, **three banner lines differ and nothing else**; counters read 4512 / 2 / 5206.
  Opened O-68, O-69, O-70.

- **★ S87 — REAL FOULS: THE COMMITTER MOVED TO THE WHISTLE, AND FIVE OF THEM SIT A MAN DOWN.**
  Foul attribution left the post-hoc harness pass and entered the engine at the moment of the whistle —
  the point being that a foul decided after the final buzzer cannot change who is on the floor.
  `PersonalFoulTracker` hangs off `GameState` (game-scoped, **not** the half-scoped `FoulTracker`, which
  would forgive first-half fouls at the break), counts are **uncapped** (the escape hatch makes 6 and 7
  reachable), and `FoulOutThreshold` lives in `RollDConfig` beside the bonus thresholds. `FoulCommitter`
  is a new engine unit holding the **Session 62 weightings verbatim** — moved, not tuned, including S62's
  own flagged `InteriorTiltScale = 40.0` debt. A third ledger, `OffensiveFoulEvent`, closes the gap where
  an offensive foul reached no foul count at all. The disqualification guard sits on the **SEAT**
  (`Roster`, injected by `GameState`, the only place a roster is constructed) rather than in the
  substitution policy, so no present or future policy can re-insert a fouled-out man.
  `MinutesAllocatorPolicy.ForceFoulOutReplacements` runs ahead of the ordinary move, keeps positional and
  legal-lineup rules, drops the minutes plan / minimum stint / one-move limit, and **ignores the dead-ball
  gate** (Emmett's S87 ruling). **Phase 78** adds twenty checks, no basketball target among them: S62
  parity at 400,000 draws with zero mismatches *plus* a guard that the zone tilt still flips; totality;
  seat-and-team conservation on all three ledgers with a hand-built wrong-team **negative control**;
  reset-proof positive-delta reconciliation with the offensive-foul residual asserted at exactly zero;
  stateful accumulation across the threshold (§2a); and **inert-mode isolation with its discriminating
  half** (vary only the foul seed → everything pre-S87 bit-identical AND the committer columns *do* move).
  Three rulings taken mid-session that the cleared prompt did not contain — see C-33, C-34, C-35.
  Opened O-64, O-65, O-66.

- **★ S86 — THE TRANSITION OPPORTUNITY SCORE AND THE COACH BAR. The first dial the S85 readout was built to grade.**
  `escape` (the ball-winner's better route counting fully, his second a third as much, renormalised) + `race`
  (four teammates' speed vs five defenders') builds an opportunity; `bar` = `BarBase - mappedPace x BarPaceSwing`
  is the height it must clear; `PushSwing x tanh(margin / MarginScale)` becomes ONE bounded Settle<->Push transfer.
  The bounded transfer is the pre-S86 arithmetic **verbatim** — only where `rawDelta` comes from changed, plus the
  clamp being lifted into `RollJGenerator.BoundPushSettleTransfer` (because under the configured dials NEITHER
  clamp is reachable end-to-end on any wired source, so only a direct call can prove it binds).
  `PaceScale` and `AthleticismGapScale` are **retired** from the config. `TransitionContext` gained
  `BallHandlerSlot`, filled by the **Resolver** at its Terminal case (the four emitters run before the pickers and
  cannot know who was chosen). `FatigueTracker` gained `AthleticismDiscount` + `EffectiveSpeed`; passing is NOT
  discounted — passing is not legs. **Phase 77** adds 26 assertions, no basketball target among them: golden
  parity vs the oracle's own committed output, FOUR separate neutral-rule early-outs, the clamp called directly,
  five one-dial monotonicity sweeps, the overlap ruling as four strict inequalities on a saturation-free quad,
  six config guards plus the `PushSwing = 0` kill switch, and the free-throw exemption across twelve cells.
  Season page gained ONE line and two per-offensive-team counters: the push% band that makes the spread readable.
  **Read the per-team band, not the league mean** — a moved mean with a collapsed band means the wire went inert.

- **★ S85 — THE FAST-BREAK READOUT. The transition-defence axis is now measurable; nothing else changed.**
  Thirteen new page lines under the S38 diet line, credited to the DEFENDING team throughout: entry rate league
  and conceded per team; the five SIBLING arms of the run-or-not decision (a turnover, foul or tie-up happens
  *instead of* a push, never after a failed one); pushes selected vs break shots produced, with the push-born /
  press-born split; the three-way shot partition with FG% for each; break FIELD-GOAL points allowed per team per
  game; block rate in all three buckets; and the concentration of break blocks on a team's top defender.
  Engine side: Roll J stamps the arm it rolled (one nullable label, five arms untouched), the shot partition
  became one exhaustive if/else-if/else chain, and `FastBreakBlkBySlot` joined the per-seat accumulators.
  Harness side: a new per-player break-block column that rides the SAME seat-to-man path as ordinary blocks and
  joined the run-to-run reproducibility contract. **Phase 76** added sixteen conservation and wiring checks,
  no basketball target among them, two of which are existence checks whose absence FAILS.
  Read the concentration line against **O-48** before designing anything from it.

- **S84 — `AssistPassMidpoint` 71.31 -> 30.73, plus the instrument that makes the next drift visible.**
  One config value is the whole behaviour change. The midpoint is not a tuning dial — it is a MEASUREMENT
  of the player pool, and it rots whenever the generator moves; S41's 71.31 was 40 points above the real
  figure. Level: league assists 9.9 -> 13.1 (LOW -> OK). Separation, which no page was showing: team
  factors 0.752-0.791 -> 0.858-1.149. The swing was NOT the problem — nothing saturates, the most extreme
  team sits 0.74 scale widths out. Page-only instrument shipped alongside: the realized factor carried out
  of the engine as a per-possession sum-and-count pair appended LAST to `PossessionRecord` (S62 convention),
  read off the same local the probability uses; proved inert by a byte-identical season page (same SHA-256)
  against a config-only run. Phase 39's bounds sub-check rewritten from a tautology into a real composition
  test at measured percentiles, with an explicit unreachable-lineup case for the ceiling arm and the floor's
  structural unreachability printed rather than asserted. Five stale doc sites corrected. Three block
  fixtures re-stamped (label only; CREDIT golden parity passed at EXACT zero on both sides).

- **S83 — the reach term becomes two-sided (engine + config + three rewritten checks + a new bench).**
  `Matchup.HeightOverDefenderShift` drops v1's `max(0, …)` clamp, so the make door's reach term is SIGNED:
  the shorter shooter is docked by the same curve the taller one is paid. No second constant, no asymmetric
  exponent, no separate negative arm — `tanh` is odd and supplies the mirror. Four config values: rim
  magnitude 15.0 -> 110.0 via `HeightMaxBonus`, with Short/Mid/Long weights divided by 110/15 so every
  non-rim zone's ABSOLUTE magnitude is unchanged (asserted at 1e-12 with a negative control, not claimed in
  a comment). Compiled class defaults deliberately left at the S55 numbers. Phase 61 rewritten (exact-zero
  set now equal-reach and Three only; SMALL_ON_BIG re-signed as the penalty arm; Long follows the sign of
  the gap; new signed probes for symmetry, both-arm monotonicity, both-arm asymptote and both-arm kill
  switch; a positive-side preservation block). Phase 71 Arm 6 split into eight-against-defaults and
  four-against-the-ruling. Phase 74's whole-section config fingerprint re-stamped on three fixtures —
  label only, not one saved number regenerated. New `reachbench` CLI instrument, NOT in the suite.
  Block and foul proved bit-identical pre-to-post at every bench row (sandbox rung — see O-55).

- **S81.3 — the help arm compares to the shooter (engine + oracle + fixtures).** `Matchup.BlockHelpThreat`
  and `Matchup.BlockHelpShiftVsShooter` are new beside the untouched neutral pair, so the rate/credit wall is
  visible in the function names: the RATE reads the shooter, CREDIT keeps the neutral bar (R2). No nullable
  shooter and no no-shooter overload — either would preserve two rate semantics indefinitely. No new config
  keys; the bar is no longer a number, it is the man shooting. Golden schema splits `rate_vs_shooter` from a
  frozen `credit_neutral` section, two new fixtures keep the credit half a regression test rather than a
  snapshot, and a negative control proves every run that the credit comparison still rejects a mis-wire.
  Three findings recorded and NOT tuned toward: league blocks rise slightly (the opposite of the prompt's
  prediction), rim help rises 7.0% because real rim shooters are tall but poor finishers, and the per-game
  blocks leaderboard is still led by a 6'4" guard on minutes (O-49's artifact, not a block-door defect).

- **S81.2 — what a vertical leap is worth (engine + config).** `Matchup.VerticalShift` (the convex
  defender-relative primitive, weight OUTSIDE `GapFn`) applied at exactly two sites — ordinary Rim at
  `RimVerticalWeight 0.40` and putbacks at `PutbackVerticalWeight 0.80` — deliberately NOT inside
  `EffectiveRating`, which runs for every zone. `Matchup.ReboundVerticalMultiplier` joins the skill
  product in both rebounder pickers (`ReboundVerticalSwing 0.20`, teammate-relative), and a team-vs-team
  gap joins `totalShift` in `OffensiveReboundShare` (`ReboundVerticalTeamWeight 0.05`). Reach weights
  retuned to 0.45 / 0.40 / 0.15 with an ordering guard ADDED on top of the kept sum-to-one guard.
  Null-defender fallbacks preserved byte-identical on both make paths. New Phase 75 (isolation sweep
  first, three neutral points kept separate, strict picker monotonicity with full preconditions, all
  five config guards). **Acceptance test:** the tall long man out-protects the rim over the short
  explosive one, 12.38 vs 7.45, skill/readiness/assignment/shooter held identical. **Within one fixed
  body more hops STILL helps** — 12.16% → 13.97% — the slope halves (4.54 → 1.82 pts), the sign does not
  move. Stage-1 population gate run before any scale was trusted: rim term is a tail effect (6.5% of
  shots move >2 pts), putback broader (23.5%). Rebound gain decomposed on a four-way grid — no
  interaction, essentially all of it the individual layer. **Also closed a hole in Phase 74's fixture
  guard**: the golden declared the three reach weights and never checked them, so a reach change would
  have sailed past it with all 1,210 rows agreeing with a wrong engine.

- **S81 — rim help is gated by assignment (engine).** `Matchup.BlockSpacing` /
  `BlockAssignmentGate` / `BlockEffectiveGate` / `BlockAssignedMan`, consumed in exactly two places
  (`BlockHelpSum` for the rate, `BlockCreditWeights` for the credit) through one named assignment
  lookup, so a coaching layer replaces slot parity without touching the gate formula. Score is a
  logistic on Outside (midpoint 45, scale 14); floor 0.30; influence Rim 1.00 / Short 1.00 / Mid 0.50
  / Long 0.20 / Three 0.00. Transition exempt (`offense = null` on a fast break reproduces the S79 tree
  bit-for-bit). Putbacks untouched and now proven byte-exact. Oracle locked with 18 self-checks before
  any C# was written; golden 1,210 rows / 1,000 gated / worst |Δ| **0.0E+000**. Phase 74's existing
  assertions classified, then re-armed against the gated tree; Phase 36 gained a realistic opponent and
  a new sub-check 11 that fails loudly if the gate is ever inverted.
  **Per situation it is large and exact** (a 6'11" rim protector dragged onto a sniper: 39.4% → 18.9%
  of his team's rim blocks, 8.821 → 3.618 blocks per 100). **League-wide it is small** (helper share
  68.4% → 63.8%; height composition ~2 points). **It is not a block-total correction** — see O-40.

- **S79.3 — the leaderboards became percentages (page-only).** Four exact on-floor counters on
  `SeasonPlayerRecord` (`OffensiveCredits`, `OpponentTwoPaOnFloor`, `SecuredBoardsOnFloor`,
  `OffensiveTeamFgmOnFloor`), staged in the existing `NoteOccupancy` record walk and drained in its
  existing roll-up. Five rate boards — BLK%, REB%, AST%, STL%, PTS/100 — plus five distribution rows on
  the qualified pool and `off poss | def poss` on the minutes board. Phase 73 gained **twelve** gates
  (four league identities, five feasibility bounds, three zero-consistency) and two honest diagnostics.
  Every existing page block byte-identical, proven by line diff against the committed build at the same
  seed. **The defect it fixes:** the per-game block board still reads a 6'4" guard first and its worst
  per-minute blocker ninth; BLK% is ten bigs. Denominators counted, never estimated — a `Credits / 2`
  estimate was measured to reorder the points top ten. No engine, config, generator, fixture or dial.

- **S79.2 — the position census (page-only instrument).** `PrintS80PositionCensus` in
  `Program.Season.Calibration.cs`: rostered players split G/W/B, sixteen skills grouped by which
  ones compete for the same family budget, each with median / p10 / p90 / share <=10 / share <=20.
  Sits beside the S78 body-band census, which is UNCHANGED so S78's recorded numbers stay
  comparable. Validated by reproducing the S79.1 census exactly — G RimProtection 27 / 34.6% <=20,
  G PostMoves 14 / 72.0% <=20. No engine math, no config, no fixture, no assertion.
  This is the readout S80 is ruled and validated on.

- **S79 — the block help arm + contribution credit.** `Matchup.BlockWeightWithHelp` composes the matched
  duel with a zone-weighted help arm in pre-tanh shift space; `BlockCreditWeights` /
  `PutbackBlockCreditWeights` replace `BlockerWeight`. Help depth is **body-only** (height + strength),
  NOT `Postness` — sharing `PostDefense` with the threat term let a better defender lower his own team's
  block rate on 8,237 of 40,000 matchups. New: `tools/block_help_oracle.py`, `tools/block_help_golden.json`
  (210 rows), Phase 74, Phase 36 sub-checks 8–10, one page-only season readout. Closes O-29.

## Red blockers — resolve before major new work

*None open.* **R-1 (the rotation) shipped in S76** — the minutes allocator replaced the fatigue fence and
took top-five share of floor time from 88% to 69.7%; historical detail in journal S76. Calibration may now
be run against a league with a real rotation, though the minute VALUES remain placeholders and the depth
chart is PROVISIONAL pending O-6.

## Open — next-session candidates

- **O-78 — THE PAGE STILL RANKS 347 SCHOOLS IN ONE NATIONAL W-L TABLE, AND THEY NO LONGER PLAY THE SAME
  NUMBER OF GAMES (opened S93).** Jacksonville leads at 17-3 over Stanford at 17-1 because the Atlantic Sun
  plays twenty and the Pacific-10 eighteen; Hampton went 15-1 and sits fourteenth. The prestige proof table
  is compressed for the same reason — an Ivy team cannot average more than seven wins and is pooled with
  leagues that get twenty chances — so it reads as though prestige stopped mattering when it did not. Both
  are artifacts of a page built for a uniform schedule, not findings. **Emmett ruled the fix out of S93 and
  named its real home:** *"there is no such thing as ranking the 360 teams into one big official w-l
  standing... we will work on conference standings, top 25 rankings, etc... down the line."* The deeper
  point survives the ordering question: with no non-conference play, no two leagues share an opponent even
  indirectly, so there is no honest basis for a national ranking this season at all.

- **O-79 — THE SAME PAIRS DOUBLE AND THE SAME PAIRS SKIP EVERY SEASON, FOREVER (opened S93).** The slate
  takes no randomness and there is no memory, so a career's year two would carry a conference schedule
  identical to year one's. Not a defect of this session — it is what a fully-determined slate with no
  history means — but it is a real basketball gap and it must not survive the career layer. **The next scheduler third's
  stored memory of who hosted last time is the natural home** (S94 shipped dates; Emmett reordered), together with the soft objectives
  (`SkipUrgency` and friends) that were held out of S93 precisely because they need that memory to apply.

- **O-80 — `conf.csv`'s `Divisions` COLUMN READS 2 FOR SIX LEAGUES AND NOTHING READS IT (opened S93).**
  R11 says every team in a conference plays the same shape; divisions are a different design with a
  different shape rule and a different tournament. Dead data, deliberately left dead, recorded so the next
  session that opens that file knows the column is not an oversight.

- **O-81 — TOURNAMENT FORMATS ARE ONE AUTHORED NUMBER TODAY AND SHOULD EVENTUALLY BE A DERIVED WALL (opened S94).**
  Emmett: every conference, real or fictional, must be able to hold no tournament, a two-team one, any bracket — *"it
  should be dynamic in how it changes their scheduling"* — ruled SAVED for down the line and out of S94's scope. Today
  the wall is `TourneyOpensDaysBeforeSelectionSunday` (with `none` first-class); the tournament session can replace
  that number with a derivation from an authored format without touching the date layer, which consumes exactly one
  date and never asks what happens after it. `TDay1..5` remain in the csv, unread, in case their spans turn out to
  encode something an origin would unlock.

- **O-77 — `data/teams.csv` MAY HOLD MORE MIS-LOCATED SCHOOLS (opened S92).**
  St. Peter's was listed in New Brunswick NJ on Rutgers' exact coordinates, about 25 miles from where the
  school actually is. It was found **by accident**, while enumerating shared places for the migration table,
  and it had never mattered because nothing read a coordinate. Geography makes every such error visible the
  moment the map prints — a wrongly-placed school reads zero miles from a neighbour it does not share a city
  with. **Nothing has ever audited that column**, and it now feeds travel, and shortly crowd reach. The cheap
  version is a one-pass eyeball of the 36 shared-place groups on the geography page; the thorough version is
  an external cross-check of all 347. Emmett owns the data call either way.

- **O-75 — ★ THE OBSERVATION RUN AND THE STRESS TEST CANNOT FAIL THE SUITE (defect, opened S91).**
  Both are `void`, both print their own PASSED/FAILED line, and both are called **after** `ok` has already
  been computed in `Program.cs`. So the suite can print `STRESS TEST FAILED` and then, eighty lines later,
  `ALL CHECKS PASSED.` and mean it. The observation run also prints its own `ALL CHECKS PASSED` mid-file,
  where it reads like the verdict. This is not a preference — it is a red line that does not mean anything,
  in a project where Emmett cannot audit the C# and is relying on it. Wiring the two verdicts into the real
  one is small; whether either SHOULD be able to fail the suite is the question to rule first.
- **O-76 — THERE IS NO WAY TO RUN THE CHECKS WITHOUT SIMMING 5,000 GAMES (opened S91).**
  Every suite run pays for the 1,000-game observation corpus and the 4,000-game stress test whether or not
  the session touches basketball. Measured at S91: 225s with them, 189s without — 36 seconds, but **770 of
  3,639 output lines**, and the noise is the larger cost when the thing being read is a 53-line phase block.
  A checks-only switch. Belongs with O-75; found because Emmett asked why a calendar test was simming
  4,000 games.

- **O-74 — NOTHING SETS HIERARCHY RANK; EVERY PLAYER IN THE LEAGUE IS A 5 (opened S90).**
  `GenMapToPlayer` leaves it at its default and no other path assigns it, so the offensive pecking order the
  field exists to express is uniform across all 4,511 men. S90 stores it anyway — two bytes, authored surface
  that will become live, and a season written without it could never get it back — but it is currently a
  constant occupying a column. Whoever designs usage hierarchy owns this.

- **O-72 — CLASS YEAR IS COMPUTED AND DISCARDED, AND THE ARCHIVE DOES NOT KEEP IT (opened S90 chores).**
  `Player.PlayerClass` (Fr/So/Jr/Sr) exists and the generator produces it, but `GenMapToPlayer`
  (`Program.Gen.cs:821`) never assigns it, so every season player carries an empty string and S90's
  roster section has nothing to store. Emmett ruled the archive is a historical record and class year
  is plainly historical — but he did not name it, so S90 did NOT fold it and it is opened here rather
  than assumed. One field through one mapping plus one byte on the roster entry; folding it later is a
  roster schema version bump, and seasons written before it will not have it (S90 R2). Note it is a
  PLACEHOLDER label decorating `Arrival` per the S42.1 ruling — the real population-structure question
  is still unowned.
- **O-73 — THE DEVELOPMENT CEILING IS COMPUTED FOR EVERY PLAYER AND THROWN AWAY (opened S90 chores).**
  `PlayerGenPass3.BuildFromDraws` computes `Latent`, `Current`, `Runway` and `Arrival` for all 4,511
  men every time a world is built; `GenMapToPlayer` copies the 33-key current card into the 38 ratings
  and drops the rest on the floor. So the season cannot tell a raw project from a finished senior of
  the same rating. **Emmett ruled the ceiling OUT of the S90 archive** — *"No, 10 years down the line,
  it doesn't matter. It should maintain a historical record"* — which settles retention, not the
  engine gap: a development layer will need this data live, and it is currently discarded one line
  before the season sees it. Two facts worth not re-deriving: `CurrentSkills` is fully redundant with
  the stored 38 (`BuildCard` sources all 23 skill keys from `Current`), and `Runway` is exactly
  `Latent − Current`. The only irreducible value is the latent card.
- **~~O-68~~ — RESOLVED WRONG AND REPLACED (S89.1).** S89 recorded S88 as "a dial set with no code".
  That was false: the code existed as UNTRACKED files, invisible to the GitHub pull Claude greps.
  The dial set is not orphaned, the config was correct, and the whole item rested on an error. See
  O-71 for what actually remains.
- **~~O-69~~ — STRUCK (S89.1). Phase 71 works.** S89 claimed it went green through a twenty-key
  config/code mismatch. It did not — it went red and named every orphan and absent key. The mismatch
  Claude "caught it missing" was one Claude had created by reverting the config, and Claude never
  opened the failing suite to see which phase reported it. Recorded rather than deleted so the
  mistake is findable. `config.json` was carrying a
  **staged, uncommitted** edit that would have ridden into the next commit unannounced: twenty new
  `Transition*` keys — `GotBackLuckFloor`, `LegsSpan`, `DepthSpan`, `EffortSpeedShare`, `PostnessScale`,
  `ArrivalSpan`, `ContestDiscount`, `BaseBreakMake`, `BaseBreakBlock`, `RimProtectionSwing`,
  `TeamPresenceSwing`, `ChaseSwing`, `ChaseLengthWeight`, `ChaseRimProtWeight`, `ChaseSpeedSwing` and five
  `ShooterZone*` multipliers — with **zero code reading any of them**, and four `HustleTransitionDefense*`
  keys deleted whose compiled defaults (`0.043 / 2.0 / 25.0 / 0.05`) are character-for-character the
  deleted values. **Behaviourally inert**, which is why it survived undetected. It is the skeleton of a
  transition-defence design that named its dials and stopped — plausibly C-32's second and third effects.
  Saved to `s88-transition-dials.json` (Emmett's Downloads) and reverted; the dial set is NOT in the repo.
  If this arc resumes, it needs O-48 for the assignment half (see O-60) and its own design conversation
  first. Recorded so the numbers are findable by name rather than by archaeology.
- **★ O-69 — PHASE 71 DOES NOT ACTUALLY CATCH A CONFIG/CODE MISMATCH (found S89, and it is the real find).**
  Phase 71 exists to lock config key names against a registry. O-68's file had **twenty keys the code has
  never heard of present and four keys the code needs absent**, and the suite went green. Whatever it is
  checking, it is not "the file and the code agree about which dials exist." Here that was survivable only
  because the missing keys' compiled defaults happened to equal the removed values — had they differed, a
  real dial would have silently reverted to a default with nothing saying so. Note this does NOT contradict
  C-25 (a missing key is quiet at runtime **by ruling**, and loud at test time via Phase 71) — it says the
  loud half is not working. Small, cheap, and it protects every dial in the project.
- **★ O-70 — RECORDED REFERENCE FIGURES MUST NAME THEIR MACHINE (found S89).** S89 established that the
  board's recorded S87 baseline was taken from a **sandbox** run: Emmett's machine reads PPP 0.9692 against
  the recorded 0.9710, foul-outs 0.844 against 0.840, 737,952 possessions against 738,211. Neither is
  wrong-as-arithmetic — `Math.Pow` is not bit-portable and the difference cascades over 738k possessions —
  but only one is the verification of record. Two consequences. (1) **The baseline block at the top of this
  board is corrected in place** for the figures S89 measured; the older S78/S84 reference numbers are
  unaudited and may carry the same defect. (2) **Standing rule:** a session recording a reference figure
  states which machine produced it, and a session predicting a figure for Emmett's run must not compute it
  in the sandbox and assert it as exact. CONVENTIONS §2 already says this about fixtures; it needs to say it
  about page numbers. **O-32 is the structural fix** and is now worth more than it looked — a page that
  prints its own fingerprint makes a cross-machine mismatch visible in one line instead of by eye.
- **O-64 — ★ FOUL CONCENTRATION: the rate is right, the spread is not (S87 finding).** Foul-outs run
  **0.840 per team-game** against roughly 0.4 in real college basketball, and **8.3% of player-games end
  at five or more** where an even spread of 17.95 fouls over the ten men who play would predict ~3%. The
  foul RATE did not move a hundredth at S87 — what the session revealed is that the committer weighting
  concentrates blame considerably more sharply than chance, on bad-discipline guards and on whichever big
  is guarding the rim. This is a calibration question about the *sharpness* of `FoulCommitter`'s
  weightings (matched share by zone, the interior tilt, the reach-in propensity spread), NOT about the
  team foul rate, which is correct. Also folds in S62's carried-over `InteriorTiltScale = 40.0` debt (the
  Anchor takes ~58% of the rim residual against ~37% estimated at draft time). **The natural S88.**
- **O-65 — The whistle-level substitution door (S87, sized and deferred).** Every foul stops play and a
  disqualified man should leave at that whistle; the engine's only substitution seam is BETWEEN trips, so
  he finishes the trip he fouled out on. Measured cost: **8,507 replacements, 8,507 trips played while
  disqualified — exactly one each.** Two sizes, both real. *Cheap (~one session):* the swap is nearly free
  because generators read the current seat occupant, but the resolver has no idea a coach exists and is
  built at 43 sites; it would leave that one trip's box score on the man who left. *Honest
  (multi-session, foundational):* splitting a trip changes the unit every board is counted in —
  `Roster.PlayerAt(slot, possessionNumber)` is the spine of the per-player layer. Do not attempt the
  honest version as a corner of another session.
- **O-66 — The per-game seed map wants a real derivation (S87).** Small offsets off `resolverSeed` have
  reached +5. A grep proves each literal offset is unused; the **arithmetic does not** — season games are
  seeded two apart, so game *g*'s +5 stream starts on the same number as game *g+2*'s governor stream, and
  the same overlap already exists for the +3/+4 attribution streams. No new bug class and nothing is
  mis-conserved, but a hash-based derivation should land before the map grows again.
- **O-67 — The offensive-foul column is populated but printed in only one place (S87).**
  `PlayerBoxTotals.OffFoul` is accumulated, reproducibility-checked, and reported on the season page. The
  **observation and stress per-player box scores still print SFL/NSF only**, so a charge is invisible in
  the two printers most used for eyeballing a player. Small; belongs with any foul work.
- **O-1 — Intent-vs-touches ruling (S60).** The usage curve pays on the offense's intended
  share, pre-tilt/pre-denial; the denied big earns nothing. Rule this before touching the
  relief scale, or the scale absorbs the error.
- **O-2 — Relief-scale tuning (S60, quantified S60.2 as ~10× too quiet).** Behind O-1 and R-1.
- **O-3 — Usage architecture: the rail authors every star, HierarchyRank is mostly dead,
  and nothing takes the ball from a cold shooter (S59.2 + S60.2).** Both channels inside
  8–40% by design; the rail back to an emergency brake; volume pricing is the tax's job.
  Live evidence: usage max 46.0% on the real world.
- **O-4 — ★ The tax↔defensive-settings coupled pair (S72 ruling, MANDATORY revisit).**
  Attention multiplies the tax; when the defensive-settings layer lands, 0.30 gets re-walked.
- **O-5 — Residual channel is dead on the real population (S72 observation).** Mean ~0.0002;
  understand-or-retire, not a dial session.
- **O-6 — ★ RAISED PRIORITY (S76) — Scout-rank modernization (S63, re-based S70).** Three old-pool
  assumptions on the record: big-ATH +8 add-back, position-relative SIZE tails / guard-leaning board,
  stretch-big Outside invisible to SKILL. One ruled session. **S76 promoted this from cosmetic to
  consequential:** until now these shaped who got RECRUITED; from S76 they decide WHO PLAYS, because the
  depth chart orders the minute targets. Targets fall sharply within a group (32→8 for guards, 26→4 for
  bigs), so a single rank inversion is a large minute swing. The chart is labelled **provisional** in
  design.md until this lands. **S78 relocated the risk precisely:** the accepted pool is drafted
  ONE-FOR-ONE (4,511 → 4,511), so draft-level masking is structurally impossible — the rank cannot stop a
  player making a roster. What it still decides is which school and which depth-chart slot, i.e. **minutes**
  (O-33). Any future "did the scout rank hide my players?" question is a stage-3 question, not stage-2.
- **O-7 — Drive gate Pass B: post-feed + usage diffusion (S59).** The "he gave it up" outcome —
  a denied drive currently always becomes his own contested jumper, never someone else's shot.
- **O-8 — Drive gate Pass C: the off-ball open-three lever (S59).**
- **O-9 — Pass A page-tuning, first item the level-neutrality finding (gate not neutral below
  rating ~48, a property of the locked spec) (S59).** Calibration — behind R-1.
- **O-10 — Re-measure the eight attribute families vs DIALED opponents / point something
  through `opponentDials` (S59.2 + S60.2).** Every S45–S55.1 finding carries the flat-50
  caveat; the instrument is built and still unused by any committed sweep.
- **O-11 — The random-vs-elite scoring spread is unexplained (S60.2).** 12-point PPG spread
  vs a nearly flat divisional ladder; do not assume benign.
- **O-12 — HelpDefense overhelp / rotation cost (recovered S60).** Feeds the
  rotation/defensive-settings design work.
- **O-13 — BasketballIQ's dead lower half — should low IQ actively hurt? (recovered S60).**
- **O-14 — DefenderPicker promotion / on-ball mismatch hunting (recovered S60).** Becomes
  real when the pick turns mismatch-hunting; this is the second door's second door.
- **O-15 — Remaining calibration queue (behind R-1): TO% 21.3 vs ~18.5 target; turnover-band
  placeholders (S37); the assist lever (10.1 vs ~13.5, S49); curve-steepness conversation;
  displacement magnitude (oracle-first only); the S48 FT-90 micro-flag.**
- **O-16 — The light block channel (S50 design question 2).** Magnitude call on a real
  population.
- **O-17 — Weight: feed Strength-adjacent channels or wait for a body-contact layer?
  (S50 design question 4; proven cosmetic today).**
- **O-18 — SelfCreation perimeter assist discount (S57 residue).** S57 did interior only.
- **O-19 — Refresh `docs/attribute-wiring-synthesis.md` when the attribute map moves (S60).**
- **O-20 — `game` demo: upgrade to real generators or retire (labeled as stub-driven on its
  banner at S73).** Micro-session, or rides any session touching Program.Game.cs.
- **O-22 — Opening-five selection is rank-blind (S75 measurement).** 93% of the league starts three
  guards (324/347 at 5/4/4) because `BuildOpeningFive` walks acquisition order under a quota floor,
  never rank or ratings. Evidence, not failure — but it belongs with coach-driven roster construction.
- **O-23 — Mismatch-hunting (S75 deferral, Emmett: "we can delay that for later").**
  `DefenderPicker` is slot-guards-slot and its own docs call it *"v1 logic"* with *"the eventual
  mismatch-hunting picker drops in here."* Consequence: a tall wing faces the opposing wing every
  possession and, in 93% of games, never sees a guard — so a big wing cannot exploit a three-guard
  lineup. The S75 ladder created the engine's FIRST cross-position matchups (7.57% of floor time,
  W→G at a +9.7 height gap); whether they are correctly priced is A11's open question.
- **O-24 — Role cost for out-of-position play (S75, measure before building).** The engine has no
  role layer: screening, off-ball movement, spacing, rotation duty and out-of-position ballhandling
  are absent rather than mispriced. S75 added no modifier by ruling. **Any future model must be
  gap-shaped** — see C-26.
- **O-26 — ★ Cross-position occupancy is 24.49% (S76 measurement).** Against S75's 7.57% and arithmetic
  floors of 5.0/5.0/14.0% by shape, the allocator reaches across position roughly twice as often as
  feasibility requires. Out-of-position play is currently free (O-24), so this is unpriced volume, not
  necessarily wrong volume. Page-only, never asserted. Understand before tuning.
- **O-27 — Substitutions run 34–39 per team-game (S76 measurement)** against a real-basketball 20–25.
  Structural cause: there is no timeout model, so substitutions cannot clump at media breaks and spread
  evenly instead. Belongs with the coaching layer, not the allocator.
- **O-28 — The three zero-target men are inert, and the session that was meant to change that never came
  (S76 ruling; re-dated S77).** Emmett's ten-man ruling gives the bottom guard, wing and big a target of
  zero, so their residual can never reach a positive enter threshold and they cannot check in. Ruled
  knowingly. **The "until S77" in the original wording is void** — see O-30. Now VISIBLE rather than
  inferred: the S77 page shows 1,018 of 4,511 player-seasons with zero games played.

- **~~O-29~~ — BLOCK CREDIT IS FLAT PER MINUTE. ★ SHIPPED S79** (moved to Built). `BlockerWeight` retired
  and deleted with its 30 config keys; credit is now the defender's positive blocking contribution over a
  luck floor. S79 also found the half of the defect S78's diagnosis missed: the block RATE consulted one
  defender, so an unmatched rim protector moved the team block rate by zero. Both fixed. The board went
  from a top-ten spanning 1.2–1.0 (nine guards) to bigs at 2.9/3.1 against guards' 0.3–0.5.

- **★ O-33 — MINUTES ARE SKEWED AGAINST BIGS (S78 ladder finding; split out of O-29).** Bigs average ~24
  mpg against guards' ~32, and every per-game leaderboard is minutes-weighted, so the better man loses the
  board on playing time. Measured: the big at rebound rank 2 takes **0.44 boards/min** against the
  leader's **0.34** and still finishes second. Unlike O-29 this is NOT a credit problem — the rebounding
  credit tracks the right men — it is who the depth chart puts on the floor. **★ CAUSE FOUND (post-S79):
  it is NOT O-6's territory either.** 321 of 347 schools open 3G/1W/1B, so four bigs share one seat's 40
  minutes while five guards share three seats' 120. The board only orders men within a group that was
  already capped by the shape. **This is O-42**, and O-33 closes when O-42 ships.

- **O-34 — ★ THE ANTI-TARGET GATE IS PAGE-ONLY, ON LOAN (S78 ruling, named end date).** The "no elite
  recruit is flat" rule was an EXACT gate that threw; S78 demoted it to a printed number because Glue was
  silently doing the work of the guaranteed hole (96 of the top 347 cards had a Glue skill as their card
  minimum) and the rule was written when the intangibles were dead. It fires at 1–3 now. **This is a
  recorded LOOSENING, proposed by Claude and accepted:** re-rule it once the season page shows what a flat
  card actually plays like. It does not leave Open by fading.

- **O-35 — The intangibles CENTRE is a placeholder (S78, explicitly UN-RULED).** `INT_A`/`INT_B` put
  BasketballIQ / Discipline / HelpDefense at mean ~53.5. Emmett's position: it cannot be judged before
  there are stats. The SHAPE is locked (shared component + idiosyncratic, [8,99], current == latent,
  runway zero); only the centre is open. Revisit after the first season's numbers.

- **O-36 — The intangibles have no DEVELOPMENT (S78, provisional by design).** All three are written
  identically to latent and current, so runway is exactly zero and they cannot grow. A named comment marks
  it. Belongs to an intangible-development session, not to a generator session.

- **O-37 — The fixture declares a tolerance the C# never reads (S78, small).** The Pass-3 fixture header
  carries `float_tolerance: 1e-9` and `Program.Checks.GenPass3.cs` uses its own `GenPass3Tol` const. They
  agree today; nothing asserts they must. One line to close, worth closing because S78 just exercised the
  fixture contract.

- **O-38 — "Elite rim protector, 5'9"" reads as a bug on a card (S78, presentation).** A consequence of
  the S78 ruling that the generator states capability and the engine prices expression. The honest fix is
  presentation — show expressed production beside the rating, or scout language that accounts for body —
  and there is no presentation layer yet, so nothing breaks by waiting. Recorded so it is not
  re-discovered as a generator bug.

- **O-30 — ★ FOUL-OUTS HAVE NO SCHEDULED HOME (orphaned, found S77).** S75 deferred foul-outs to "S77" and
  S77 became the stat page, so nothing is scheduled to build them. The deferral itself is still correct —
  committer selection is post-hoc in the harness, so disqualification needs an RNG restructuring that must
  not share a diff with a roster change. It needs a real session number. Blocks O-28.

- **O-32 — The season page should print its OWN fingerprint (proposed S77, not ruled).** The recorded
  season SHA-256 comes from a recipe that exists nowhere in the repo; the sandbox cannot reproduce it under
  any line-ending convention, so the reference can only come from Emmett's machine via a hand-run shell
  command. The page already prints `Schedule fingerprint:` — a self-computed page fingerprint would be
  reproducible anywhere, immune to line endings and console encoding, and would retire the copy-paste step.
- **O-21 — Normalize the three config loader shapes (S74 deferral).** Eighteen sections are sectioned
  `Deserialize`; `RollAConfig` is root-flat; `RollEConfig` is nineteen hand-written `GetProperty`
  assignments. The divergence is declared and asserted by Phase 71's registry, not hidden — but folding
  RollA/RollE into the common shape is its own session with its own drift audit. Not urgent: RollE's
  binding is now behaviourally proven.

- **★ O-39 — THE BLOCK CONTEST UNDER-WEIGHTS RIM-PROTECTION SKILL AT THE RIM (S79 finding; RE-SCOPED
  AND RE-SEQUENCED at S79.2).** `BlockContestWeights` prices skill at Rim **0.40** — identical to Three —
  against a schedule of Rim 0.40 / Short 0.45 / Mid 0.50 / Long 0.42 / Three 0.40, an inverted U peaking at
  Mid. The source comment beside those weights says skill should count for MORE near the rim; the numbers
  do not.
  **S79.2 measured the lever and it works:** on real generated players, an elite rim protector's share of
  his lineup's rim-block credit against a chase-down wing's runs 43.2% vs 33.4% at the current 0.40, 46.5%
  vs 25.9% at 0.55, and 49.3% vs 16.7% at 0.70.
  **But the weight is not why length wins, so this item is now the SECOND half of a two-part fix.** See
  O-44: the two arms are measured against the same neutral point of 50 while sitting on entirely different
  distributions (length median 59.7, rim-defence median 33.2), so the length arm pays out to 86.5% of the
  league and the skill arm to 17.1%. Raising the skill weight works by making more of the league a
  non-threat (positive-threat share falls 53.8% → 37.1%) rather than by making rim protection matter.
  **Sequencing ruled at S79.2: this waits for S80.** Rim-defence median moves 33.2 → 25.9 (linear) or 17.6
  (convex) under S80's candidates, so any neutral point set today is wrong the moment S80 lands. The length
  half (O-44) does NOT move under S80 and can go first. Emmett's Phase 7 anchor; still his call.

- **O-40 — BLOCK RATE CALIBRATION (S79, expected).** Blocks read 4.2 against the 3.5 target, up from S78's
  4.1. The help arm only ever adds, so this was predicted in the prompt and recorded rather than chased.
  Dials: `BlockHelpShare{Zone}` and `BlockHelpPositionalSwing`. Belongs to a calibration session against a
  real generated population, not to the session that shipped the mechanism.

- **O-41 — THE C6 MAKE-DOOR HELP AGGREGATE IS UNWEIGHTED (S79 audit finding, RULED OUT of S79).** Roll H's
  C6 aggregates the four off-ball defenders' HelpDefense on a fixed denominator of 4.0 with **no positional
  weighting** — a point guard's help suppresses the make rate as much as a centre's — and it never reads
  `RimProtection`. (Roll E carries a parallel all-five/5.0 aggregate into selection.) Ruled out of S79
  because moving make% through a second door in the same session makes the season page unattributable.
  Own session. Measured size for scale: swapping an ordinary lineup for a menace lineup moves C6's
  make-shave by 0.12 percentage points, against the S79 block door's 3.6.

- **★ O-42 — LINEUP SHAPE NEVER VARIES, AND IT IS THE REAL CAUSE OF O-33 (design conversation after S79).**
  Every roster is exactly 5G/4W/4B, the seating floor is 2G/1W/1B, and the fifth seat goes to best
  available — so **321 of 347 schools open 3G/1W/1B**. Four bigs and four wings each compete for ONE seat
  (40 minutes); five guards share three seats (120). That is the whole 24-vs-32 minute gap, and it is NOT
  the scout rank: the board only decides *which* big gets the 40, the shape already decided there are only
  40. Compounding it, defenders are assigned by **slot parity**, so identical shapes league-wide mean a
  wing is always guarded by a wing — no size mismatch can ever occur, nothing punishes three guards, and
  the loop is self-sealing.
  **★ Emmett's ruling (2026-07-27):** a good wing with size should have a field day against three shorter
  guards, and that should force the other team to answer with its own wing — "a great wing is too big for a
  guard and too quick for a post." **The engine already prices both halves** (the S55 height-over-defender
  make bonus and the athleticism gap in `EffectiveRating`); they never fire because the matchup never
  happens. **Shape variety alone lights the fuse under the existing man-to-man wiring** — two teams in
  different shapes produce a wing-on-guard through slot parity, with no matchup-assignment layer needed.
  **★ Build-shape ruling: a team must OWN A SET of deployable lineups and initially deploy one**, not merely
  get a smarter opening five. Same visible result; the difference is that in-game looks and matchup counters
  later become *choosing a different member of a set that already exists* rather than a rewrite.
  **★ THE STANDING ACCEPTANCE TEST for this whole arc, in Emmett's words:** *a 6'10" plodding big who
  averages 4 minutes plays 14 against the one team in the conference with a 6'11" center.* Nothing short of
  that counts. Four things stand between here and it: (1) shape is not a concept anywhere in the code;
  (2) minute targets are per-season, identical every game, and blind to the opponent; (3) slot parity cannot
  express "I brought him in to guard their guy"; (4) nothing observes the game in progress, so "if I don't
  feel like my lineup can function" has no input.

- **★ O-43 — THE ON-BALL CONTEST SHOULD BLEND THE OTHER FOUR DEFENDERS (Emmett's design, 2026-07-27).**
  Today the make contest reads `DefenseRating(matched defender)` alone. Ruled shape: read roughly
  **80% matched man + 20% the aggregate of the other four's ON-BALL defense** — and down the line make that
  blend a **coaching switch setting** (a switch-everything coach moves toward 50/50, and a switching team
  with poor defenders gets burned for it).
  Why this shape and not the alternative: Emmett first proposed a positional **bleed** (~10% toward each
  neighbouring slot) and then **rejected it himself** in favour of this. Correctly — bleed would make slot
  order *spatial*, and the source is explicit that slot 1–5 is a list index with no floor meaning, so
  adjacency would be a load-bearing architectural commitment everywhere at once (and ~24% of floor time is
  already someone playing out of position, who would inherit a seat's neighbours along with the seat).
  The blend needs none of that.
  Three properties that make it cheap: it is **not a new effect** — same single wiring site, different
  input, so nothing double-counts; the other four contribute their **on-ball** ratings, NOT HelpDefense,
  which keeps it from colliding with the help door (O-41); and because the contest already weights perimeter
  vs post defense **by zone**, a switch-everything team whose bigs cannot guard the perimeter should get
  burned *specifically on threes* with nothing extra wired. **Verify that last one before relying on it.**
  It also delivers what the possession-interior idea was reaching for — an elite perimeter defender is no
  longer glued to one man — **without** modelling time inside a possession, which was by far the largest
  build discussed.

- **O-44 — CLOSED, SHIPPED IN S81.3 (2026-07-29).** The fixed yardstick is gone: help is measured against
  the man shooting, so the bar now moves with the matchup and the question this item asked stops existing.
  Closure was made conditional on the realized post-build report, and that condition was met on Emmett's
  machine — the concentration collapse this item warned about does **not** occur (best defender's
  probability-weighted conditional credit share 49.3% -> 49.0%; matched/helper route split 46.0/54.0 ->
  45.8/54.2). **Its stale "blast radius is three call sites" line was wrong and is corrected here:**
  `PutbackDefenderShift` is a separate function, not a caller of `BlockDefenderThreat`, and the putback door
  was already approximately shooter-relative by another route — see O-53. Two measurements from this item
  survive it and are carried forward: the length arm paid out to nearly everyone while skill paid one man in
  six, and **the obvious fix alone would have made it worse** (raising the bar flattens the board onto the
  luck floor). Both are why the fix had to move the bar and re-price what sits above it together, which
  comparing to the shooter does automatically.

- **★ O-45 — THE MAKE DOOR HAS NO BODY GATE ON INTERIOR-DEFENCE SKILL (S79.2, Emmett asked for this
  item).** Blocking is the smaller half of what `RimProtection` does. It is also 65% of
  `DefenseRating(Rim)` (`PostDefense` the other 35%; `PerimeterDefense` contributes **zero** at the rim),
  which feeds `EffectiveRating`'s skill shift and therefore rim make percentage directly — plus
  `DefensiveResistance`'s top-3 blend, which moves shots away from the rim before anyone shoots.
  **The defect, worked example:** Pool_85 (Robert Morris, seed 20260720) is **5'8" with a 43 wingspan and
  RimProtection 96 / PostDefense 88**. His rim defensive rating is **93.2** against **37.0** for the 6'9"
  big beside him, and he takes **35.5%** of his team's rim-block credit to the big's 32.6%.
  *(S79.3 note: he led the nation's ninth-most blocks PER GAME on the old board; on the S79.3 BLK% board
  he is nowhere near the top ten. The rate boards fixed the presentation half. This item is the OTHER
  half and is untouched by that — his rating still suppresses rim FG% through the make door.)* **He is not rare** — of the 49 rostered men with RimProtection >= 80,
  **12 are under 6'0" and only 7 are 6'6"+**.
  **★ Why the engine does not disbelieve him, precisely:** the two doors read his body differently and the
  make door reads it CORRECTLY. `Reach = (Height + Wingspan)/2` = **41.5** for him and deliberately excludes
  Vertical, so a tall shooter collects nearly the full `HeightOverDefenderShift` bonus. `LengthRating =
  (Height + Wingspan + Vertical)/3` = **52.0**, above the 50 bar — his 73 vertical launders his arms. The
  exclusion is deliberate per the `Reach` source comment, so whether shot-blocking should credit vertical at
  one-third weight is a live basketball question, not an oversight.
  **The open question, Emmett's to rule:** the make door damps him through the shooter's height bonus but
  still consumes his 93.2 in the skill channel with no body gate at all. Should a small man's elite interior
  rating stop suppressing rim FG%, and if so by what mechanism? **Neither S80 nor O-44 covers this** — S80
  makes him rarer, O-44 stops the block board believing him, and this is the third piece.
  Context that matters: S78 removed the old hard height cap (`34 + 65·clamp((h−46)/28)`, which pinned a
  5'8" man at 34) **deliberately**, on the recorded ruling *the generator says what a man can do; the engine
  says whether it is felt*, and named "elite rim protector, 5'9"" as the accepted counterweight. The
  generator half shipped; the engine half never did. **This item is that missing half — it is not a
  regression and the answer is not to restore the cap.**

- **★ O-46 — NO PLAYER IN THE ENGINE RUNS AN OFFENCE (S79.3 finding; ★ S84 ANSWERED ITS OPEN QUESTION
  AND SPLIT IT IN TWO).** The AST% board S79.3 shipped made it readable: the middle looked ordinary and the
  elite tail was simply absent — no player who is the reason his team scores. O-46 asked whether that was a
  generation question or a Roll C question. **S84 measured it: it is both, and a third thing that is neither.**

  The level half of the complaint is now GONE. S84's midpoint recentre took league assists 9.9 -> 13.1
  (target 13.5, OK) and the best passer's AST% 26.3% -> **36.3%**, which is inside the 35-40% band this item
  originally cited as the elite real-world mark. What survives is the SHAPE, and against the real 2024-25 D1
  leaderboard it is worse than AST% suggested: engine rk1 **4.8 apg against 9.4** (51% of life), rk10 4.1 vs
  6.7, rk50 3.4 vs 5.2, and the real board is 1.81x from first to fiftieth where the engine is 1.40x.

  **Strand 1 — the Roll C half, MEASURED and RULED CLOSED at C-31.** `AssistPicker.Pick` weights the four
  eligible teammates LINEARLY on `max(1, AssistWeight)`. A team's best passer carries 47.7 against playing
  teammates at 27.6 (1.72x); three teammates at 27.6 sum to 82.8 against his 47.7, so even never shooting and
  never sitting he wins at most **36.5%** of the four-man draws — net of shooter-exclusion and bench time,
  **21.4%** of his team's assists (median 20.9%, league-best 36.1%). Real lead guards take near half. The door
  CAN'T concentrate, exactly as this item suspected — **but it must not be made to** (C-31): concentration is
  a coaching output, so the fix is O-57's dial, not a picker exponent. Blocked on O-57; until it exists the
  top of this board is **not a calibration target**.

  **Strand 2 — the generation half, still open and independent.** A 1.72x edge over teammates may simply not
  be an outlier passer. Even a heavy iso dial needs someone worth funnelling to, so if the generator does not
  produce true creators, no coaching change fixes the top of the board. Sits with the parked Pass 2 notes and
  the divisional-sorting ruling; unmeasured, named.

- **★ O-47 — THE DRIVE GATE IS ANCHORED IN ABSOLUTE RATING POINTS, AND THE POPULATION SITS UNDER IT
  (S79.3 conversation; Emmett's read that the league is undertalented, given a mechanism).**
  `Matchup.DriveTools` (`Matchup.cs:1427-1436`) is `(FirstStep, Quickness composite) × unlock`, where
  `unlock = clamp((BallHandling − 28) / (48 − 28), 0, 1)`. **Below BallHandling 28 the unlock is exactly
  zero, so drive tools are zero however fast the player is**, and `ApplyDriveGate` compares
  `gap = DriveTools − matched.PerimeterDefense` with only the wall side firing — so that player eats
  full rim suppression. **Measured on the S79.3 census: median BallHandling is G 25 / W 20 / B 16.**
  More than half the guards in the league — the players whose job is beating a man off the dribble —
  generate nothing at this gate. Bigs are largely shielded by the orientation term (a post-up is not a
  drive), so it bites guards and wings hardest, which is backwards.
  **Where it lands:** rim FG% **51.8 against a 61.0 target**, the largest single miss on the calibration
  page. **Not new, and that is the point:** design.md records this as a known SPEC property from S59
  (*"the gate is NOT level-neutral below rating ~48"*), ruled **ship-as-is** at the time because no real
  population existed to measure against, and names it **"the first item for the Pass A tuning pass."**
  There is now a real population and it sits below the anchor.
  **★ The distinction that decides which session fixes it:** whether the RATINGS are too low is a
  generation question; whether the ENGINE'S ANCHORS are calibrated for the ratings it actually receives
  is a tuning question. This item is the second kind. Related evidence pointing the same way, none of it
  yet connected: guard Outside median 42 against the oracle's own recorded **arc target ~50**; AST% has
  no elite tail (O-46); rim-defence median 33.2 with 83% of the league below the block bar (O-44).
  **Sequencing:** S80 moves this the RIGHT way on its own — freed interior budget flows to perimeter
  skills, BallHandling among them, pushing more guards over 28 — so take this AFTER S80, measured
  against the moved population, not before.

- **O-61 — FATIGUE REACHES LEGS ONLY, AND EMMETT ASKED WHETHER IT SHOULD REACH EVERYTHING (S86).**
  His words at the S86 ruling: *"fatigue should cause virtually everything to drop tiny bit by tiny bit, right?"*
  The honest answer given: yes for the physical side, and it is already tiny-by-tiny because the meter is a
  trickle that steepens into a cliff — **but it is not everything.** `EffectiveAthleticism` / `EffectiveSpeed` are
  the only fatigue-discounted reads in the engine. Passing, handle and shooting are untouched by tiredness
  everywhere. That matches S86's own ruling ("passing is not legs"), so it is not a defect — but the broader
  question is real and was NOT smuggled into S86. Opens as a design conversation: which attributes should sag, by
  how much, and does a tired shooter's jumper fall or only his legs under it? Note the interaction with O-57:
  fatigue is currently the only thing that makes a late-game possession differ from an early one.

- **O-62 — THE FREE-THROW BOARD IS EXEMPT FROM THE OPPORTUNITY WIRE AND NEEDS ITS OWN ARCHETYPE TABLE (S86 ruling).**
  Not a defect and not sediment — a deliberate scope wall with the measurement attached. Base Push 0.08 against a
  swing of 0.22 means the score can subtract more than the whole pie: a plodding rebounder reads **exactly 0.0%**
  at grind and neutral pace, an *average* rebounder against a fast defense also reads 0.0%, and everyone with legs
  jumps to ~28%. Bimodal, which is the S81 lesson in a new place. The locked oracle never modelled this source
  (three base-weight pairs in its golden fixture, no 0.08/0.82 row), so exempting it matched the signed artifact.
  **The plumbing is already done:** Roll M's arm shares the `DefensiveRebound` reason, so the ticket already
  carries a `BallHandlerSlot`. What it needs is Emmett's table for a source whose base is small — most likely a
  per-source swing, which is a new dial and therefore a new ruling. Phase 77's B8 will go red the moment the
  exemption lapses.

- **O-63 — THREE SMALL S86 CARRY-FORWARDS, GROUPED BECAUSE NONE IS WORTH ITS OWN SESSION.**
  (a) **`TeamPaceBias` is dead.** It was the signed fallback for a null `OffenseSide`; the neutral rule now
  short-circuits before any bar is computed, so nothing reads it. Kept at S86's scope wall, and Phase 71
  name-parity faithfully keeps a dead key aligned in `config.json` — exactly the sediment CONVENTIONS §6c warns
  about. Either give it a job or remove it from both sides.
  (b) **Roll K's `LiveBallTurnover` arm never gets a ball-handler.** Its reason misses the stealer-pick gate and
  the engine picks a stealer in exactly one place, so that ticket rides null and takes the neutral rule: a live-ball
  turnover off an offensive rebound runs at its flat 35% regardless of who scooped it. Adding a pick there is a
  behaviour change, hence deferred.
  (c) **The halftime seat exception.** A half that ENDS on a defensive rebound spawns its transition possession
  across the halftime break, where substitutions do run — so that break reads off whoever now holds the seat.
  ~1,000 entries a season out of ~307,000; nothing mis-conserves, the pie reads a different real player's legs.
  Named in the code comment. Only worth revisiting if a live-ball substitution feature ever lands.

- **★ O-48 — TRANSITION HAS NO ASSIGNMENT MODEL (S81 ruling; Emmett: its own session).**
  On a fast break nobody is matched up — defenders sprint back and pick up whoever is closest — but slot
  parity is the engine's only assignment model. S81 therefore **exempts** transition entirely
  (`BlockerPicker.ResolveOffensiveLineup` returns null when `state.FastBreak`), which makes every gate
  1.0 and preserves Emmett's ruling that transition is one of the two ways guards legitimately get
  blocks. That is the correct conservative call and it is not the answer: a break has *some* structure
  (who is ahead of the ball, who is trailing), and nothing in the engine models it.
  **Scope note:** this is not block-specific. Any future rule that reads "who is this defender
  guarding" inherits the same hole.

- **O-59 — THE OBSERVATION RUN'S BREAK SENTINEL HAS BEEN WAITING FOR EXACTLY THESE COUNTERS (S85 finding).**
  *(S86 note: still open and still untouched — S86 changed the season page, not the observation run. It is now
  the smallest transition item on the board and belongs with the next transition session.)*
  `ObservationRunV1` still prints `DEFERRED SENTINELS (counter-plumbing needed — future session): Press frequency
  / break rate at game level`. S85 built the per-possession counters that sentinel was waiting on, but the
  observation run is a DIFFERENT surface from the season page and was deliberately left untouched (scope wall).
  Two questions, and the second is the real one: is the sentinel now satisfied by wiring the same counters
  through, or does a GAME-level view want something the possession record still cannot see? Recorded now rather
  than left to rot another fifty sessions — this is the standing stale-plan hazard, and the sentinel predates
  the counters that answer it.

- **★ O-60 — BREAK-BLOCK CREDIT IS NEARLY FLAT, AND THAT IS O-48 MEASURED (S85 measurement; Emmett's ruled
  effect depends on it).** Top defender's share of his team's fast-break blocks: min 13.8% / p10 19.0% /
  median 25.0% / p90 35.7% / max 57.1%, across all 347 teams. Five interchangeable defenders would produce
  20%, so the median team is barely above chance. Cause is known and deliberate: on a break the engine assigns
  nobody (`BlockerPicker` exempts transition entirely, every gate 1.0), which was the correct conservative call
  at S81 and is not the answer. **Emmett's S84 ruling that a fast lineup should widen this spread therefore
  cannot be built at the block door — it needs O-48 first.** Blocked on O-48. The page line now exists, so the
  effect of any future assignment model on this distribution is visible rather than argued.
  *(S86 re-measured it after the push rewire: median held at **25.0%** (min 11.5% / p90 36.4% / max 48.0%).
  Confirms the diagnosis — moving WHO pushes does not move who gets the block, because on a break the engine
  still assigns nobody. Blocked on O-48, unchanged.)*

- **★ O-49 — THE RARE-EVENT LEADERBOARDS USE A MINUTES FLOOR WHERE THEY NEED AN OPPORTUNITY FLOOR
  (S81 measurement; belongs with the S79.3 percentage-board work).**
  At the 100-minute floor the BLK% board's top four read **10 of 91**, **10 of 92**, **12 of 117**,
  **13 of 129**, all at ~4 mpg. At a true 4% rate over 91 attempts, posting 10% is ordinary luck. The
  actual best shot blocker in the league (Pool_4503, **59 of 642**, 24 mpg) sat **eighth**. At the
  500-minute floor he is **first** and every man on the board carries 596–823 opportunities.
  **REB% at the same floor is fine** — its leaders carry ~1,100 opportunities — because rebounds are
  common and blocks are not. So the defect is not the floor's value, it is the floor's *unit*: BLK% and
  STL% want a denominator threshold (opportunities faced), not a minutes threshold.
  **Not S81's defect and not S81's fix.** Purely a reporting question; the simulation is byte-identical
  across both floors (blocks 4.3, location 89.7%/10.3%, 44,821 credited blocks), which confirms S79.3's
  reporting-only design held.

- **★ O-50 — MORE THAN HALF OF EVERY SHOT IN THE ENGINE IS AT THE RIM (S81 measurement; documentation
  only, NOT acted on).**
  League mean shot diet, measured on the generated population through the locked tendency derivation:
  **Rim 52.7 / Short 5.9 / Mid 7.4 / Long 4.0 / Three 30.1.** Real college basketball is roughly a third
  at the rim. The engine's own credited-block location agrees the shots are there (89.7% of blocks at
  Rim+Short).
  **Why it matters here:** blocks read HIGH (4.3 against 3.5) and the rim is where blocks happen, so a
  rim-heavy diet may be a substantial part of that miss — which would make O-40 partly a *diet* problem
  rather than a *block-door* problem. Do not calibrate the block door against a diet this skewed.
  **Second consequence, and the reason the S81 score is what it is:** the same derivation is bimodal per
  player — arc share reads p25 6.5 / p75 70.5 with nothing between, ~67% of the league pinned at one end
  or the other. That is why S81's spacing score is a logistic on Outside rather than the more direct
  shot-diet read, which classified better (AUC 0.970 vs 0.944) but has no middle to grade.
  **When this settles, revisit S81's spacing score** — the arc-share read becomes correct and drops in
  behind the same lookup with no call-shape change.

- **O-51 — CLOSED, SHIPPED IN S81.2 (2026-07-29).** Both questions ruled and built. Wingspan now
  outweighs height (0.45 / 0.40) and the leap keeps 0.15 of reach — a small strictly positive share, so
  within any fixed body more hops still helps a defender block; what falls is the slope. The leap's real
  value moved to its own terms: the rim (0.40), the second jump (0.80), and the glass in two layers.
  Sequenced BEFORE the shooter-relative change rather than inside it, reversing S81.1's plan line, so
  the help arm is measured against corrected reach.

- **★ O-52 — THE GENERATOR MAKES ALMOST NO LONG FLAT-FOOTED BIGS (S81.2 measurement; documentation
  only, NOT acted on).** Scanning the 4,511-man drafted population for the two archetypes S81.2's change
  most affects found **61 springy small men** (hops 85+, small frame) and **7 long flat-footed bigs**
  (hops ≤35, big frame). `PlayerGenPass3.SIZE_COEF["Vertical"] = -0.02` — hops are essentially
  height-independent in generation, so a 7'0" with a 20-inch vertical is nearly as rare as one with a
  40-inch vertical. **The archetype S81.2 most rewards barely exists yet**, which caps how much of the
  change the league can express. Belongs with the generation arc, not the block door. Emmett's call.


- **★ O-53 — THE TWO BLOCK DOORS REACH SHOOTER-RELATIVITY BY DIFFERENT ROUTES (S81.3 finding; Emmett's call).**
  The located-shot door now compares the helper to the man shooting, on both arms, at every zone. The PUTBACK
  door does something adjacent but not the same: `PutbackBlockRate` subtracts a finisher-resistance term
  measured against the midpoint, so it is *approximately* shooter-relative by a different shape, and
  `PutbackDefenderShift` still reads the neutral bar. Whether the two should be unified is a real basketball
  question and not tidiness: a go-back-up is contested by the whole interior in a scramble, which may
  legitimately justify a different shape from a located jumper contested by one man with help. Not scoped, not
  urgent, and NOT a bug — recorded so the divergence is a decision rather than an accident.

- **O-54 — MID FG% MOVED THE WRONG WAY AT S83, AND IT IS UNEXPLAINED.** The signed term docks
  undersized shooters at Mid, so mid FG% was expected to fall about a tenth. It ROSE 40.0 -> 40.2
  while mid attempts rose 54,125 -> 54,420 and rim attempts fell. Mid is the thinnest slice of the
  term (4.5 rating points at full saturation, on the shallowest make curve in the engine), so an
  ecology shift plausibly outweighs it — but "plausibly" is not measured. The per-attempt maths is
  proven (Phase 61 §4: the positive side is preserved to 1e-12 and the negative side is new by
  design), so this is a WHO-SHOOTS-WHAT question, not a wiring one. Worth a counterfactual read at
  the next calibration session; NOT worth a dial today. Nothing was tuned toward it.

- **O-55 — THE BLOCK/FOUL NO-LEAK PROOF IS ONE RUNG SHORT ON EMMETT'S MACHINE.** The bench asserts
  block and foul are bit-identical pre-to-post at every row — the proof that a make-door change did
  not leak into the block door through the shared body attributes. It needs the pre-change bench's
  `reach_bench.tsv` passed as a third argument, and the delivered command omitted it, so the check
  ran green in the sandbox only. The property is at the MIDDLE rung (compiled + ran in-sandbox), not
  the top one. Cheap to close: keep a pre-change tree, or commit the pre-change tsv as a fixture.

- **O-56 — THE 6'8"+ RIM-FINISHER LEADERBOARD CANNOT BE READ.** S83's prompt predicted 6'8"+ men in
  the top 50 RIM finishers going 6 -> ~41, and the season page prints no rim-only leaderboard, so the
  prediction could not be checked either way. The overall FG% board is a different cut (it went from
  six bigs in the top ten to eight, led by a 7'1" centre at 75.9%). Recorded as UNMEASURED rather
  than substituted for. A per-zone shooting board is a small season-page addition whenever the next
  page session opens.

- **★ O-57 — THE SEASON ASSIGNS NO COACH PROFILE TO ANY SCHOOL. This gates three boards, not one.**
  `CoachProfile` exists, carries three dials on a 1-10 scale (`HeliocentricBias`, `ShotSelectionBias`,
  `PaceBias`), and `HeliocentricBias` is properly wired to Roll E's hierarchy exponent — it decides who
  gets the SHOTS. But `RunSeasonCore` never calls `SetCoach`: all 347 schools play all 5,205 games on the
  compiled default (5/5/5). Consequence, and it is large: the nation's leading scorer today IS essentially
  its highest-rated scorer, because every team in the country runs the identical system. The same holds for
  the assist board, the shot-diet board and pace. Emmett's framing (S84): *"a guy leading the conference in
  assists does not necessarily mean he has the best passing rating — it means he has the ability to pass
  plus his coach's strategy was conducive to it."* Found only because his design point sent the search to
  the coaching surface rather than to the assist picker. **Blocks the second strand of O-46.** Opens as a
  design conversation, not a build: where a school's profile comes from (prestige? conference? a coach
  object with its own generation and career arc?) is Emmett's call, and it reshapes scoring concentration,
  shot diet and pace at the same time as assists.

- **O-58 — THE PHASE 74 FINGERPRINT RE-STAMP RITUAL HAS NOW RUN THREE SESSIONS RUNNING (S81.3, S83, S84).**
  The block fixtures hash the ENTIRE `Matchup` section, so any change in it invalidates goldens that never
  read the changed key. S83's note said "revisit if the re-stamp ritual recurs." It has recurred twice. The
  obvious narrowing — hash only the keys the block path reads — was rejected at S83 as the riskier fix,
  because a list that silently misses a key turns a loud guard into a quiet lie. That reasoning still holds,
  so this is a recurring COST to be ruled on, not a defect with an obvious fix. Cheap alternative worth
  considering: a one-line tool that re-stamps all three files and prints a diff proving no saved number
  moved, which is currently done by hand every time.

## Parked — waiting on a named prerequisite





- **P-1 — Shooting-foul positional lean (~50.7/49.3) → the help-defense/rotation model (S62).**
- **P-2 — Steals' pressure-DIAL side → the coaching/pressure layer (S52, updated S58).**
- **P-3 — BallHandling's pressure-dialed test → the coaching layer (S49).**
- **P-4 — OT-LOW (~2.8% vs 6% target) → the coaching / late-game-strategy layer (post-S41).**
- **P-5 — Endurance's temporal shape → a time-sliced bench (S50).**
- **P-6 — Wingspan's jump-ball tip → a first-possession counter (S50).**
- **P-7 — PostDefense-as-size coupling → the synthesis pass (S52).**
- **P-8 — Age/class population structure → the season/recruiting layer (S42.1 ruling).**
- **P-9 — Roll G lineup-context bend (teammate spacing/gravity as selection) → its own
  design conversation; the attributes are carried on Player, unread.**
- **P-10 — Shooting-foul rate dial (bridge #3) → no longer urgent post-S40; a page question.**
- **P-11 — Reconciled team-rebound line → instrument work (S41; credited gap ~4.9/team/game
  is a definition gap, not a sim error).**
- **P-12 — The full personal-turnover attribution flip → the per-event attribution rework
  (S56 flattened the inversion; the committer-picker channel still climbs with usage).**
- **P-13 — Small parked tail (owner sessions in parentheses):** `Outside == 0` buzzer heave
  (S35); personality/timidity on usage (S36); per-player attribution for held balls / Roll K
  (S33); press-frequency sentinels; length-in-make% defender term (S17/S40-era); per-zone
  location-blend weights + corner-three split (P9); reference-card pinning + multi-seed
  (S31/32); EqualShare centralization (P28); opening-five/lineup logic; FT unattributed
  bonus-trip fallback (~0 volume on populated rosters); displacement "advantaged" bin (S36);
  code hygiene (WeightedAggregate duplication, Mk consolidation, RollE stub double-build).
- **P-14 — Long-term watches (design before the relevant layer ships, not now):** save-file
  schema versioning; end-to-end RNG/determinism review before the full season layer; the
  Player data layer at 21k+ actives; moddability. (working-with-emmett §7)

## Closed by ruling (looks unfinished — is not; do not "fix")

- **C-33 — AN OFFENSIVE FOUL COUNTS TOWARD THE MAN'S FIVE, AND NEVER TOUCHES THE TEAM (Emmett's ruling,
  S87 check-in).** The cleared prompt accounted for only ONE offensive foul — the loose-ball shove in the
  rebound scrum, 0.38/game — and planned a new Discipline-alone draw for it. The check-in found three more
  sources (Roll A's entry, Roll C's turnover pie at 6.8% of the halfcourt shares, Roll K's scrum) all
  landing on the `OffensiveFoul` terminal at ~1.5 per team per game, **and** found that `Resolver.cs` has
  named the man who commits a charge since **Phase 34** via `TurnoverInteriorPicker` — recorded as a
  turnover, reaching no foul count. Emmett ruled charges count toward five and that S87 reuses the
  already-named man, so the charge bucket consumes no new randomness. Do NOT add a second rule for who
  commits an offensive foul; there is one answer and it is the turnover picker's.
- **C-34 — THE SCRUM FOUL IS ON THE MEN IN THE SCRUM (Emmett's ruling, S87).** The loose-ball foul on the
  offense draws on the same interior weighting a charge uses — post-weighted, guard-floored — not on
  discipline spread across all five, which would put real weight on the guard standing at the top of the
  key. This is deliberately the same rule as C-33 rather than a sibling of the reach-in draw.
- **C-35 — A FOUL-OUT REPLACEMENT DOES NOT WAIT FOR A DEAD BALL, AND THE MAN FINISHES HIS TRIP (Emmett's
  ruling, S87).** Forced replacement runs ahead of the ordinary rotation move and ignores the `isDeadBall`
  gate, so a foul-out no longer waits when the next trip starts on the run. Selection is *best available
  man at that position* — positional and legal-lineup rules kept, minutes plan and minimum stint dropped.
  The remaining gap (he finishes the trip his fifth foul landed in) is **accepted and bounded at exactly
  one trip**, not a defect to fix in passing; closing it properly is O-65. Two men reaching five at the
  same whistle is **not a design case** — one whistle, one man (Emmett corrected this framing directly).
- **C-32 — TRANSITION DEFENCE IS A REAL EFFECT SET, AND IT GETS AN INSTRUMENT BEFORE A DIAL (Emmett's ruling,
  S84 design conversation 2026-07-29; recorded at S85 because it had never reached this board or the journal).**
  Four effects ruled: speed should take a larger role in push/settle than the athleticism aggregate alone; four
  fast guards should raise the odds of a stop, a block, a miss, or of forcing the offence to settle before a
  break becomes real; the spread of who gets a break block should widen with a fast lineup; and none of it is
  designed until something measures it. S85 built the measurement and moved no dial. The ruling is not "closed"
  in the sense of finished — it is closed as a RULING, so no future session re-litigates whether these effects
  belong. What remains is the wiring, and the third effect is blocked on O-48 (see O-60).

- **C-30 — THE ENGINE MAY BREAK AT THE ABSURD EXTREME; no artificial ceiling is imposed on a
  mismatch (Emmett's ruling, 2026-07-29).** S83's stress bench read 87.4% for a max-Finishing 6'8"
  finisher in a frozen best-case matchup, against a real-world two-season D1 mark of 77-79%. The
  bench's hard ceiling was therefore red — and had been red at 82.6% BEFORE the change too, which is
  the tell that the bar could never have held on that instrument. Emmett: *"The engine should allow
  for the absurd extremes. If I put an all american team against the worst team possible, it should
  'break' the engine so to speak."* The real-world figure is a SEASON number earned against a
  schedule of varied opponents; one fixed favourable matchup is allowed to run past it. The
  assertion was **removed, not loosened** — the number is printed with the ruling recorded beside it
  and only the ORDERING is asserted. Consequence recorded so it is not read as a defect: extreme
  size mismatches will produce extreme shooting numbers, at both ends, by design. Do not "fix" this
  by capping the term. The place to read a real-world shooting mark is the season page.

- **★ C-31 — ASSIST CONCENTRATION IS A COACHING OUTPUT, NOT AN ATTRIBUTE OUTPUT. Do not add a
  concentration exponent to `AssistPicker`.** Offered at S84 as the cheap fix for the flat assist board and
  ruled out by Emmett. A league-wide exponent bakes ONE funnel level into the math permanently, which is
  exactly the decision the strategy layer is supposed to make per team: a motion team should read low, an
  iso team should read high, and one number for everybody is wrong even if it lands the real board's average.
  The scoring analogy is the test — the nation's leading scorer is not automatically its highest-rated
  scorer, and the assist leader is ability PLUS a system conducive to it. Consequence for future sessions:
  the top of the assist board is **not a calibration target** until O-57 lands, and a session that "fixes"
  the concentration with picker or attribute math is tuning against a missing input. This also settles the
  earlier AST% finding (best passer 26.3% -> 36.3% at S84 against elite real marks near 45-50%): the
  remaining gap is system, not the door.

- **C-28 — A big who cannot guard the perimeter is as legitimate as a guard who cannot protect the
  rim; the shared defensive bid pair is INTENDED (Emmett's ruling, 2026-07-27).** `PlayerGenPass3`
  drives `pref["PerimDefense"]` and `pref["InteriorDefense"]` from ONE pair of constants
  (`DEF_BID_LO` / `DEF_BID_SPAN`, `:440-441`), mirrored about the defensive plane. S79.2's check-in
  flagged this as a scope contradiction in the S80 prompt, which claimed to change interior defence
  and "nothing else in generation". Emmett ruled the mirror is the design: **perimeter-defending
  bigs should become rare in the same fashion that rim-protecting guards do.** So S80 changes both
  sides together and does NOT split the constants. Consequence, recorded so it is not read as
  collateral damage: a big's `PerimeterDefense`, `Steals` and `OffBallDefense` low tail widens by
  exactly as much as a guard's interior tail. The census (S79.2) prints the two families adjacent
  so the mirror is inspectable.
- **C-1 — Defensive ratings are the MAN-TO-MAN wire; a future scheme layer TOGGLES distinct
  wiring sets (S61 architecture ruling).**
- **C-2 — Team-aggression fouls belong to the coach/pressure layer, not Hustle (S61; the
  S45 Hustle→foul coupling retired S62, its dead dials deleted S73).**
- **C-3 — The ball-dominance / initiation layer is NOT built; assists attach after the
  fact, accepted as ornamental for now (S57.1).**
- **C-4 — Turnover KIND stays flat; attributes drive how often, not which flavor.**
- **C-5 — Roll D foul flavor stays flat (fires before Roll G — no zone stamped).**
- **C-6 — Offensive-foul flavor stays flat beyond the frontcourt/backcourt split.**
- **C-7 — The and-1 split (MafFraction) is per-zone, not matchup-aware.**
- **C-8 — Tendencies are deterministic in ratings; same ratings, same diet.**
- **C-9 — Held-ball losses stay off the turnover line (S33 R1).**
- **C-10 — The tendency oracle's population-mean diet is a directional diagnostic only.**
- **C-11 — DisplacementMaxMagnitude = 0 is ablation only; it does not undo Route B.**
- **C-12 — Turnover clock is court-aware bands, not a shifted center (S37); an
  offensive-rebounded possession times as a frontcourt turnover.**
- **C-13 — The fast break sets a modern base diet bent per shooter (S38); Roll K putbacks
  are excluded from fast-break FGA accounting (S38).**
- **C-14 — Three-point VOLUME is a generation/era-profile lever, not a runtime knob (S39).**
- **C-15 — Shooting fouls count toward the team-foul total (S40).**
- **C-16 — The halfcourt turnover mix is 50/50 live/dead, "to start" (S41).**
- **C-17 — The assist lineup-passing midpoint tracks the generated population (S41).**
- **C-18 — The 55/45 rebound team split stays; the picker was the culprit and was fixed
  (S45 ruling, S46 fix).**
- **C-19 — Rebounder-picker body attribution keeps the S46 shape (Emmett's rulings).**
- **C-20 — The recruiting line is first-past-the-line at R_LINE 17, "all of college";
  top-of-class selection is the divisional layer's job (S66, explicitly reversible).**
- **C-21 — Position follows the defensive plane, exact-count 40/30/30; height gets no
  vote; offensive role rides as flavor (S70).**
- **C-22 — The generated rebounding scale keeps, explicitly reversible (S70).**
- **C-23 — Divisional sorting: size/athleticism are the unofficial gates between divisions;
  skill overlaps heavily and is premium-overridable (Emmett ruling 2026-07-12).**
- **C-24 — Pass 2 (skill-first generator) is RETIRED and archived (S73); its oracle rulings
  (S42/S42.1) are historical record under `tools/archive/pass2/` and journal S42–S44.**
- **C-26 — Size is priced RELATIVELY, never absolutely, and any future role cost must be
  gap-shaped (Emmett ruling 2026-07-25).** *"You only get punished for size if the other team can
  punish it."* Verified against source: rebounding composes sizeShift/skillShift/hustleShift, each
  `GapFn(offense − defense)`, bent through tanh — equal teams get zero bend, so two five-guard teams
  get the same rebounding split as two five-big teams. Blocking is the same shape. There is no
  absolute size floor. A flat out-of-position penalty would be the first absolute physical term in
  the codebase and would break small-ball coherence.
- **C-27 — Positional eligibility is the ONE-STEP ladder, evaluated from the stored position and
  NOT transitive (Emmett ruling 2026-07-25).** G↔W and W↔B; a guard never reaches a big seat and a
  big never reaches a guard seat. Emmett: *"Every PG can play SG. Every SG can play SF, etc… not
  well, but there is real position flexibility baked into basketball."*
- **C-25 — A missing config key stays QUIET AT RUNTIME (compiled default applies, the game boots)
  and becomes LOUD AT TEST TIME via Phase 71 (Emmett ruling 2026-07-25).** Refuse-to-boot was
  considered and rejected: it would force every future dial into two places forever.

## Next approved candidate — exactly ONE

**HOME COURT.** Unchanged by S93 and worth stating plainly, because the ordering's original REASON no longer holds and
the choice should be made on purpose rather than inherited. S92 put home court before the scheduler on the grounds that
the season already played a balanced 15 and 15 — the exact condition under which the real ~60% home win rate is measured
— so the effect dropped onto the *existing* schedule would read clean. **S93 replaced that schedule.** Every team now
plays its own league's number and hosts exactly half, so the balanced condition SURVIVES and is promised harder than
before; what is gone is the clean before-and-after, because the opponents changed. Home court will now land on top of a
schedule that is one session old. Emmett was told this at the S93 gate and chose the scheduler anyway, on the grounds
that the non-conference graph being protected was a placeholder due for deletion regardless. That reasoning holds.

What it inherits and must not re-derive:

- **R4 — the crowd is PRESTIGE and DISTANCE, and there is no city size.** Population, market size and arena
  capacity are ruled out by name; a place has no population field and must never grow one. *"A small town
  college can have an incredible homecourt advantage."* Both surviving inputs already exist on every school.
- **R6 — the effect ships FLAT**, inheriting the experience/cohesion axis later rather than waiting for it.
- **R7 — it lands on the TEAM, uniformly, with an EMERGENT magnitude.** *"It shouldn't the individual players
  do poorly, it's the odds of everyone goes down the same on the road, but if you have a team full of
  freshman, it brings it down more."* One number on all five, its SIZE built from the five on the floor —
  that is what keeps the crowd number off the no-scalar wall, and it is the load-bearing design constraint of
  the whole session.
- **R3 — the host fact already exists.** `GameSite` carries a place and a host; a neutral site is a game nobody hosts,
  not a category. ★ **S93 CHANGED WHAT IS AVAILABLE HERE**: every conference game now has a decided host, and the
  orientation can be handed pre-fixed venues. No game record carries a `GameSite` yet — that binding is still unbuilt —
  but the home/away fact is no longer a placeholder graph's accident.
- **Phase 83's A9 isolation check will need rewriting**, not extending: it forbids the four season-path files from so
  much as naming a geography type, and the moment home court lands they legitimately will. ★ **It is also a live
  landmine for S94**: the venue seam deliberately carries plain school ids, and the day it reaches for `GameSite` or
  `GameHost` that check goes red with nothing wrong.

**The scheduler's remaining thirds follow — REORDERED BY EMMETT AT S94** (dates were taken first; recorded so the
inversion cannot read as a stale plan). **DATES SHIPPED as S94**: the `D1,D2,D3` nights are read at last, the window and
wall derive from three authored numbers, and Phase 85 owns the proofs. **Next scheduler third — the host memory**: who
hosted last time, filling the `FixedResidualHost` seam S93 built and left empty; the answer to **O-79**, and the thing
that makes doubled pairs rotate year over year. `TravelPart`'s 38 pairs are Friday–Saturday road pairings that belong
to a scheduling session, not to rivalries — still unbuilt, still logged. **Non-conference scheduling is its own session
after that** and starts from nothing — it inherits the map (the WAC's worst internal trip is Hawaii ↔ Louisiana Tech at
4,030 miles against the Ivy League's 295), the open nights S94's loose windows leave empty, and the fact that fourteen
schools currently play no basketball at all.
