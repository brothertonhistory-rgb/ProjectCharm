# Contracts and the non-conference log — design brief r3

**Arc:** non-conference scheduling (O-92). ★ **THIS SESSION MOVES TO THE FRONT OF THE ARC.**
All rulings Emmett's, 2026-08-05, unless marked.

---

## 0. WHY THIS COMES FIRST

The arc was heading for shelf-and-odds next. ★ **Emmett caught the ordering error two sessions
before it would have cost us:** *"Things like home-and-homes, 2-for-1s, these showcases — I think
they need to be built before we go deeper into non-conference scheduling, right? Because each
season these will be inherited by the new season and need to be accommodated for."*

He is right, and the reason is structural rather than aesthetic. **A home-and-home is a season-N
decision that binds season N+1.** A scheduler built against a world where nothing is inherited
hands Duke a clean slate every year, and the obligation to visit Michigan State either gets
dropped or bolted on afterwards as an exception — which means reopening every part of the
scheduler to make room. The layer that carries state across seasons has to exist before the layer
that consumes it.

★ **AND ONE PERSISTED CHANGE SERVES THREE FEATURES.** The season log records conference hosting
and nothing else. Contracts, the 10–12 season repeat ceiling, and the recency demotion all need
the same thing: the log carrying non-conference state. Building it once is far cheaper than three
times, and it is the moment this project's standing save-format note is about — get a stored
shape right before there are careers sitting in it.

**New arc order:** contracts → the showcase/event pool → the shelf and the odds → sites and
nights → the Independents.

---

## 1. R6 IS REVISED, NOT QUIETLY DROPPED

The brief currently rules that **off-campus series carry no forward debt** — *a series is one game
or two agreed up front, never an obligation into an unguaranteed future.* A contract is precisely
a forward debt.

★ **R6 was ruled before any persistence existed.** It was a correct call for an engine that could
not remember anything; it is the wrong call for one that can. **R6 is superseded by R22 below**,
and the docs must say so in those words rather than letting it disappear.

---

## 2. R22 — THE CONTRACT OBJECT

A contract is **two schools, an ordered list of games, and a window**. One general shape covers
every form, because writing it generally costs almost nothing now and writing three special cases
costs a migration later:

| form | games | window | hosts |
|---|---|---|---|
| home-and-home | 2 | 2 seasons | one each, ordered — ★ degenerate under R22a: games equals window at once, so it is forced immediately and simply plays out |
| 2-for-1 | 3 | 3 seasons | A hosts twice, B once |
| 3-in-4 | 3 | 4 seasons | as authored |
| neutral series | n | as authored | none — neutral |

Each game — a **leg** — carries a stable id, **who hosts** (or that it is neutral), and a status
of Outstanding or Completed. ★ **NO LEG IS PINNED TO A NAMED SEASON**, and there is no past season
to read back through, which is what makes a contract cheap to persist and impossible to strand.

★ **THE LEG LIST PERSISTS; `gamesRemaining` IS DERIVED, NEVER STORED.** *(Correction, r3 — r2 said
"two counters, not a calendar" and that was wrong. If the executor chooses WHICH leg, a scalar
count cannot reconstruct which legs survive: Oklahoma State taking the Tulsa-hosted leg first
leaves four specific home legs, and `gamesRemaining = 4` cannot say that.)* The one stored counter
is the window. The invariant:

```
gamesRemaining == count(legs where status == Outstanding)
```

★ **DUPLICATE TRUTH IS THE BUG THIS AVOIDS** — a stored count and a leg list can disagree, and the
disagreement would surface seasons later.

★ **THE WINDOW IS LOAD-BEARING, NOT DECORATION.** "Three games in four years" is a count *and* a
deadline. With R22a below it is also what guarantees completion.

### R22a — the executor, the option, and forced acceptance

★ **THE EXECUTOR IS EXPLICIT IN THE AUTHORED CONTRACT, not derived at runtime.** Normally it is
the school hosting the most — the one writing cheques — but host plurality does not resolve a
home-and-home (one each), a neutral series (neither hosts), or any even split, and all three are
legal forms in §2. Authoring it removes the branch.
Emmett's example: Oklahoma State agrees to play Tulsa five times in eight years, one of them in
Tulsa. **Each season, before non-conference scheduling begins, the executor may exercise one game
of the contract — and chooses WHICH game**, including taking its away leg first if it wants.
*"In theory if Oklahoma State wanted to choose to do the away game first, second or anything else
they could. This particularly would be useful for when a user controls the program."* ★ Building
it as a real choice now costs nothing and saves a retrofit when a human manages a program.

★ **THE WINDOW INCLUDES THE CURRENT SEASON**, and is decremented at ROLLOVER, never before the
decision. A three-in-four contract therefore opens at 3 games / 4 window and season one is a real
choice; decline and season two reads 3/3 and is forced. ★ Checking after decrementing would force
season one and silently delete a year of flexibility.

★ **FORCED ACCEPTANCE: when games remaining equals window remaining, the executor must exercise.**
Decline freely while games < window.

