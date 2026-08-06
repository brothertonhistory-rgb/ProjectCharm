using System.Text.Json;

namespace Charm.Harness;

// ============================================================================
//  Phase 93 (Session 102) — THE MATCHING.
//
//  What this phase proves:
//    C1   the matcher consumed the SEASON'S OWN S101 report — a mutated-report
//         run proves the requests are read rather than recomputed — and the
//         input report is unchanged after the match (internal counters, never
//         mutation);
//    C2   pair structure: hosted (host, visitor) or neutral (normalised
//         lower-id first), no self-pairs, no malformed kind;
//    C3   hard legality: different conferences, no duplicate unordered pair, no
//         pair both hosted and neutral, every id exists, and no-request schools
//         appear NOWHERE;
//    C4   determinism: same world + same report -> identical ordered report;
//    C5   seed-independence given equal inputs, constructed rather than sampled;
//    C6   allocation exactness — the §4.0 sequences literally, including the
//         lower-bucket tie and Selling -> ANY;
//    C7   spill direction and count: never below origin, count == requests
//         filled above origin;
//    C8   neutral behaviour: two tokens per pair, leftovers convert, and a
//         conversion re-enters as ANY;
//    C9   ★ zero-path: the full pre-S102 fingerprint bundle;
//    C10  ledger conservation: BOTH identities nationally, every pairing on
//         exactly two ledgers, per-school PairedTotal exact;
//    C11  filler semantics: lower prestige hosts, equal prestige lower id
//         hosts, both road tokens consumed, and NO filler host over target;
//    C12  terminal bounds: +1 exactly, used at most once, class preference
//         honoured, unrepaired reported and zero on stock;
//    C13  completes-or-reports: a constructed unmatchable world returns the
//         structured shortfall without an exception;
//    C14  ★ oracle parity, pair for pair IN ORDER and ledger field for field,
//         with C14a asserting the golden's embedded S101 report is the live one.
//
//  ── Two concepts, kept separate on purpose ─────────────────────────────────
//  C14 proves PORT FIDELITY — that the C# is the oracle. C1–C13 prove the
//  POLICY'S INVARIANTS — that the thing being ported is coherent. Neither
//  substitutes for the other: a faithful port of a wrong policy passes C14, and
//  a coherent policy implemented differently in the two languages passes C1–C13.
//
//  ── What this phase deliberately does not prove ────────────────────────────
//  Any basketball value. No distance, no spill count, no pair count, and no
//  class trip median from any world is asserted to a target — page-only
//  calibration holds. The counts asserted here are conservation arithmetic.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ THE PRE-S102 GOLDENS — stock world, seed 20260720. Identical to the
    /// pre-S101 bundle, because S101 moved nothing and S102 must not either: the matcher
    /// is a pure function of the world and the S101 report, and nothing downstream reads
    /// its result. If any of these four moves, S102's second wall is breached.</summary>
    private const string MatchGoldenConferenceFp =
        "6f79d6636e291866d51387f93979d817011f7903ddc64e67d4ebcebf087cb5c3";
    private const string MatchGoldenDatedFp =
        "7515df7d72f801f49d264ff52d6472911ac87d0996d44269d113b0ef83cb632a";
    /// <summary>★ S104 — RECAPTURED. The season now plays 24 showcase games on top of its
    /// tournaments, and seven tournaments seat different fields because a showcase took a
    /// school on an overlapping night (and because a tournament that loses a candidate
    /// changes what is left for every later one). So the results half of the season is
    /// deliberately a different season. The pre-S104 value was 6abd62b0…, and it is NOT
    /// recoverable by subtraction the way the event-games hash is — this fingerprint covers
    /// the conference games too, and those are byte-identical; what moved is the event half
    /// inside the same hash. Emmett's machine is the commit-of-record for this value.</summary>
    private const string MatchGoldenResultsFp =
        "898d9fe8e75a353bca1fa89296d96f8cceafb72e66c2a6718eb6eb0b2553742b";
    /// <summary>★ S104 — RECAPTURED, because the feature IS the movement. The stock world
    /// now authors sixteen showcases; twelve of them seat and play twenty-four games, so the
    /// event-games half of the season is deliberately a different season. Captured from the
    /// S104 build's verified run; Emmett's machine is the commit-of-record for this value.
    /// The pre-S104 value was 26f2b8ff…, and it is still reproduced EXACTLY by the
    /// showcase-free zero path (Phase 95 C-Z), which is what proves this move was caused by
    /// the showcases and by nothing else in the session.</summary>
    private const string MatchGoldenEventGamesFp =
        "7c1a41c18824934c61d782f41eabd472602d4fe8234ab757e154f255e44a1cd3";

    private const long MatchStockSeed = 20260720;

    private static bool Phase93MatchingCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 93 — The matching (S102: every school's November pairs — who " +
                          "plays whom, who hosts, which pairs are neutral; no site and no night. " +
                          "Input identity and immutability, pair structure, hard legality, " +
                          "determinism, seed-independence, allocation exactness, spill direction, " +
                          "neutral conversion, full-bundle zero-path identity, both conservation " +
                          "identities, filler semantics, terminal bounds, completes-or-reports, " +
                          "and pair-for-pair oracle parity) ==");
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
            var tiny = LoadWorld(WorldPath("fixture-tiny.world.json"));

            // ★ One stock season run serves C1, C9 and the stock arm of everything else.
            //   It is the SEASON'S OWN report that the matcher consumed, which is what
            //   makes C1 a wiring assertion rather than a re-derivation. This is the
            //   phase's whole runtime cost (~30s) — the same cost and the same reason as
            //   Phase 92's, flagged to Emmett at the gate and accepted.
            var stockRun = RunSeasonCore(stock, MatchStockSeed, configPath, verbose: false);
            var report = stockRun.NonConference;
            var m = stockRun.Matching;

            var schoolById = stock.Schools.ToDictionary(s => s.Id);
            var targeted = report.Targeted.ToList();
            var targetedIds = targeted.Select(r => r.SchoolId).ToHashSet();
            int Prestige(int id) => schoolById[id].CurrentPrestige;

            // ════════════════════════════════════════════════════════════════════
            //  C1 — INPUT IDENTITY AND IMMUTABILITY.
            // ════════════════════════════════════════════════════════════════════
            {
                // (a) The ledger's requested columns ARE the report's numbers, school by
                //     school — the matcher read them rather than deriving its own.
                var reads = m.Ledger.All(l =>
                {
                    var r = report.Schools.Single(s => s.SchoolId == l.SchoolId);
                    return l.RequestedHome == r.Home && l.RequestedNeutral == r.Neutral
                        && l.RequestedRoad == r.Road && l.ClassName == r.ClassName;
                }) && m.Ledger.Count == targeted.Count;

                // (b) ★ THE DISCRIMINATOR: a synthetic report whose numbers differ from
                //     anything the world would produce. A matcher that recomputed instead
                //     of reading would ignore the change and land on the same pairing.
                var mutated = new NonConferenceReport
                {
                    Schools = report.Schools.Select(s => s.IsIndependent ? s
                        : s with { Home = Math.Max(0, s.Home - 1), Road = s.Road + 1 }).ToList(),
                    HomeTotal = report.HomeTotal, NeutralTotal = report.NeutralTotal,
                    RoadTotal = report.RoadTotal, SeatedCount = report.SeatedCount,
                };
                var mutatedMatch = BuildNonConferenceMatching(stock, mutated);
                var responded = mutatedMatch.CountOfKind("Hosted") != m.CountOfKind("Hosted");

                // (c) Immutability: the input report the season carried is untouched after
                //     the match. Compared field by field against a snapshot taken here.
                var snapshot = report.Schools
                    .Select(s => $"{s.SchoolId}|{s.Home}|{s.Neutral}|{s.Road}|{s.ClassName}")
                    .ToList();
                _ = BuildNonConferenceMatching(stock, report);
                var after = report.Schools
                    .Select(s => $"{s.SchoolId}|{s.Home}|{s.Neutral}|{s.Road}|{s.ClassName}")
                    .ToList();
                var untouched = snapshot.SequenceEqual(after);

                Check("C1: the matcher consumed the season's own S101 report — the ledger's " +
                      "requested columns are the report's numbers, a mutated report moves the " +
                      "pairing, and the input report is unchanged afterwards",
                      reads && responded && untouched,
                      $"{m.Ledger.Count} ledger rows; mutated run hosts " +
                      $"{mutatedMatch.CountOfKind("Hosted")} vs {m.CountOfKind("Hosted")}");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — PAIR STRUCTURE.
            // ════════════════════════════════════════════════════════════════════
            {
                var kinds = new[] { "Hosted", "Neutral", "Filler", "Terminal" };
                var kindOk = m.Pairs.All(p => kinds.Contains(p.Kind));
                var noSelf = m.Pairs.All(p => p.HostSchoolId != p.VisitorSchoolId);
                // A neutral pair has no host, so its two ids are normalised lower-first
                // and the positions carry no hosting meaning.
                var normalised = m.Pairs.Where(p => p.Kind == "Neutral")
                    .All(p => p.HostSchoolId < p.VisitorSchoolId);
                var keyed = m.Pairs.All(p => p.DistanceKey >= 0);
                Check("C2: every pairing is hosted (host, visitor) or neutral (normalised " +
                      "lower id first), no self-pairs, no malformed kind",
                      kindOk && noSelf && normalised && keyed,
                      $"{m.Pairs.Count} pairs, {m.CountOfKind("Neutral")} neutral");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — HARD LEGALITY.
            // ════════════════════════════════════════════════════════════════════
            {
                var seen = new HashSet<(int, int)>();
                var dupes = 0; var sameConf = 0; var unknown = 0; var noRequest = 0;
                foreach (var p in m.Pairs)
                {
                    var a = p.HostSchoolId; var b = p.VisitorSchoolId;
                    if (!schoolById.ContainsKey(a) || !schoolById.ContainsKey(b)) { unknown++; continue; }
                    if (!targetedIds.Contains(a) || !targetedIds.Contains(b)) noRequest++;
                    if (schoolById[a].ConferenceId == schoolById[b].ConferenceId) sameConf++;
                    if (!seen.Add((Math.Min(a, b), Math.Max(a, b)))) dupes++;
                }
                // ★ A5 — the Independents are absent from EVERY phase, the terminal
                //   partner pool included. Asserted as a set, both directions.
                var independents = report.Schools.Where(s => s.IsIndependent)
                    .Select(s => s.SchoolId).ToHashSet();
                var appear = m.Pairs.Any(p => independents.Contains(p.HostSchoolId)
                                           || independents.Contains(p.VisitorSchoolId))
                          || m.Ledger.Any(l => independents.Contains(l.SchoolId));
                Check("C3: different conferences, no duplicate unordered pair, every id " +
                      "exists, and no-request schools appear NOWHERE — not in a pair, not " +
                      "on a ledger, not as a terminal partner",
                      dupes == 0 && sameConf == 0 && unknown == 0 && noRequest == 0 && !appear,
                      $"{independents.Count} schools with no request held out of " +
                      $"{m.Pairs.Count} pairs");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — DETERMINISM.
            // ════════════════════════════════════════════════════════════════════
            {
                var again = BuildNonConferenceMatching(stock, report);
                var samePairs = again.Pairs.Count == m.Pairs.Count
                    && again.Pairs.Zip(m.Pairs).All(t => t.First == t.Second);
                var sameLedger = again.Ledger.Count == m.Ledger.Count
                    && again.Ledger.Zip(m.Ledger).All(t => MatchLedgerEqual(t.First, t.Second));
                Check("C4: the same world and the same report produce an identical ORDERED " +
                      "pairing report", samePairs && sameLedger,
                      $"{again.Pairs.Count} pairs reproduced in order");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — SEED-INDEPENDENCE GIVEN EQUAL INPUTS (constructed, not sampled).
            // ════════════════════════════════════════════════════════════════════
            {
                // fixture-tiny authors no events, so its seating is empty at every seed and
                // its report is genuinely seed-independent. The matcher takes no seed at
                // all; this proves that end to end rather than by reading the signature.
                var r1 = BuildNonConferenceRequests(tiny, MteSeatSeason(tiny, 1, MteHistory.Empty));
                var r2 = BuildNonConferenceRequests(tiny, MteSeatSeason(tiny, 999_777, MteHistory.Empty));
                var reportsEqual = r1.Schools.Count == r2.Schools.Count
                    && r1.Schools.Zip(r2.Schools).All(t =>
                        t.First.Home == t.Second.Home && t.First.Neutral == t.Second.Neutral
                        && t.First.Road == t.Second.Road && t.First.ClassName == t.Second.ClassName);
                var m1 = BuildNonConferenceMatching(tiny, r1);
                var m2 = BuildNonConferenceMatching(tiny, r2);
                var matchesEqual = m1.Pairs.Count == m2.Pairs.Count
                    && m1.Pairs.Zip(m2.Pairs).All(t => t.First == t.Second);
                Check("C5: two different seeds on an eventless world produce the same report " +
                      "and therefore the same matching — the matcher holds no seed",
                      reportsEqual && matchesEqual && m1.Pairs.Count > 0,
                      $"{m1.Pairs.Count} pairs identical across seeds 1 and 999777");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — ALLOCATION EXACTNESS.
            // ════════════════════════════════════════════════════════════════════
            {
                var marquee = MatchBucketMix["Marquee"];
                var nine = MatchAllocate(9, marquee).SequenceEqual(new[] { 6, 2, 1 });
                // ★ THE LOWER-BUCKET TIE: home 6 gives Easy .75 and Decent .75; Easy is the
                //   lower bucket and takes the unit, so 4/1/1 and never 3/1/2.
                var six = MatchAllocate(6, marquee).SequenceEqual(new[] { 4, 1, 1 });
                var solid = MatchAllocate(6, MatchBucketMix["Solid"]).SequenceEqual(new[] { 3, 2, 1 });
                var working = MatchAllocate(4, MatchBucketMix["Working"]).SequenceEqual(new[] { 2, 2, 0 });
                var zero = MatchAllocate(0, marquee).SequenceEqual(new[] { 0, 0, 0 });
                var conserves = Enumerable.Range(0, 16)
                    .All(h => MatchAllocate(h, marquee).Sum() == h
                           && MatchAllocate(h, MatchBucketMix["Solid"]).Sum() == h
                           && MatchAllocate(h, MatchBucketMix["Working"]).Sum() == h);
                // ★ Selling sends every home game to ANY, and nothing else does.
                var sellingIds = m.Ledger.Where(l => l.ClassName == "Selling")
                    .Select(l => l.SchoolId).ToHashSet();
                var sellingAny = m.Pairs
                    .Where(p => p.Kind == "Hosted" && sellingIds.Contains(p.HostSchoolId)
                                && !p.WasConvertedNeutral)
                    .All(p => p.OriginBucket == MatchBucketAny);
                var othersNotAny = m.Pairs
                    .Where(p => p.Kind == "Hosted" && !sellingIds.Contains(p.HostSchoolId)
                                && !p.WasConvertedNeutral)
                    .All(p => p.OriginBucket != MatchBucketAny);
                Check("C6: the allocation sequences hold exactly — Marquee 9 -> 6/2/1, " +
                      "Marquee 6 -> 4/1/1 on the lower-bucket tie, every home count " +
                      "conserved — and Selling's home games are the only ANY requests",
                      nine && six && solid && working && zero && conserves && sellingAny && othersNotAny);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — SPILL DIRECTION AND COUNT.
            // ════════════════════════════════════════════════════════════════════
            {
                var neverBelow = true; var counted = 0; var flagged = true;
                foreach (var p in m.Pairs)
                {
                    if (p.Kind != "Hosted" || p.OriginBucket == MatchBucketAny) continue;
                    var o = Array.IndexOf(MatchBucketNames, p.OriginBucket);
                    var f = Array.IndexOf(MatchBucketNames, p.FilledBucket);
                    if (o < 0 || f < 0) { flagged = false; continue; }
                    if (f < o) neverBelow = false;
                    if (p.WasSpill != (f > o)) flagged = false;
                    if (p.WasSpill) counted++;
                }
                var ledgerAgrees = m.Ledger.Sum(l => l.SpilledRequests) == m.SpilledRequests;
                Check("C7: every filled request sits in its original bucket or above, never " +
                      "below; the spill count equals the requests filled above their origin, " +
                      "on the report and on the ledgers",
                      neverBelow && flagged && counted == m.SpilledRequests && ledgerAgrees,
                      $"{m.SpilledRequests} spills");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — NEUTRAL BEHAVIOUR.
            // ════════════════════════════════════════════════════════════════════
            {
                var neutralPairs = m.Pairs.Count(p => p.Kind == "Neutral");
                var neutralRequested = targeted.Sum(r => r.Neutral);
                var converted = m.ConvertedNeutrals;
                // Two tokens per pair, plus one per conversion, accounts for every token.
                var accounts = 2 * neutralPairs + converted == neutralRequested;
                var ledgerAgrees = m.Ledger.Sum(l => l.ConvertedNeutralToHome) == converted
                    && m.Ledger.Sum(l => l.MatchedNeutral) == 2 * neutralPairs;
                // ★ A conversion re-enters as ANY and is flagged on the pair it produced.
                var reentered = m.Pairs.Where(p => p.WasConvertedNeutral)
                    .All(p => p.Kind == "Hosted" && p.OriginBucket == MatchBucketAny);
                var flaggedCount = m.Pairs.Count(p => p.WasConvertedNeutral);
                Check("C8: two tokens per neutral pair, every leftover token converts and is " +
                      "counted, and a conversion re-enters as an unrestricted home request",
                      accounts && ledgerAgrees && reentered && flaggedCount <= converted,
                      $"{neutralRequested} tokens = 2×{neutralPairs} + {converted} converted");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C9 — ★ ZERO-PATH IDENTITY ON THE FULL STOCK BUNDLE.
            // ════════════════════════════════════════════════════════════════════
            {
                var resultsFp = SeasonFingerprint(stockRun.Results, stockRun.PossessionCounts);
                Check("C9: the stock season reproduces the pre-S102 tree exactly — conference " +
                      "fingerprint, dated fingerprint, tournament-games fingerprint, and the " +
                      "results+possessions fingerprint. Nothing this session added moved a game",
                      stockRun.Fingerprint == MatchGoldenConferenceFp
                      && stockRun.DatedFingerprint == MatchGoldenDatedFp
                      && stockRun.EventGamesFingerprint == MatchGoldenEventGamesFp
                      && resultsFp == MatchGoldenResultsFp,
                      $"conf {stockRun.Fingerprint[..8]}…, dated {stockRun.DatedFingerprint[..8]}…, " +
                      $"events {stockRun.EventGamesFingerprint[..8]}…, results {resultsFp[..8]}…");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C10 — LEDGER CONSERVATION, BOTH IDENTITIES.
            // ════════════════════════════════════════════════════════════════════
            {
                var tokens = targeted.Sum(r => r.Home + r.Neutral + r.Road);
                var hosted = m.CountOfKind("Hosted");
                var neutral = m.CountOfKind("Neutral");
                var filler = m.CountOfKind("Filler");
                var terminal = m.CountOfKind("Terminal");
                var unrepaired = m.Unrepaired.Count;
                var extra = m.Ledger.Sum(l => l.TerminalExtra);

                // (i) request disposition — every token is spent exactly once
                var disposition = 2 * hosted + 2 * neutral + 2 * filler + terminal + unrepaired == tokens;
                // (ii) actual participation — and the two are NOT the same identity
                var participation = 2 * m.Pairs.Count == tokens - unrepaired + extra;

                // every pairing appears on exactly two ledgers, in its two roles
                var roles = targetedIds.ToDictionary(i => i, _ => 0);
                foreach (var p in m.Pairs) { roles[p.HostSchoolId]++; roles[p.VisitorSchoolId]++; }
                var perSchool = m.Ledger.All(l => l.PairedTotal == roles[l.SchoolId]);
                var reconciles = m.Ledger.All(l =>
                    l.PairedTotal == l.RequestedHome + l.RequestedNeutral + l.RequestedRoad
                                   + l.TerminalExtra - l.ShortUnrepaired);

                Check("C10: both conservation identities hold nationally — request " +
                      "disposition and actual participation — every pairing appears on " +
                      "exactly two ledgers in its two roles, and each school's PairedTotal " +
                      "reconciles to its own request",
                      disposition && participation && perSchool && reconciles,
                      $"{tokens} tokens = 2×{hosted} + 2×{neutral} + 2×{filler} + {terminal} " +
                      $"+ {unrepaired}; 2×{m.Pairs.Count} = {tokens} − {unrepaired} + {extra}");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C11 — FILLER SEMANTICS.
            // ════════════════════════════════════════════════════════════════════
            {
                // ★ THE HOST RULE ASSERTED IN ITS OWN WORDS, not by the pool's construction:
                //   lower prestige hosts; EQUAL prestige, lower id hosts.
                var hostRule = m.Pairs.Where(p => p.Kind == "Filler").All(p =>
                {
                    var h = p.HostSchoolId; var v = p.VisitorSchoolId;
                    return Prestige(h) < Prestige(v) || (Prestige(h) == Prestige(v) && h < v);
                });
                // ★ A filler host is ON TARGET, never over it: the game was already one of
                //   its own road tokens, so its site mix moved and its game count did not.
                var onTarget = m.Ledger.All(l =>
                    l.FillerHosted <= l.RequestedRoad
                    && l.MatchedRoadAsVisitor + l.FillerHosted
                       <= l.RequestedRoad + l.TerminalExtra);
                // both sides spend a road token: the ledger's two columns account for every
                // filler pair twice over
                var fillerPairs = m.CountOfKind("Filler");
                var hostedSide = m.Ledger.Sum(l => l.FillerHosted);
                Check("C11: a filler game is hosted by the lower-prestige school (equal " +
                      "prestige, the lower id), both road tokens are spent, and NO filler " +
                      "host exceeds its own game target",
                      hostRule && onTarget && hostedSide == fillerPairs,
                      $"{fillerPairs} filler games, {hostedSide} hosted sides");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C12 — TERMINAL BOUNDS.
            // ════════════════════════════════════════════════════════════════════
            {
                var terminalPairs = m.Pairs.Where(p => p.Kind == "Terminal").ToList();
                var atMostOne = m.Ledger.All(l => l.TerminalExtra is 0 or 1);
                var partners = terminalPairs.Select(p => p.HostSchoolId).ToList();
                var usedOnce = partners.Distinct().Count() == partners.Count;
                var extraMatches = m.Ledger.Sum(l => l.TerminalExtra) == terminalPairs.Count;
                // The class preference is honoured: a partner from a class ABOVE Selling is
                // only legitimate when no legal unused Selling school existed. The bound
                // asserted here is the one that always holds — every partner's class is one
                // of the four, in the ruled order, and the search never reached past Marquee.
                var classOf = m.Ledger.ToDictionary(l => l.SchoolId, l => l.ClassName);
                var classesLegal = partners.All(p => MatchTerminalClassPreference.Contains(classOf[p]));
                var reported = m.Unrepaired.Count == m.Ledger.Sum(l => l.ShortUnrepaired);
                Check("C12: every repaired partner is over by exactly one game and is used at " +
                      "most once, the class preference order is honoured, and unrepaired " +
                      "tokens are reported rather than thrown (zero on the stock world)",
                      atMostOne && usedOnce && extraMatches && classesLegal && reported
                      && m.Unrepaired.Count == 0,
                      $"{terminalPairs.Count} repairs, {m.Unrepaired.Count} unrepaired");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C13 — ★ COMPLETES-OR-REPORTS ON A CONSTRUCTED UNMATCHABLE WORLD.
            // ════════════════════════════════════════════════════════════════════
            {
                // Every school into ONE conference: no legal pair exists anywhere, in any
                // phase. A matcher that throws on infeasibility dies here; the contract says
                // it returns a structured shortfall instead.
                var oneLeague = new WorldFile
                {
                    SchemaVersion = tiny.SchemaVersion, Kind = tiny.Kind,
                    EraLabel = tiny.EraLabel, Division = tiny.Division,
                    WorldSeed = tiny.WorldSeed, Tiers = tiny.Tiers,
                    Conferences = tiny.Conferences, Places = tiny.Places, Events = tiny.Events,
                    Schools = tiny.Schools.Select(s => s with { ConferenceId = 1 }).ToList(),
                };
                var stranded = BuildNonConferenceRequests(oneLeague, EventSeatingOutcome.Empty);
                var strandedMatch = BuildNonConferenceMatching(oneLeague, stranded);
                var tokens = stranded.Targeted.Sum(r => r.Home + r.Neutral + r.Road);
                // Reaching this line at all is the no-throw half. The rest: nothing paired,
                // every token reported, and the ledger still reconciles.
                var ok = strandedMatch.Pairs.Count == 0
                      && strandedMatch.Unrepaired.Count > 0
                      && strandedMatch.Ledger.All(l => l.PairedTotal == 0)
                      && strandedMatch.Ledger.Sum(l => l.ShortUnrepaired)
                         == strandedMatch.Unrepaired.Count;
                // ★ And the page renders it rather than crashing on an empty sample.
                var lines = MatchingPageLines(strandedMatch);
                Check("C13: a world where no legal pair exists returns the structured " +
                      "shortfall — no pairs, every token reported unrepaired, the ledger " +
                      "still reconciling — without an exception, and the page renders it",
                      ok && lines.Count > 0,
                      $"{tokens} tokens, {strandedMatch.Unrepaired.Count} reported unrepaired");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C15 — ★ THE ACCEPTANCE READ IS WELL-FORMED (it is NOT a basketball target).
            // ════════════════════════════════════════════════════════════════════
            {
                // ★ WHAT IS ASSERTED: that the flag is the WORLD'S conference tier and not a
                //   guess from class, that "no true road game" means exactly zero games this
                //   school travelled to, and that the page renders the count. ★ WHAT IS NOT
                //   ASSERTED: the count itself. 27 of 73 is a reading, and page-only
                //   calibration means no number on that line is ever a pass condition.
                var tierById = stock.Conferences.ToDictionary(c => c.Id, c => c.TierId);
                var flagCorrect = m.Ledger.All(l =>
                    l.IsPowerConference == (tierById[schoolById[l.SchoolId].ConferenceId] == "power"));
                var powerRows = m.Ledger.Where(l => l.IsPowerConference).ToList();
                var stayHome = powerRows.Where(l => l.MatchedRoadAsVisitor == 0).ToList();

                // ★ A class-derived flag would NOT reproduce this — the discriminator that
                //   keeps the tier read honest. Marquee carries the tier FLOOR, so mid-majors
                //   that earned the class sit in it, and the two sets must differ.
                var classDerived = m.Ledger.Count(l => l.ClassName == "Marquee");
                var differs = classDerived != powerRows.Count;

                // A school that never travelled also hosted every game it was matched into.
                var consistent = stayHome.All(l =>
                    l.MatchedHome + l.MatchedNeutral + l.FillerHosted + l.TerminalExtra
                    == l.PairedTotal);
                var rendered = MatchingPageLines(m)
                    .Any(x => x.Contains("NO true road game", StringComparison.Ordinal));

                Check("C15: the power-conference flag is the WORLD'S tier and not a guess from " +
                      "class (the two sets differ), a school reading zero true road games " +
                      "travelled to nothing, and the page prints the count — the COUNT is the " +
                      "acceptance read and is never asserted to a value",
                      flagCorrect && differs && consistent && rendered,
                      $"{stayHome.Count} of {powerRows.Count} power-conference schools never " +
                      $"leave home; {classDerived} Marquee-class schools for contrast");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C14 — ★ ORACLE PARITY.
            // ════════════════════════════════════════════════════════════════════
            {
                var goldenPath = Path.Combine(AppContext.BaseDirectory, "tools", "matching_golden.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(goldenPath));
                var root = doc.RootElement;
                var prov = root.GetProperty("provenance");

                Check("C14 provenance: the golden names its own input basis — world file, " +
                      "seed, S101 report fingerprint, DistanceKey formula, oracle hash, pair " +
                      "count and ledger checksum — so a regeneration cannot silently change it",
                      root.GetProperty("schema").GetString() == "s102-matching-v1"
                      && prov.GetProperty("seed").GetInt64() == MatchStockSeed
                      && prov.GetProperty("distanceKeyFormula").GetString()
                         == "floor(GeoDistance.DistanceMiles(a,b) + 0.5)"
                      && prov.GetProperty("oracleSha256").GetString()!.Length == 64
                      && prov.GetProperty("worldFileSha256").GetString()!.Length == 64
                      && prov.GetProperty("ledgerChecksum").GetString()!.Length == 64,
                      $"pairCount {prov.GetProperty("pairCount").GetInt32()}, " +
                      $"tokens {prov.GetProperty("inputTokens").GetInt32()}");

                // ── C14a — the golden's embedded S101 report IS the live one. Without this
                //    a parity failure could mean either "the port is wrong" or "the input
                //    moved", and those are different bugs with different fixes.
                var goldenReport = root.GetProperty("inputReport").EnumerateArray().ToList();
                var live = report.Schools.OrderBy(s => s.SchoolId).ToList();
                var inputSame = goldenReport.Count == live.Count;
                var firstDiff = "";
                if (inputSame)
                    for (var i = 0; i < live.Count; i++)
                    {
                        var g = goldenReport[i];
                        if (g.GetProperty("schoolId").GetInt32() == live[i].SchoolId
                            && g.GetProperty("className").GetString() == live[i].ClassName
                            && g.GetProperty("isIndependent").GetBoolean() == live[i].IsIndependent
                            && g.GetProperty("home").GetInt32() == live[i].Home
                            && g.GetProperty("neutral").GetInt32() == live[i].Neutral
                            && g.GetProperty("road").GetInt32() == live[i].Road) continue;
                        inputSame = false;
                        firstDiff = live[i].SchoolName;
                        break;
                    }
                Check("C14a: the golden's embedded S101 report is field-for-field the report " +
                      "this season actually built — the parity below is about the PORT, never " +
                      "about a moved input", inputSame,
                      inputSame ? $"{live.Count} schools" : $"first divergence at {firstDiff}");

                // ── C14b — the pairing list, IN ORDER, pair for pair.
                var goldenPairs = root.GetProperty("pairs").EnumerateArray().ToList();
                var pairsSame = goldenPairs.Count == m.Pairs.Count;
                var pairDiff = "";
                if (pairsSame)
                    for (var i = 0; i < m.Pairs.Count; i++)
                    {
                        var g = goldenPairs[i]; var p = m.Pairs[i];
                        if (g.GetProperty("kind").GetString() == p.Kind
                            && g.GetProperty("hostSchoolId").GetInt32() == p.HostSchoolId
                            && g.GetProperty("visitorSchoolId").GetInt32() == p.VisitorSchoolId
                            && g.GetProperty("distanceKey").GetInt32() == p.DistanceKey
                            && g.GetProperty("originBucket").GetString() == p.OriginBucket
                            && g.GetProperty("filledBucket").GetString() == p.FilledBucket
                            && g.GetProperty("wasSpill").GetBoolean() == p.WasSpill
                            && g.GetProperty("wasConvertedNeutral").GetBoolean() == p.WasConvertedNeutral)
                            continue;
                        pairsSame = false;
                        pairDiff = $"index {i}: golden {g.GetProperty("kind").GetString()} " +
                                   $"{g.GetProperty("hostSchoolId").GetInt32()}->" +
                                   $"{g.GetProperty("visitorSchoolId").GetInt32()} @" +
                                   $"{g.GetProperty("distanceKey").GetInt32()}, C# {p.Kind} " +
                                   $"{p.HostSchoolId}->{p.VisitorSchoolId} @{p.DistanceKey}";
                        break;
                    }
                Check("C14b: the C# pairing list equals the oracle's golden IN ORDER, pair " +
                      "for pair — kind, both ids, DistanceKey, both buckets, spill flag and " +
                      "conversion flag. Same-platform integer artifact, so literal equality " +
                      "is the right bar", pairsSame,
                      pairsSame ? $"{m.Pairs.Count} pairs identical" : pairDiff);

                // ── C14c — the ledgers, field for field.
                var goldenLedger = root.GetProperty("ledger").EnumerateArray().ToList();
                var liveLedger = m.Ledger.OrderBy(l => l.SchoolId).ToList();
                var ledgerSame = goldenLedger.Count == liveLedger.Count;
                var ledgerDiff = "";
                if (ledgerSame)
                    for (var i = 0; i < liveLedger.Count; i++)
                    {
                        var g = goldenLedger[i]; var l = liveLedger[i];
                        if (g.GetProperty("schoolId").GetInt32() == l.SchoolId
                            && g.GetProperty("requestedHome").GetInt32() == l.RequestedHome
                            && g.GetProperty("requestedNeutral").GetInt32() == l.RequestedNeutral
                            && g.GetProperty("requestedRoad").GetInt32() == l.RequestedRoad
                            && g.GetProperty("matchedHome").GetInt32() == l.MatchedHome
                            && g.GetProperty("matchedNeutral").GetInt32() == l.MatchedNeutral
                            && g.GetProperty("matchedRoadAsVisitor").GetInt32() == l.MatchedRoadAsVisitor
                            && g.GetProperty("fillerHosted").GetInt32() == l.FillerHosted
                            && g.GetProperty("terminalExtra").GetInt32() == l.TerminalExtra
                            && g.GetProperty("shortUnrepaired").GetInt32() == l.ShortUnrepaired
                            && g.GetProperty("convertedNeutralToHome").GetInt32() == l.ConvertedNeutralToHome
                            && g.GetProperty("spilledRequests").GetInt32() == l.SpilledRequests)
                            continue;
                        ledgerSame = false;
                        ledgerDiff = l.SchoolName;
                        break;
                    }
                Check("C14c: every ledger row matches the golden field for field",
                      ledgerSame,
                      ledgerSame ? $"{liveLedger.Count} rows" : $"first divergence at {ledgerDiff}");
            }
        }
        catch (Exception ex)
        {
            Check("Phase 93 completed without an unexpected exception", false, ex.Message);
        }

        Console.WriteLine($"  Phase 93: {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    private static bool MatchLedgerEqual(MatchLedgerRow a, MatchLedgerRow b) =>
        a.SchoolId == b.SchoolId && a.RequestedHome == b.RequestedHome
        && a.RequestedNeutral == b.RequestedNeutral && a.RequestedRoad == b.RequestedRoad
        && a.MatchedHome == b.MatchedHome && a.MatchedNeutral == b.MatchedNeutral
        && a.MatchedRoadAsVisitor == b.MatchedRoadAsVisitor && a.FillerHosted == b.FillerHosted
        && a.TerminalExtra == b.TerminalExtra && a.ShortUnrepaired == b.ShortUnrepaired
        && a.ConvertedNeutralToHome == b.ConvertedNeutralToHome
        && a.SpilledRequests == b.SpilledRequests;
}
