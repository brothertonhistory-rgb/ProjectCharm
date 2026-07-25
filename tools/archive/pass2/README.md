# Archived — Pass-2 player generation (RETIRED, not production)

The three files here are the oracle, replay checker, and golden fixture for the
**Pass-2 skill-first player generator** (Sessions 42–44). The generator itself was
retired from the tree at **Session 73**: nothing in the game has drawn from it since
the S70 bridge swap pointed the divvy at the Pass-3 two-plane budget generator.

These are kept as the historical proof record, not as runnable production tooling.
The C# implementation (`PlayerGenPass2.cs`, `PlayerGenPass2Live.cs`) and its suite
phases (59/60) were deleted at S73; `Sampling.cs` — the shared draw layer built
alongside Pass 2 — remains live in the engine, consumed by the Pass-3 generator.

Full history: `docs/journal.md`, Sessions 42–44 (build), S70 (unwired), S73 (retired).