★ **AT MOST ONE LEG PER CONTRACT PER SEASON IS A CONTRACT RULE, ASSERTED HERE.** S102's legality
also forbids a pair meeting twice in a season, but that is a **second defence, not the
definition** — upstream contract legality must not depend on a downstream test that a later
session could weaken. This yields the invariant the build asserts rather than hopes
for: **a contract always completes inside its window and can never quietly rot.** Games can never
exceed window, because S102's legality already forbids a pair meeting twice in one season — the
two constraints agree by construction.

★ **THE OTHER SCHOOL HAS NO SAY, AND THAT IS CORRECT.** The executor dictates and the other side
adapts — the same shape as R4 in the matching. Named explicitly because "one team decides when the
other team plays" reads wrong until the reason is stated.

★ **A NOTICED CONSEQUENCE, NOT A SURPRISE: the away leg drifts to the end.** Nothing makes an
executor exercise the game it must travel to, so a naive policy defers it until forced and the
contract finishes with the trip nobody wanted. Realistic enough to allow; recorded so it is not
discovered on the page in season eight.

★ **THE PLACEHOLDER'S LEG ORDER (Emmett, r3): home leg, then neutral, then away, authored order
breaking ties.** This follows directly from his accepting the away-leg drift as realistic — a
school reaches for the home date and travels when it must. **Forced contracts always exercise;
optional ones consult the placeholder.** A fixture may inject an explicit leg choice, so human
control later selects a leg without restructuring anything.

★ **EXERCISING IS NOT SIGNING.** Negotiation stays out of scope (§5), but declining an option is
itself a decision, so the session needs some rule or contracts cannot run at all. **The exercise
policy ships as a placeholder constant at the R8 seam that coach temperament inherits later** —
the same treatment the quality axis is getting. Claude's recommendation, accepted: a placeholder
lets a full career run end to end and shows Emmett the behaviour, where a fixture-authored
exercise pattern would only ever show what the fixture said.

---

## 3. R23 — CONTRACTED GAMES ARE PLACED FIRST

Emmett: *"The 'owing' a game stuff has to be put on the schedule before any sort of larger
non-con scheduler takes place. Those games have guaranteed spots before the rest are populated."*

★ **ORDER FOR THE WHOLE NON-CONFERENCE LAYER: exercise options → place contracted games →
event and showcase seats → the scheduler fills what remains.** True in basketball terms as well: a coach opens the season already
owing a trip to East Lansing and holding a bracket seat, and books the buy games around them.

**What this does to the request builder.** Today it computes open games and splits them into home,
neutral and road. It now subtracts **three** things off the top — conference games, event games,
and games already owed — and splits only the remainder. ★ **A contracted game arrives already
knowing its host**, which is exactly the shape S102's matcher wants as input, so nothing downstream
has to learn a new object.

★ **"GAMES OWED" MEANS LEGS EXERCISED THIS SEASON, NOT EVERY OUTSTANDING LEG.** A five-in-eight
contract with four legs outstanding consumes **one** slot this season. Getting this backwards
would starve a school of its whole November.

★ **A CONTRACTED GAME BYPASSES MATCHING ENTIRELY — it is not a request.** It already names both
schools and the host, so there is nothing to match. Execution produces a **fixed pairing**
registered before ordinary matching begins: both schools lose an opening, the unordered pair joins
the used set, and the site counts move. ★ **It never enters the opponent pool**, so the matcher
cannot rematch, reject or drop a guaranteed game.

★ **AND THIS DISSOLVES THE "WHAT IF IT DOESN'T FIT" QUESTION.** Placed first, a contracted game
cannot fail to fit. It only becomes impossible when a school owes more games than it has open
slots — a broken world, reported and never silently dropped. ★ **Verified against live source:
S101's home/neutral/road split is DERIVED from whatever open games remain, not a cap that a
contract could violate**, so total open games is genuinely the only hard bound.

### R23a — the season's contract lifecycle, in order

★ Ownership must be exact or "live contracts ride in the season record" describes two different
designs. **Season N+1's record carries its own complete live-contract state; executing a season
never reopens an earlier record.** At rollover: resolve deaths → decrement each window exactly
once → archive completed and dead contracts → write the survivors into the new season's record.

Within a season, before anything else touches the slate:

1. load live contracts; resolve school identities
2. **terminate same-conference contracts and report** — ★ before any exercise, or an
   equality-forced contract could be exercised through the wall
3. determine every **forced** leg and place it
4. validate capacity against total open games
5. only then evaluate **optional** exercises — ★ optional legs can never consume capacity a forced
   leg needs
6. deterministic order throughout

★ **CONTRACTS ARE PROCESSED PAIR-GLOBALLY, ONCE PER SEASON, AGAINST A STABLE `ContractId` — never
in a per-school loop.** A contract touches two schools; a school-by-school pass invites double
handling, order dependence, and two mutable references to one object. Schools reference contract
ids; they do not own copies.

★ **Conference alignment is settled before season scheduling**, so membership cannot change
mid-season. Asserted rather than assumed.

