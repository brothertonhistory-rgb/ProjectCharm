# Save anywhere, resume exactly — design brief r3.1 (r3 plus the addendum in §14)

**Arc:** the calendar (C-67), step 2: save anywhere and reload anywhere (O-134), fresh dice (O-132), and a home for
league state that changes.

**Status: DESIGN ONLY.** The architecture is approved in principle (ChatGPT's audit of r3). Phase A is ready for its
own build prompt, and building it still needs Emmett's go-ahead. B1 is held until the contract-migration ledger (§14a)
is re-verified at its gate. B2, B3 and C are gated one at a time.

Drafted against `65773fe` (S122) and revised twice after ChatGPT's audits (2026-10-11). §13 lists every change. Every
code claim below was opened at draft time. Nothing was built.

> **★ After S123 (2026-10-12).** **Phase A shipped in S123** (Phase 114; §14h). **Emmett ruled Q1–Q8 on 2026-10-11
> (C-68), and three rulings differ from this brief's recommendations:** Q1 — **store everything**, not re-play; Q2 —
> autosave is a **setting** (every day, week, two weeks or month; **weekly** by default); Q5 — the season log **is**
> written as games are played. The others were ruled as recommended. **Where this brief says otherwise, C-68 governs.**
> B2 (§7) is marked for revision; that revision is the B2 draft's job, not a rewrite of this brief.

---

## 0. In plain terms

**The problem.** A career season today is one command that runs from start to finish. If it stops partway — a crash,
a closed window, Ctrl+C, a power cut — **the career is over.** The game has already recorded "season 4 has started"
and spent its numbers, but the season's record of games is never finished. The next run refuses: *"season 4 of this
career kept no season log … start a new career."* That was a fair shortcut while a season was one 15-second command.
It cannot survive "sim day / sim week / save and quit in January".

**What this brief proposes:**

1. **A save is one small file that says where the career is.** The large history files from finished seasons stay
   exactly as they are. Mid-season, the save adds only three things: the next day to be played, the dice in use, and a
   fingerprint of everything that has happened so far.
2. **Loading a mid-season save re-plays the season from its start, from the game's own records, up to that day.**
   This was measured in the sandbox at about 15 seconds for a January save and 31 seconds for late March. Your
   machine ran S122's checks about twice as fast, so expect roughly **7 and 15 seconds**. §3c has a command to measure
   it on your machine.
3. **The re-play is checked, not trusted.** A fingerprint covers every game's full box score, tournament progress, and
   the rosters and schedule the season started with. If a re-play ever comes out different in any of it, the game
   refuses by name. It never continues a season that has quietly changed.
4. **Writing a save can never break the previous save.** New files are written first, and the one small file is
   switched over last, in a single step. A crash at any moment leaves either the old save or the new one, never half
   of each. A backup copy of the small file is kept, and **recovering from it never destroys it.**
5. **The world file stays the starting universe and is never rewritten.** What changes during a career gets its own
   small file, the league state. Prestige is the first thing in it; coaches and recruits come later.
6. **It is built in five small steps, each proven before the next:**
   - **A** — the season can stop and continue.
   - **B1** — saves are safe. After B1, an interruption no longer ends a career: the season simply restarts from its
     first day.
   - **B2** — a season resumes on the day it stopped.
   - **B3** — fresh dice, autosave and the sim commands.
   - **C** — prestige is read from the league state.

### What Emmett is asked to rule on

| # | Question | Recommendation |
|---|---|---|
| Q1 | Load a mid-season save by **re-playing** the season up to that day (measured: about 7–15 s on your machine, depending how late), or by **storing everything in memory** (instant, but every future feature must remember to save its own piece, and a forgotten piece breaks seasons silently)? | **Re-play.** Storing everything can be added later as a speed-up, with re-play as its check (§3). Run the measurement in §3c first. |
| Q2 | A crash loses everything since the last save. **When does the game save on its own?** | **After every sim command** (sim day / week / month / to next event). A mid-season save is one small file, so it is cheap. |
| Q3 | Careers from before this change: **refuse** them with the standing sentence (*"saved by an older version of the game — start a new career"*, C-60), or **upgrade** them? | **Refuse** (C-60), as every format change since S117 has. |
| Q4 | Fresh dice: **games already played never change; every day after the save re-rolls each time you load it.** Loading and quitting without playing changes nothing. Correct? | **Yes.** Fixed dice remain for the suite only. |
| Q5 | O-134 said the save session would also write the season log in play order. **Under re-play it isn't needed.** Drop it? | **Drop it.** No old file format changes; the log stays as S122 left it. |
| Q6 | If a finished season's file is damaged or missing: **refuse to load** (as today), or try to continue without it? | **Refuse by name**, naming the file. |
| Q7 | **May you change the game's settings or code between seasons** of a career (tuning, new features), or does a career lock its rules forever? | **Change freely between seasons.** Each season records the rules it was played under. |
| Q8 | **A mid-season save opened by a different build** (you updated the game, then reopened a January save). Strict: refuse to resume, and offer to **restart that season from its first day** under the new rules. Permissive: continue if everything already played re-plays exactly, recording each rules change. | **Strict, for the first release** (changed from r2, on ChatGPT's advice). It is far simpler to build and to prove. Restarting the season loses only that season's games so far. The permissive rule can be added later; the save format already allows it. |

**One standing rule changes if Q1 is ruled as recommended.** S90's "a season is never resumed" becomes: "a season is
resumed only by re-playing it from its start, and only if the re-play matches the save."

---

## 1. What exists today (the gate, at `65773fe`)

### 1a. The files a career writes

| File | Written | Read by | Notes |
|---|---|---|---|
| `career.json` (history v3) | at creation, and **on every id reservation** (`HistoryStore.Reserve` → `PublishAtomically`) | everything: the lineage, the world fingerprint, the start year and the three counters | each write is atomic; the counters advance **before play** |
| `career.events/season-N.json` | **before play**, with the seating, the contracts and status NotPlayed (`MtePublishRecord`); **replaced after play** with the finishes (`MteReplaceRecordWithFinishes`) | later seasons: seating history, 4 seasons back (`MteReadHistory`); host memory, rotation and debt, 8 back (`ReadCareerMemory`); live contracts (`ReadLiveContracts`) | **readers build the path themselves.** A missing or unreadable earlier record is a **silent hole**: it is skipped, and the next season's basketball changes without a word |
| `career.gamelog/season-N.scout` | **before play** (`ScoutingFile.Write`) | next season's camp (`CareerDevelop`) | written once; an existing file is refused |
| `career.gamelog/season-N.log` | `.inprogress` is created **before play** with the roster section; game blocks are held in memory and appended **after play** (S122 K2); finished by renaming | next season's people (`ReadCareerPeople`); the pages (`ReadCareerLogs`) | an existing final file **and** an existing `.inprogress` are both refused (`CrashedLogPresent`) |
| `*.lock` | sidecars | — | the career lock is **released before the simulation** (`CloseReservations`) |

### 1b. The interrupted season, traced

1. Reservation publishes `nextSeasonId = N+1` before play.
2. The event record and the scouting file are written, and the log is `.inprogress`.
3. The run is interrupted.
4. On the next run, `PeekNextSeasonId` is N+1. `ReadCareerPeople(N)` finds no `season-N.log` and refuses
   (`Program.Season.People.cs:74`).
5. Season N cannot be re-run either: its number is spent, its event record exists (`MteRefuseExistingRecord`), and its
   `.inprogress` file is refused.

**There is no supported way to continue the career.**

An interruption *after* the log is finished does not strand the career. A failure replacing the event record is
reported (`EventRecordStatus.WriteFailed`) and leaves a deliberate hole. The stranding window runs from the first
reservation to the log's rename, which is nearly the whole run.

### 1c. What a season reads

The inputs:
- the world (immutable, bound by its fingerprint);
- the previous season's log (people) and scouting file (hidden state);
- up to eight earlier event records;
- the counters;
- config.json;
- the season seed, **which today comes from the command line on every run** (`season <world> <seed> --history`).

Everything else is computed from those inputs, deterministically:
- the schedule, its dates, seating, matching, the contracts outcome and the buy plans;
- the turnover and the camp;
- every game, rolled on engine seeds `seed + 2g` and `+1` by fixture ordinal;
- every bracket and tournament, and every accumulator.

S122 plays them in calendar order, and Phase 113 C2d proves the files come out byte for byte.

**This list is a claim, not yet an audit.** Phase B1's gate turns it into one (§7).

### 1d. What changes, and where it lives

Changing information already persists, through specialised records:
- people, in the logs (ruling 3);
- hidden state, in the scouting files (ruling B);
- contracts and scheduling memory, in the event records.

What is missing is **a home for a fact that changes and cannot be recomputed from history.** Prestige is the first such
fact. It is read only from the world file (`world.Schools[..].CurrentPrestige`), at **about 26 places on the season
path**:
- the roster draft and the freshman handout (`Program.Divvy.cs`, 10);
- the non-conference classes (6);
- the season and its page (5);
- seating (3);
- matching (1);
- calibration (1).

The world-building tools add 14 more. The career is bound to the world file's fingerprint, so prestige cannot move
without breaking the career.

---

## 2. What a save is

> **A save is the career file (v4).** It lists, by hash, every file the career stands on — including the league state
> for the last season boundary. It holds the counters and each season's rules record. Mid-season, it also holds the
> next day to be played, the dice segments and the integrity fingerprint. Everything else is rebuilt by re-play from
> the last boundary, and the re-play must reproduce the fingerprint exactly.

### 2a. The authority invariant

> **No calculation may read a career file that the active career file does not list and whose hash it has not
> verified.**

**It is enforced at the read boundary, not by cleanup:**
- On load, the verified file list becomes one object, `CareerFiles`.
- Every historical reader takes its paths from that object: `MteReadHistory`, `ReadCareerMemory`,
  `ReadLiveContracts`, `ReadCareerPeople`, `ScoutingFile.Read`, and the pages' `ReadCareerLogs`.
- **No reader builds a career path itself.**
- A listed file that is missing, or does not match its hash, is refused by name (Q6).
- An unlisted file is an orphan. It cannot be read, because no reader can reach it.

**Quarantine is housekeeping, not correctness.** On load, orphans are moved to `career.orphans/`. One that cannot be
moved (locked, read-only) is left in place and named in a warning; nothing can read it.

**This closes today's silent holes.** A past record can no longer vanish and quietly change next season's basketball.

Three more invariants:
- **A listed file is never modified.** Content files are written once.
- **Writing a new save never touches the files the current save lists**, so a failed write leaves the current save
  whole.
- **A season's content files are written only while that season is unlisted.** If an unmovable orphan holds one of
  their names, the season's close is refused by name (F6), and the career stays at its last save.

### 2b. Two kinds of save point

**A season boundary**, after a season closes:
- The career file lists that season's log, event record and scouting file, and the boundary's league-state file.
- The counters advance.
- It is a checkpoint: nothing before it is ever re-played.

**A day boundary inside a season.** The career file adds a `progress` block with three things:
- `nextDay` — **the first day not yet played.** Every day before it is fully played; it and every later day are
  untouched. A save always sits between two days, because the sim commands are day-sized. Before any game,
  `nextDay` is November 1.
- the dice segments (§5);
- the integrity fingerprint (§4e).

**No other file is written.** A mid-season save is one small atomic rewrite.

### 2c. Ids are provisional until the season closes

Today `HistoryStore.Reserve` publishes the counters on every call, before play. Under v4:

- The career opens with its counters. During a season, every `ReservePersons`, `ReserveSeason` and `ReserveGames`
  call **allocates from an in-memory copy** of those counters and publishes nothing.
- The order of reservations is fixed: the season, then every game (`NumberSeasonSchedule`), then persons (the turnover
  or the draft). So the ranges are a pure function of the counters at the boundary, and a re-play allocates the same
  ids.
- **Ids become authoritative at the season's close.** The advanced counters are published in the same career-file swap
  that lists the season's files. There is one publication point.
- "An id is never issued twice" becomes **"an id is never published twice."** An id that existed only in a crashed or
  failed run appears in no listed file and is never read.
- The career's first season follows the same rule: the bootstrap draft's persons are part of season 1's close.
- Tested by T-B1.5.

### 2d. The rules record

Every season carries a list of `(fromDay, configSha256, rulesStamp)` entries:

- **`configSha256`** is the SHA-256 of config.json as read.
- **`rulesStamp`** is the SHA-256 of the three local assemblies that can change how a career plays or is read:
  `Charm.Engine`, `Charm.History` and `Charm.Harness`. .NET's deterministic build makes these identical for identical
  source.
- **The stamp is provenance and a routing switch, not a compatibility proof.** Any code change alters it, including
  one that changes no basketball.
- The first entry is the season's start. **Under the strict Q8 the list never has a second entry**: a mid-season load
  whose config or stamp differs from the current build is refused, and the restart offer is made (§4d).
- **The list costs nothing now, and it is what lets the permissive rule be added later without a format change** (a
  format change would refuse every career, C-60). That is the only reason it is a list.

### 2e. What is deliberately NOT in a save

- Mid-season rosters, standings, accumulators, bracket progress, a tournament's seeded field, and the held log blocks.
  These are re-played and checked by the fingerprint.
- A decision journal for the player's in-season choices. There are none yet.

**The rule for later:** the session that adds the first in-season choice adds the journal, with a format bump. A choice
cannot be recovered by rolling the same dice (§8).

---

## 3. Why re-play rather than store everything

### 3a. The comparison

**Correctness.**
- *Re-play from the boundary (recommended):* the same code path, the same dice and the same order (S122) — **and
  checked** against the fingerprint (§4e).
- *Store everything (snapshot):* only as good as the serializer. Every live field must be written and read back bit
  for bit: 45 outcome properties, the league accumulator's dozens of counters and floating-point sums, every source's
  state, and the held blocks.

**A forgotten piece.**
- *Re-play:* nothing is stored, so nothing can be forgotten; a re-play that differs is refused.
- *Snapshot:* silent divergence, caught only if a test happens to exercise that piece.

**New features.**
- *Re-play:* a new system that is deterministic from (inputs, date, dice) re-plays at no extra cost.
- *Snapshot:* every new system must write and read its own state, forever.

**Save cost.**
- *Re-play:* one small file.
- *Snapshot:* the whole live state.

**Load cost.**
- *Re-play:* the season re-played up to the saved day — **at most one season** (§3c).
- *Snapshot:* near-instant.

### 3b. The limit, stated honestly

Re-play reproduces what the same code does with the same inputs. The fingerprint *proves* it did, for everything the
fingerprint covers (§4e). Neither says anything about **days not yet played.**

Under the strict Q8 that question never arises: a mid-season save resumes only on the same rules. Under the same build,
in fixed mode, an interrupted season and an uninterrupted one are identical. Phase B2 proves that.

### 3c. Load time — measured, and to be measured again

| Save point | What re-play does | Sandbox (stock, season one, measured twice) | Emmett's machine (estimate, ~2.1× faster on Phase 113) |
|---|---|---|---|
| any | set-up before the first game | about 6 s | about 3 s |
| early January (`nextDay` = Jan 1) | set-up plus 2,029 games | 15.6 / 16.6 s | about 7–8 s |
| late March, or at the championship | set-up plus 5,357 games | 31.2 / 32.9 s | about 15 s |

The sandbox plays about 210 games a second. A career's season two adds the turnover and the camp to the set-up.

To measure on the target machine (the month lines print as each month finishes):

```
dotnet run -c Release --project src\Charm.Harness\Charm.Harness.csproj -- season worlds\stock-d1.world.json 20260720 --history time-career.json | ForEach-Object { "{0:HH:mm:ss.f}  {1}" -f (Get-Date), $_ } | Select-String "Regenerating|\.\.\. |STANDINGS"
```

**The bound grows** with the engine, and with more divisions (D2, D3, JUCO). When it starts to hurt, a snapshot is the
speed-up — and re-play stays its correctness reference. That is the right order to build the two in.

---

## 4. Writing a save, and what a crash leaves

### 4a. The career file's own integrity

The career file is UTF-8 JSON with LF line endings, written by the history writer's canonical serializer (2-space
indent, fixed key order — the S119 golden's rules). A final line follows it:

```
sha256:<64 lowercase hex>\n
```

That hash is taken over **the exact bytes before that line, as read** — never over a re-serialization.

A file whose last line is missing, or whose hash does not match, is **torn**. This check is independent of the hashes
the file holds for other files.

### 4b. The three ways to publish

- **Creation (a new career; no `career.json` yet).** Write `career.json.new`, flush, then move it to `career.json`
  without overwriting (`File.Move`, overwrite off — this fails if a file is already there). There is no `.bak` yet.
- **An ordinary save (a verified `career.json` exists).**
  1. Write each new content file under its final name — a name no listed file holds (§2a) — flush it, and compute its
     SHA-256.
  2. Write `career.json.new` and flush it.
  3. `File.Replace(career.json.new, career.json, career.json.bak)`.

  `File.Replace` overwrites `.bak` with the file being replaced. **So the invariant is: `File.Replace` is called only
  when the current `career.json` was verified by this process** — loaded, checked and trusted. The backup is therefore
  always the last good save. Season close uses steps 1–3 with the season's log, event record (written once, final —
  no NotPlayed stage), scouting file and league-state file. A mid-season save is steps 2–3 alone.
- **Recovery (ChatGPT RED 1, r2).** This applies when `career.json` is missing or torn and `career.json.bak` is
  verified.
  1. Move the torn `career.json`, if there is one, to `career.orphans/` as evidence.
  2. Copy `.bak` to `career.json.new`, flush it, and verify the copy's bytes and hash.
  3. Move `career.json.new` to `career.json`, without overwriting.

  **`.bak` is never touched.** Afterwards both files are whole and identical, so a sound fallback remains. The next
  ordinary save then replaces `.bak` with this verified primary. The recovery is reported on screen. If `.bak` is also
  missing or torn, the load is refused by name, and nothing is written.

  `career.json.new` is **never authoritative.** Any existing `.new` is overwritten before use and ignored on load.
  So a crash at any point in recovery leaves either the torn primary or no primary, with `.bak` intact. The next load
  runs the same recovery again.

### 4c. Loading

1. Read and verify `career.json` (§4a). If it is missing or torn, recover from `.bak` (§4b).
2. Verify every listed file's hash, and build `CareerFiles` (§2a).
3. Quarantine orphans (housekeeping).
4. If the career is mid-season: check the rules (§4d), re-play from the boundary to `nextDay` using each day's dice
   (§5), then check the fingerprint (§4e).
5. The career is ready.

**The lock** is held from load until the process ends. Today it is released before the simulation, which is harmless
only because a second run refuses on a missing log.

### 4d. The three guards

**1. The format version** of the career file and of each listed file. An older format is refused with the C-60
sentence (Q3).

**2. The rules check (§2d; Q7 and Q8 strict).**
- **At a season boundary:** a different config or stamp is allowed (Q7). The next season starts its own rules record.
- **Mid-season, same config and stamp:** re-play, then the integrity check.
- **Mid-season, different config or stamp:** refused before re-play, with the offer to **restart season N from its
  first day under the current rules.** Accepting drops the `progress` block — the season's games so far are lost, and
  nothing else is. In live mode the season keeps its recorded seed. The offer is a choice for the player, never taken
  silently.

**3. The integrity fingerprint (§4e).** Under the same rules, a mismatch is a bug in the build. It is refused by name,
naming the first differing section, with the same restart offer.

### 4e. The integrity fingerprint — a versioned canonical contract

**Scope: a save resumes on the platform it was made on** (Windows x64, Emmett's). Doubles go into the fingerprint as
raw IEEE-754 bits, so it is strict. A save carried to another operating system resumes only if its re-play matches bit
for bit. Otherwise it is refused, never continued wrong. (S81.3: a few math functions are not bit-identical across
platforms. Phase 113 pins only whole numbers across platforms, for that reason.)

**Construction.** SHA-256 over this byte sequence, UTF-8, LF line endings, invariant culture:

```
charm-season-integrity v1\n
setup|<hex>\n
game|<ordinal>|<hex>\n          one line per played game, in PLAY ORDER (date, then ordinal)
sources|<hex>\n
```

Each `<hex>` is a lowercase SHA-256 of a byte string defined below.

**Where a byte form already exists, the fingerprint reuses it, never a second encoding:**

- **`setup`** is the SHA-256 of the concatenation, in this order, of:
  1. the log's **roster section bytes**, exactly as `GameLogWriter` serializes them (persons ascending; ratings as
     shorts; scout rank and defensive plane as raw double bits — the log's own encoding, already versioned);
  2. the **scouting file's body bytes**, exactly as `ScoutingFile` serializes them (doubles already written as raw bits);
  3. the **event record's NotPlayed body bytes**, exactly as `MtePublishRecord` would write them (seating, contracts and
     the pairing log, in its own canonical form);
  4. a canonical text block of: the dated-schedule fingerprint, the non-conference-dates fingerprint and the
     buy-games-plan fingerprint (existing hashes); the season id; the first person id and game id; and the counts of
     each.
- **`game|<ordinal>|<hex>`** — `<hex>` is the SHA-256 of the **game block bytes** exactly as `GameLogWriter.AppendGame`
  encodes them: facts, score by period, the rows sorted by person, the block checksum. Phase B2 factors the encoder
  out of the writer, so the fingerprint and the file can never encode a game differently.
- **`sources`** is the SHA-256 of a canonical text block — the only new encoding:
  - one line per bracket event, in `(tier, eventId)` order: occupancy slot by slot, and placings by seat ascending;
  - one line per conference tournament, in conference-id order: `unseeded`, or the seeded field and occupancy by game
    index;
  - showcase results in `(event, game index)` order;
  - buy-game rows in pairing order.

  Its rules: `|` separates fields; integers are in decimal; `-` means null; booleans are `0` or `1`; dates are
  `yyyy-MM-dd`; **every dictionary is emitted sorted by key, and no `Dictionary` or `HashSet` is ever enumerated
  unsorted.**

**Left out, on purpose:** the league accumulator's page-only figures (floating-point calibration sums). Nothing reads
them to decide anything (S122 A5). The B2 gate re-verifies that.

**The version line** is bumped whenever any part of this definition changes. A save carrying an older version is
refused with the restart offer.

**The full field list is a B2-gate deliverable**, written as a table in the B2 prompt. The rules above are fixed now.

**Cost.** Each game's block hash is computed once, as it is played, from bytes already in hand. A save hashes about 30
lines. Measured at the B2 gate.

---

## 5. Dice (O-132) — Phase B3

### 5a. Segments are half-open date intervals

A season's dice are a list of segments, each written `(startDay, seed)`. Segment `k` covers the days
`[startDay_k, startDay_{k+1})`, and the last segment runs to the season's end.

**Every calendar day, with or without games, belongs to exactly one segment.** Segment 0 starts on the season's first
day, and its seed is the season seed.

### 5b. What uses a segment

A game with fixture ordinal `g`, played on day `d`, rolls `seed_k + 2g` and `seed_k + 2g + 1`, where segment `k`
contains `d`. **An uninterrupted season has one segment, so it is exactly today's.**

Pre-season randomness — seating, the schedule, the draft, the turnover and the camp — stays on the season seed.

### 5c. Live mode

- The season seed is drawn fresh when each new season starts.
- **Each load of a mid-season save draws a pending segment seed**, starting at `nextDay`. A pending seed is **not yet
  in the save and not yet used by anything.**
- **It becomes recorded only when a day is played under it.** The next autosave (Q2) writes it into the save along with
  that day's results.
- Load, then quit without playing: the pending seed is discarded, and **the save is unchanged.**
- Load twice without playing: two pending seeds are drawn, and neither is ever recorded.
- Save immediately after loading, without playing: the new save is byte-identical to the loaded one.
- **A zero-length segment is never recorded.**
- **A segment may start on a day with no games.** It starts at `nextDay` whatever that day holds, and covers that empty
  day and every day until the next segment.
- **A crash mid-command loses the unsaved days, and their seed with them.** Nothing unsaved is history; on reload those
  days re-roll, which is the save-scum rule. **A re-play only ever uses recorded segments.**
- Seeds come from the operating system's random source (`RandomNumberGenerator`), 32 bits each, as today's season seed
  is. Two equal seeds are possible but harmless: they re-create a roll that could have happened anyway.
- **Different seeds do not promise different results.** The promise is "re-rolled", not "unique" — and the tests test
  the draw, not the scores (T-B3).

### 5d. Fixed mode (the suite)

The season seed comes from the career's master seed: season k = master + k − 1, today's `SeasonTwoSeed` convention.
**No new segment is ever drawn.** An interrupted-and-resumed season is therefore identical to an uninterrupted one.

The mode is set when the career is created, and recorded. A fixed-mode career is a test career.

### 5e. A rule for every future in-season system

Practice, injuries and in-season recruiting draw their randomness keyed by **(the day's segment seed, date, purpose,
who)** — never from a running stream shared across days. That keeps re-play exact, and keeps results independent of
the order things happen within a day.

---

## 6. The world and the league state

**The world file is the starting universe.** It is never rewritten. Its fingerprint stays the career's lineage binding.

**The league state is the universe now.**
- There is one file per season boundary, `career.league/league-N.json`, listed by hash.
- `league-0` is written at creation, from the world.
- `league-N` is written at season N's close.

**The rule for what belongs in it:** *a fact goes in the league state if and only if it changes during a career and
cannot be recomputed from the world plus the listed history files.*
- **Prestige** qualifies, once it moves.
- **People** do not — they live in the logs (ruling 3).
- **Hidden state** does not — it lives in the scouting files (ruling B).
- **Contracts and scheduling memory** do not — they live in the event records.
- **Coaches, recruiting boards and injuries** will, in their own sessions.

**Two copies of one fact must never be able to disagree.**

**Staging (ChatGPT r2, accepted).**
- **The file and its place in the career file land in B1.** Its one field is prestige by school, copied from the world.
  Until C, nothing reads it to play, **and B1 asserts at every load and every close that it equals the world's prestige
  for every school**, refusing by name otherwise. Two copies exist briefly, and they cannot disagree.
- **Phase C** reroutes the about 26 season-path readers (§1d) and retires the equality assertion. The values are
  identical, so no game moves. A test-only prestige change then proves that every career-mode reader follows the
  league state (T-C).

---

## 7. The migration path

Five sessions, each a separate audited pass (CONVENTIONS §6), and **each with its own behavioral contract and its own
proof** (ChatGPT r2, accepted). The risk ratings are ChatGPT's.

### Phase A — the runner becomes a stoppable object

*Structural. Moderate risk. Accepted in principle by the outside review.*

**What changes.**
- `RunSeasonCore` splits into a **setup** and a **run** object.
  - The **setup** is everything before the first game, unchanged — including what it writes: the same files, at the
    same moments, with the same ids.
  - The **run** object owns what the `PlayOneGame` closure captures today: the slots, the accumulators, the sources and
    the counters.
- The six test switches become one options record.
- **`RunSeasonCore` stays as a wrapper** (setup, then `AdvanceThrough(last day)`, then `Finish`). The 89 call sites in
  the suite do not change.
- **Nothing written to disk changes.**

**The contract.**
- **`NextDay`** — the first day not yet played. At creation, it is the season's first legal day.
- **`AdvanceThrough(day)`** plays every day in `[NextDay, day]`, then sets `NextDay = day + 1`.
  - Empty days advance `NextDay` without playing anything.
  - `day == NextDay − 1` is a no-op.
  - `day < NextDay − 1` (backwards) is refused by name.
  - `day` past the last legal day is refused by name.
- **`Finish()`** is legal only when `NextDay` is past the last legal day. Before that, twice, or followed by another
  `AdvanceThrough`, it is refused by name.
- **`IsComplete`** reports whether the season is done.

**Proof (Phase 114).**
- Every pin, and Phase 113 in full.
- **Stop and continue in one process:** the same season advanced in one call, day by day, week by week, in random-sized
  chunks, and with repeated no-op calls, gives an identical outcome and identical files.
- The four refusals, each by name.

### Phase B1 — the career file is the authority, and saves are safe

*Persistence. High risk.*

> ★ **Split into three sessions (Emmett, 2026-10-12, at the S123 next-prompt pass)**, because B1 as written bundled a new
> career-file format, the read boundary, crash recovery and about fifteen rewritten checks into one session:
> - **B1a — nothing is written until the season closes. ✅ Shipped S124** (journal S124; §14i). Covers "All season files
>   written at the season's close", "Provisional ids", the retirement of the NotPlayed pre-play record and the pre-play
>   scouting write, and the contract's "an interrupted season leaves the career at its last boundary, so the season
>   simply restarts from its first day". Its proof is Phase 115 as S124 built it, not §10's B1 list.
> - **B1b — the career file becomes trustworthy. ✅ Shipped S125** (journal S125; §14j): v4 with its checksum and file
>   list (§4a, §14c), the three ways to publish (§4b), the backup and recovery, the rules record (§2d — the record only;
>   §4d's check is B2's), the league-state file (§6), the C-60 refusal of v3 careers. Its proof is Phase 116.
> - **B1c — nothing reads a file the career does not list. ✅ Shipped S126** (journal S126; §14k): one read door on the
>   store for every reader, quarantine at every open (§2a, §4c), the close's names check; S124's set-aside retired (§14i).
>   Its proof is Phase 117. **B1 is finished.**

**The gate, before any code:**
- **A formal input audit**, as a table:
  - every value a season reads;
  - every seed derivation;
  - every place iteration order could matter — dictionary enumeration on the season path, and `OrderBy` without a
    full tie-break;
  - every static mutable field in `Charm.Harness` and `Charm.History`, such as `HistoryStore._idSource` and config
    caches;
  - every wall-clock read (Phase 82 A11 already scans for these);
  - every path a reader builds itself.
- Anything outside §1c is fixed before the build.

**What is built:**
- Career file v4 with its own checksum (§4a).
- The three ways to publish (§4b).
- `CareerFiles`, quarantine, and the lock (§2a, §4c).
- Provisional ids (§2c).
- The rules record and the boundary rules check (§2d, §4d).
- The league-state file, as the baseline with its equality assertion (§6).
- All season files written at the season's close. The NotPlayed pre-play record and the pre-play scouting write are
  retired.
- The C-60 refusal of v3 careers.

**No mid-season loading yet.**

**The contract.** Completed seasons publish atomically. An interrupted season leaves the career at its last boundary,
**so the season simply restarts from its first day**: the career-ending bug is fixed here. No calculation reads an
unlisted file.

**Proof: Phase 115** (T-B1, §10).

**Pins.**
- The final event record, every log and every game stay at today's values (Phase 113 C2a, C2b and the wall).
- The career file's bytes change by design, so **Phase 113 C2c is re-pinned under ruling**, with its old value
  recorded.

### Phase B2 — a season resumes on the day it stopped (fixed dice)

> **★ TO BE REVISED FOR Q1 (C-68).** This section, and the re-play loading it rests on (§2b's `progress` block, §2e,
> §3, §4c step 4), describe r3's design. Q1 ruled **store everything**: B2 is redesigned around a **snapshot** — its
> schema, shared references restored (`resultSlots` is one list the tournaments share), live source state, resources
> rebound — **with re-play kept as the suite's oracle**: a snapshot restored in a fresh process must finish identical to
> an uninterrupted season. The starting point is Phase 114's state inventory (§14h). The revision is the B2 draft's job.

*High risk.*

**The gate:**
- The integrity fingerprint's full field list (§4e).
- Re-verify that the accumulator is page-only.
- Measure the hash's cost.

**What is built:**
- The `progress` block (§2b).
- Re-play to `nextDay` (§4c).
- The integrity fingerprint, with the encoder factored out of the writer (§4e).
- The mid-season rules check, strict, with the restart offer (§4d).
- One command, `advance <career> --through <date>`, which the cross-process tests drive.

**Fixed mode only.**

**The contract.** In fixed mode, an interrupted-and-resumed season, across processes, is byte-identical to an
uninterrupted one. A re-play that differs in anything the fingerprint covers is refused by name.

**Proof: Phase 116** (T-B2).

### Phase B3 — fresh dice, autosave, and the sim commands

*Moderate risk.*

**What is built:**
- Dice segments, with live and fixed modes (§5).
- Autosave after each sim command (Q2).
- The commands: sim a day, a week, a month, and to the next game or event (the next date holding a game, or the next
  dated season event), plus season end.

**The contract.**
- A recorded segment re-plays exactly.
- A pending seed never reaches the save until a day is played under it.
- Fixed mode is unchanged.

**Proof: Phase 117** (T-B3).

### Phase C — prestige is read from the league state

*Low-to-moderate risk.*

**What is built:** the about 26 season-path readers are rerouted (§1d). The equality assertion is retired.

**The contract.** In career mode, every prestige consumer reads the league state; legacy mode reads the world. No game
moves.

**Proof: Phase 118** (T-C).

**Then O-133**, the calendar runs the year, on the same re-play and boundary model. **Whether the checkpoint boundary
moves from season close to July 1 is decided there**, not here.

---

## 8. Minimum versus future

| Phase | Builds | Why it is needed |
|---|---|---|
| A | the stoppable runner and its contract | resumption needs a runner that can stop |
| B1 | the authority, safe publication, provisional ids, the read boundary, the rules record, the league-state file (baseline) | **ends the career-stranding bug**; the format is complete from day one (r1 RED 2) |
| B2 | `progress`, re-play, the integrity fingerprint, strict rules, `advance --through` | resume mid-season |
| B3 | dice segments, autosave, the sim commands | rulings Q2 and Q4 (O-132) |
| C | prestige read from the league state | needed before prestige movement, coaching or recruiting |

**Future only — do NOT build now:**
- storing everything for fast loads;
- the decision journal (no in-season choices exist yet);
- the permissive Q8;
- the log in play order (Q5);
- pages over an in-progress season;
- the offseason calendar (O-133);
- moving code out of `Program`.

---

## 9. Failure scenarios

| # | What happens | Expected | Phase · test |
|---|---|---|---|
| F1 | a crash mid-simulation | B1: the season restarts from its first day. B2 onward: the last save loads, and the days since it are lost (in live mode, they re-roll) | B1·3, B2·2, B2·3 |
| F2 | a crash while writing a season's content file | the old career file still governs; the partial file is unlisted, never read, and quarantined | B1·3 |
| F3 | a crash after every content file is written, before the swap | same as F2 | B1·3 |
| F4 | a crash during the swap, or a torn `career.json` | recovered from `.bak` (§4b) with `.bak` untouched; both files are whole afterwards | B1·4 |
| F4b | `career.json` and `.bak` are both torn | refused by name; nothing written | B1·4 |
| F5 | a listed file is missing or changed | refused by name, naming the file (Q6) | B1·4 |
| F6 | an orphan holds a season file's name and cannot be moved | the load goes ahead (no reader can reach it, and a warning names it); that season's close is refused by name; the career stays at its last save | B1·3 |
| F7 | a second process opens the career during play | refused: the lock is held | B1·6 |
| F8 | the rules changed at a season boundary | allowed (Q7); the next season records its own rules | B1·7 |
| F9 | the rules changed mid-season | refused before re-play, with the restart offer (Q8 strict) | B2·5 |
| F10 | a crash during the season's close | B1: the season restarts. B2: the save still says "in season, `nextDay` = last day + 1"; re-play, then close again; fixed mode gives identical files | B1·3, B2·2 |
| F11 | a career from before v4 | refused with the C-60 sentence (Q3) | B1·8 |
| F12 | a save on the day a conference tournament opens, mid-bracket, or on an empty day | the re-play re-seeds and re-fills exactly as an uninterrupted season | B2·2 |
| F13 | a failed close, then a successful one | no published id is shared by two entities; every id equals the uninterrupted run's | B1·5 |

---

## 10. The tests, by phase

**Every equivalence test runs in fixed mode. From B1 on, every resume runs in a fresh process**: the harness launches
itself (`Environment.ProcessPath`), so no static or in-memory state can carry across. **Every comparison uses final
files byte for byte, and, from B2 on, the full integrity fingerprint** — never scores alone.

### Phase A (Phase 114)

The proof in §7 A.

### Phase B1 (Phase 115)

1. **No change.** Two completed career seasons through the new path give today's games and files (the wall; Phase 113
   C2a and C2b). Phase 113 C2c is re-pinned.
