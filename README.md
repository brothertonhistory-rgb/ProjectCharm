# Project Charm

A possession-level college basketball dynasty simulation, built one validated
subsystem at a time. Every possession resolves through a chain of "Roll"
generators (A–M) driven by individual player matchups; team strength emerges from
those matchups, never from a single team rating. All tunable values live in
`src/Charm.Harness/config.json`.

The authoritative documents:

- `docs/design.md` — current-state architecture, by subsystem
- `docs/journal.md` — append-only session history (newest first)
- `docs/status.md` — the living status board (open / parked / closed items)
- `docs/attribute-meaning.md` — measured meaning of every player rating

## Repository map

- `src/Charm.Engine` — the engine: `Rolls/` and `Generators/` (the possession
  chain), `Core/` (players, game state, matchup math, pickers, player
  generation), `Config/` (loaders for `config.json`)
- `src/Charm.Harness` — the console runner: the validation suite plus the
  `season`, `game`, `bench`, `gen`, and `sweep` commands
- `tools/` — locked Python oracles and golden fixtures (committed before every
  C# port; parity checks bind to them), `tools/sweep/` measurement configs,
  `tools/archive/` retired tooling
- `worlds/` — world files (`stock-d1.world.json`: 347 schools, 32 conferences)
- `docs/briefs/archive/`, `docs/prompts/archive/` — shipped design briefs and
  old session prompts, history only

## Run it

The validation suite (every committed phase; ends `ALL CHECKS PASSED`):

```bash
dotnet run --project src/Charm.Harness -c Release
```

A full 347-school season (5,205 real engine games; deterministic from the seed):

```bash
dotnet run --project src/Charm.Harness -c Release -- season worlds/stock-d1.world.json 20260720
```

Other commands (`game` — a single stub-generator demo game; `bench` / `gen` /
`sweep` — measurement instruments driven by JSON configs at the repo root and in
`tools/sweep/`) are documented where they are built, in `docs/design.md`.

## Deliberately incomplete

No rotation or substitution consequences yet (the fatigue fence is wired into
the season runner, but starters take ~88% of possessions and no foul-out logic
exists), no persistence (a season is recomputed from its seed, never saved), and
no coaching/defensive-settings layer (pace, shot-selection, pressure, and scheme
dials are named but deferred). These are tracked on `docs/status.md`.
