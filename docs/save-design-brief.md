# Save anywhere, resume exactly — design brief r1

**Arc:** the calendar (C-67) — step 2: save anywhere, reload anywhere (O-134), fresh dice (O-132), and a home for
league state that changes. **Status: DESIGN ONLY. Not cleared to build.** Drafted against `65773fe` (S122) after an
architectural review by Claude and ChatGPT that agreed on the direction (2026-10-11). Every code claim below was
opened at draft time. Nothing was built.

---

## 0. In plain terms

Today a career season is one command that runs start to finish. If it stops partway — a crash, a closed window,
Ctrl+C, a power cut — **the career is over**. The game has already written down "season 4 has started" and spent its
numbers, but the season's record of games is never finished. The next run refuses: *"season 4 of this career kept no
season log … start a new career."* That was a fair shortcut while a season was one 15-second command. It cannot
survive "sim day / sim week / save and quit in January".

This brief proposes:

1. **A save is one small file that says where the career is.** The bulky history files from finished seasons stay
   exactly as they are. Mid-season, the save holds the date reached and the dice used. Nothing else.
2. **Loading a mid-season save re-plays the season from its start, from the game's own records, up to the saved day.**
   For January, that means the game silently re-plays November and December — several seconds; a March save, an
   estimated 10–20 on Emmett's machine (to be measured before the ruling). Because every game
   rolls the dice it is given, the re-played games come out the same — and the save carries a fingerprint of them,
   so if they ever didn't, the game refuses by name instead of continuing a season that has quietly changed.
3. **Writing a save can never break the previous save.** New files are written first; the one small file is
   switched over last, in a single step. A crash at any moment leaves either the old save or the new one — never
   half of each.
4. **The world file stays the starting universe and is never rewritten.** What changes during a career (prestige
   first, then coaches and recruits) gets its own small file, starting from the world's values.

### What Emmett is asked to rule on

| # | Question | Recommendation |
|---|---|---|
| Q1 | Load a mid-season save by **re-playing** the season up to that day (seconds — up to an estimated 10–20 for March; exact by construction), or by **storing everything in memory** (instant; every future feature must remember to save its own piece, and a forgotten piece breaks seasons silently)? | **Re-play.** Storing everything can be added later as a speed-up, with re-play as its check (§3). |
| Q2 | A crash loses everything since the last save. **When does the game save on its own?** | **After every sim command** (sim day / week / month / to next event). A mid-season save is one small file, so it is cheap. |
| Q3 | Careers from before this change: **refuse** with the standing sentence (*"saved by an older version of the game — start a new career"*, C-60), or **upgrade** them? | **Refuse** (C-60), as every format change since S117 has. |
| Q4 | Fresh dice (your rulings: *"10,000 unique results"*, *"save scummed 10 times … 10 slightly different results"*): **games already played never change; every day after the save re-rolls each time you load it.** Correct? | **Yes.** Fixed dice remain for the suite only. |
| Q5 | O-134 said the save session would also write the season log in play order. **Under re-play it isn't needed.** Drop it? | **Drop it.** No old file format changes; the log stays as S122 left it. |
| Q6 | If a finished season's file is damaged or missing, **refuse to load** (as today), or try to continue without it? | **Refuse by name**, naming the file. |

One standing rule changes if Q1 is ruled as recommended: **S90's "a season is never resumed"** becomes "a season is
resumed only by re-playing it from its start, and only if the re-play matches the save".

---

## 1. What exists today (the gate, at `65773fe`)

### 1a. The files a career writes

| File | Written | Read by | Notes |
|---|---|---|---|
| `career.json` (history v3) | at creation; **on every id reservation** (`HistoryStore.Reserve` → `PublishAtomically`) | everything (lineage, world fingerprint, start year, the three counters) | atomic per write; counters advance **before play** |
| `career.events/season-N.json` | **before play** (seating, contracts, NotPlayed: `MtePublishRecord`); **replaced after play** with finishes (`MteReplaceRecordWithFinishes`) | next seasons: seating history (4 back, `MteReadHistory`), host memory / rotation / debt (8 back, `ReadCareerMemory`), live contracts (`ReadLiveContracts`) | a missing or unreadable earlier record is a **silent hole** — the reader skips it and the next season's basketball changes without a word |
| `career.gamelog/season-N.scout` | **before play** (`ScoutingFile.Write`) | next season's camp (`CareerDevelop`) | written once, refuses an existing file |
| `career.gamelog/season-N.log` | `.inprogress` created **before play** (roster section); blocks held in memory and appended **after play** (S122 K2); finalized by rename | next season's people (`ReadCareerPeople`), the pages | refuses an existing final **and** an existing `.inprogress` (`CrashedLogPresent`) |
| `*.lock` | sidecars | — | the career lock is **released before the simulation** (`CloseReservations`) |

