# Project Charm

A simulation of college basketball built one validated roll at a time. See
`docs/design.md` for the architecture and `docs/journal.md` for session history.

## Run the Roll A harness

```bash
dotnet run -c Release --project src/Charm.Harness
```

Prints sample possessions, runs a 100,000-possession batch that checks outcome
rates against the configured pie and confirms every exit hands off cleanly, then
proves the generator→roll seam carries signal.

## Tune

All numbers live in `src/Charm.Harness/config.json` — edit and re-run.

## Layout

- `src/Charm.Engine` — the engine (pie, results, Roll A, resolver, stubs, config).
- `src/Charm.Harness` — console runner and validation harness.