---

## 4. R24 — HOW A CONTRACT DIES. BOTH WAYS FAIL CLOSED.

**(a) The two schools become conference mates → TERMINATED, hard stop.** Emmett: *"if suddenly
they become conference mates, it is terminated hard stop."* Consistent with the matching, where
same-conference is already a wall rather than a preference. Remaining games are not rescheduled,
not carried, not honoured elsewhere.

**(b) A damaged or unreadable season file → DROPPED.** ★ **And the report is honest about what it
cannot know.** If the record carrying the live contracts is unreadable, the engine knows the
collection was lost, not which contracts were in it — so the page reports a **collection-level
loss**, never a named pairing it could not read. Naming one would require a partial-salvage path
that contradicts failing closed. Emmett: *"if it's damaged, I think it just
drops it. Hopefully that's a rare occurrence."* ★ **This deliberately matches S96's fail-closed
host-memory rule rather than making an exception to it** — one story for "the record is unreadable
so we do not guess," not two. Damage means a corrupted or truncated season file, not a normal
event.

★ **NEITHER DEATH IS SILENT.** A terminated or dropped contract is reported on the page. The
failure mode this project keeps finding is a rule that fails invisibly.

---

## 5. SCOPE — THE WALL, AND WHERE IT FALLS

Emmett asked whether contracts were too much. **The record is general; the negotiation does not
exist yet.**

**IN**
- the season log carries **non-conference pairings** — the persisted format change and version bump
- ★ **TWO COLLECTIONS, NOT ONE.** A played pairing is an append-only historical FACT; a live
  contract is mutable FORWARD state. They may share a record and must never share a collection or
  be inferred from one another — "the log carries non-conference state" otherwise invites one
  overloaded object.
- ★ **MIGRATION IS THE HONEST KIND: a missing field reads as an EMPTY collection.** Never null
  meaning unknown, never reconstruction from past schedules, never contracts inferred from
  repeated pairings. A pre-contract career owes nothing.
- the **contract object**, general enough for every form in §2, spanning seasons
- the request builder **subtracts what is owed** before splitting anything
- contracts **placed first**, per R23
- both deaths, both reported
- proven against **fixture-authored contracts** — honouring is tested without signing existing

**OUT**
- ★ **anything that CREATES a contract.** Who approaches whom, what terms are accepted, how a
  school weighs a 2-for-1 against a guarantee game — a behavioural system with real basketball
  judgment in it, coach-adjacent, and it would swallow the session whole.
- **the repeat ceiling and the recency demotion** — they read the log this session adds, so they
  become a small session afterwards instead of a large one. ★ Named so they are not absorbed.

★ **THE ENGINE ENDS THIS SESSION ABLE TO KEEP PROMISES IT CANNOT YET MAKE.** That is the whole
shape of the scope wall, and it is deliberate.

---

## 6. WHAT THIS UNBLOCKS, AND WHAT IT LEAVES ON THE PAGE

**Unblocks:** the ceiling and the recency memory (same log). Home-and-homes as the real answer to
a Marquee school's true road games — Duke going to Michigan State is a **return leg**, not a
leftover, which is the honest fix for the Oklahoma State reading that started this whole arc.

**Leaves standing, knowingly:** the buy-game shelf is still keyed on prestige rather than
conference tier, Northwestern still schedules like Duke, and the trips-by-class inversion (C-41)
is unresolved. ★ **Those are visible and harmless for two more sessions. Rebuilding the scheduler
around inherited obligations later would be neither.**

---

## 7. OPEN — FOR THE BUILD PROMPT, NOT FOR EMMETT

Claude's to specify and audit against live source at draft time:

1. The persisted shape and version bump; what a pre-contract season file reads as.
2. ★ **SETTLED — live contracts ride in the season record**, not a sibling file. Emmett: a school
   is only ever carrying two or three, since ten or fifteen would mean return games had eaten the
   slate. **That is a bound the build ASSERTS, not merely an expectation** — the open-game
   arithmetic forbids it.
3. ★ **DISSOLVED.** A contract reads no past season, so a missing or damaged year cannot strand a
   game. The two counters carry forward; §4(b) covers the one case that does bite.
4. The exact predicate for "damaged" — ★ **reuse S96's existing status rather than inventing a
   second definition**, so there is one story for an unreadable record.
5. Whether S102's five legality tests need to know a pair is contracted, or whether placing
   first makes that automatic (★ likely the latter — the pair is already used).
6. What the page prints: contracts honoured, exercised, declined, terminated, dropped, outstanding.

★ **LOGGED FOR THE SITES SESSION, NOT THIS ONE — the semi-home is a third category the engine
does not have.** A game today is hosted or neutral, full stop, and the ledger counts them that
way. A semi-home is neither: the host keeps the advantage but does not play on campus (real Duke
played Texas in Charlotte; real Kansas State played Mississippi State off site). It is a SITE
decision about a game already arranged, so it belongs with R9–R12 — but whether it is a home game
with a different address or its own kind is a genuine ruling, recorded here so the sites session
does not discover it.