### 1b. The interrupted season, traced

Reservation publishes `nextSeasonId = N+1` before play → the event record and scouting file exist → the log is
`.inprogress` → interruption. Next run: `PeekNextSeasonId` is N+1, `ReadCareerPeople(N)` finds no `season-N.log` and
refuses (`Program.Season.People.cs:74`). Season N cannot be re-run either: its number is spent, its event record
exists (`MteRefuseExistingRecord`), its `.inprogress` is refused. **No supported path continues the career.**

*Qualification (ChatGPT, accepted):* an interruption **after** the log is finalized does not strand the career. A
failure replacing the event record afterwards is reported (`EventRecordStatus.WriteFailed`) and leaves a deliberate
NotPlayed hole. The strand window is from the first reservation to the log's rename — which is nearly the whole run.

### 1c. What a season reads from earlier seasons

The world (immutable, fingerprint-bound); the previous season's log (people) and scouting file (hidden state); up to
eight earlier event records (seating history, host memory, rotation, debt, live contracts); the counters; **the season
seed, which today comes from the command line on every run** (`season <world> <seed> --history`); config.json.

**Everything else in a season is computed from those inputs**, deterministically: the schedule, dates, seating,
matching, contracts outcome, buy plans, the turnover, the camp, every game (engine seeds `seed + 2g`, `+1` by fixture
ordinal), every bracket and tournament, every accumulator (S122 plays them in calendar order; Phase 113 C2d proves the
files byte for byte).

### 1d. What is mutable, and where it lives

*Qualification (ChatGPT, accepted):* changing information already persists, through specialised records — people in
the logs (ruling 3), hidden state in the scouting files (ruling B), contracts and scheduling memory in the event
records. **What is missing is a home for a fact that changes and cannot be recomputed from history.** Prestige is the
first: it is read only from the world file (`world.Schools[..].CurrentPrestige` — the season, seating, matching,
non-conference and calibration code, about a dozen sites), and the career is bound to that file's fingerprint, so
prestige cannot move without breaking the career.

---

## 2. What a save is

> **A save is the career file (v4). It names, by hash, every file the career stands on; it holds the counters, the
> league-state file for the last season boundary, and — mid-season — the date reached, the dice used since the
> boundary, and a fingerprint of every game played so far. Everything else is reconstructed by re-play from the last
> boundary, and the re-play is checked against that fingerprint.**

### 2a. The authority rule

