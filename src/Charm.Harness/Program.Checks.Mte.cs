using System.Globalization;
using System.Text.Json;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 88 (Session 97) — THE MTE POOL.
//
//  What this proves, and what it deliberately does NOT:
//
//  ★ PAGE-ONLY CALIBRATION HOLDS. Not one assertion here says a field should
//    look a certain way, that a given school should be in a given tournament,
//    or that some number of events should run. Those are basketball values and
//    they live on the page. What is asserted is MECHANISM: the two absolutes,
//    the fallback order, the four-year arithmetic, determinism, isolation, the
//    refusals, and the record's binding rules.
//
//  ★ THE ZERO PATH IS THE FIRST CHECK AND ITS GOLDENS ARE PRE-S97. They were
//    captured from the pristine v4 tree before a line of this session existed —
//    a golden taken after the schema work could have absorbed a page change the
//    schema work introduced, and then agreed with itself forever.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ fixture-tiny RECAPTURED at S105.2 (Emmett's ruling: its five-team
    /// leagues play 12 conference games, not 16 — 16 could not obey the new
    /// weekday/weekend rule; 30 league games over 9 weeks sits exactly on the
    /// 2·floor(n/2)=4 ceiling). These are no longer the pre-S97 pristine artifacts
    /// for tiny; they remain the fixed authority C1 pins the zero path to.
    /// The MEMORY pair is still the genuine pre-S97 capture — its dates are
    /// untouched by the rule, which is itself evidence the rule changed nothing
    /// it did not need to.</summary>
    private const string MteTinyScheduleSha =
        "6fc122dd3bc4f48a6f7c8b3787dcc236603536d4d610bf53ad0934480b189981";
    private const string MteTinyDatedSha =
        "93e27e5b663c87483e28aa67359123f1cf0421e206dc60bc62b72739e6f7fcf0";
    private const string MtePreS97MemoryScheduleSha =
        "eee5e256b0c6fc871d565b8c27c2925824e3b3ba8e76a717a3fdae4c6c0b36dc";
    private const string MtePreS97MemoryDatedSha =
        "bbff75ce74cf9363684cac326658019fd1a0e169c11c085ed56e343f4098798c";

    private const long MteCheckSeed = 20260720;

    private static bool Phase88MteCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 88 — The MTE pool (S97: events exist and fields are seated — zero-path " +
                          "identity against pre-S97 goldens, load refusals by name, the two absolutes at " +
                          "every fallback level, four-year arithmetic at both boundaries, tier and slot " +
                          "order, determinism with a bounded seed sweep, isolation, the transaction, and " +
                          "the record's career binding) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        static string? Refusal(Action a)
        {
            try { a(); return null; }
            catch (Exception ex) { return ex.Message; }
        }

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s97-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(scratch);
            var baseDir = AppContext.BaseDirectory;
            var tiny = LoadWorld(Path.Combine(baseDir, "worlds", "fixture-tiny.world.json"));
            var mem = LoadWorld(Path.Combine(baseDir, "worlds", "fixture-memory.world.json"));
            var mte = LoadWorld(Path.Combine(baseDir, "worlds", "fixture-mte.world.json"));

            // ════════════════════════════════════════════════════════════════════
            //  C1 — THE ZERO PATH. An empty pool is the pre-S97 engine, exactly.
            // ════════════════════════════════════════════════════════════════════
            Check("C1a: fixture-tiny and fixture-memory author NO events (the zero path exists)",
                  tiny.Events.Count == 0 && mem.Events.Count == 0);

            var tinySched = BuildSeasonSchedule(tiny, MteCheckSeed);
            var tinyFp = ScheduleFingerprint(tinySched);
            var tinyDated = SeasonDateSchedule(tiny, tinySched, SeasonDefaultStartYear);
            Check("C1b: ★ fixture-tiny's schedule matches its fixed golden (recaptured at " +
                  "S105.2: the 12-game ruling plus the weekday/weekend rule)",
                  tinyFp == MteTinyScheduleSha && tinyDated == MteTinyDatedSha,
                  tinyFp[..16] + "… / " + tinyDated[..16] + "…");

            var memSched = BuildSeasonSchedule(mem, MteCheckSeed);
            var memFp = ScheduleFingerprint(memSched);
            var memDated = SeasonDateSchedule(mem, memSched, SeasonDefaultStartYear);
            Check("C1c: ★ fixture-memory's schedule matches the PRE-S97 golden too",
                  memFp == MtePreS97MemoryScheduleSha && memDated == MtePreS97MemoryDatedSha,
                  memFp[..16] + "… / " + memDated[..16] + "…");

            var emptySeating = MteSeatSeason(tiny, MteCheckSeed, MteHistory.Empty);
            var emptyOutcome = new EventSeasonOutcome(
                emptySeating, EventRecordStatus.NotApplicable, null, Array.Empty<string>());
            Check("C1d: ★ an empty pool prints NOTHING — no heading, no blank line, which is what " +
                  "makes the byte-identity claim honest",
                  emptySeating.PoolIsEmpty && MtePageLines(emptyOutcome).Count == 0);

            // ════════════════════════════════════════════════════════════════════
            //  C2 — LOAD VALIDATION. Every violation refused BY NAME.
            // ════════════════════════════════════════════════════════════════════
            var v4Path = Path.Combine(baseDir, "worlds", "fixture-v4-retired.world.json");
            var v4Msg = Refusal(() => LoadWorld(v4Path));
            Check("C2a: ★ a genuine v4 world is refused by name, with the re-conversion command",
                  v4Msg is not null && v4Msg.Contains("schemaVersion 4 is retired")
                  && v4Msg.Contains("world convert"),
                  v4Msg is null ? "NO REFUSAL" : "refused");

            var v1Path = Path.Combine(baseDir, "worlds", "fixture-v1-retired.world.json");
            Check("C2b: the v1 refusal is untouched — one shared guard, four retired versions",
                  Refusal(() => LoadWorld(v1Path))?.Contains("schemaVersion 1 is retired") == true);

            // Structural violations, constructed in memory and pushed through the same
            // validator the loader calls. Each names the offending EVENT, not a field index.
            var okEvent = mte.Events.Single(e => e.Id == 10);
            WorldFile WithEvents(params WorldEvent[] evs) => new()
            {
                SchemaVersion = mte.SchemaVersion, Kind = mte.Kind, EraLabel = mte.EraLabel,
                Division = mte.Division, WorldSeed = mte.WorldSeed, Tiers = mte.Tiers,
                Conferences = mte.Conferences, Places = mte.Places, Schools = mte.Schools,
                Events = evs.ToList(),
            };

            var cases = new (string What, WorldEvent Ev, string Expect)[]
            {
                ("unknown placeId",   okEvent with { PlaceId = 99999 },        "unknown placeId"),
                ("tier below one",    okEvent with { Tier = 0 },               "must be 1 or greater"),
                ("fieldSize seven",   okEvent with { FieldSize = 7 },          "must be exactly 8 or 4"),
                ("slot count wrong",  okEvent with { Slots = okEvent.Slots.Take(7).ToList() }, "slot(s) for a field"),
                ("persistence > 1",   okEvent with { Persistence = 1.5 },      "finite number in [0, 1]"),
                ("persistence NaN",   okEvent with { Persistence = double.NaN }, "finite number in [0, 1]"),
                ("inverted band",     okEvent with { Slots = InvertFirstBand(okEvent) }, "is inverted"),
                ("band out of domain", okEvent with { Slots = OutOfDomainBand(okEvent) }, "prestige domain"),
                ("unknown scope word", okEvent with { Slots = BadScope(okEvent) }, "not in the vocabulary"),
                ("window too long",   okEvent with { LastDay = "11-27" },      "plays on EXACTLY"),
                ("window backwards",  okEvent with { FirstDay = "11-27", LastDay = "11-26" }, "runs backwards"),
                ("window not a date", okEvent with { FirstDay = "02-30", LastDay = "02-31" }, "not a real date"),
                ("window wraps year", okEvent with { FirstDay = "12-31", LastDay = "01-02" }, "year-wrapping"),
            };
            foreach (var (what, ev, expect) in cases)
            {
                var msg = Refusal(() => ValidateWorld(WithEvents(ev)));
                Check($"C2c: {what} refused by name",
                      msg is not null && msg.Contains(expect) && msg.Contains(ev.Name),
                      msg is null ? "NO REFUSAL" : "");
            }

            var dupPlace = Refusal(() => ValidateWorld(WithEvents(
                okEvent, okEvent with { Id = 999, Name = "Second Event Same Town" })));
            Check("C2d: two events in one town ON THE SAME NIGHTS refused by name",
                  dupPlace is not null && dupPlace.Contains("One event per place per day"));

            // ★ S104 — THE NEGATIVE CONTROL, and without it C2d is decorative: the refusal
            //   above fires under the OLD one-event-per-place rule and the NEW per-day rule
            //   alike, so on its own it cannot tell whether the dates are being read at all.
            //   Emmett's ruling (2026-08-06): the Garden holds the Holiday Festival one week
            //   and the Jimmy V the next, and that must LOAD.
            var sameTownDifferentWeek = Refusal(() => ValidateWorld(WithEvents(
                okEvent,
                okEvent with { Id = 998, Name = "Same Town, Another Week",
                               FirstDay = "12-01", LastDay = "12-03" })));
            Check("C2d-bis: ★ two events in one town on DIFFERENT nights are ACCEPTED — the " +
                  "discriminator, without which C2d passes under the retired rule too",
                  sameTownDifferentWeek is null,
                  sameTownDifferentWeek ?? "");

            var dupId = Refusal(() => ValidateWorld(WithEvents(okEvent, okEvent with { Name = "Clone" })));
            Check("C2e: duplicate event ids refused by name",
                  dupId is not null && dupId.Contains("duplicate event id"));

            var badScope = Refusal(() => ValidateWorld(new WorldFile
            {
                SchemaVersion = mte.SchemaVersion, Kind = mte.Kind, EraLabel = mte.EraLabel,
                Division = mte.Division, WorldSeed = mte.WorldSeed,
                Tiers = mte.Tiers.Select(t => t.Id == "low" ? t with { EventScope = "elite" } : t).ToList(),
                Conferences = mte.Conferences, Places = mte.Places, Schools = mte.Schools,
                Events = new List<WorldEvent>(),
            }));
            Check("C2f: a tier whose eventScope is not power or mid is refused by name",
                  badScope is not null && badScope.Contains("eventScope 'elite'"));

            var tierAny = Refusal(() => ValidateWorld(new WorldFile
            {
                SchemaVersion = mte.SchemaVersion, Kind = mte.Kind, EraLabel = mte.EraLabel,
                Division = mte.Division, WorldSeed = mte.WorldSeed,
                Tiers = mte.Tiers.Select(t => t.Id == "low" ? t with { EventScope = "any" } : t).ToList(),
                Conferences = mte.Conferences, Places = mte.Places, Schools = mte.Schools,
                Events = new List<WorldEvent>(),
            }));
            Check("C2g: ★ 'any' is a SLOT word, not a tier word — a tier may not decline to answer",
                  tierAny is not null && tierAny.Contains("eventScope 'any'"));

            // ════════════════════════════════════════════════════════════════════
            //  C3 — DETERMINISM, in a form that cannot be flaky.
            // ════════════════════════════════════════════════════════════════════
            string Fingerprint(EventSeatingOutcome o) => string.Join("|",
                o.Active.Select(e => e.EventId + ":" + string.Join(",", e.Seats.Select(s => s.SchoolId))))
                + "||" + string.Join(",", o.Dormant.Select(d => d.EventId));

            var a1 = MteSeatSeason(mte, MteCheckSeed, MteHistory.Empty);
            var a2 = MteSeatSeason(mte, MteCheckSeed, MteHistory.Empty);
            Check("C3a: the same seed seats the identical fields twice",
                  Fingerprint(a1) == Fingerprint(a2));

            var prints = new HashSet<string>();
            var neverActive = true;
            var alwaysActive = true;
            var slates = new HashSet<string>();
            for (var seed = 1; seed <= 64; seed++)
            {
                var o = MteSeatSeason(mte, seed, MteHistory.Empty);
                prints.Add(Fingerprint(o));
                if (o.Active.Any(e => e.EventId == 12)) neverActive = false;   // persistence 0.0
                if (!o.Active.Any(e => e.EventId == 13)) alwaysActive = false; // persistence 1.0
                slates.Add(ScheduleFingerprint(BuildSeasonSchedule(mte, seed)));
            }
            Check("C3b: ★ seeds 1..64 produce at least two distinct fields — a bounded search, never " +
                  "'any two seeds differ'",
                  prints.Count >= 2, $"{prints.Count} distinct across 64 seeds");
            Check("C3c: ★ persistence 0 NEVER activates and persistence 1 ALWAYS does, at all 64 seeds " +
                  "(the integer threshold, not a very-likely float)",
                  neverActive && alwaysActive);
            Check("C3d: ★ the conference slate is IDENTICAL across all 64 seeds — the league schedule " +
                  "still consumes no randomness",
                  slates.Count == 1, $"{slates.Count} distinct slate(s)");

            var forcedOn = mte.Events.Select(e => e with { ForcedActive = true }).ToList();
            var forcedOff = mte.Events.Select(e => e with { ForcedActive = false }).ToList();
            WorldFile Swap(List<WorldEvent> evs) => new()
            {
                SchemaVersion = mte.SchemaVersion, Kind = mte.Kind, EraLabel = mte.EraLabel,
                Division = mte.Division, WorldSeed = mte.WorldSeed, Tiers = mte.Tiers,
                Conferences = mte.Conferences, Places = mte.Places, Schools = mte.Schools, Events = evs,
            };
            var onAll = MteSeatSeason(Swap(forcedOn), 7, MteHistory.Empty);
            var offAll = MteSeatSeason(Swap(forcedOff), 7, MteHistory.Empty);
            Check("C3e: forcedActive overrides persistence in BOTH directions",
                  onAll.Active.Count == mte.Events.Count && onAll.Dormant.Count == 0
                  && offAll.Active.Count == 0 && offAll.Dormant.Count == mte.Events.Count);

            // ════════════════════════════════════════════════════════════════════
            //  C4 — SEATING. The absolutes, the fallback ladder, the four-year rule.
            // ════════════════════════════════════════════════════════════════════
            var confById = mte.Conferences.ToDictionary(c => c.Id);
            var schoolById = mte.Schools.ToDictionary(s => s.Id);

            var allLevels = new List<EventSeatFallback>();
            var capViolations = 0;
            var doubleBooked = 0;
            for (var seed = 1; seed <= 64; seed++)
            {
                var o = MteSeatSeason(mte, seed, MteHistory.Empty);
                var seenThisSeason = new HashSet<int>();
                foreach (var e in o.Active)
                {
                    var keys = new HashSet<long>();
                    foreach (var s in e.Seats)
                    {
                        allLevels.Add(s.Fallback);
                        if (!seenThisSeason.Add(s.SchoolId)) doubleBooked++;
                        var sc = schoolById[s.SchoolId];
                        if (!keys.Add(MteConferenceCapKey(sc, confById[sc.ConferenceId]))) capViolations++;
                    }
                }
            }
            Check("C4a: ★ ABSOLUTE 1 — no school is in two events in one season, across 64 seeds and " +
                  "every fallback level reached",
                  doubleBooked == 0, $"{doubleBooked} violation(s)");
            Check("C4b: ★ ABSOLUTE 2 — no field holds two schools sharing a conferenceCapKey, across " +
                  "64 seeds and every fallback level reached",
                  capViolations == 0, $"{capViolations} violation(s)");
            Check("C4c: the fallback ladder is actually exercised (levels beyond None occur), so C4a/C4b " +
                  "are not passing only on the easy path",
                  allLevels.Distinct().Count() >= 2,
                  string.Join("/", allLevels.Distinct().OrderBy(x => x)));

            // ★ THE INDEPENDENTS EXEMPTION, POSITIVELY: a zero-game container is NOT a league,
            //   so more than one of its schools may share one field.
            var indConf = mte.Conferences.Single(c => c.Games == 0);
            var maxIndInAField = 0;
            for (var seed = 1; seed <= 64; seed++)
                foreach (var e in MteSeatSeason(mte, seed, MteHistory.Empty).Active)
                    maxIndInAField = Math.Max(maxIndInAField,
                        e.Seats.Count(s => schoolById[s.SchoolId].ConferenceId == indConf.Id));
            Check("C4d: ★ two or more independents CAN share one field — the cap key, not an " +
                  "exemption branch",
                  maxIndInAField >= 2, $"max {maxIndInAField} independents in one field");

            // ★ THE FOUR-YEAR BOUNDARY, both sides, with hand-written history.
            var eightId = 10;
            var baseline = MteSeatSeason(mte, MteCheckSeed, MteHistory.Empty);
            var baseField = baseline.Active.Single(e => e.EventId == eightId)
                                   .Seats.Select(s => s.SchoolId).ToHashSet();

            var atN4 = new MteHistory();
            foreach (var id in baseField) { atN4.SeatedInEvent.Add((eightId, id)); atN4.Appearances[id] = 1; }
            var afterN4 = MteSeatSeason(mte, MteCheckSeed, atN4).Active.Single(e => e.EventId == eightId);
            var repeatsUnderExclusion = afterN4.Seats
                .Count(s => baseField.Contains(s.SchoolId) && s.Fallback != EventSeatFallback.BandScopeAndFourYear);
            Check("C4e: ★ an appearance inside the window excludes — every returning school had to " +
                  "relax the four-year rule to get back in",
                  repeatsUnderExclusion == 0, $"{repeatsUnderExclusion} unexplained repeat(s)");

            // The other side of the boundary: history the reader never loaded (N-5) is not
            // history at all, so the identical seed reproduces the identical field.
            var afterN5 = MteSeatSeason(mte, MteCheckSeed, MteHistory.Empty).Active.Single(e => e.EventId == eightId);
            Check("C4f: ★ outside the window there is no exclusion — the same seed seats the same field",
                  afterN5.Seats.Select(s => s.SchoolId).SequenceEqual(baseline.Active
                      .Single(e => e.EventId == eightId).Seats.Select(s => s.SchoolId)));

            // ★ RECENCY IS DECISIVE, and is applied even at the last level: give one school a
            //   recent appearance and its rival none, and the fresh one wins at every seed.
            var decisive = true;
            var pair = baseField.Take(2).ToArray();
            for (var seed = 1; seed <= 32 && decisive; seed++)
            {
                var h = new MteHistory();
                h.Appearances[pair[0]] = 3;   // heavily used
                var o = MteSeatSeason(mte, seed, h).Active.Single(e => e.EventId == eightId);
                var seats = o.Seats.Select(s => s.SchoolId).ToList();
                if (seats.Contains(pair[0]) && !seats.Contains(pair[1])) decisive = false;
            }
            Check("C4g: ★ the SOFT preference bites — a school with three recent appearances never " +
                  "displaces an equally-eligible school with none",
                  decisive);

            // ★ TIER ORDER DISCRIMINATES. A higher-id tier-1 event must seat before a lower-id
            //   tier-2 event; sorting by id alone would fail this.
            var tierProbe = Swap(new List<WorldEvent>
            {
                mte.Events.Single(e => e.Id == 11) with { Id = 2, Tier = 2, ForcedActive = true },
                mte.Events.Single(e => e.Id == 13) with { Id = 9, Tier = 1, ForcedActive = true },
            });
            var tierOrdered = MteSeatSeason(tierProbe, MteCheckSeed, MteHistory.Empty);
            Check("C4h: ★ TIER ORDER, NOT ID ORDER — the tier-1 event with the higher id seats first",
                  tierOrdered.Active[0].EventId == 9 && tierOrdered.Active[1].EventId == 2,
                  string.Join(" then ", tierOrdered.Active.Select(e => $"id {e.EventId} (tier {e.Tier})")));

            // ★ SLOT ORDER MATTERS. Reversing the authored slots of the eight-team event must
            //   be capable of producing a different legal field; if it never did, the authored
            //   order would be decoration.
            var reversed = Swap(new List<WorldEvent>
            {
                mte.Events.Single(e => e.Id == eightId) with
                {
                    Slots = mte.Events.Single(e => e.Id == eightId).Slots.Reverse().ToList(),
                    ForcedActive = true,
                },
            });
            var forward = Swap(new List<WorldEvent>
            {
                mte.Events.Single(e => e.Id == eightId) with { ForcedActive = true },
            });
            var revField = MteSeatSeason(reversed, MteCheckSeed, MteHistory.Empty).Active[0]
                           .Seats.Select(s => s.SchoolId).ToList();
            var fwdField = MteSeatSeason(forward, MteCheckSeed, MteHistory.Empty).Active[0]
                           .Seats.Select(s => s.SchoolId).ToList();
            Check("C4i: ★ the AUTHORED slot order is load-bearing — reversing it seats a different field",
                  !revField.SequenceEqual(fwdField));

            // ★ SEATED SHORT is a diagnostic, never a refusal — and a field can be short at zero.
            var starved = Swap(new List<WorldEvent>
            {
                mte.Events.Single(e => e.Id == eightId) with
                {
                    Slots = Enumerable.Range(0, 8).Select(_ => new WorldEventSlot(100, 100, "any")).ToList(),
                    ForcedActive = true,
                },
            });
            // A band nothing can satisfy still fills at the last level (the band is relaxed), so
            // starvation has to come from the pool itself: a field wider than the world's
            // distinct cap keys.
            var tinyPool = MteSeatSeason(
                MteStarvedWorld(mte), MteCheckSeed, MteHistory.Empty);
            var shortEvent = tinyPool.Active.SingleOrDefault();
            Check("C4j: ★ a field the world cannot fill is SeatedShort — a diagnostic, never a refusal, " +
                  "and the season stays valid",
                  shortEvent is not null
                  && shortEvent.SeatingStatus == EventSeatingStatus.SeatedShort
                  && shortEvent.Seats.Count < shortEvent.FieldSize,
                  shortEvent is null ? "no event" : $"{shortEvent.Seats.Count}/{shortEvent.FieldSize} seated");
            Check("C4k: an impossible band does NOT starve a field — the band relaxes, which is the " +
                  "whole point of the ladder",
                  MteSeatSeason(starved, MteCheckSeed, MteHistory.Empty).Active[0].Seats.Count == 8);

            // ★ THE PULL — measured on the axis it is about (league composition), with a
            //   flat-draw negative control. A per-event table looks plausible under BOTH
            //   settings, which is exactly why the check is league-wide.
            var mteSchoolById = mte.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
            double MeanSeatedPrestige(int pull)
            {
                var vals = new List<int>();
                for (var seed = 1; seed <= 32; seed++)
                    foreach (var e in MteSeatSeason(mte, seed, MteHistory.Empty, pull).Active)
                        foreach (var s in e.Seats) vals.Add(mteSchoolById[s.SchoolId]);
                return vals.Count == 0 ? 0 : vals.Average();
            }
            var flat = MeanSeatedPrestige(1);
            var pulled = MeanSeatedPrestige(MteSeatPull);
            Check("C4l: ★ THE PULL DOES SOMETHING — over 32 seeds the schools a seat lands on are " +
                  "measurably stronger than a flat draw within the same bands. Without this control " +
                  "the pull could be wired to nothing and every other check would still pass",
                  pulled > flat + 1.0,
                  $"flat {flat:F1} vs pull-{MteSeatPull} {pulled:F1}");
            Check("C4m: and the pull adds NO randomness of its own — the same seed still seats the " +
                  "identical field, and a flat draw remains deterministic too",
                  Fingerprint(MteSeatSeason(mte, MteCheckSeed, MteHistory.Empty, 1))
                      == Fingerprint(MteSeatSeason(mte, MteCheckSeed, MteHistory.Empty, 1))
                  && Fingerprint(MteSeatSeason(mte, MteCheckSeed, MteHistory.Empty))
                      == Fingerprint(a1));

            // ════════════════════════════════════════════════════════════════════
            //  C5 — THE OVERLAP REFUSAL, from the production sequence, negatively.
            // ════════════════════════════════════════════════════════════════════
            //  Constructed from the WINDOW side rather than by authoring a league into
            //  November: both produce the same collision against the same refusal, and the
            //  window side needs no second date-layer fixture. The stock world cannot reach
            //  it either way — its earliest conference night is December 7.
            //
            //  ★ AND THE WINDOW IS DERIVED, NOT GUESSED. A hand-picked window is a coin flip
            //    on whether the seated school happens to play those three nights, which is
            //    exactly the kind of check that passes for a year and then goes red when a
            //    fixture is edited. So: seat the field, date the slate, find a night a seated
            //    school actually plays, and author the window over it.
            var clashSeatOnly = Swap(new List<WorldEvent>
            {
                mte.Events.Single(e => e.Id == eightId) with { ForcedActive = true },
            });
            var clashSeating = MteSeatSeason(clashSeatOnly, MteCheckSeed, MteHistory.Empty);
            var seatedIds = clashSeating.Active[0].Seats.Select(s => s.SchoolId).ToHashSet();
            var clashProbe = BuildSeasonSchedule(clashSeatOnly, MteCheckSeed);
            SeasonDateSchedule(clashSeatOnly, clashProbe, SeasonDefaultStartYear);
            var hit = clashProbe.First(g => g.Date is not null
                                            && (seatedIds.Contains(g.HomeId) || seatedIds.Contains(g.AwayId)));
            var hitDate = hit.Date!.Value;
            var clashWorld = Swap(new List<WorldEvent>
            {
                mte.Events.Single(e => e.Id == eightId) with
                {
                    FirstDay = hitDate.ToString("MM-dd", CultureInfo.InvariantCulture),
                    LastDay = hitDate.AddDays(2).ToString("MM-dd", CultureInfo.InvariantCulture),
                    ForcedActive = true,
                },
            });
            var clashHistory = Path.Combine(scratch, "clash.json");
            string? clashMsg;
            long peekedBefore, peekedAfter;
            using (var store = HistoryStore.Open(clashHistory, WorldFingerprint(clashWorld)))
            {
                peekedBefore = store.PeekNextSeasonId;
                clashMsg = Refusal(() =>
                    RunSeasonCore(clashWorld, MteCheckSeed, configPath, verbose: false, store));
                peekedAfter = store.PeekNextSeasonId;
            }
            Check("C5a: ★ a seated school double-booked inside its window refuses BY NAME, naming the " +
                  "school, the event, the window, the date and the opponent",
                  clashMsg is not null && clashMsg.Contains("SEASON EVENT OVERLAP")
                  && clashMsg.Contains("is seated in") && clashMsg.Contains("conference game vs"),
                  clashMsg is null ? "NO REFUSAL" : "refused");
            Check("C5b: ★ THE TRANSACTION, NEGATIVELY — the refusal spent NO season id",
                  peekedBefore == peekedAfter, $"peek {peekedBefore} → {peekedAfter}");
            Check("C5c: ★ and wrote NO record file",
                  !Directory.Exists(MteRecordFolderFor(clashHistory))
                  || Directory.GetFiles(MteRecordFolderFor(clashHistory)).Length == 0);

            // ════════════════════════════════════════════════════════════════════
            //  C6 — THE RECORD: round trip, career binding, holes, gaps.
            // ════════════════════════════════════════════════════════════════════
            var careerPath = Path.Combine(scratch, "career.json");
            EventSeasonOutcome s1, s2;
            using (var store = HistoryStore.Open(careerPath, WorldFingerprint(mte)))
                s1 = RunSeasonCore(mte, MteCheckSeed, configPath, verbose: false, store).Events;
            using (var store = HistoryStore.Open(careerPath, WorldFingerprint(mte)))
                s2 = RunSeasonCore(mte, MteCheckSeed, configPath, verbose: false, store).Events;

            Check("C6a: a career-bound season writes its record, empty pool or not",
                  s1.RecordStatus == EventRecordStatus.Written
                  && s2.RecordStatus == EventRecordStatus.Written
                  && File.Exists(MteRecordPathFor(careerPath, 1))
                  && File.Exists(MteRecordPathFor(careerPath, 2)));

            var f1 = s1.Seating.Active.Single(e => e.EventId == eightId).Seats.Select(x => x.SchoolId).ToHashSet();
            var f2 = s2.Seating.Active.Single(e => e.EventId == eightId).Seats.ToList();
            var unexplainedRepeat = f2.Count(x => f1.Contains(x.SchoolId)
                                                  && x.Fallback != EventSeatFallback.BandScopeAndFourYear);
            Check("C6b: ★ season 2 PROVABLY consulted season 1's record — at the same seed the field " +
                  "turns over, and anyone who came back had to relax the four-year rule",
                  unexplainedRepeat == 0 && !f2.Select(x => x.SchoolId).ToHashSet().SetEquals(f1),
                  $"{f2.Count(x => f1.Contains(x.SchoolId))} of 8 returned, all via fallback");

            // Binding: the three that make a hole, and the one that deliberately does NOT.
            var recPath = MteRecordPathFor(careerPath, 1);
            var good = File.ReadAllText(recPath);

            long ReadCount(string mutated)
            {
                File.WriteAllText(recPath, mutated);
                using var store = HistoryStore.Open(careerPath, WorldFingerprint(mte));
                var h = MteReadHistory(store, 2);
                return h.SeatedInEvent.Count;
            }
            var wholeYear = ReadCount(good);
            Check("C6c: the intact record reads back its whole year",
                  wholeYear > 0, $"{wholeYear} seat facts");
            Check("C6d: a record from ANOTHER CAREER is a hole",
                  ReadCount(good.Replace("\"historyId\":", "\"historyId\": \"not-this-career\", \"ignored\":")) == 0);
            Check("C6e: a record naming a DIFFERENT SEASON is a hole",
                  ReadCount(good.Replace("\"seasonId\": 1", "\"seasonId\": 77")) == 0);
            Check("C6f: an unsupported record version is a hole — and v1 is NOT one: a "
                  + "pre-contract record keeps its whole tournament memory (the S103 widening "
                  + "this check would have silently missed)",
                  ReadCount(good.Replace("\"formatVersion\": 2", "\"formatVersion\": 99")) == 0
                  && ReadCount(good.Replace("\"formatVersion\": 2", "\"formatVersion\": 1")) == wholeYear);
            Check("C6g: malformed JSON is a hole",
                  ReadCount("{ this is not json") == 0);
            Check("C6h: ★ A CHANGED WORLD FINGERPRINT IS *ACCEPTED* — the record binds to the CAREER, " +
                  "not the world. Asserted directly, so it reads as the ruling it is rather than as " +
                  "validation somebody forgot",
                  ReadCount(good.Replace("\"worldFingerprint\": \"sha256-v1:", "\"worldFingerprint\": \"sha256-v1:ff"))
                  == wholeYear);

            File.WriteAllText(recPath, good);
            File.Delete(recPath);
            using (var store = HistoryStore.Open(careerPath, WorldFingerprint(mte)))
            {
                var h = MteReadHistory(store, 3);
                Check("C6i: a deleted year is a silent hole and its neighbour still reads",
                      h.SeatedInEvent.Count > 0);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — WRITE FAILURE AFTER THE COMMIT. The one deliberately non-atomic seam.
            // ════════════════════════════════════════════════════════════════════
            var failPath = Path.Combine(scratch, "failing.json");
            // The seam, with no production test hook: a regular FILE standing where the
            // record folder must be. Directory creation fails, so publication fails before
            // any final file can exist — which is exactly the branch C7 is about.
            File.WriteAllText(MteRecordFolderFor(failPath), "not a directory");
            EventSeasonOutcome failed;
            long spentBefore, spentAfter;
            using (var store = HistoryStore.Open(failPath, WorldFingerprint(mte)))
            {
                spentBefore = store.PeekNextSeasonId;
                failed = RunSeasonCore(mte, MteCheckSeed, configPath, verbose: false, store).Events;
                spentAfter = store.PeekNextSeasonId;
            }
            Check("C7a: ★ a record write that fails AFTER the commit does not invalidate the season — " +
                  "the schedule played and the season id is provably spent",
                  failed.RecordStatus == EventRecordStatus.WriteFailed && spentAfter == spentBefore + 1,
                  $"peek {spentBefore} → {spentAfter}");
            Check("C7b: the page carries a stable diagnostic rather than a raw exception message",
                  MtePageLines(failed).Any(l => l.Contains("EVENT RECORD NOT WRITTEN")));

            // ════════════════════════════════════════════════════════════════════
            //  C8 — ISOLATION. Events cannot perturb one possession of basketball.
            // ════════════════════════════════════════════════════════════════════
            var withEvents = RunSeasonCore(Swap(forcedOn), MteCheckSeed, configPath, verbose: false);
            var without = RunSeasonCore(Swap(forcedOff), MteCheckSeed, configPath, verbose: false);
            // ★ S98 NARROWED THIS, AND THE NARROWING IS FORCED. Until this session an active
            //   pool played no games, so "the complete per-game results are identical" was the
            //   right claim. The brackets now PLAY, so an active pool legitimately has more
            //   games than a dormant one and the old comparison would go red with nothing
            //   wrong. The surviving claim — the one this check was always about — is that
            //   events cannot perturb one possession of CONFERENCE basketball, and that is a
            //   prefix comparison. C8d below keeps it from becoming vacuous.
            var confOn = SeasonFingerprint(
                withEvents.Results.Take(withEvents.ConferenceGameCount).ToList(),
                withEvents.PossessionCounts.Take(withEvents.ConferenceGameCount).ToList());
            var fpOff = SeasonFingerprint(without.Results, without.PossessionCounts);
            Check("C8a: ★ every event active vs every event dormant, same seed — the COMPLETE per-game " +
                  "CONFERENCE results and possession counts are identical. Not win totals: a seed " +
                  "perturbation could preserve winners while moving every box score",
                  confOn == fpOff, confOn == fpOff ? confOn[..16] + "…" : $"{confOn[..16]}… vs {fpOff[..16]}…");
            Check("C8b: and the schedule itself is untouched",
                  withEvents.Fingerprint == without.Fingerprint
                  && withEvents.DatedFingerprint == without.DatedFingerprint);
            Check("C8c: the discriminator — the two runs really did seat different pools, so C8a is not " +
                  "comparing a run against itself",
                  withEvents.Events.Seating.Active.Count > 0 && without.Events.Seating.Active.Count == 0);
            // ★ S98 — the SECOND discriminator, and it is what stops C8a decaying into a
            //   comparison of two identical conference-only seasons. The active run really did
            //   play extra basketball beyond the prefix; the dormant run really did play none.
            Check("C8d: ★ S98 — and the active run PLAYED its brackets while the dormant one played " +
                  "nothing, so the prefix in C8a is a prefix of something longer",
                  withEvents.TournamentGameCount > 0
                  && withEvents.Results.Count == withEvents.ConferenceGameCount + withEvents.TournamentGameCount
                  && without.TournamentGameCount == 0
                  && without.Results.Count == without.ConferenceGameCount,
                  $"{withEvents.TournamentGameCount} tournament games on, {without.TournamentGameCount} off");

            // ════════════════════════════════════════════════════════════════════
            //  C9 — THE COLLISION REFUSAL, before anything is spent.
            // ════════════════════════════════════════════════════════════════════
            var collidePath = Path.Combine(scratch, "collide.json");
            string? collideMsg;
            long cBefore, cAfter;
            byte[] plantedBefore, plantedAfter;
            using (var store = HistoryStore.Open(collidePath, WorldFingerprint(mte)))
            {
                cBefore = store.PeekNextSeasonId;
                Directory.CreateDirectory(MteRecordFolderFor(collidePath));
                var planted = MteRecordPathFor(collidePath, cBefore);
                File.WriteAllText(planted, "{ \"planted\": true }");
                plantedBefore = File.ReadAllBytes(planted);
                collideMsg = Refusal(() =>
                    RunSeasonCore(mte, MteCheckSeed, configPath, verbose: false, store));
                cAfter = store.PeekNextSeasonId;
                plantedAfter = File.ReadAllBytes(planted);
            }
            Check("C9a: ★ a record already claiming the pending season refuses the schedule commit, " +
                  "naming the path",
                  collideMsg is not null && collideMsg.Contains("SEASON EVENT RECORD COLLISION")
                  && collideMsg.Contains("season-" + cBefore.ToString(CultureInfo.InvariantCulture)),
                  collideMsg is null ? "NO REFUSAL" : "refused");
            Check("C9b: ★ the pending season id is provably UNSPENT — the precheck runs before the commit",
                  cBefore == cAfter, $"peek {cBefore} → {cAfter}");
            Check("C9c: the existing file is byte-untouched and was never mistaken for this run's output",
                  plantedBefore.SequenceEqual(plantedAfter));
        }
        catch (Exception ex)
        {
            Check("Phase 88 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (IOException) { /* scratch cleanup is best effort */ }
        }

        Console.WriteLine(pass ? "  Phase 88: PASS" : "  Phase 88: FAIL");
        return pass;
    }

    // ── Small constructors used only by Phase 88 ─────────────────────────────────

    private static List<WorldEventSlot> InvertFirstBand(WorldEvent e)
    {
        var slots = e.Slots.ToList();
        slots[0] = slots[0] with { BandLo = 90, BandHi = 10 };
        return slots;
    }

    private static List<WorldEventSlot> OutOfDomainBand(WorldEvent e)
    {
        var slots = e.Slots.ToList();
        slots[0] = slots[0] with { BandLo = 0, BandHi = 200 };
        return slots;
    }

    private static List<WorldEventSlot> BadScope(WorldEvent e)
    {
        var slots = e.Slots.ToList();
        slots[0] = slots[0] with { Scope = "elite" };
        return slots;
    }

    /// <summary>A world whose pool cannot fill an eight-team field: one playing league, so
    /// one conferenceCapKey, so exactly one seat is fillable no matter how the soft rules
    /// relax. Starvation has to come from the ABSOLUTES — the ladder can relax a band, and a
    /// check that starved a field with an impossible band would be testing nothing.</summary>
    private static WorldFile MteStarvedWorld(WorldFile mte)
    {
        var league = mte.Conferences.First(c => c.Games > 0);
        var members = mte.Schools.Where(s => s.ConferenceId == league.Id).ToList();
        return new WorldFile
        {
            SchemaVersion = mte.SchemaVersion, Kind = mte.Kind, EraLabel = mte.EraLabel,
            Division = mte.Division, WorldSeed = mte.WorldSeed, Tiers = mte.Tiers,
            Conferences = new List<WorldConference> { league },
            Places = mte.Places,
            Schools = members,
            Events = new List<WorldEvent>
            {
                mte.Events.Single(e => e.Id == 10) with { ForcedActive = true },
            },
        };
    }
}
