# Project Charm — World Structure & Prestige: Design Brief

Settled in the design conversation of 2026-07-02. This is the design record the
world-structure arc builds against. It is a brief, not a build prompt — build
prompts for each pass get drafted, audited, and reviewed per CONVENTIONS §6.

---

## 1. The frame

**The product is a college basketball universe sandbox.** The default experience
is traditional NCAA structures; the architecture never assumes them.

**The starting file is a bootstrap, not a product.** Users are expected to sim
N seasons and jump in mid-universe, inheriting teams with history, stats, and
turned-over rosters. Initial rosters and even initial prestige only need to be
plausible enough to converge; everything the user touches is output of the
dynamics.

**The finish line for this arc is the burn-in:** simulate the universe forward
many seasons and it still looks like college basketball — the pyramid holds,
blue bloods are mostly still blue bloods, an occasional Gonzaga ascends, and a
Kansas can become nationally irrelevant — a shell of itself, protected from
the literal bottom only by its conference floor. (The full "former giant
becomes a true nobody" story arrives with realignment, when a fallen program
can lose the membership floor too — promised then, not before the world has
the mechanism to permit it.) The league-level readout that proves this is part
of the arc, same lineage as the bench and the observation run, one level up.

**The central engineering risk this design answers:** prestige → generated
talent → wins → prestige is a closed feedback loop neither prior attempt had
(their prestige moved on results, but results didn't come from
prestige-generated rosters). The loop must be stabilized and *proven* stable,
not hoped.

---

## 2. Standing rules (hold for every pass in this arc and after)

1. **Count-agnostic.** Nothing assumes a population size. The pyramid is
   defined as *proportions*, never absolute counts. Conference tiers are
   properties a file assigns, not a fixed list. 350 schools is the shape at
   n=350; 20 or 1,000 work without code changes. (Downstream layers with real
   count assumptions — a 68-team tournament over 20 schools — decide their own
   caveats when they arrive; the *skeleton* never blocks them.)

2. **Structures are data.** Schools, conferences, tiers, and (later) tournament
   fields and formats live in files, not code. Stock files ship traditional
   NCAA; a custom file is just a different file.

3. **Schools are swappable rows.** Identity is an internal id; name, city,
   conference, colors are plain data attached to it. The dynamics never read a
   name. Real-name and fictional-name worlds are just two files.

4. **Prestige never touches the possession engine.** The no-scalar wall stands:
   game outcomes emerge from player matchups only. Prestige shapes roster
   *generation* (and later recruiting); it is never an input to game
   resolution.

5. **Era files.** A starting file is stamped to a year and carries membership
   and prestige as of that year (2002 file, 2026 file, fictional file — same
   engine). This is the first persistent schema we design, which makes the
   save-format discipline (design before the layer that needs it) start in
   this arc.

6. **Files validate loudly.** A world file passes a general integrity
   validator at load/build time — ordinary schema checks (unique ids, every
   school points to a real conference, every conference to a real tier and one
   division, all values in bounds) plus the special one: given each
   conference's member count and floor, the target pyramid and the prestige
   bounds must be simultaneously satisfiable. An infeasible file fails with an
   explanation naming the conflict — never a silent floor weakening or a
   quietly distorted pyramid. Flexibility does not mean pretending every
   combination of inputs is coherent.

7. **Generated worlds are reproducible.** Every generated bootstrap world
   stores its world seed in the file's metadata, and all seeding decisions are
   reproducible from file + seed — so a fictional world can be shared exactly,
   a weird burn-in can be replayed, and the harness can compare code changes
   against the identical initial universe.

---

## 3. The settled model

### 3a. Divisions

D1, D2, D3, JUCO are **separate ecosystems, each with its own 0–99 prestige
scale**. They never overlap in play (exhibitions aside, far future). The
division marker is carried on every program from day one because recruiting
eventually combines division × prestige (a 99 D3 school and a 99 D1 school
coexist and mean different things) — but the conversion is recruiting-arc
design, deliberately parked. This arc builds D1; the others are later files
over the same machinery.