2. **The read boundary.** Every listed reader runs off `CareerFiles`. **Negative control:** a test-only reader that
   builds its own path reaches a planted orphan, and the read boundary's reader does not.
3. **Crash injection.** A test-only fault is thrown:
   - after each content file;
   - before the swap;
   - during the close;
   - with an **unmovable orphan** holding a season file's name.

   Each time, the previous boundary governs and nothing unlisted is read. The season is re-run from its first day and
   finishes identical. In the unmovable-orphan case, the close is refused by name until the orphan is cleared.
4. **Recovery.**
   - A torn `career.json` is recovered from `.bak`. **Afterwards `.bak` still verifies and both files are identical.**
     A torn primary is the case where this matters: the r2 sequence would have overwritten `.bak` with it, and this
     must not.
   - The next ordinary save makes a new `.bak` from the verified primary.
   - When `.bak` is torn too, the load is refused by name and nothing is written.
   - A missing or changed listed file is refused by name.
   - **Control:** an untouched career loads.
5. **Ids.**
   - Every id is identical to the uninterrupted run.
   - After a failed close and a successful one, no published id is shared by two entities, and an id from the failed
     run appears in no listed file.
6. **The lock.** A second process during play is refused.
7. **Rules at a boundary.** A config change between seasons loads, and the next season records its new rules.
8. **v3.** A v3 career is refused by the C-60 sentence.
9. **The league-state baseline.** It equals the world, school for school. A test-only edit to the file is refused at
   load by the equality assertion.

