using System.Globalization;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 95 — SHOWCASES (S104: the event pool learns a second kind).
//
//  A showcase invites four schools out for two stand-alone games in one day.
//  No bracket, no advancement, no placement, no champion.
//
//  ── The checks that actually discriminate ──────────────────────────────────
//  Most of what this session touches would stay green under a WRONG build, and
//  the checks below were chosen for the opposite property. Three in particular:
//
//   • THE RELEASE FIXTURE (R30). A showcase that cannot fill must free every
//     school it provisionally held. On a world where the showcase CAN fill,
//     every wall check passes whether the release exists or not — so the
//     fixture is built to starve one showcase deliberately and then prove its
//     stranded invitees seat in a later one the same season.
//
//   • THE OVERLAPPING FIXTURE (A2). Every double-booking check passes on
//     NON-overlapping dates, which is what the stock slate mostly has. Only a
//     constructed collision discriminates.
//
//   • THE ZERO PATH BY SUBTRACTION. The stock world with its showcases removed
//     must reproduce the pre-S104 event-games fingerprint EXACTLY. That is what
//     proves the session's machinery — the per-kind walls, the packed draw key,
//     the provisional commit, the changed play order — moved nothing on its own,
//     and therefore that everything which DID move was moved by showcases.
//
//  ── What this phase deliberately does not prove ────────────────────────────
//  Any basketball value. No active count, no seat quality, no fallback or
//  radius-step rate, no distance, no participation count. Page-only calibration
//  holds: the page prints what happened and Emmett judges the slate by reading
//  it, never by a test agreeing with it.
// ============================================================================

internal static partial class Program
{
    private const long ShowcaseCheckSeed = 20260720;

    /// <summary>★ THE PRE-S104 EVENT-GAMES GOLDEN. The stock world's tournament half before
    /// this session existed. It is asserted against the stock world with its showcases
    /// REMOVED, which is the only form in which it can still be true — and the only form in
    /// which it proves anything.</summary>
    private const string ShowcaseGoldenPreS104EventGamesFp =
        "26f2b8ff16ba169403aa741bb93ee9d5426d656fef38a83cf884c27a005c2c4b";

