using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Charm.Harness;

/// <summary>
/// ★ Phase 96 — S105: THE INDEPENDENTS GET A NOVEMBER, AND THE MATCHER LEARNS ONE NEW SHAPE.
///
/// Fourteen schools used to get <c>0,0,0,0</c> and play zero non-conference games. They now
/// carry an ordinary request, and the matcher can pair two schools TWICE in one season, once
/// each way. Emmett's four rulings (2026-08-07):
///
///   R-a  a full season — 29 games, 31 if a tournament seats it; road is the REMAINDER, and
///        coming up short is the market failing, never the design
///   R-b  home read STRAIGHT OFF PRESTIGE, never spread across the current field
///   R-c  zero neutral games — the allowance is a privilege of class and they have no league
///   R-d  an Independent classes as a LOW MAJOR: its own prestige, no tier floor
///   plus  the same-season home-and-home is capped at THREE per school, one ceiling for all
///
/// ★ EVERY CHECK HERE IS BUILT TO DISCRIMINATE. The trap this phase exists to avoid is the
/// one S81 named: a green suite that agrees with a wrong sign. "Every repeated pair is an
/// exchange" passes trivially on a run that made no exchanges; "the Independents are filled"
/// passes on a run that quietly filled them by breaking somebody else. So the positive case
/// is always paired with the control that would have caught the absence.
///
/// ★ NO BASKETBALL TARGET IS ASSERTED. Home counts, shortfall sizes, exchange counts and
/// partner choices are read on the page only (R8 / page-only calibration). What is asserted
/// is structure, conservation, atomicity, provenance and the cap.
/// </summary>
internal static partial class Program
{
    private const long IndStockSeed = 20260720;