### Phase B2 (Phase 116)

1. **No interruption, no change** (as B1·1).
2. **Interrupt at every day.**
   - On fixture-mte (391 games): save with `nextDay` = d and resume in a fresh process, for **every** day of the
     season, empty days included. Each finished season is byte-identical to the uninterrupted one.
   - On stock, at points that stress state:
     - before the first game;
     - after the first day;
     - an ordinary run of league games (a mid-February day with no tournament);
     - an empty day mid-season (Christmas);
     - mid-bracket, between rounds;
     - the day before a conference tournament opens;
     - the day it opens;
     - mid-tournament;
     - after the last game, before the close.
3. **Chained.** Save, resume, save, resume … through one stock season, about eight cuts: identical.
4. **Two seasons.** Interrupt season two in mid-January, with the camp, the turnover and the scouting file in play:
   identical through the start of season three.
5. **The guards.**
   - A changed config, and a changed stamp, are each refused before re-play with the restart offer.
   - Accepting the offer re-plays the season from day one under the new rules.
   - A test-only switch that changes **one man's assist in one played game, leaving every score and possession count
     unchanged**, is refused by the integrity fingerprint, which names the `game` section.
   - A test-only change to one man's rating, on a save made before the first game, is refused, naming `setup`.
   - **Control:** no change loads.