    private static bool Phase95ShowcasesCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 95 — Showcases (S104: the event pool learns a second kind — two " +
                          "stand-alone games in one day, no bracket and no champion. The per-kind " +
                          "walls, the overlapping-window fixture, the R30 release fixture, the " +
                          "tournament-only exemption, seat identity through the record round-trip, " +
                          "the radius ladder and its exact boundary, provenance parsing, and the " +
                          "zero path by subtraction) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        string? Refusal(Action act)
        {
            try { act(); return null; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }

        try
        {
            string WorldPath(string file) =>
                Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var tiny = LoadWorld(WorldPath("fixture-tiny.world.json"));
            var mte = LoadWorld(WorldPath("fixture-mte.world.json"));

            WorldFile With(WorldFile w, IEnumerable<WorldEvent> evs) => new()
            {
                SchemaVersion = w.SchemaVersion, Kind = w.Kind, EraLabel = w.EraLabel,
                Division = w.Division, WorldSeed = w.WorldSeed, Tiers = w.Tiers,
                Conferences = w.Conferences, Places = w.Places, Schools = w.Schools,
                Events = evs.ToList(),
            };

            WorldEventSlot Slot(int lo, int hi) => new(lo, hi, "any");
            WorldEvent Showcase(int id, int tier, int placeId, string day, int? draw,
                                params WorldEventSlot[] slots)
                => new(id, $"Showcase {id.ToString(CultureInfo.InvariantCulture)}", tier, placeId,
                       day, day, 4, slots.ToList(), 1.0, true,
                       WorldEventKindShowcase, draw);

            // ════════════════════════════════════════════════════════════════════
            //  C1 — THE AUTHORED SHAPE. Refusals by name, and the loader's own
            //       walls held to the kind rather than to the field size.
            // ════════════════════════════════════════════════════════════════════
            {
                var wide = Showcase(900, 1, stock.Places[0].PlaceId, "11-20", null,
                                    Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99));

                var cases = new (string What, WorldEvent Ev, string Expect)[]
                {
                    ("a showcase of eight",   wide with { FieldSize = 8 },              "seats EXACTLY 4"),
                    ("a two-day showcase",    wide with { LastDay = "11-21" },          "plays on EXACTLY 1"),
                    ("a zero radius",         wide with { Draw = 0 },                   "National is null, never zero"),
                    ("a negative radius",     wide with { Draw = -5 },                  "National is null, never zero"),
                    ("an unknown kind word",  wide with { Kind = "jamboree" },          "not in the vocabulary"),
                };
                foreach (var (what, ev, expect) in cases)
                {
                    var msg = Refusal(() => ValidateWorld(With(stock, new[] { ev })));
                    Check($"C1a: {what} refused by name",
                          msg is not null && msg.Contains(expect) && msg.Contains(ev.Name),
                          msg is null ? "NO REFUSAL" : "");
                }

                // ★ A TOURNAMENT MAY NOT CARRY A RADIUS THIS SESSION. Draw lives on the
                //   shared shape so a future ruling costs no migration; authoring one today
                //   is refused rather than silently meaning something nobody designed.
                var radiusTourney = Refusal(() => ValidateWorld(With(stock, new[]
                {
                    wide with { Kind = WorldEventKindTournament, Draw = 500, LastDay = "11-21" },
                })));
                Check("C1b: ★ a TOURNAMENT with a radius draw is refused — draw is shared shape, " +
                      "but a regional tournament is an unmade decision",
                      radiusTourney is not null && radiusTourney.Contains("unmade design decision"),
                      radiusTourney is null ? "NO REFUSAL" : "");

                // ★ THE OLD-SHAPE WORLD STILL LOADS. Optional-with-defaults is the whole
                //   reason there is no schemaVersion bump, and this is that claim asserted.
                var oldShape = Refusal(() => ValidateWorld(mte));
                Check("C1c: ★ a world authoring NO kind and NO draw loads unchanged — every event " +
                      "reads as a National tournament, which is what it always was",
                      oldShape is null && mte.Events.All(e => !e.IsShowcase && e.Draw is null),
                      oldShape ?? "");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — R25: TWO WALLS, ONE PER KIND.
            // ════════════════════════════════════════════════════════════════════
            {
                // A tournament and a showcase on dates that cannot collide.
                var tourney = mte.Events.First() with { Id = 1, Tier = 1, ForcedActive = true };
                var show = Showcase(50, 2, mte.Places.First(p => p.PlaceId != tourney.PlaceId).PlaceId,
                                    "12-05", null, Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99));
                var w = With(mte, new[] { tourney, show });
                var seating = MteSeatSeason(w, ShowcaseCheckSeed, MteHistory.Empty);

                var t = seating.Active.Single(e => e.EventId == 1);
                var s = seating.Active.Single(e => e.EventId == 50);
                var both = t.Seats.Select(x => x.SchoolId).Intersect(s.Seats.Select(x => x.SchoolId)).ToList();

                Check("C2a: ★ R25 — a school may sit in a tournament AND a showcase in one season",
                      s.Seats.Count == 4 && both.Count > 0,
                      $"{both.Count} school(s) in both");
                Check("C2b: and never twice within one kind",
                      seating.Active.Where(e => !e.IsShowcase).SelectMany(e => e.Seats)
                             .GroupBy(x => x.SchoolId).All(g => g.Count() == 1)
                      && seating.Active.Where(e => e.IsShowcase).SelectMany(e => e.Seats)
                             .GroupBy(x => x.SchoolId).All(g => g.Count() == 1));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — ★ THE OVERLAPPING-WINDOW FIXTURE (A2). The discriminator: every
            //       wall check passes on non-overlapping dates, so only a
            //       constructed collision can tell whether the rule exists.
            // ════════════════════════════════════════════════════════════════════
            {
                var baseT = mte.Events.First();
                var otherPlace = mte.Places.First(p => p.PlaceId != baseT.PlaceId).PlaceId;

                // The showcase seats FIRST (tier 1 against the tournament's tier 2) and its
                // single day sits inside the tournament's window.
                var showFirst = Showcase(60, 1, otherPlace, baseT.FirstDay, null,
                                         Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99));
                var tourneySecond = baseT with { Id = 61, Tier = 2, ForcedActive = true };
                var overlap = MteSeatSeason(
                    With(mte, new[] { showFirst, tourneySecond }), ShowcaseCheckSeed, MteHistory.Empty);
                var sc = overlap.Active.Single(e => e.EventId == 60);
                var tn = overlap.Active.Single(e => e.EventId == 61);
                var clash = sc.Seats.Select(x => x.SchoolId)
                              .Intersect(tn.Seats.Select(x => x.SchoolId)).ToList();

                Check("C3a: ★ THE DOUBLE-BOOKING EXCLUSION BITES — a school seated in the showcase " +
                      "is NOT in the tournament whose window contains that day; nobody is in two " +
                      "places on one night",
                      sc.Seats.Count == 4 && clash.Count == 0,
                      clash.Count == 0 ? "" : $"{clash.Count} double-booked");

                // ★ THE NEGATIVE CONTROL, and without it C3a proves nothing: on dates that do
                //   NOT collide the same two events DO share schools, which is what shows the
                //   exclusion above was the dates talking and not the kind wall.
                var showAway = Showcase(60, 1, otherPlace, "12-05", null,
                                        Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99));
                var apart = MteSeatSeason(
                    With(mte, new[] { showAway, tourneySecond }), ShowcaseCheckSeed, MteHistory.Empty);
                var shared = apart.Active.Single(e => e.EventId == 60).Seats.Select(x => x.SchoolId)
                    .Intersect(apart.Active.Single(e => e.EventId == 61).Seats.Select(x => x.SchoolId))
                    .ToList();
                Check("C3b: ★ the discriminator — move that showcase off the window and the SAME two " +
                      "events share schools again, so C3a measured the dates and not the kind wall",
                      shared.Count > 0, $"{shared.Count} shared when the dates clear");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — ★ THE R30 RELEASE FIXTURE. A showcase that cannot fill creates
            //       zero pairings, zero charges, consumes nobody, burns no clock —
            //       and its stranded invitees seat in a LATER showcase the same
            //       season, which is what makes the standby events real.
            // ════════════════════════════════════════════════════════════════════
            {
                // A radius so tight that only a handful of schools are reachable, and a band
                // narrow enough that the fourth seat cannot be filled at any step.
                var home = tiny.Places.First();
                var farthest = tiny.Schools
                    .OrderByDescending(s => GeoDistance.DistanceMiles(
                        home.Coordinate, tiny.Places.First(p => p.PlaceId == s.PlaceId).Coordinate))
                    .First();
                var lonely = tiny.Places.First(p => p.PlaceId == farthest.PlaceId);

                // Seat 1..4 all demand an impossible band, so the ladder runs to its floor and
                // the radius runs out — the degenerate fail-closed path, deliberately reached.
                var starved = Showcase(70, 1, lonely.PlaceId, "12-05", 1,
                                       Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99));
                var rescue = Showcase(71, 2, tiny.Places.Skip(1).First().PlaceId, "12-05", null,
                                      Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99));
                var released = MteSeatSeason(
                    With(tiny, new[] { starved, rescue }), ShowcaseCheckSeed, MteHistory.Empty);

                var shortOne = released.Active.Single(e => e.EventId == 70);
                var laterOne = released.Active.Single(e => e.EventId == 71);

                Check("C4a: ★ the starved showcase really did seat SHORT — the fixture reaches the " +
                      "degenerate path rather than quietly filling",
                      shortOne.SeatingStatus == EventSeatingStatus.SeatedShort,
                      $"{shortOne.Seats.Count}/4");
                Check("C4b: ★ R30 — a short showcase creates ZERO pairings and therefore zero " +
                      "charges and zero used pairs",
                      MteShowcasePairingsOf(shortOne).Count == 0
                      && MteAllShowcasePairings(released).All(p => p.EventId != 70));
                Check("C4c: ★ R30 — and it CONSUMES NOBODY: the later showcase seats a full field, " +
                      "which is what makes a standby event a real replacement",
                      laterOne.SeatingStatus == EventSeatingStatus.Complete && laterOne.Seats.Count == 4);
                Check("C4d: ★ R30 — a short showcase burns nobody's four-year clock either: it " +
                      "carries no seats forward at all, so the record cannot record an appearance",
                      shortOne.Seats.Count == 0);
                Check("C4e: and it does not play — no game, no reservation, no ordinal",
                      !MteEventPlays(shortOne)
                      && MteExpectedBracketSlots(released).All(k => k.EventId != 70));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — A3/A4: THE KIND OWNS THE PLAY PATH.
            // ════════════════════════════════════════════════════════════════════
            {
                var show = Showcase(80, 1, stock.Places[0].PlaceId, "12-05", null,
                                    Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99));
                var seated = MteSeatSeason(With(stock, new[] { show }), ShowcaseCheckSeed, MteHistory.Empty);
                var e = seated.Active.Single();

                var routed = Refusal(() => MteBracketRoutesFor(e));
                Check("C5a: ★ A3 — a showcase reaching a bracket route table REFUSES BY NAME. Its " +
                      "field of four would otherwise route into BracketRoutes4 and produce a " +
                      "plausible three-game bracket with a champion, every structural check green",
                      routed is not null && routed.Contains("has no bracket"),
                      routed is null ? "NO REFUSAL" : "");

                Check("C5b: ★ two games, not three — the count is dispatched on the KIND, never on " +
                      "the field size",
                      MteEventGameCount(e) == 2 && BracketGameCount(4) == 4);

                var pairings = MteShowcasePairingsOf(e);
                var bySeat = e.Seats.ToDictionary(s => s.Seat);
                Check("C5c: ★ R27 — roles come from STORED SEAT NUMBERS: game 0 is seats 1-2 (the " +
                      "headliner), game 1 is seats 3-4 (the undercard)",
                      pairings.Count == 2
                      && pairings[0] is { GameIndex: 0, SeatA: 1, SeatB: 2 }
                      && pairings[1] is { GameIndex: 1, SeatA: 3, SeatB: 4 }
                      && pairings[0].SchoolAId == bySeat[1].SchoolId
                      && pairings[1].SchoolBId == bySeat[4].SchoolId);

                // ★ THE ANTI-SORT CONTROL. Re-ordering the seat LIST must not change which
                //   game is the headliner — the seat number is the fact, its position is not.
                var shuffled = e with { Seats = e.Seats.Reverse().ToList() };
                var shuffledPairs = MteShowcasePairingsOf(shuffled);
                Check("C5d: ★ and reversing the seat LIST changes nothing — an upset or a re-sort " +
                      "must never relabel which game was authored as the nightcap",
                      shuffledPairs[0].SchoolAId == pairings[0].SchoolAId
                      && shuffledPairs[1].SchoolBId == pairings[1].SchoolBId);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — A1: THE EXEMPTION SEES ONLY TOURNAMENT SEATS.
            // ════════════════════════════════════════════════════════════════════
            {
                var show = Showcase(85, 1, stock.Places[0].PlaceId, "12-05", null,
                                    Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99));
                var seated = MteSeatSeason(With(stock, new[] { show }), ShowcaseCheckSeed, MteHistory.Empty);
                var report = BuildNonConferenceRequests(stock, seated);
                var confById = stock.Conferences.ToDictionary(c => c.Id);

                var rows = seated.Active.Single().Seats
                    .Select(s => report.Schools.First(r => r.SchoolId == s.SchoolId))
                    .Where(r => !r.IsIndependent).ToList();

                Check("C6a: ★ A1 — a SHOWCASE-seated school is NOT flagged as event-seated, so it " +
                      "plays 29 and not 31. Left unfiltered it would take a 31-game season and " +
                      "three phantom event games, and the totals would still reconcile",
                      rows.Count > 0 && rows.All(r => !r.Seated),
                      $"{rows.Count(r => r.Seated)} wrongly seated");

                Check("C6b: ★ and its OPEN games are the unseated arithmetic exactly — " +
                      "29 minus conference games",
                      rows.All(r => r.Open == NonConSeasonGamesUnseated
                                    - confById[stock.Schools.First(s => s.Id == r.SchoolId).ConferenceId].Games),
                      string.Join(", ", rows.Take(3).Select(r => $"{r.SchoolName} open {r.Open}")));

                Check("C6c: ★ R26 — the showcase game is CHARGED, never added: home + neutral + " +
                      "road is one BELOW open, and every one of them owes exactly one showcase game",
                      rows.All(r => r.ShowcaseGames == 1
                                    && r.Home + r.Neutral + r.Road == r.Open - 1),
                      string.Join(", ", rows.Take(3).Select(
                          r => $"{r.SchoolName} {r.Home}/{r.Neutral}/{r.Road} of {r.Open}")));

                // ★ THE WORKED NOVEMBER (§3.4), asserted rather than described: a school in a
                //   tournament AND a showcase plays 31, its tournament accounting for what its
                //   bracket guarantees, and the showcase eating one of the remaining open
                //   games — never a 32nd.
                //
                //   ★ CONSTRUCTED, NOT DRAWN. An earlier version of this check seated a real
                //   tournament and a real showcase and looked for a school in both — which is
                //   four seats out of 347 schools finding eight, so it found nobody and the
                //   check proved nothing while looking thorough. The arithmetic is what is
                //   under test, so the seating that exercises it is built directly.
                //
                //   ★ S105.1 — RUN AT BOTH TOURNAMENT SIZES, because three concepts collide in
                //   this one school and all three must stay separate: how big the tournament
                //   is (4 or 8), what the tournament is charged (2 or 3, and it MOVES with the
                //   size), and what the showcase is worth (always one charged game, always
                //   zero exemption, and it does NOT move). The old single case ran at field 4
                //   and expected three — the bug, hardcoded into the check that was supposed
                //   to police it. RECOMPUTED, never relaxed.
                var subject = stock.Schools.First(s =>
                    stock.Conferences.First(c => c.Id == s.ConferenceId).Games > 0);
                var subjectConfGames = stock.Conferences.First(c => c.Id == subject.ConferenceId).Games;

                EventSeat Seat(int n) => new(n, subject.Id, subject.Name, n - 1, EventSeatFallback.None);

                EventSeatingOutcome ConstructBoth(int tournamentField) => new()
                {
                    Active = new[]
                    {
                        new SeatedEvent(1, $"Constructed Tournament {tournamentField}", 1,
                            stock.Places[0].PlaceId, stock.Places[0].Name, "11-24", "11-25",
                            tournamentField, EventSeatingStatus.Complete, new[] { Seat(1) }),
                        new SeatedEvent(2, "Constructed Showcase", 1, stock.Places[1].PlaceId,
                            stock.Places[1].Name, "12-05", "12-05", 4,
                            EventSeatingStatus.Complete,
                            new[]
                            {
                                new EventSeat(1, subject.Id, subject.Name, 0, EventSeatFallback.None),
                                new EventSeat(2, stock.Schools[1].Id, stock.Schools[1].Name, 1, EventSeatFallback.None),
                                new EventSeat(3, stock.Schools[2].Id, stock.Schools[2].Name, 2, EventSeatFallback.None),
                                new EventSeat(4, stock.Schools[3].Id, stock.Schools[3].Name, 3, EventSeatFallback.None),
                            },
                            WorldEventKindShowcase),
                    },
                    Dormant = Array.Empty<DormantEvent>(),
                };

                var matrixOk = true; var matrixDetail = new List<string>();
                foreach (var (field, exemption) in new[] { (4, 2), (8, 3) })
                {
                    var bothReport = BuildNonConferenceRequests(stock, ConstructBoth(field));
                    var r = bothReport.Schools.First(x => x.SchoolId == subject.Id);
                    var expectedOpen = NonConSeasonGamesSeated - subjectConfGames - exemption;
                    var ok = r.Seated
                             && r.Open == expectedOpen
                             && r.ShowcaseGames == 1
                             && r.Home + r.Neutral + r.Road == expectedOpen - 1;
                    matrixOk = matrixOk && ok;
                    matrixDetail.Add($"field {field}: open {r.Open} (expected {expectedOpen}) " +
                                     $"-> {r.Home}/{r.Neutral}/{r.Road}, showcase {r.ShowcaseGames}");
                }

                Check("C6d: ★ THE WORKED NOVEMBER, AT BOTH TOURNAMENT SIZES — a school in a " +
                      "tournament AND a showcase plays 31; its tournament accounts for what " +
                      "its bracket guarantees (2 at a field of four, 3 at a field of eight) " +
                      "and its showcase spends one of the remaining open games either way. " +
                      "The showcase's four-school field buys NO exemption, so a bigger " +
                      "tournament moves OPEN and the showcase never does",
                      matrixOk, $"{subject.Name} conf {subjectConfGames} — " +
                                string.Join("; ", matrixDetail));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — THE CHARGE CHAIN AND ITS PRIORITY.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C7a: ★ R26 — the chain is neutral → road → home, in that order",
                      ApplyShowcaseCharges(5, 2, 3, 1) == (5, 1, 3)
                      && ApplyShowcaseCharges(5, 0, 3, 1) == (5, 0, 2)
                      && ApplyShowcaseCharges(5, 0, 0, 1) == (4, 0, 0));