### 3b. The pyramid (target distribution, as proportions)

Derived from the reference file's shape (347 teams), with the 20–39 band
thinned into the base per Emmett's call — a true pyramid, widest at the bottom:

| Band  | ~% of population | at n≈350 |
|-------|------------------|----------|
| 95+   | ~1%              | 3–4      |
| 85–94 | ~6%              | ~20      |
| 75–84 | ~9%              | ~30      |
| 60–74 | ~14%             | ~50      |
| 40–59 | ~23%             | ~80      |
| 20–39 | ~21%             | ~75      |
| <20   | ~26%             | ~90      |

These are the target shape the conservation force (3f) maintains, and the shape
the initial generator seeds. Percentages are the spec; counts are illustrative.
The width of the <20 base is the acknowledged dial for how much of the sport is
cannon fodder (30-point November blowouts are honest; the dial exists if
burn-in shows too many).

### 3c. Conference tiers

Four authored tiers: **power, high mid-major, low mid-major, low**. Each tier
carries three numbers (all placeholders until burn-in tuning):

- **floor** — hard. A member's current prestige never falls below it. The
  floor protects *membership, not name*: it applies because you are in the
  conference today. And because current prestige drives roster generation, the
  floor is plainly a **roster-quality guarantee** — membership guarantees a
  minimum recurring talent level, which is intended (conference affiliation is
  a real structural advantage), and stated here so it is never mistaken for a
  display-only guardrail. (Memberships are fixed per file for now; realignment
  is a named future arc, and when it arrives, falling out of a conference
  means losing its floor. Until then the power floor is accepted as a soft
  anchor — a power-conference Kansas can be mediocre forever but not a true
  nobody.)
- **equilibrium** — the resting level the tier pulls its members toward.
- **pullback intensity** — how hard the elastic pulls, scaling *inversely*
  with tier: intense for low conferences, gentle for power.

**No ceilings anywhere.** Any program can reach the top of the scale (99) on
results. Prestige above your conference's station is *rented*; the rent is
continued winning, and the worse the conference, the higher the rent. A Big
South school with five straight titles hits the top of the scale — and once
the run ends, the pull drags it back toward its conference's equilibrium. The
pull is intense, but it is a pull, not a reset: a still-very-good season slows
the slide; only genuine mediocrity lets the conference reclaim it fully.
(Exactly how hard "no longer transcendent" vs. "actually collapsed" pulls is
Pass 3 oracle tuning.) This is the Gonzaga mechanism: decades of sustained
payment on a very high rent, not an exemption from it.

**No intra-tier ranking.** The Big 12 is never authored better than the Big
Ten; any difference between same-tier conferences emerges from members'
results.

### 3d. Two-value programs

Every program carries two numbers:

- **Current prestige** — where you stand today. Moves on results. Tethered to
  the conference equilibrium (3c) with the hard floor beneath. This is the
  number roster generation reads.
- **Historical prestige** — banked national memory. Earned by sustained
  accomplishment, never an eternal authored truth. Decays *generationally*:
  fifteen bad seasons barely dent it; a hundred seasons genuinely erode it.
  Kansas can fade into national irrelevance in 50 seasons; Kentucky's
  accomplishments from 100
  seasons ago fade. **Seeding differs by file kind:** a fictional/bootstrap
  world seeds historical equal to current (no memory exists yet, and burn-in
  banks it); a **real-era file authors both values** — a 2026 file enters a
  sport with a century of memory, so a merely-good 2026 Kansas carries
  historical well above its current, or the file has no memory of Kansas at
  all; a post-burn-in save carries wholly generated state.

**Historical is a ladder accelerator, not a parachute — strictly asymmetric.**
It never cushions a fall: within the conference floor's structural protection,
a program falls on the same results logic as any other program — Kentucky's
decline runs at the same speed as a no-name's. Its only effect is on the way
back up: with high historical backing, two or three tournament runs convert
into a rapid climb where a no-name with identical results inches. Memory makes
the country quick to *re-believe*, not unwilling to forget.