6. **The encoding is shared.** For every played game, the fingerprint's block hash equals the SHA-256 of the bytes the
   finished log holds for that block.
7. **Negative controls for B2·2.** Each of these must fail by name:
   - a tournament re-seeded from the national records rather than the day's;
   - one dropped game;
   - one bracket slot filled wrongly.

### Phase B3 (Phase 117)

1. **Fixed mode is unchanged** (B2·2, re-run on a sample).
2. **Segments.** In live mode:
   - Save and load on a day with no games.
   - Load twice without playing: **the save is unchanged and nothing is recorded; the two pending seeds were drawn
     independently** (asserted on the seeds, not the scores).
   - Save immediately after loading, without playing: the new save is byte-identical to the loaded one.
   - Three recorded segments in one season, re-played in a fresh process: every played game is reproduced exactly,
     under the full fingerprint.
   - Before and after each load, every game before `nextDay` is identical.
   - **Page-only:** how many games differed after a re-roll.
3. **The commands.** Day, week, month and next-event each advance to the date they name, and each autosaves.
4. **A control.** A test-only re-play that uses a pending (unrecorded) seed fails the fingerprint.

### Phase C (Phase 118)

1. **No change.** Every pin is unchanged.
2. **Every consumer follows the league state.** A test-only prestige change written at a boundary moves **each**
   career-mode consumer, each asserted on its own:
   - the draft and the freshman handout;
   - the non-conference classes;
   - matching;
   - seating;
   - the season page;
   - calibration.

   The change survives a save and a reload.
