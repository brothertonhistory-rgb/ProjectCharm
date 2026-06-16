## observations.md — Project Charm flight recorder

This file archives every `ObservationRunV1` (and future vN) sentinel block in full, newest first. Each block is self-describing: it carries everything needed to reproduce the experiment. No judgment lines — these are readings, not verdicts.

**Caveat (repeated from the design doc, worth reading once):** A frozen corpus stabilizes aggregate distributions across runs. It does NOT freeze individual possession outcomes when a code change alters RNG draw topology — same corpus, comparable means, not event-for-event pairing. Do not chase a "bug" that is simply a downstream draw shift from an unrelated code change.

---

## Run 1 — frozen-corpus-v1 (2026-06-16)

```
=== OBSERVATION RUN — frozen-corpus-v1 ===
  config hash:      5196d857a614e5194088e6663cd47f7cb9ff54160f531fa37e8e4353204ece23
  corpus:           frozen-corpus-v1  (seeds 1..1000; rosters: Marcus Webb/DeShawn Pryor/Trey Holloway/Javon Okafor/Cory Baptiste vs Kendrick Shaw/Rashid Monroe/Antoine Dupree/Darius Eze/Malik Thornton)
  sample size:      1000 games
  strategy/context: home-press=1.0  away-press=1.0  halves=2  half=1200s
  session/commit:   Session 48 / ObservationRunV1 harness landed

  Mechanics:  ALL OK — scoring reconciled, count invariant held, zero parks, no throws

--- PACE (total possessions per game) ---
    Total                                  mean=  133.3  sd=  3.8  min=  122  max=  146
    Home                                   mean=   67.0  sd=  2.0  min=   61  max=   73
    Away                                   mean=   66.3  sd=  2.0  min=   60  max=   73
    NoShot (end-of-half, per game)         mean=    0.2  sd=  0.4  min=    0  max=    2
  Distribution (total):
    [120,130)     16.9%  (169)
    [130,140)     77.8%  (778)
    [140,150)      5.3%  (53)

--- PPP (points per possession) ---
    Home                                   mean= 1.1911  sd=0.1406  min= 0.7794  max= 1.7879
    Away                                   mean= 1.1937  sd=0.1447  min= 0.7101  max= 1.6393
    Combined                               mean= 1.1923  sd=0.0956  min= 0.9127  max= 1.4924
  Distribution (combined):
    [0.9,1.1)     16.4%  (164)
    [1.1,1.3)     70.6%  (706)
    [1.3,1.5)     13.0%  (130)

--- SCORE & MARGIN ---
    Home score                             mean=   79.8  sd=  9.5  min=   53  max=  118
    Away score                             mean=   79.1  sd=  9.9  min=   49  max=  111
    Margin (Home−Away)                     mean=    0.7  sd= 14.0  min=  -48  max=   48

--- TRANSITION FREQUENCY (Transition entries / total possessions) ---
    Transition freq                        mean= 0.2985  sd=0.0396  min= 0.1818  max= 0.4173

--- TERMINAL MIX (fractions across all possessions all games) ---
  Made-FG                0.4128  (55,037)
  FT-made                0.1135  (15,131)
  DefensiveRebound       0.2377  (31,688)
  Turnover               0.1927  (25,692)
  OOB                    0.0216  (2,883)
  JumpBall               0.0202  (2,691)
  NoShot                 0.0015  (205)
  Parked                 0.0000  (0)
  Other                  0.0000  (0)

--- END-OF-HALF INTENT (counts per game) ---
    HoldShootLast                          mean=    1.6  sd=  0.6  min=    0  max=    2
    ShootEarly                             mean=    0.5  sd=  0.7  min=    0  max=    4
    NoShot                                 mean=    0.2  sd=  0.4  min=    0  max=    2

--- APL (avg possession length, seconds) ---
    APL                                    mean=18.0155  sd=0.5143  min=16.4384  max=19.6721

--- FOULS PER TEAM PER GAME ---
    Home fouls                             mean=   11.3  sd=  3.1  min=    1  max=   22
    Away fouls                             mean=   11.5  sd=  3.1  min=    3  max=   23

--- DEFERRED SENTINELS (counter-plumbing needed — future session) ---
  FG% / 3P% / FT%  (shooting splits)
  Shot mix (per-zone attempt distribution)
  ORB% / DRB%
  FTr (FTA/FGA)
  Press frequency / break rate at game level

=== END OBSERVATION RUN — frozen-corpus-v1 ===
```

**Notes on this reading:**
- Pace ~133 total / ~67 per team is **realistic** — real D1 college basketball runs ~65–72 possessions per team per game. No pace calibration was needed; the engine lands here naturally.
- Combined PPP ~1.19 is somewhat above real D1 (~1.0–1.1). The more notable number to track going into calibration.
- Combined PPP ~1.19 is somewhat above real D1 (~1.0–1.1). Consistent with the un-calibrated probability weights.
- APL ~18.0s is stable and matches the single prior game reading.
- Zero parks confirms the engine chain is complete and wired end-to-end through Roll M.
- Terminal mix: Made-FG at 41% is the largest category; turnover at 19% is plausible shape; FT-made at 11% captures the FT chain.