### 3e. The forces on current prestige (per season)

1. **Results force** — performance vs. expectation, where expectation derives
   from current prestige and tier context (a low-tier team is not punished for
   missing a tournament that was never its baseline). Overperform → rise;
   underperform → fall. Symmetric.
2. **Conference pullback** — toward the tier equilibrium, intensity per 3c.
3. **Historical acceleration** — multiplies *upward* movement only, scaled by
   the gap between historical and current (a fallen giant rebounds fast; a
   program at its historical level gets no boost).
4. **Conservation pressure** — see 3f.

Items 1–4 are *forces*. The **conference floor and the global 0–99 bounds are
constraints (clamps), not forces** — applied after all forces, never tuned as
gradual nudges: a program can collapse all the way to its floor and stop; the
floor never actively lifts anyone.

Historical prestige itself moves slowly: sustained success banks it upward;
generational time decays it. Exact rates are burn-in tuning.

**The application order is a Pass 3 contract, not implementation discretion** —
order changes behavior materially. Two rules are fixed now: the **clamps are
applied last and beat every force, including conservation** (otherwise the
floor is not hard); and **conservation may never reverse the sign of the
earned move**, where the earned move is defined as the *full pre-conservation
delta* — results + pullback + historical acceleration summed. Conservation may
shrink that total toward zero but never across it, and a pre-conservation zero
gets no conservation movement at all. Conservation is a dampener, never a
separate author of team movement.

### 3f. Distribution conservation