3. **Legacy mode still reads the world.**
4. **A control.** Without the change, every consumer matches the baseline.

**Runtime budget.** The every-day sweep runs on the small world only, and the stock cuts are chained in one career. B2
adds about three to four seasons of re-play, estimated at **+2 to +4 minutes** on Emmett's machine. B1, B3 and C each
add under two minutes. Each is measured in the sandbox and stated in its prompt.

---

## 11. Risks and limits

- **Load time** grows with the engine and with more divisions (§3c). Watch it; a snapshot is the answer when it is
  needed, not before.
- **Phase A's hidden captures.** The closure captures about 15 mutable locals. One left behind as a local would pass a
  one-call run and fail a chunked one. The chunked test is designed to catch exactly that.
- **`File.Replace` on Windows** is the call for an ordinary save, and §4b keeps it away from recovery. Its behaviour in
  a cloud-synced folder (OneDrive) is not assumed.
- **Determinism is load-bearing.** Anything that reads a wall clock, a dictionary's order, or an unkeyed shared random
  stream breaks re-play. B1's audit finds today's cases. The integrity guard refuses tomorrow's, by name, at load.
- **The strict Q8 refuses every mid-season save after any build change**, including a refactor that changes no
  basketball. The restart offer is the remedy. The permissive rule is a later decision, and the format already allows
  it (§2d).
- **Out of scope:** recruiting, transfers, coaching, injuries, the offseason calendar, any UI, any move of code out of
  `Program`, any change to the possession engine, and the log format.

---

## 12. Questions for the next review

1. Is the recovery sequence (§4b) safe at every step, including a crash between moving the torn primary aside and
   moving `.new` into place? (§4b's last paragraph argues it is: `.bak` is never written during recovery, and `.new`
   is never trusted.)
2. Is reusing the writers' own byte forms for the fingerprint (§4e) sufficient as the canonical contract, with only the
   `sources` block newly encoded?