                Check("C7b: ★ an invited bottom school with no neutral pays a ROAD game — it gave " +
                      "up a road trip to attend a sponsored event, which is exactly right and is " +
                      "why the Selling neutral allowance stays 0",
                      NonConShowcaseAllowance[0] == 0
                      && ApplyShowcaseCharges(2, 0, 8, 1) == (2, 0, 7));

                var empty = Refusal(() => ApplyShowcaseCharges(0, 0, 0, 1));
                Check("C7c: an exhausted chain is an INVARIANT VIOLATION, not a silent zero",
                      empty is not null && empty.Contains("nothing to charge"));

                // ★ PRIORITY, ruled by extension of R23: contracts eat the neutral bucket
                //   first and the showcase takes the next one down. Asserted as arithmetic so
                //   collection order can never decide it.
                var afterContract = ApplyContractCharges(6, 1, 4, new ContractChargeSet(0, 0, 1));
                var afterShowcase = ApplyShowcaseCharges(
                    afterContract.Home, afterContract.Neutral, afterContract.Road, 1);
                Check("C7d: ★ CHARGE PRIORITY — a school with a contract neutral leg AND a showcase " +
                      "in one season: the contract eats the neutral, the showcase takes road",
                      afterContract == (6, 0, 4) && afterShowcase == (6, 0, 3));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — THE RADIUS LADDER, ITS BOUNDARY, AND THE PROVENANCE WORDS.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C8a: ★ the boundary is INCLUSIVE and pinned at distance == radius — the same " +
                      "quantised ruler the matcher uses, floor(miles + 0.5) and never ties-to-even",
                      MteDistanceKey(300.0) == 300 && MteDistanceKey(300.4) == 300
                      && MteDistanceKey(300.5) == 301 && MteDistanceKey(2.5) == 3);

                Check("C8b: the radius steps are authored → +200 → +400 and never national",
                      MteRadiusStepOffsets.SequenceEqual(new[] { 0, 200, 400 }));

                // ★ A5 — the two provenance dimensions are ORTHOGONAL words, and the radius
                //   reader is STRICT while the fallback reader stays lenient (it must keep
                //   reading pre-S104 records whose vocabulary legitimately predates it).
                Check("C8c: ★ A5 — every radius word round-trips, and ABSENCE reads as National " +
                      "rather than as damage: a pre-S104 record has no radius field",
                      Enum.GetValues<EventSeatRadiusStep>()
                          .All(s => MteRadiusStepFromWord(MteRadiusStepWord(s)) == s)
                      && MteRadiusStepFromWord(null) == EventSeatRadiusStep.National);

                var badWord = Refusal(() => MteRadiusStepFromWord("Plus9000"));
                Check("C8d: ★ but an UNRECOGNISED radius word throws rather than defaulting — every " +
                      "value it will ever see was written by this code, so a strange one is damage",
                      badWord is not null && badWord.Contains("unknown radius step word"));

                Check("C8e: ★ and the old fallback words still round-trip untouched — widening the " +
                      "vocabulary must not silently reinterpret a saved career's seats",
                      new[] { EventSeatFallback.None, EventSeatFallback.Band,
                              EventSeatFallback.BandAndScope, EventSeatFallback.BandScopeAndFourYear }
                          .All(f => MteFallbackFromWord(MteFallbackWord(f)) == f));

                // The ladder exhausts INSIDE each radius step: a band nobody meets relaxes
                // before the radius widens, so a seat filled at the authored radius records
                // Base and not Plus200.
                var local = Showcase(90, 1, stock.Places[0].PlaceId, "12-05", 400,
                                     Slot(99, 99), Slot(99, 99), Slot(99, 99), Slot(99, 99));
                var localSeating = MteSeatSeason(With(stock, new[] { local }), ShowcaseCheckSeed, MteHistory.Empty);
                var localEvent = localSeating.Active.Single();
                Check("C8f: ★ R28(c) — an impossible band RELAXES before the radius widens: a local " +
                      "showcase invites a weaker neighbour before it ever reaches further out",
                      localEvent.Seats.Count == 4
                      && localEvent.Seats.All(s => s.RadiusStep == EventSeatRadiusStep.Base)
                      && localEvent.Seats.Any(s => s.Fallback != EventSeatFallback.None),
                      string.Join(", ", localEvent.Seats.Select(
                          s => $"{MteFallbackWord(s.Fallback)}/{MteRadiusStepWord(s.RadiusStep)}")));

                Check("C8g: and a NATIONAL showcase records National on every seat — 'no radius to " +
                      "widen' is a different fact from 'found somebody at the authored radius'",
                      MteSeatSeason(With(stock, new[]
                      {
                          Showcase(91, 1, stock.Places[0].PlaceId, "12-05", null,
                                   Slot(0, 99), Slot(0, 99), Slot(0, 99), Slot(0, 99)),
                      }), ShowcaseCheckSeed, MteHistory.Empty)
                      .Active.Single().Seats.All(s => s.RadiusStep == EventSeatRadiusStep.National));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C9 — ★ THE ZERO PATH BY SUBTRACTION. The whole session's isolation
            //       claim, and the only check that can prove it.
            // ════════════════════════════════════════════════════════════════════
            {
                var noShowcases = With(stock, stock.Events.Where(e => !e.IsShowcase));
                var run = RunSeasonCore(noShowcases, ShowcaseCheckSeed, configPath, verbose: false);

                Check("C9a: ★ THE STOCK WORLD WITH ITS SHOWCASES REMOVED reproduces the pre-S104 " +
                      "event-games fingerprint EXACTLY — so the per-kind walls, the packed draw " +
                      "key, the provisional commit and the changed play order moved NOTHING on " +
                      "their own, and everything that did move was moved by showcases",
                      run.EventGamesFingerprint == ShowcaseGoldenPreS104EventGamesFp,
                      run.EventGamesFingerprint);

                Check("C9b: and it plays no showcase games at all",
                      run.ShowcaseGameCount == 0 && run.ShowcaseResults.Count == 0);

                // ★ THE DISCRIMINATOR: the full slate really does differ, so C9a is not
                //   passing because the showcases were inert.
                var full = RunSeasonCore(stock, ShowcaseCheckSeed, configPath, verbose: false);
                Check("C9c: ★ the discriminator — the FULL slate really does play showcase games " +
                      "and really does move the event-games fingerprint, so C9a is a wall and not " +
                      "an accident",
                      full.ShowcaseGameCount > 0
                      && full.EventGamesFingerprint != ShowcaseGoldenPreS104EventGamesFp,
                      $"{full.ShowcaseGameCount} showcase games");

                // ★ THE TWO WALLS THAT MUST NOT MOVE, on the full slate.
                Check("C9d: ★ the conference schedule and its dates do not move an inch — the " +
                      "league season is byte-identical with sixteen showcases authored",
                      full.Fingerprint == MatchGoldenConferenceFp
                      && full.DatedFingerprint == MatchGoldenDatedFp,
                      full.Fingerprint == MatchGoldenConferenceFp ? "" : full.Fingerprint);

                // ★ THE HONEST TOURNAMENT WALL (the gate's finding, measured not assumed):
                //   every tournament seating BEFORE the first showcase overlap is identical.
                //   After that point fields are free to move — a lost candidate changes what
                //   is left for every later tournament, so the effect cascades.
                var bareSeating = MteSeatSeason(noShowcases, ShowcaseCheckSeed, MteHistory.Empty);
                var fullSeating = MteSeatSeason(stock, ShowcaseCheckSeed, MteHistory.Empty);
                var showDays = fullSeating.Active.Where(x => x.IsShowcase)
                    .Select(x => (Key: (x.Tier, x.EventId), Day: MteWindowDate(x.FirstDay))).ToList();

                var identicalBefore = true;
                var reachedFirstOverlap = false;
                foreach (var t in bareSeating.Active.Where(x => !x.IsShowcase)
                                             .OrderBy(x => x.Tier).ThenBy(x => x.EventId))
                {
                    var first = MteWindowDate(t.FirstDay);
                    var last = MteWindowDate(t.LastDay);
                    if (showDays.Any(s => s.Key.CompareTo((t.Tier, t.EventId)) < 0
                                          && s.Day >= first && s.Day <= last))
                    { reachedFirstOverlap = true; break; }

                    var after = fullSeating.Active.FirstOrDefault(x => x.EventId == t.EventId);
                    if (after is null || !after.Seats.Select(s => s.SchoolId)
                                               .SequenceEqual(t.Seats.Select(s => s.SchoolId)))
                    { identicalBefore = false; break; }
                }
                Check("C9e: ★ every tournament seating BEFORE the first showcase overlap is " +
                      "byte-identical. After it, fields move — a tournament that loses a candidate " +
                      "changes what is left for every later one, so the effect cascades",
                      identicalBefore && reachedFirstOverlap,
                      reachedFirstOverlap ? "" : "no overlap on the slate — the wall is untested");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C10 — THE FIXTURE WORLDS' ZERO PATH. No showcase exists on any of
            //        them, so every one must be untouched by this session.
            // ════════════════════════════════════════════════════════════════════
            {
                var worlds = new[]
                {
                    ("fixture-tiny", tiny), ("fixture-mte", mte),
                    ("fixture-memory", LoadWorld(WorldPath("fixture-memory.world.json"))),
                    ("fixture-schedule", LoadWorld(WorldPath("fixture-schedule.world.json"))),
                    ("fixture-rotation", LoadWorld(WorldPath("fixture-rotation.world.json"))),
                    ("fixture-format", LoadWorld(WorldPath("fixture-format.world.json"))),
                };
                var offenders = worlds
                    .Where(w => w.Item2.Events.Any(e => e.IsShowcase || e.Draw is not null))
                    .Select(w => w.Item1).ToList();
                Check("C10a: ★ no fixture world authors a showcase or a radius, so every pre-S104 " +
                      "claim about them is untouched by construction",
                      offenders.Count == 0, string.Join(", ", offenders));

                var seatings = worlds.Select(w => MteSeatSeason(w.Item2, ShowcaseCheckSeed, MteHistory.Empty));
                Check("C10b: and none of them produces a showcase pairing, a charge or an obligation",
                      seatings.All(s => MteAllShowcasePairings(s).Count == 0
                                        && MteShowcaseObligations(s).Count == 0));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 95 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine(pass ? "  Phase 95 PASS" : "  Phase 95 FAIL");
        return pass;
    }
}