A standing, population-level force nudging the *shape* of the distribution
toward the pyramid — the stabilizer the feedback loop needs, stated as a
design goal rather than a patch. Design intent: prestige is fundamentally
relative (national attention is a finite pie), so the population is softly
renormalized toward the target shape as a whole; no individual team is yanked
to a script, and a team that earns a rise genuinely rises past others. The
sport as a whole cannot inflate to everyone-an-80 or sag to everyone-a-45.
The concrete mechanism is an engineering proposal for the Pass 3 dynamics
design (Claude's lane) — Pass 1 proves the initial pyramid exists; Pass 3
decides how it stays stable.

**Conservation is observable and bounded, never hidden.** "Feels invisible" is
the design goal but cannot be the only acceptance test for the force that
stabilizes the whole universe. The burn-in readout instruments it: average and
maximum per-team adjustment, adjustments by band and by tier, how often it
offsets a material share of a team's results movement, and confirmation it
never reverses a net direction. Acceptance is both together: the shape holds
*and* no individual story is silently rewritten — that is what separates a
stabilizer from a scriptwriter.

---

## 4. The arc map (each pass its own design → prompt → build cycle)

Dependency-ordered; scope walls per session as always.

- **Pass 1 — the static skeleton.** The era-file schema (schools, conferences,
  tiers, division marker, the two prestige values); the school list sourced
  from the reference csv (real names, swappable); the pyramid seeder
  (proportional, count-agnostic); a **distribution readout** (the pyramid on
  the page: histogram by band, per-conference summaries). No seasons, no
  dynamics execution — the skeleton and the proof it was seeded to shape.
  Deliberately data + readout only. **Seeder semantics are explicit:** the
  seeder is *tooling, never a load-time mutation* — a file that carries
  authored prestige values is loaded as written (the readout may report its
  deviation from the target shape, but never "corrects" it); the seeder
  generates fictional/bootstrap files. Small-population apportionment is
  deterministic (largest-remainder with stable tie-breaking from the world
  seed), so the same inputs always produce the same shape at any n.
- **Pass 1.5 — national pool & divvy** (governing record:
  `docs/roster-genesis-brief.md`; shipped Session 29). Rosters for every
  program in a world file, drawn from one national talent pool by
  prestige-weighted access — inserted ahead of the season loop so the Pass 3
  dynamics are tuned against pool-built populations, never per-team-generated
  ones.
- **Pass 2 — minimal season loop.** Enough scheduling to produce a season of
  results at population scale (round-robin-ish placeholder; real scheduling is
  its own later arc), driving the possession engine per game. This exists to
  feed the dynamics, not to be the real scheduler.
- **Pass 3 — the dynamics.** The prestige dynamics of 3e/3f — the four forces,
  then the clamps — run season over season.
  Oracle-first: the force math is Python-traced for convergence before C#.
  The force application order, the expectation model, and the conservation
  mechanism are this pass's design contracts (see 3e/3f/§5).
- **Pass 4 — the burn-in readout.** Sim N seasons; the league-level instrument
  reports shape-over-time, tier stability, rise/fall case studies (did a
  Gonzaga happen; did a blue blood fall). **Acceptance is quantitative, with
  the case studies beside the metrics, not instead of them:** distance from
  the target shape over time within a tolerance band; band mobility over
  10/25/50 seasons, **broken out by initial band** (a pyramid can look stable
  while individual bands are frozen — can 95+ fall, can 20–39 rise, is
  under-20 a trap); top-tier persistence vs. freeze; rise and fall rates;
  recovery asymmetry (matched fallen programs, high vs. low historical);
  **prestige-to-team-quality correlation** (higher prestige must mean stronger
  generated teams and results through the intended route — clearly positive,
  never near-deterministic); runaway/collapse detection over long horizons;
  many seeds, never one attractive universe; and at least one
  **no-conservation comparison run** to prove the force stabilizes rather than
  cosmetically reshapes.
- **Persistence** enters where it becomes load-bearing (the era file is
  already a schema in Pass 1; multi-season state persists by Pass 3 at the
  latest). Format designed before the pass that needs it, per the standing
  note.

**Deferred out of this arc entirely:** recruiting (the marker is carried; the
conversion is parked), real scheduling, postseason/tournaments (structures-as-
data rule protects the future 14-team double-elim sandbox case), coaches,
realignment, D2/D3/JUCO files, engine-side migration of generation.

---

## 5. Open items (flagged, not blocking)

- **Roster generation vs. the world.** The current gen module takes a bare
  prestige number; Pass 1 gives that number a home. Whether generation needs
  any change beyond reading from the world (it shouldn't, for the skeleton) is
  confirmed at Pass-1 prompt time against source.
- **The expectation model (Pass 3 contract).** "Expectation derives from
  prestige and tier context" must be pinned to a *named* model before the
  dynamics build — e.g. prestige sets expected underlying strength while tier
  shapes schedule quality and opportunity — never an unnamed tier term layered
  silently on top of prestige's roster effect. Unnamed context terms become
  tuning magnets that later read as magic.
- **Historical acceleration's target (Pass 3 contract — Emmett's call).** Does
  historical memory multiply the raw positive results force, or only the net
  positive movement *after* pullback is combined? The second means memory can
  never turn a neutral-or-losing season into growth and never outmuscles a low
  conference's rent (external review's recommendation); the first lets a
  fallen giant's rebound fight through heavy pullback more visibly. This is a
  basketball call about how strong national memory is against structural
  gravity — decided in the Pass 3 design conversation, not here.
- **Roster variance (recorded assumption).** The loop's health depends on how
  much variance generation permits: too deterministic and the pyramid freezes
  (Gonzaga stories become impossible); too noisy and prestige goes cosmetic.
  The assumption Pass 3 relies on: at equal prestige, generated teams vary
  meaningfully; across prestige levels, expected strength is monotonic but
  *overlapping* — the overlap is where upsets, ascents, and believable decline
  live. Verified against the generator when the dynamics land.
- **Season length vs. dynamics cadence.** The forces are specified per-season;
  Pass 2's placeholder schedule defines what "a season" is for burn-in
  purposes. Fine for tuning; revisit when real scheduling lands.
- **Compute check.** A 350-team season is ~5,000 possession-engine games per
  season × N burn-in seasons. Expected fine; measured at Pass 2, and if it
  isn't, that's an engineering problem for Claude's lane (not a design change).