3. Is "a save resumes on the platform it was made on, and anywhere else only if it re-plays bit for bit" an acceptable
   scope?
4. Does each phase's contract (§7) stand on its own, so that any one phase could be the last one shipped without
   leaving the career unsafe?

---

## 13. What changed

### Review round 2 (ChatGPT's audit of r2, 2026-10-11)

| Item | r2 | r3 |
|---|---|---|
| **RED 1** — recovery destroys the backup | `.bak` copied to `.new`, then `File.Replace` | recovery never calls `File.Replace`: the torn primary is moved aside, the copy is moved in without overwriting, and `.bak` is untouched. `File.Replace` only ever replaces a primary verified by this process (§4b). B1·4 asserts `.bak` is sound afterwards |
| **RED 2** — the fingerprint's encoding | conceptual | a versioned byte contract: a version line; sections in fixed order; games in play order; the log's own block and roster bytes, the scouting file's and the event record's — never a second encoding; one new canonical block for tournament state; doubles as raw bits; dictionaries sorted (§4e). Scope: same platform, refused elsewhere if different. The full field list is a B2-gate deliverable |
| The career file's own checksum | undefined | a final `sha256:` line over the exact bytes before it (§4a) |
| The first save | `File.Replace` throughout | creation is a no-overwrite move (§4b) |
| The rules stamp | Engine and Harness | Engine, History and Harness; stated as provenance, not proof (§2d) |
| Repeated rules changes | one `rulesChangedOn` | a per-season list of rules entries. Under the strict Q8 it has one entry; it is a list only so the permissive rule needs no format change (§2d) |
| **Q8** | permissive recommended | **strict** recommended for the first release, with a restart-the-season offer (§0, §4d) |
| Prestige coverage | "about a dozen sites"; one test on seating | about 26 season-path reads, counted (§1d). Phase C's test asserts every consumer separately (§10 C·2) |
| The live-dice test | "the next day differs" | asserts that the seeds were drawn independently; scores are page-only (§10 B3·2) |
| **Staging** | A and B, two sessions | **A, B1, B2, B3, C**, each with its own contract and proof (§7). B1 alone ends the career-stranding bug |
| The league state | a full reroute in B | the file and its place in the career file in B1, held equal to the world by assertion; the reroute in C (§6) |

### Review round 1 (ChatGPT's audit of r1, 2026-10-11)

| Item | r1 | r2 (carried into r3) |
|---|---|---|
| RED 1 — the fingerprint | six fields per game | everything the season publishes, plus tournament state |
| RED 2 — the league state | a phase after v4 | in the v4 format from the start |
| RED 3 — segment boundaries | "at the saved day" | `nextDay`; half-open intervals; pending segments; no zero-length segment |
| RED 4 — compatibility | one config check | format version, rules record, integrity fingerprint; Q7 and Q8 |
| Ids, `.bak`, orphans, seeds, load time, the runner's API, the input audit, the cut points | — | as r2 §13 |

---

## 14. Addendum r3.1 (ChatGPT's audit of r3, 2026-10-11)

The audit approved the architecture and asked for a short addendum rather than a rewrite. This section amends §2, §4,
§6, §7 and §10 where it says so; everywhere else r3 stands.

### 14a. The contract-migration ledger (RED — must be settled before B1)

B1 deliberately changes three existing rules:

- **Ids are durable when reserved** → ids are provisional until the season closes (§2c).
- **The event record is written twice** (NotPlayed before play, finishes after) → it is written once, at close.
- **A missing or unreadable earlier record is a silent hole** → a listed file that is missing or changed is refused at
  load (Q6), so no reader ever meets a hole in a v4 career.

The suite enforces the old rules on purpose. Below is every check found that does. It was built by searching the check
files for the reservation, record, log, scouting and history APIs at `65773fe`.

**The B1 gate re-verifies it.** Run the suite on the B1 build and give every red check a row. **No red check is edited,
re-pinned or deleted unless its row says why.** A red check with no row is a bug in the build.

**Kind 1 = still required as written. Kind 2 = replaced, because the rule changed on purpose. Kind 3 = kept as the
contract of a low-level API that survives.**