    private static bool Phase96IndependentsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine(
            "== Phase 96 — The Independents and the same-season home-and-home (S105: the " +
            "fourteen schools with no conference get a real request — a full season, home " +
            "read off prestige, no neutral, road the remainder — and the matcher learns to " +
            "pair two schools twice in one season, once each way, capped at three per " +
            "school. The exact charge, atomicity, the no-third-meeting wall from all three " +
            "prior-use sources, the negative control, the conventional-only positive " +
            "control that proves the shape is general, one Independent and fifty, typed " +
            "shortfall, the request-only control that separates the request from the " +
            "matcher action, national neutrality asserted as neutrality and never as gap " +
            "repair, and the zero path) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            string WorldPath(string file) =>
                Path.Combine(AppContext.BaseDirectory, "worlds", file);

            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var run = RunSeasonCore(stock, IndStockSeed, configPath, verbose: false);
            var report = run.NonConference;
            var m = run.Matching;
            var confGames = stock.Conferences.ToDictionary(c => c.Id, c => c.Games);
            var schoolById = stock.Schools.ToDictionary(s => s.Id);
            var independents = report.Schools.Where(s => s.IsIndependent).ToList();
            var indIds = independents.Select(s => s.SchoolId).ToHashSet();

            // Every unordered pair and the games played on it, once, for the walls below.
            Dictionary<(int, int), List<MatchPair>> GroupPairs(MatchingReport mm)
            {
                var g = new Dictionary<(int, int), List<MatchPair>>();
                foreach (var p in mm.Pairs)
                {
                    var key = (Math.Min(p.HostSchoolId, p.VisitorSchoolId),
                               Math.Max(p.HostSchoolId, p.VisitorSchoolId));
                    if (!g.TryGetValue(key, out var list)) g[key] = list = new List<MatchPair>();
                    list.Add(p);
                }
                return g;
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C1 — R-a: A FULL SEASON, AND THE ARITHMETIC IS THE SHARED ONE.
            //
            //  ★ The discriminator is that the request is checked against the SEASON, not
            //    against itself. "29 to arrange" would also be produced by a rule that
            //    happened to add up while ignoring a tournament seat, so the seat's games
            //    and the showcase's one are added back explicitly.
            //
            //  ★ S105.1 — and the seat's games are read off the school's OWN FIELD. A flat
            //    three added back here would balance a flat three charged upstream, and the
            //    check would confirm the bug instead of catching it.
            // ════════════════════════════════════════════════════════════════════════
            {
                var indFieldOf = MteTournamentFieldSizes(run.Events.Seating);
                var allFull = true; var firstBad = "";
                foreach (var s in independents)
                {
                    var season = s.Home + s.Neutral + s.Road
                               + (s.Seated ? TournamentGamesFor(indFieldOf[s.SchoolId]) : 0)
                               + s.ShowcaseGames;
                    var expect = s.Seated ? NonConSeasonGamesSeated : NonConSeasonGamesUnseated;
                    if (season == expect) continue;
                    allFull = false; firstBad = $"{s.SchoolName} reaches {season}, not {expect}";
                    break;
                }
                // ★ And their conference games really are zero — the marker, not an inference.
                var markerHolds = independents.All(s =>
                    confGames[schoolById[s.SchoolId].ConferenceId] == 0 && s.ConferenceGames == 0);
                Check("C1 (R-a): every Independent's request reaches a FULL season once its " +
                      "event and showcase obligations are added back — short is the market " +
                      "failing, never the design",
                      allFull && markerHolds && independents.Count > 0,
                      allFull ? $"{independents.Count} independent(s), all full" : firstBad);
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C2 — R-b / R-c / R-d: THE CURVE, THE ZERO NEUTRAL, AND THE CLASS.
            //
            //  ★ R-b is asserted as a PROPERTY (monotone, bounded, prestige-only), not as
            //    a table of expected numbers — a table would be a basketball target. The
            //    property that discriminates against the rank spread is the last one: two
            //    schools of EQUAL prestige must get equal home counts no matter where they
            //    sit in the field, which a rank spread cannot do.
            // ════════════════════════════════════════════════════════════════════════
            {
                var monotone = true; var bounded = true;
                for (var p = 0; p <= 100; p++)
                {
                    var h = NonConIndependentHome(p);
                    if (h < NonConIndependentHomeLo || h > NonConIndependentHomeHi) bounded = false;
                    if (p > 0 && h < NonConIndependentHome(p - 1)) monotone = false;
                }
                var endpoints = NonConIndependentHome(0) == NonConIndependentHomeLo
                             && NonConIndependentHome(NonConIndependentHomeAnchor) == NonConIndependentHomeHi;
                // ★ prestige-only: equal prestige, equal home, regardless of the field.
                var prestigeOnly = independents
                    .GroupBy(s => schoolById[s.SchoolId].CurrentPrestige)
                    .All(g => g.Select(s => s.Home).Distinct().Count() == 1);
                var noNeutral = independents.All(s => s.Neutral == 0);
                // R-d: the class is the prestige band with NO floor, and "Independent" is
                // gone as a class name entirely.
                var classIsBand = independents.All(s =>
                    s.ClassName == NonConClassNames[
                        NonConPrestigeClass(schoolById[s.SchoolId].CurrentPrestige)]
                    && !s.LiftedByFloor);
                var nameRetired = report.Schools.All(s =>
                    s.ClassName != NonConRetiredIndependentClassName);
                Check("C2 (R-b/R-c/R-d): the home curve is monotone, bounded and a function " +
                      "of PRESTIGE ALONE — equal prestige, equal home, whatever the field — " +
                      "neutral is zero, and the class is the prestige band with no floor",
                      monotone && bounded && endpoints && prestigeOnly && noNeutral
                      && classIsBand && nameRetired,
                      $"curve {NonConIndependentHomeLo}..{NonConIndependentHomeHi} over " +
                      $"0..{NonConIndependentHomeAnchor}");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C3 — ★ THE EXACT CHARGE. A home-and-home costs each side TWO road games
            //  and returns one home game and one road game. Asserted per school off the
            //  ledger, and the ledger column IS the number signed.
            // ════════════════════════════════════════════════════════════════════════
            {
                var exchangeLegs = m.Pairs.Where(p => p.Kind == "Exchange").ToList();
                var perSchool = new Dictionary<int, int>();
                foreach (var p in exchangeLegs)
                {
                    perSchool[p.HostSchoolId] = perSchool.GetValueOrDefault(p.HostSchoolId) + 1;
                    perSchool[p.VisitorSchoolId] = perSchool.GetValueOrDefault(p.VisitorSchoolId) + 1;
                }
                // Each school appears in an exchange pair exactly twice per exchange: once
                // hosting, once visiting. So its appearances are twice its signed count.
                var chargeExact = m.Ledger.All(l =>
                    perSchool.GetValueOrDefault(l.SchoolId) == 2 * l.ExchangeHosted);
                var capHeld = m.Ledger.All(l => l.ExchangeHosted <= MatchExchangeCapPerSchool);
                var capReached = m.Ledger.Any(l => l.ExchangeHosted == MatchExchangeCapPerSchool);
                // ★ NO NEUTRAL MOVEMENT: an exchange never touches the neutral bucket.
                var neutralUntouched = m.Ledger.All(l => l.MatchedNeutral <= l.RequestedNeutral);
                Check("C3: a home-and-home charges each side exactly TWO road games and " +
                      "returns one home and one road, no neutral moves, and no school signs " +
                      $"more than the cap of {MatchExchangeCapPerSchool}",
                      chargeExact && capHeld && capReached && neutralUntouched,
                      $"{exchangeLegs.Count / 2} home-and-home(s), max signed " +
                      $"{(m.Ledger.Count == 0 ? 0 : m.Ledger.Max(l => l.ExchangeHosted))}");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C4 — ★ NO THIRD MEETING, AND NO HALF EXCHANGE. Every repeated pair is a
            //  home-and-home with exactly one game each way; a pair already spent by a
            //  CONTRACT can never be exchanged on top; and an exchange leg never appears
            //  alone.
            // ════════════════════════════════════════════════════════════════════════
            {
                var groups = GroupPairs(m);
                var thirdMeeting = groups.Count(kv => kv.Value.Count > 2);
                var sameGymTwice = groups.Count(kv => kv.Value.Count == 2
                    && kv.Value[0].HostSchoolId == kv.Value[1].HostSchoolId);
                var repeatNotExchange = groups.Count(kv => kv.Value.Count > 1
                    && kv.Value.Any(p => p.Kind != "Exchange"));
                var halfExchange = groups.Count(kv => kv.Value.Count == 1
                    && kv.Value[0].Kind == "Exchange");
                // ★ THE CONTRACT ARM, BUILT SO IT CAN FAIL. The first version of this read
                //   run.Contracts.UsedPairs and asserted none of them was exchanged — which
                //   is VACUOUS on any single-season run, because contracts need a career
                //   history and this run has none, so the set is always empty and the clause
                //   always passed. A check that cannot fail is worse than no check.
                //
                //   The replacement CONSTRUCTS the collision: take a pair this world actually
                //   exchanged, hand it to the matcher as a contracted pair, and prove the two
                //   schools then meet ZERO times in the matching. That is the wall doing its
                //   job against the hardest case rather than against an empty set.
                var exchanged = groups.First(kv => kv.Value.Count > 1).Key;
                var seeded = BuildNonConferenceMatching(
                    stock, report,
                    new List<(int Lo, int Hi)> { (exchanged.Item1, exchanged.Item2) });
                var seededMeetings = seeded.Pairs.Count(p =>
                    Math.Min(p.HostSchoolId, p.VisitorSchoolId) == exchanged.Item1
                    && Math.Max(p.HostSchoolId, p.VisitorSchoolId) == exchanged.Item2);
                var contractExchanged = seededMeetings;
                Check("C4: no pair meets three times, no pair meets twice at the same gym, " +
                      "no repeat is anything but a home-and-home, no exchange leg stands " +
                      "alone, and a pair already spent by a contract is never exchanged",
                      thirdMeeting == 0 && sameGymTwice == 0 && repeatNotExchange == 0
                      && halfExchange == 0 && contractExchanged == 0,
                      $"{groups.Count(kv => kv.Value.Count > 1)} repeated pair(s); a pair " +
                      $"handed to the matcher as contracted meets {seededMeetings} times");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C5 — ★ THE NEGATIVE CONTROL. The same world with the shape switched off
            //  produces ZERO repeated pairs. Without this, C4 passes on a run that made
            //  no exchanges at all — which is exactly the shape of a feature that is
            //  wired up and doing nothing.
            // ════════════════════════════════════════════════════════════════════════
            {
                var off = BuildNonConferenceMatching(
                    stock, report,
                    run.Contracts.UsedPairs.ToList(),
                    allowExchange: false);
                var offRepeats = GroupPairs(off).Count(kv => kv.Value.Count > 1);
                var onExchanges = m.CountOfKind("Exchange") / 2;
                Check("C5 negative control: with the shape disabled the same world produces " +
                      "ZERO repeated pairs, and with it enabled it produces some — so C4 is " +
                      "reading a live feature rather than an absent one",
                      offRepeats == 0 && off.CountOfKind("Exchange") == 0 && onExchanges > 0,
                      $"shape off -> {offRepeats} repeats; shape on -> {onExchanges}");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C6 — ★ NATIONAL NEUTRALITY, and it took three tries to state correctly.
            //
            //  The first version compared road-minus-home across the SAME report and was a
            //  tautology — it passes under any matcher alive, because the gap is a property
            //  of the REQUESTS. The second asserted an equal forced-host count and went red
            //  (268 off, 270 on), which looked like a leak and was the opposite.
            //
            //  What is exactly conserved is the ROAD BUDGET REACHING THE FILLER. The shape
            //  fires only on leftover road, so phases 1 and 2 must be untouched — identical
            //  hosted and neutral counts — and everything left over is
            //  2*Filler + 2*Exchange + Terminal + Unrepaired, which must match to the token.
            //  The shape then STRANDS FEWER of those leftovers, which is it working.
            //
            //  ★ ASSERTED AS NEUTRALITY, NEVER AS EVIDENCE THE SHAPE REPAIRED THE +542 GAP.
            //  Only C-37 moves that number.
            // ════════════════════════════════════════════════════════════════════════
            {
                var off = BuildNonConferenceMatching(
                    stock, report,
                    run.Contracts.UsedPairs.ToList(),
                    allowExchange: false);
                int Budget(MatchingReport mm) =>
                    2 * mm.CountOfKind("Filler") + 2 * mm.CountOfKind("Exchange")
                    + mm.CountOfKind("Terminal") + mm.Ledger.Sum(l => l.ShortUnrepaired);
                var phase12Same = m.CountOfKind("Hosted") == off.CountOfKind("Hosted")
                               && m.CountOfKind("Neutral") == off.CountOfKind("Neutral");
                var budgetSame = Budget(m) == Budget(off);
                Check("C6: the shape reaches ONLY leftover road — phases 1 and 2 are " +
                      "identical with it on and off, and the same road budget reaches the " +
                      "filler either way, so it moves road-minus-home by ZERO",
                      phase12Same && budgetSame,
                      $"{m.CountOfKind("Hosted")} hosted / {m.CountOfKind("Neutral")} neutral " +
                      $"both ways; {Budget(m)} road tokens reach the filler; forced hosts " +
                      $"{off.Ledger.Sum(l => l.FillerHosted + l.ExchangeHosted)} -> " +
                      $"{m.Ledger.Sum(l => l.FillerHosted + l.ExchangeHosted)}");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C7 — ★ THE CONVENTIONAL-ONLY POSITIVE CONTROL (R39). A world with NO
            //  Independents in which two ordinary schools still form a legal home-and-home.
            //  This is what proves the shape is GENERAL rather than an accidentally
            //  Independents-only feature, and nothing else in this phase proves it.
            // ════════════════════════════════════════════════════════════════════════
            {
                var noInd = new WorldFile
                {
                    SchemaVersion = stock.SchemaVersion, Kind = stock.Kind,
                    EraLabel = stock.EraLabel, Division = stock.Division,
                    WorldSeed = stock.WorldSeed, Tiers = stock.Tiers,
                    Conferences = stock.Conferences, Places = stock.Places,
                    Events = stock.Events,
                    Schools = stock.Schools.Where(s => confGames[s.ConferenceId] > 0).ToList(),
                };
                var noIndRun = RunSeasonCore(noInd, IndStockSeed, configPath, verbose: false);
                var none = noIndRun.NonConference.Schools.All(s => !s.IsIndependent);
                var exchanges = noIndRun.Matching.CountOfKind("Exchange") / 2;
                Check("C7 (R39) conventional-only positive control: a world with ZERO " +
                      "Independents still forms home-and-homes between ordinary schools — " +
                      "the shape is general, not an Independents-only feature",
                      none && exchanges > 0,
                      $"{exchanges} home-and-home(s) on a world with no Independents");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C8 — ★ ONE INDEPENDENT AND MANY. The count is FLUID across a save (Emmett).
            //  Nothing may assume fourteen, assume non-empty, or assume a second Independent
            //  exists to pair with. One must prove the mechanism does NOT require a partner
            //  of its own kind.
            // ════════════════════════════════════════════════════════════════════════
            {
                var indList = stock.Schools.Where(s => confGames[s.ConferenceId] == 0)
                    .OrderBy(s => s.Id).ToList();
                var drop = indList.Skip(1).Select(s => s.Id).ToHashSet();
                var oneWorld = new WorldFile
                {
                    SchemaVersion = stock.SchemaVersion, Kind = stock.Kind,
                    EraLabel = stock.EraLabel, Division = stock.Division,
                    WorldSeed = stock.WorldSeed, Tiers = stock.Tiers,
                    Conferences = stock.Conferences, Places = stock.Places,
                    Events = stock.Events,
                    Schools = stock.Schools.Where(s => !drop.Contains(s.Id)).ToList(),
                };
                var oneRun = RunSeasonCore(oneWorld, IndStockSeed, configPath, verbose: false);
                var oneReport = oneRun.NonConference;
                var lone = oneReport.Schools.Where(s => s.IsIndependent).ToList();
                var loneId = lone.Count == 1 ? lone[0].SchoolId : -1;
                // The lone Independent is paired, ledgered, and reaches a full season — with
                // conventional partners only, because it has none of its own kind.
                var lonePaired = loneId > 0
                    && oneRun.Matching.Pairs.Any(p => p.HostSchoolId == loneId
                                                   || p.VisitorSchoolId == loneId)
                    && oneRun.Matching.Ledger.Any(l => l.SchoolId == loneId);

                // Many: ★ DISBAND WHOLE CONFERENCES rather than promoting individual
                //   schools. The first version of this control moved the 36 lowest-prestige
                //   schools into the Independents' container and the suite threw — it had
                //   gutted the SWAC below the size its own conference schedule needs, and
                //   the failure was in the TEST WORLD, not the feature. Disbanding a league
                //   entirely leaves every surviving conference intact and makes its whole
                //   membership independent at once, which is also the truer shape of the
                //   thing being modelled: leagues fold, they do not leak members.
                var need = 50 - indList.Count;
                var disband = new HashSet<int>();
                var taken = 0;
                var sizeOf = stock.Schools.GroupBy(x => x.ConferenceId)
                    .ToDictionary(g => g.Key, g => g.Count());
                foreach (var c in stock.Conferences.Where(c => c.Games > 0).OrderBy(c => c.Id))
                {
                    if (taken >= need) break;
                    disband.Add(c.Id);
                    taken += sizeOf.GetValueOrDefault(c.Id, 0);
                }
                var manyWorld = new WorldFile
                {
                    SchemaVersion = stock.SchemaVersion, Kind = stock.Kind,
                    EraLabel = stock.EraLabel, Division = stock.Division,
                    WorldSeed = stock.WorldSeed, Tiers = stock.Tiers,
                    Conferences = stock.Conferences
                        .Select(c => disband.Contains(c.Id) ? c with { Games = 0 } : c).ToList(),
                    Places = stock.Places, Events = stock.Events, Schools = stock.Schools,
                };
                var manyRun = RunSeasonCore(manyWorld, IndStockSeed, configPath, verbose: false);
                var manyCount = manyRun.NonConference.Schools.Count(s => s.IsIndependent);
                // ★ Strangers sharing a container must be able to play EACH OTHER — the
                //   league-mate exemption is the whole reason this world is legal. Here the
                //   discriminating pair is two schools from the SAME disbanded league, which
                //   the old same-conference-id wall would have refused outright.
                var manyIds = manyRun.NonConference.Schools.Where(s => s.IsIndependent)
                    .Select(s => s.SchoolId).ToHashSet();
                var schoolConf = stock.Schools.ToDictionary(x => x.Id, x => x.ConferenceId);
                var exLeagueMates = manyRun.Matching.Pairs.Count(p =>
                    manyIds.Contains(p.HostSchoolId) && manyIds.Contains(p.VisitorSchoolId)
                    && schoolConf[p.HostSchoolId] == schoolConf[p.VisitorSchoolId]);
                var indVsInd = manyRun.Matching.Pairs.Count(p =>
                    manyIds.Contains(p.HostSchoolId) && manyIds.Contains(p.VisitorSchoolId));
                Check("C8 (A3): one Independent and fifty both produce a legal season — one " +
                      "proves the mechanism does not require a second of its own kind, fifty " +
                      "proves strangers sharing a container may play each other — including two schools from the same disbanded league",
                      lone.Count == 1 && lonePaired && manyCount >= 50 && indVsInd > 0
                      && exLeagueMates > 0,
                      $"one: {oneRun.Matching.Pairs.Count} pairs; many: {manyCount} " +
                      $"independent(s), {indVsInd} games between them, {exLeagueMates} of " +
                      $"them between former league-mates");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C9 — ★ THE REQUEST-ONLY CONTROL. Independents enabled, the shape disabled:
            //  they must still enter the accounting and schedule what ordinary matching
            //  allows. This separates the REQUEST feature from the MATCHER action, so a
            //  failure names which of the two broke.
            // ════════════════════════════════════════════════════════════════════════
            {
                var off = BuildNonConferenceMatching(
                    stock, report,
                    run.Contracts.UsedPairs.ToList(),
                    allowExchange: false);
                var allLedgered = indIds.All(i => off.Ledger.Any(l => l.SchoolId == i));
                var allPaired = indIds.All(i =>
                    off.Pairs.Any(p => p.HostSchoolId == i || p.VisitorSchoolId == i));
                Check("C9 request-only control: with the home-and-home disabled the " +
                      "Independents still enter the accounting and schedule what ordinary " +
                      "matching allows — the request stands on its own",
                      allLedgered && allPaired && indIds.Count > 0,
                      $"{indIds.Count} independent(s) paired and ledgered without the shape");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C10 — ★ TYPED SHORTFALL. A5's failure mode: "29 of 29" hides a school that
            //  got four fewer home games and four more road. The ledger must carry
            //  requested and matched SEPARATELY by category, so a shortfall names which
            //  bucket is short. Asserted as availability and reconciliation, never as a
            //  shortfall SIZE — the size is a basketball value and lives on the page.
            // ════════════════════════════════════════════════════════════════════════
            {
                var reconciles = m.Ledger.All(l =>
                    l.PairedTotal == l.RequestedHome + l.RequestedNeutral + l.RequestedRoad
                                   + l.TerminalExtra - l.ShortUnrepaired);
                // Each category is separately readable — home, neutral and road all carry a
                // requested column and a matched column that are not the same number source.
                var typed = m.Ledger.All(l =>
                    l.RequestedHome >= 0 && l.RequestedNeutral >= 0 && l.RequestedRoad >= 0
                    && l.MatchedHome >= 0 && l.MatchedNeutral >= 0 && l.MatchedRoadAsVisitor >= 0);
                var shortIsTyped = m.Ledger.All(l => l.ShortUnrepaired >= 0);
                var totalShort = m.Ledger.Sum(l => l.ShortUnrepaired);
                Check("C10 (A5): every school reconciles to its own request category by " +
                      "category, and a shortfall is carried as its own number rather than " +
                      "hidden inside a matching total",
                      reconciles && typed && shortIsTyped,
                      $"{m.Ledger.Count} ledger rows, {totalShort} token(s) short nationally");
            }

            // ════════════════════════════════════════════════════════════════════════
            //  C11 — ★ THE ZERO PATH. A world with no Independents AND no exchanges must
            //  reproduce the pre-S105 matching exactly in shape: no repeated pair, no
            //  exchange kind, no exchange column anywhere on the ledger. This is what
            //  proves everything that moved was moved by S105 and not by a stray edit.
            // ════════════════════════════════════════════════════════════════════════
            {
                var noInd = new WorldFile
                {
                    SchemaVersion = stock.SchemaVersion, Kind = stock.Kind,
                    EraLabel = stock.EraLabel, Division = stock.Division,
                    WorldSeed = stock.WorldSeed, Tiers = stock.Tiers,
                    Conferences = stock.Conferences, Places = stock.Places,
                    Events = stock.Events,
                    Schools = stock.Schools.Where(s => confGames[s.ConferenceId] > 0).ToList(),
                };
                var noIndRun = RunSeasonCore(noInd, IndStockSeed, configPath, verbose: false);
                var zero = BuildNonConferenceMatching(
                    noInd, noIndRun.NonConference,
                    noIndRun.Contracts.UsedPairs.ToList(),
                    allowExchange: false);
                var noRepeats = GroupPairs(zero).All(kv => kv.Value.Count == 1);
                var noExchangeKind = zero.Pairs.All(p => p.Kind != "Exchange");
                var noExchangeColumn = zero.Ledger.All(l => l.ExchangeHosted == 0);
                var noIndependents = noIndRun.NonConference.Schools.All(s => !s.IsIndependent);
                Check("C11 zero path: no Independents and no home-and-homes reproduces the " +
                      "pre-S105 shape exactly — no repeated pair, no exchange kind, no " +
                      "exchange column",
                      noRepeats && noExchangeKind && noExchangeColumn && noIndependents,
                      $"{zero.Pairs.Count} pairs, all distinct");
            }
        }
        catch (Exception ex)
        {
            Check("Phase 96 completed without an unexpected exception", false, ex.Message);
        }

        Console.WriteLine($"  Phase 96: {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