**The career file is the only authority.** A file on disk counts only if the career file lists it, with its SHA-256.
A listed file that is missing or does not match is refused by name. An unlisted file is an orphan of an interrupted
save: it is moved to `career.orphans/` on load, before anything reads, and never read. (This closes today's silent
holes: a past record cannot vanish and quietly change next season's basketball.)

### 2b. Two kinds of moment

- **A season boundary** (after a season closes). The career file lists that season's four files and the league-state
  file, and the counters advance. This is a checkpoint: nothing before it is ever re-played.
- **A day boundary inside a season.** The career file adds `progress`: the date reached (a save always sits between
  two days — sim buttons are day-sized), the dice segments (§5), and the played-games fingerprint. **No other file is
  written.** A mid-season save is one small atomic rewrite.

### 2c. Ids without a pre-play reservation

Today ids are reserved and published before play, so a crash still spends them. Under this design **no id is
published until the season closes**. Re-play recomputes the same ids: reservations happen in a fixed order (season, then
every game, then persons — `NumberSeasonSchedule`, then the turnover or the draft) from the counters at the boundary,
so the ranges are a pure function of the boundary. "An id is never issued twice" becomes **"an id is never published
twice"**: an id that existed only in a crashed run appears in no listed file and is never read.

### 2d. What is deliberately NOT in a save

Rosters mid-season, standings, accumulators, bracket progress, a tournament's seeded field, the held log blocks — all
re-played. A decision journal (the player's in-season choices) — there are no such choices yet. **The rule for later**
is recorded in §8 instead: the session that adds the first in-season decision adds the journal, with a format bump.

---

## 3. Why re-play rather than store-everything

| | **Re-play from the boundary (recommended)** | **Store everything (snapshot)** |
|---|---|---|
| Exactness | by construction: the same code path, the same dice, the same order (S122) | only as good as the serializer: every live field — 45 outcome properties, the league accumulator's dozens of counters and floating sums, every source's state, the held blocks — must be written and read back bit for bit |
| A forgotten piece | impossible to forget: nothing is stored | silent divergence, caught only if a test happens to exercise that piece |
| New features | free: a new system that is deterministic from (inputs, date, dice) re-plays for nothing | every new system writes and reads its own state, forever |
| Save cost | one small file | the whole live state |
| Load cost | re-play to the saved day: **up to one season** (estimate 10–20 s on Emmett's machine for March; measure at the gate) | near-instant |
| Old saves after an engine change | re-play diverges → **refused by name** (the fingerprint, §4d) — the same policy as C-60 | loads, then plays on under new rules from mixed state |

**The cost is load time, and it is bounded:** never more than one season, because every season boundary is a
checkpoint. If it becomes a problem, a snapshot can be added later as a speed-up — and re-play is then its proof
(snapshot-loaded and re-played must match), which is the right order to build the two in.

---

## 4. Writing a save, and what a crash leaves

### 4a. The protocol

1. Write every new content file **under its final name, which no listed file holds** (a season-N file is written only
   while season N is unlisted), flush it, and compute its SHA-256.
2. Write the new career file to `career.json.new`, flush it, and replace `career.json` with it, keeping the old one as
   `career.json.bak` (`File.Replace` — the replace-with-backup call, not the move `PublishAtomically` uses today).
3. Only then is the save made.

Season close is steps 1–3 with the season's log, event record (written once, final — no NotPlayed stage), scouting file
and league-state file. A mid-season save is steps 2–3 alone.

### 4b. Loading

Read `career.json` (or, if it is missing or torn, `career.json.bak` — and say so) → verify every listed file's hash →
quarantine orphans → re-play from the boundary to `progress.date` → check the fingerprint → ready.

### 4c. The lock

The career lock is held from load until the process ends. (Today it is released before the simulation; harmless only
because a second run refuses on a missing log.)

### 4d. The version guard

The save stores the config.json hash (an early, clear refusal if a dial changed) and the **played-games fingerprint**:
the S112 digest shape (`ordinal|home|away|homeScore|awayScore|possessions`) over every game played so far, **in play
order**. After re-play the fingerprint must match, or the load is refused: *"this save was made by a different version
of the game and cannot be continued — start a new career"* (C-60's sentence). This catches an engine change that config
cannot see.

---

## 5. Dice (O-132)

**A season's games are played in segments.** Segment 0 starts at the season's first day; every load of a mid-season
save in **live mode** starts a new segment at the saved day, with a freshly drawn seed, recorded in the save. A game
with fixture ordinal `g` played in segment `k` rolls `seed_k + 2g` and `seed_k + 2g + 1`. Segment 0's seed is the
season seed, so **an uninterrupted season is exactly today's**.

- **Live mode:** the season seed is drawn fresh at each new season and recorded; each load draws a new segment seed.
  Games already played never change (they are re-played on their own segment's seed). Everything after the save
  re-rolls on every load — Emmett's save-scum ruling.
- **Fixed mode (the suite):** the season seed comes from the career's master seed (season k = master + k − 1, today's
  `SeasonTwoSeed` convention) and **no new segment is ever started**. An interrupted-and-resumed season is therefore
  identical to an uninterrupted one — which is what the tests prove.
- Pre-season randomness (seating, schedule, the draft, the turnover, the camp) stays on the season seed.
- **A rule for every future in-season system** (practice, injuries, in-season recruiting): any randomness drawn during
  the calendar is keyed by `(segment seed, date, purpose, who)` — never a running stream shared across days. That
  keeps re-play exact and keeps the result independent of the order things happen within a day.

---

## 6. The world and the league state

- **The world file is the starting universe.** It is never rewritten. Its fingerprint stays the career's lineage
  binding.
- **The league state is the universe now.** One file per season boundary (`career.league/league-N.json`), listed by
  hash, initialised from the world at career creation.
- **What belongs in it — the rule:** *a fact goes in the league state if and only if it changes during a career and
  cannot be recomputed from the world plus the listed history files.* Prestige qualifies (once it moves). People do not
  (the logs, ruling 3). Hidden state does not (scouting, ruling B). Contracts and scheduling memory do not (the event
  records). Coaches, recruiting boards and injuries will, in their sessions. **Two copies of one fact are never kept.**
- **First tenant: prestige**, and only prestige. Every season-path read of `CurrentPrestige` reads the league state
  in career mode and the world in legacy mode. Values are identical until prestige movement exists, so no game moves.
- **Not required for save and resume.** It is required before any system changes a school (prestige movement,
  coaching, recruiting). It is built after save/resume (§7, phase C), small, and proven by a test-only change.

---

## 7. The migration path

Three sessions, each a separate audited pass (CONVENTIONS §6). The risk ratings are ChatGPT's, accepted.

### Phase A — the runner becomes a stoppable object (structural; **moderate risk**)

- `RunSeasonCore` splits into a **setup** (everything before the first tip, unchanged, writes included — same files,
  same moments, same ids) and a **run** object that owns what the `PlayOneGame` closure captures today (the slots,
  the accumulators, the sources, the counters) with `AdvanceTo(date)` and `Finish()`.
- The six test switches become one options record.
- **`RunSeasonCore` stays as a wrapper** (setup → `AdvanceTo(last day)` → `Finish`), so the 89 call sites in the suite do
  not change.
- **Nothing written to disk changes in this phase.**
- Proof: every pin; Phase 113 whole; plus **stop-and-continue in one process** — the same season advanced in
  one call, day by day, week by week and in random chunk sizes gives an identical outcome and identical files. This is
  ChatGPT's second guarantee.

### Phase B — save and resume (persistence; **high risk**)

- Career file v4 (§2) and the commit protocol (§4).
- All season files written at close; the NotPlayed pre-play record and the pre-play scouting write retired.
- Ids published at close (§2c); the lock held (§4c); orphans quarantined; re-play on load; the version guard (§4d);
  dice segments with live and fixed modes (§5).
- A command surface to drive it: advance a career to a date, by N days, or to the season's end, saving after each
  (Q2). The `season … --history` command becomes "advance to season end".
- Proof: **Phase 114** (§10).
- Pins: the final event record, every log and every game stay at today's values (Phase 113 C2a/b, the wall). The
  career file's bytes change by design: **Phase 113 C2c is re-pinned under ruling**, its old value recorded.

### Phase C — the league state (**low–moderate risk**)

- `league-N.json`, prestige its only field, the dozen read sites rerouted (§6).
- Proof: every pin unchanged; a test-only prestige change written at a boundary moves the next season's seating,
  survives save and reload, and a control without it matches the baseline.

**Then O-133** (the calendar runs the year — offseason events on their dates), on the same re-play and boundary
model. **Whether the checkpoint boundary moves from season close to July 1 is decided there**, not here.

---

## 8. Minimum versus future

| Required for save and resume | Required by ruling, small | Before dynasty systems, not for resume | Future only — do NOT build now |
|---|---|---|---|
| Phase A split | dice segments, live / fixed (O-132) | league state, prestige only (C) | storing everything for fast loads |
| career file v4 as the authority | autosave after each sim command (Q2) | | a decision journal (no decisions exist yet) |
| close-time writes; ids published at close | | | the log in play order (Q5) |
| orphans, lock, `.bak`, version guard | | | pages over an in-progress season |
| re-play on load | | | the offseason calendar (O-133 — its own session) |
| advance-to-date commands; Phase 114 | | | moving code out of `Program` |

---

## 9. Failure scenarios

| # | What happens | Expected | Test |
|---|---|---|---|
| F1 | crash mid-simulation between saves | load the last save; days since it are lost (and, in live mode, re-roll) | T2, T3 |
| F2 | crash while writing a season's content file | the old career file still rules; the partial file is an orphan, quarantined on load | T5 |
| F3 | crash after every content file, before the career-file swap | same as F2 | T5 |
| F4 | crash during the swap | `career.json` or `.bak` is whole; load names which it used | T6 |
| F5 | a listed file missing or changed (deleted, disk damage) | refused by name, naming the file (Q6) | T6 |
| F6 | an orphan with a listed file's name (a crashed close of the same season) | quarantined before anything reads; never read | T5 |
| F7 | a second process opens the career during play | refused: the lock is held | T10 |
| F8 | config.json changed since the save | refused before re-play (config hash) | T7 |
| F9 | engine changed, config not | re-play diverges → refused by the played-games fingerprint | T7 |
| F10 | crash during season close | the save still says "in season, day d"; re-play to d; the close runs again; fixed mode gives identical files | T5 |
| F11 | a pre-v4 career | refused with the C-60 sentence (Q3) | T11 |
| F12 | a save at the day a conference tournament opens, or mid-bracket | re-play re-seeds and re-fills exactly as uninterrupted | T2 |

---

## 10. How Phase 114 proves it (phase B)

Every equivalence test runs in **fixed mode**. Every resume runs in **a fresh process** — the harness launches itself
(`Environment.ProcessPath`), so no static or in-memory state can carry across, which a same-process test cannot rule
out.

- **T1 — no interruption, no change.** A career season through the new path equals today's games and files; the wall
  and Phase 113 C2a/C2b hold; C2c re-pinned.
- **T2 — interrupt at every day (★).** On fixture-mte (391 games): save after day d and resume in a fresh process,
  for **every** game day d; each finished season is byte-identical to the uninterrupted one. On stock: the cut points
  that stress state —
  - before the first game;
  - after the first day;
  - mid-bracket (between rounds);
  - the day before a conference tournament opens;
  - the day it opens;
  - mid-tournament;
  - after the last game, before close.
- **T3 — chained.** Save, resume, save, resume … through one stock season (about six cuts): identical.
- **T4 — two seasons.** Interrupt season two mid-January (the camp, the turnover and the scouting file all in play):
  identical through season three's start.
- **T5 — crash injection.** A test-only fault thrown after each content file, before the swap, and during close.
  Each time the previous save loads, orphans are quarantined, and the career finishes identical.
- **T6 — torn and damaged files.**
  - A truncated career file falls back to `.bak`, said by name.
  - A missing or changed listed file is refused by name.
  - Control: the untouched career loads.
- **T7 — the guard.**
  - A changed dial is refused before re-play.
  - An engine-side change with config untouched is refused by the fingerprint: a test-only switch that perturbs one
    game's dice after the save.
  - Control: no change loads.
- **T8 — fresh dice.** In live mode:
  - Load one save twice. Games before the save are identical in both; the days after differ.
  - A live career with three loads behind it, re-played, reproduces its own history exactly (segments recorded).
  - Control: fixed mode, loaded twice, is identical.
- **T9 — ids.** Every person, game and season id identical to the uninterrupted run. An id from a crashed run (F2)
  appears in no listed file.
- **T10 — the lock.** A second process during play is refused.
- **T11 — v3 careers.** Refused by the C-60 sentence.
- **T12 — negative controls for T2.** A resume that skips restoring one thing must fail T2 by name:
  - a tournament re-seeded from the national rather than the day's records;
  - the wrong segment seed for one day;
  - one dropped game.

**Runtime budget:** the full every-day sweep runs on the small world only. Stock cuts are chained in one career, so
the stock cost is about three to four seasons of re-play. Estimate **+2 to +4 minutes** on Emmett's machine; measured in
the sandbox and stated in the prompt.

---

## 11. Risks and limits

- **Load time** grows with the engine (§3). Watch it; the snapshot speed-up is the answer, not before it is needed.
- **Phase A's hidden captures.** The closure captures about 15 mutable locals; one left behind as a local would pass a
  one-call run and fail a chunked one. The chunked test is designed to catch exactly that.
- **`File.Replace` on Windows** is the call; its behaviour on a cloud-synced folder (OneDrive) is not assumed. The
  `.bak` fallback exists for a torn swap.
- **Determinism is load-bearing.** Anything that reads a wall clock, a dictionary's order, or an unkeyed shared
  random stream breaks re-play. S91's purity scan (Phase 82 A11) covers the clock; the guard (§4d) catches the rest at
  load, by name.
- **Out of scope:** recruiting, transfers, coaching, injuries, the offseason calendar, any UI, any move of code out of
  `Program`, any change to the possession engine, the log format.

---

## 12. Questions for the independent review

1. Is the authority rule (§2a) sufficient for multi-file consistency, or is there a sequence of writes that leaves a
   listed file and its career file disagreeing?
2. Is "an id is never published twice" (§2c) a safe weakening of "never issued twice", given that ids appear only in
   career files?
3. Does any input to a season escape §1c — anything read that is neither the world, a listed file, config, nor the
   dice? (Re-play is exact only if the list is complete.)
4. Is the dice-segment rule (§5) exact on re-play, including a load on a day with no games, and two loads on the same
   day?
5. Is T2's cut-point list for stock sufficient, given the every-day sweep runs only on fixture-mte?
6. Is re-play load time acceptable as a first implementation, or should Emmett see a measured number before ruling Q1?