| Check | What it enforces today | Kind | Under B1 |
|---|---|---|---|
| ✅ **S124** Phase 80 B1 (non-reuse, high-water, other counters still) | 17 ids across three batches and a reload; the stored next-person is 18 | **2** | Rewritten for the two-stage rule. (a) Allocate 17 in three batches **and close**, reload: the next allocation starts at 18, and the other two counters are unmoved. (b) **New:** allocate 5 **without** closing, reload: the same 5 are allocated again, and none of them appears in any listed file. |
| ✅ **S125** Phase 80 B7 (a reservation near the ceiling; a zero reservation writes nothing) | `Reserve` arithmetic and its no-op | **2** | The same arithmetic, applied to the in-memory allocation. "Writes nothing" now holds for every allocation until close. |
| ✅ **S125** Phase 80 B10 (born as a valid history; the canonical output equals `history_v3_golden.json`; validation leaves the file byte-identical; the world untouched; a different world refused; a held lock refused) | the v3 file format and lifecycle | **2** for the golden; **1** for the rest | A **hand-written v4 golden** replaces the v3 one, as S119 did: payload plus checksum line (§14c). A v3 file is refused by the C-60 sentence. The world, lock and different-world assertions stand unchanged; the lock assertion is extended to "held for the whole run" (§4c). |
| ✅ **S125** Phase 80 B4, B8, B9, B11 | identity uniqueness, isolation, pool slots, no file without a history | **1** | Unchanged. |
| ✅ **S124** Phase 105 C2b, C3c (the high-water after season one is 4,512; freshmen move it by the class size) | counters after a completed season | **1** | Unchanged — published at close, with the same values. |
| ✅ **S126** Phase 105 C6a (the previous season kept no log → refused, nothing spent) | a season can exist without a log | **3** | The test-only path that runs a career season without a log (`bootstrapPeopleForTest`, which the schedule, event, contract and memory checks rely on) is kept: such a season is published with a `noLog` flag that only that switch can set. The refusal for a real season after a `noLog` one stands, with its message. **New beside it:** a listed log deleted from disk is refused at load by name (Q6) — a different, earlier refusal. |
| ✅ **S125** Phase 105 C6b (history mode without the retained log is refused) | the log is mandatory | **1** | Unchanged. |
| ✅ **S125** Phase 81 A10 (an existing finished log refuses, with no artifact left behind) | the log writer never overwrites | **3** | The writer keeps this refusal. B1 reaches it only through an unmovable orphan, and then the close is refused (F6, B1·3). |
| ✅ **S125** Phase 81 A5, A9, A11, A1, A2 | the log's own format and integrity | **1** | Unchanged. The log format does not change (Q5). |
| ✅ **S124** Phase 112 C4 (the scouting round trip; an existing file is never overwritten) | the scouting file's writer | **1 / 3** | The round trip is unchanged. "Never overwritten" stays the writer's own contract; B1 writes the file at close instead of before play. |
| ✅ **S124** Phase 89 C8a, C8c (finishes persisted; dormant events still NotPlayed) | the final record | **1** | Unchanged bytes (Phase 113 C2b pins them). |
| ✅ **S124** Phase 89 C8e–C8h (the replacement refuses a Completed record or mismatched seats, and leaves the file byte-untouched) | the finalizer's validations, run against the file on disk | **2** | Same validations, new input. The finalizer becomes a pure function over the **in-memory NotPlayed body** (the bytes `MtePublishRecord` would have written), §14e. The four refusals are asserted on constructed in-memory bodies; "byte-untouched" becomes "nothing written". |
| ✅ **S124** Phase 89 C8i (a failure forced at the rename leaves the NotPlayed record) | a half-finished season keeps a NotPlayed record | **2** | Replaced by B1·3: a failure at close leaves the career at its last boundary and the season re-runs. There is no NotPlayed record any more. |
| ✅ **S124** Phase 88 C7a (a record write failing after the commit does not invalidate the season) | `WriteFailed`: the season stands, with a hole | **2 — the rule reverses** | A failed close does not publish the season; it re-runs from day one. `EventRecordStatus.WriteFailed` and its page line are retired. |
| ✅ **S124** Phase 88 C5b, C5c (a refusal spends no season id and writes no record) | refusals are free | **1** | Stronger now: nothing is published before close. |
| ✅ **S126** Phase 88 C9a–C9c (an existing record for the pending season refuses the commit and is never mistaken for this run's) | `MteRefuseExistingRecord` | **2** | An unlisted record for the pending season is an orphan: quarantined at load, unreachable by any reader, and refused at close only if it cannot be moved (F6). The refusal's message moves to the close. |
| ✅ **S126** Phase 88 C6d–C6i (another career's record, a different season's, an unsupported version, malformed JSON, or a deleted year is each a **silent hole**; a changed world fingerprint is accepted) | how the record reader treats a gap | **3** | **Unreachable in a v4 career**: a listed file is verified at load, and an unlisted one cannot be read. Kept as the reader's own contract — B1 changes no reader's internals — and driven by constructed `CareerFiles` with gaps. **New beside them:** the same gaps in a real career are refused at load (B1·4). Removing hole tolerance from the readers is a later cleanup, not B1. |
| ✅ **S126** Phase 90 (rotation) C2a-iv, C5a–C5c; Phase 91 (host debt) C1d, C3b–C3c; Phase 94 (contracts) C10b, C10e, C8b/C10d; Phase 87 (season memory) C8iii | holes, damage and forgotten records in the 8-season window | **3** | As the row above: unreachable in a v4 career, kept as the readers' contracts, driven by constructed `CareerFiles`. |
| ✅ **S124** Phase 87 C9c (peeking moves nothing on disk, and the peek is the next reservation) | peek equals the next issue | **1** | Unchanged: the peek equals the next provisional allocation, which equals the next published id. |
| ✅ **S124** Phase 89 C10b, C10c (the ledger spends exactly the counted ids; dormant events spend none) | counts | **1** | Unchanged. |
| ✅ **S124** Phase 110 (the season after 9998 is refused with nothing written) | edge refusals | **1** | Unchanged. |
| ✅ **S125** Phase 113 C2c (the career file after two seasons is the S121 build's, byte for byte) | v3 bytes | **2** | Re-pinned to the v4 bytes under ruling, with the old value recorded (§7 B1). C2a, C2b and C2d are unchanged. |

★ **Marked after S124 (B1a; §14i).** ✅ = settled by B1a as the row says (Phase 80 B1 publishes where the row says
"close"; C8i is replaced by Phase 115 C2's interrupted closes rather than a "B1·3"). ◐ = half settled: Phase 88 C9's
planted record is set aside **at the close**, byte-untouched, into `career.orphans/` — the quarantine at load is B1c's.
Unmarked rows are B1b's or B1c's. **Rows B1a found that this ledger did not list:** Phase 87 C2c (a hand-spent season
reopened by C2d/C2e) and Phase 89 C7 (a hand-built season reopened to read host memory) — both now publish before
closing; Phase 114 C2i, C2l, C4d–C4h (the pre-play files the S123 build wrote, and the season log's lock) — rewritten;
Phase 114 C2f — compared a folder with the career lock still held.

★ **Marked after S125 (B1b; §14j).** ✅ = settled by B1b. B10's golden is `tools/history_v4_golden.json` (hand-written, with
`tools/league_v1_golden.json` for the league-0 it lists); v3 joins v1 and v2 in the refused set; two torn cases added.
**Phase 113 C2c re-pinned** from `2045cb6c…` (v3 bytes) to the v4 career file's **portable digest** `29a10651…` under the
fixed-rules seam — not its raw bytes, because a v4 file lists the hashes of the log and scouting file, which hold decimals
that are not bit-portable (§14j). ◐ = half settled, with B1c's half named: Phase 105 C6a stands unchanged — a
`bootstrapPeopleForTest` season lists no log, and the reader's refusal stands; no `noLog` flag was added (the list itself
says the season kept none). The hole rows (Phase 88 C6d–C6i; Phases 87, 90, 91, 94) are **unreachable in a real v4 career**,
as the row says, and kept as the readers' contracts — but driven by **re-sealing the career around the planted file**
(`RelistCareerForCheck`) rather than by "constructed `CareerFiles`", since no reader takes its paths from the list until
B1c; **"a listed file missing or changed is refused at load" is Phase 116 C2c**. Rows B1b found that this ledger did not
list: Phase 87 C2 and Phase 89 C6/C7 (hand publishes, now `PublishSeason`); Phases 87 C8iii, 88 C6, 90, 91 C1d, 94 C10,
105 C6c, 106 C7, 107 C2/C3g/C5d, 112 C4/C5b, 113 C5m (each plants a damaged or older file, now re-sealed); Phase 114 C2i,
C4d (the folder gains `league-0`), C4g (re-pinned `9c892e97…` → `de270510…`, portable, and the backup in the folder);
Phase 115 C1a, C2, C4, C5 (the league step and the fifth set-aside kind); Phase 110 C5 (v3 refused beside v2); Phase 81 A6
(born v4).

★ **Marked after S126 (B1c; §14k).** Every row is now settled. Phase 105 C6a: a season that kept no log lists none, and
"not listed" takes the reader's old "absent" branch — the refusal and its message stand (the path is no longer named; an
unlisted file has none a reader may build). Phase 88 C9: a record for the pending season planted **before** the open is set
aside by the open and the season publishes its real record (C9a–c); planted **after** the open, the close's names check
refuses by name with nothing published (new C9d). The hole rows (Phase 88 C6d–C6i; Phases 87, 90, 91, 94): the readers
keep their hole handling, since "not listed" is the same branch as "absent"; they are driven by `RelistCareerForCheck`,
which since S126 also **adds** a present unlisted file of a published season. **Rows B1c found that this ledger did not
list:** Phase 112 **C5b-4** (a zeroed scouting file written after the re-list — re-listed after the write; missed by the
B1c prompt's own ledger, found at the gate); Phase 87 C2d/C2e (re-listed after the plant) and C8iv (season two published by
hand listing the copied log); Phases 94 C10e→C4, 106 C7e, 107 C3g (a restored file, re-listed by the existing call);
Phase 115 C2, C4 (set aside by the open), C4c (the open warns, the names check refuses), C5 (`names` replaces
`set-aside`); Phase 116 C3e (set aside by the `season` command's open).

**The new invariants that replace kind 2**, each with its own B1 test:

- A published id is never published again (B1·5).
- A provisional id is reused after a crash and appears in no listed file (Phase 80 B1 (b)).
- No calculation reads an unlisted file (B1·2).
- A failed close publishes nothing (B1·3).
- A listed file that is missing or changed is refused at load (B1·4).

### 14b. Restarting a season under new rules (YELLOW — clarifies §4d for B2)

r3 said accepting the restart offer "drops the `progress` block". That is incomplete. The restart is an **explicit
transition to "season N begun, nothing played"**, published atomically as an ordinary save (§4b). The new career file
holds:

- **Season id:** the same provisional N. Nothing was published, so the counters at the boundary are unchanged, and the
  provisional ids recompute from them (§2c).
- **Season seed:** kept. In fixed mode it is the derived seed; in live mode it is the recorded segment 0 seed.
- **Rules record:** **replaced**, not appended, with one entry: (the season's first day, current config, current stamp).
  No game stands under the old rules, so there is nothing to record about them.
- **`progress`:** `nextDay` = the season's first day; segments `[(first day, season seed)]`; a fingerprint holding only
  `setup`, recomputed under the new rules.

**"Season begun, nothing played" is distinct from a season boundary.** A boundary has no `season` block at all; a begun
season has one, with `nextDay` = its first day. The same state is what a new live season looks like once its seed is
drawn and recorded (B3).

**Durability:** the restart is made only when the swap completes. Interrupted before that, the old save loads, is
refused again, and the offer is made again. Interrupted after, the new state loads under the current rules.

**Tested in B2·5:** the restart is crash-injected before and after the swap.

### 14c. The career file is a checksummed JSON payload, not plain JSON (YELLOW — amends §4a)

`career.json` v4 is **a JSON body followed by one checksum line**. It is documented as that: a custom format that no
plain JSON parser will read whole. The reader:

1. Reads all bytes.
2. Splits off the last line.
3. Verifies `sha256:<hex>` over the bytes before it.
4. Only then passes the body to the JSON parser.

The writer produces exactly the inverse. The file keeps its name, so `--history <career.json>` and every command are
unchanged.

**Rejected alternative:** a self-hashed JSON field. It would need a second, canonical re-serialization to verify, which
is the two-encodings problem §4e avoids.

The v4 golden (Phase 80 B10) pins both parts.

### 14d. Phase A keeps the fixture-order mode (YELLOW — amends §7 A)

`SeasonPlayOrder.FixtureOrdinalForTest` survives the split, unchanged in what it plays:

- A run created in that mode accepts **only** `AdvanceThrough(last day)` — one call that plays every game in ordinal
  order, as S122's loop does today.
- Any other `AdvanceThrough` in that mode is refused by name: a chunked advance has no meaning in fixture order.

Phase 113 C1e and C2d keep driving it, and Phase A's proof includes them unchanged.

### 14e. The event record is built once, by the same rules (B1 contract — amends §7 B1)

`MteReplaceRecordWithFinishes` reads the NotPlayed record from disk, validates it, and writes the final record. Under B1
there is no file to read, so it splits into two parts:

- **The NotPlayed body** is generated in memory at set-up — the exact bytes `MtePublishRecord` writes today — and is
  never written.
- **The finalizer** keeps every validation the replacement runs today:
  - the record names this career and season;
  - every event is NotPlayed, with no finishes;
  - the seats are the field that played, compared as a whole map;
  - every event that played is present.

  It runs them **against that in-memory body**, then produces the final bytes with the existing
  `MteRecordBytesWithFinishes`.

The final file's bytes are unchanged (Phase 113 C2b's pin), and the record never reads an unlisted file. The same body
is the `setup` fingerprint's third part (§4e), so the fingerprint and the finalizer start from one source.

### 14f. The league-state negative control must reach its assertion (YELLOW — amends §10 B1·9)

Editing `league-0.json` on disk would be refused by its listed hash before the equality assertion ever ran — green for
the wrong reason. **B1·9 is split into two tests:**

- **(a)** A league-state file with one school's prestige changed, **published through the real save path**, so the
  career file lists the edited file's correct hash and carries a valid checksum line. It loads past the hash check and
  is refused by the equality assertion, which names the school.
- **(b)** The same edit made on disk, without re-publishing. It is refused by the hash check, which names the file.

Each refusal is asserted to come from its own guard, by name.

### 14g. The order from here (ChatGPT r3 §7, accepted)

1. **Emmett rules on Q1–Q8** (§0). Phase A depends on none of them.
2. **The Phase A prompt** is drafted against the source, narrowly scoped. Its gate re-reads `RunSeasonCore` and the
   S122 loop.
3. The prompt is audited.
4. **Emmett clears Phase A to build.**
5. Phase A is built and verified on Emmett's machine (the full suite).
6. **B1 is then evaluated against the new code**: this ledger is re-verified, and B1's prompt is drafted.

### 14h. After S123 — what Phase A delivered, and the next gate

**Phase A shipped (S123, verified on Emmett's machine; journal S123, design "The season on the calendar").**
`SeasonBegin` → `AdvanceThrough` → `Finish`, with §7 A's contract, §14d's fixture order, and a lifecycle §7 did not
name: a failure while playing or closing faults the run and every later call is refused naming the day; `Dispose`
releases the season log's file and lock. Nothing on disk or in the basketball moved. Corrections to this brief's
counts: `RunSeasonCore` had **95** call sites, not 89; the game reads **20** set-up values, and the close about 28 more,
so the run holds 48 values (§11's "about 15" was low). **Added because Q1 ruled store everything:** Phase 114's state
inventory names every field of the run, the walk and the five game sources — 105 — by name, type and one of five
categories (mutable, immutable set-up, derived, shared reference, resource or callback). It is a top-level list; it
does not see inside the stats accumulator, behind a reference, or into a callback. It is B2's starting point, not its
proof.

**The next gate (B1):**
1. **The ledger (§14a) is re-verified against the S123 build** before B1's prompt is drafted (§14g step 6).
2. **Q5 against the authority rule (§2a), settled before B1 finalizes its transaction design.** A log written as games
   are played must be an **uncommitted working file until the season closes**: a file the current save lists is never
   appended to. The live log itself lands later; the rule that governs it is fixed in B1.

### 14i. After S124 — what B1a delivered

**B1a shipped (S124, verified on Emmett's machine; journal S124, design "A season leaves no trace until it closes").**
Ids are provisional (§2c): a reservation moves the in-memory counters only, and `HistoryStore.PublishReservations()`
writes them as the **last** write of a season's close — a transitional v3 publication that B1b's manifest swap replaces.
The career lock is held for the whole season. The set-up writes nothing; the close checks everything in memory, then
writes: the set-aside, the log, the scouting file, the event record **once** (§14e — `MteFinalRecordBytes` over the
in-memory NotPlayed body), the counters. The publish is the last fallible step; an exception after it is a committed
season. **Not §2a's authority rule yet:** with no file list, B1a recovers by a **temporary v3 rule** — at the close, every
recognised file of a season at or past the pending one is moved to `career.orphans/`, byte-untouched (four kinds only;
a file that cannot be moved refuses the close). It is exact for v3 because no reader opens a file of the pending season
or later, and **B1c replaces it** with the career's file list. `EventRecordStatus.WriteFailed` and its page lines are
retired (§14a, Phase 88 C7a). **Proof: Phase 115** — an interrupted season, mid-season or at any close step, leaves the
career file byte-identical and plays again in a **fresh process** byte for byte; after the publish it is committed; no
number is published twice; the set-aside and its refusal; the close's order, step by step.

### 14j. After S125 — what B1b delivered

**B1b shipped (S125, verified on Emmett's machine; journal S125, design "History schema v4" and "The career file is
trustworthy").** The career file is v4: the v3 keys, then `files` (every file the career owns with its SHA-256, by
**name-free path** — `gamelog/season-1.log`, resolved next to the career file, because a career's folders are named after
its file), `seasons` (one rules entry per season: first legal day, settings hash, build stamp), `progress` (reserved,
null), and a trailer line over the bytes before it (§4a, §14c). One contract refuses every malformed list by its own error.
The three ways to publish are §4b's, with two amendments made at review (ChatGPT R1 of the B1b prompt): **creation refuses
when a `.bak` exists** (an existing career with a lost primary is recovered, never re-created) and sets aside a stray
`league-0` from a creation that died; **recovery requires the backup to verify whole**, including every file it lists, and
copies the backup rather than moving it, so an interrupted recovery re-runs. A valid career file with a damaged listed file
is refused under Q6, never rolled back. **K8** stated for every session from here: a listed file is never written to,
renamed or deleted by the game. The league state is born with the career and written at every close (§6), asserted equal to
the world at every open and close. The rules record is recorded only — §4d's boundary rules check is B2's. v3 careers are
refused by the C-60 sentence. **Proof: Phase 116.**

**Not B1b, still B1c:** no reader takes its paths from `CareerFiles` yet; nothing is quarantined at load; S124's close-time
set-aside stays (now five kinds — a `league-N` left by a close stopped before its save is the fifth).

**Two findings that bear on later phases.** (1) **A career file's raw bytes are no longer portable**: it lists the hashes
of the log and scouting file, whose decimals differ in the last bit between Windows and Linux. Any pin of a career file is
its portable digest (the `gamelog/` hashes masked) — B2's progress block must not put a machine-dependent value in a
pinned field either. (2) **Verification reads every listed file at every open**: a stock season is about 26 MB, not the
~1.2 MB the B1b prompt assumed. Not measurable at two seasons (Phase 114: 288.1s on S125, 289.3s on S124,
alternated); worth re-measuring when careers run long.

### 14k. After S126 — what B1c delivered

**B1c shipped (S126, verified on Emmett's machine; journal S126, design "The career file is trustworthy" and "A season
leaves no trace until it closes"). B1 is finished: the career file alone decides what a career is.** One read door on the
store, `ListedPath(kind, season)`, returns a file's path only when the career lists it; every reader of an old season — the
pages, last season's people, host memory and the rotation, contracts, tournament memory, the camp's scouting read, the
latest league state — takes its path from it, and "not listed" takes the branch "absent" always took, so a career the game
saved reads exactly what it read before. **Quarantine at every open** (§4c step 3): files of the five recognised kinds that
no protecting manifest lists move into `career.orphans/`, byte-untouched; the protecting manifests are the career file and
a `.bak` whose trailer verifies and which parses — a torn `.bak` protects nothing, safe because quarantine never deletes;
directories, unrecognised names, lock sidecars, temp files and `.new` stay; one attempt per file, a `WARNING:` line for one
that cannot move. **The close's first step is a names check** (free = neither a file nor a directory), refusing by name
before any writer runs. S124's close-time set-aside is retired. Readers do not re-hash on read (verified at open, the lock
held; O-137). O-136 closed: a scouting failure prints a `SEASON ERROR` line. **Proof: Phase 117**, with a source scan that
keeps readers off the path builders and the re-list helper inside the checks.

**What it hands B2.** (1) B2's working files — the live log (Q5) and the snapshot — must be listed, live under a name that is
not one of the five recognised kinds, or be set aside at every open by design; the choice is B2's, made explicitly. (2) A
new reader asks `ListedPath`; one that builds a path moves Phase 117 C3's counted table. (3) A career-file pin stays a
portable digest (§14j).
