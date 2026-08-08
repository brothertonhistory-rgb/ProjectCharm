using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  Phase 86 (Session 95) — HOME COURT.
//
//  What this phase proves:
//    B1  the zero path is the OLD ENGINE, against a fingerprint captured from the
//        pre-S95 tree before a line of production wiring existed;
//    B2  a floor with no host tilts nobody (ruling 3, the neutral half);
//    B3  the shaved/exempt classification is CORRECT, derived independently rather
//        than echoed from production's own list;
//    B4  the clone is exhaustively right — every shaved rating down, every exempt
//        rating and every non-rating field untouched, the SOURCE unchanged, and
//        Player.Athleticism EXACTLY equal, which is the skills-not-bodies ruling
//        stated as an assertion;
//    B5  every rostered man is transformed, bench included, with the side's
//        non-player surface carried by reference and no accumulation across
//        repeated application;
//    B6  the wiring asymmetry: home passes through, away transforms, and only when
//        hosted and non-zero (ruling 3, the home half);
//    B7  determinism at a non-zero shave;
//    B8  coverage — every hosted game's road side was actually shaved;
//    B9  the dead road free-throw seam is gone, and the dial's guards hold.
//
//  WHAT THIS PHASE DELIBERATELY DOES NOT PROVE: that the shave produces any
//  particular home win rate. The ratified 59% is a CALIBRATION target and lives on
//  the season page, never in the suite — the page-only calibration principle in
//  full. A suite that asserted 59% would go red for a tuning decision and would
//  have to be edited every time the dial moved, which is how a red line stops
//  meaning anything.
//
//  ── The trap this phase is built around ────────────────────────────────────
//  Almost every check here would stay green if the shave were applied to the WRONG
//  SIDE. Conservation holds, determinism holds, the counts hold, the clone is
//  perfectly formed either way — a flipped shave is invisible to all of it. Two
//  things discriminate: B6, which names home and away separately at the seam, and
//  Phase 55's independent hand replay, which seats a fresh pair and must reproduce
//  the recorded score. Neither is decoration.
// ============================================================================

internal static partial class Program
{
    // ── The golden, captured from the PRE-S95 TREE ────────────────────────────
    //
    //  PROVENANCE, which is the only thing that makes a bare hash auditable: these
    //  three constants were emitted on 2026-08-03 from a pristine pull of `main` at
    //  the pre-S95 commit, by a temporary capture routine calling the SAME
    //  `SeasonFingerprint` helper this check calls — the serialization was never
    //  hand-reproduced. The fixture is the tiny world at Phase 55's own fixed seed.
    //
    //  The game count is asserted BEFORE the hash is compared, deliberately. A hash
    //  mismatch on its own tells you nothing about what moved; "160 games, wrong
    //  hash" and "23 games" are completely different failures and should not arrive
    //  wearing the same face.
    private const long GoldenSeed = 20260703;
    // ★ S105.2 recapture: Emmett's ruling took the tiny world's leagues from 16 to
    //   12 conference games, so the zero-shave season is 120 games now and the hash
    //   was re-emitted by the SAME SeasonFingerprint helper on the new world. The
    //   pre-S95 provenance above is history for the 160-game shape; the ASSERTION —
    //   zero shave reproduces one fixed season, byte for byte — is unchanged.
    private const int GoldenGameCount = 120;
    private const string GoldenZeroSha256 =
        "7b5b21d0906ff690e51f7a95393693640436c453ae3527561f108211807ff3ce";

    /// <summary>The SEVENTEEN exempt ratings, spelled out here INDEPENDENTLY of
    /// production. B3 derives the expected shaved set as (live public int surface −
    /// these) and asserts production's set equals it, so this is a correctness check on
    /// the classification rather than a consistency echo of it.
    ///
    /// <para>Grouped by WHY, because the reasons differ and a future session editing this
    /// list needs to know which argument it is arguing with.</para></summary>
    private static readonly string[] HomeCourtExemptRatingNames =
    {
        // Bookkeeping, not performance.
        "PlayerId", "HierarchyRank",
        // WHERE a man shoots from is identity. The odds lean; the diet stays his.
        "RimTendency", "ShortTendency", "MidTendency", "LongTendency", "ThreeTendency",
        // Body facts. A man is the same size in a hostile gym.
        "Height", "Wingspan", "Weight",
        // The six physicals. Because ALL SIX are here, Player.Athleticism — their mean,
        // computed on read — cannot move on the road. B4 asserts that as exact equality.
        "Strength", "Speed", "Quickness", "FirstStep", "Vertical", "Endurance",
        // Effort travels (Emmett, 2026-08-03). A man competes exactly as hard away.
        "Hustle",
    };

    /// <summary>Every readable public instance property on <see cref="Player"/> at S95:
    /// the 40 numbered ratings, Name, PlayerClass, Arrival, the three development maps,
    /// and the three derived values. Pinned as a COUNT so that a property added in some
    /// future session cannot slip through B4 unclassified — B3 catches a new int, and
    /// this catches a new anything-else.</summary>
    private const int PlayerReadablePropertyCount = 49;

    private static bool Phase86HomeCourtCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 86 — Home court (S95: the road penalty — zero-path identity vs a " +
                          "pre-S95 golden, neutral isolation, classification, exhaustive clone, " +
                          "side transformation, wiring asymmetry, determinism, coverage, retirement) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            var tinyPath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json");
            var tiny = LoadWorld(tinyPath);

            // ════════════════════════════════════════════════════════════════════
            //  B3 — classification correctness, derived independently.
            // ════════════════════════════════════════════════════════════════════
            var livePublicInts = typeof(Player)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0
                            && p.GetMethod is { IsPublic: true })
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            var exempt = HomeCourtExemptRatingNames.ToHashSet(StringComparer.Ordinal);
            var missingExempt = exempt.Except(livePublicInts).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Check("B3: every name on the check's exempt list is a real rating (a renamed " +
                  "rating cannot sit here quietly exempting nothing)",
                  missingExempt.Length == 0,
                  missingExempt.Length == 0 ? $"{exempt.Count} exempt names"
                                            : $"not on Player: [{string.Join(", ", missingExempt)}]");

            var expectedShaved = livePublicInts.Except(exempt).ToHashSet(StringComparer.Ordinal);
            var extraInProduction = ShavedRatingNames.Except(expectedShaved)
                                     .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var missingFromProduction = expectedShaved.Except(ShavedRatingNames)
                                     .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Check("B3: production's shaved set is exactly (live public int surface − the " +
                  "check's own exempt list) — disjointness and totality fall out of the algebra",
                  extraInProduction.Length == 0 && missingFromProduction.Length == 0,
                  extraInProduction.Length + missingFromProduction.Length == 0
                    ? $"{livePublicInts.Count} ratings = {expectedShaved.Count} shaved + {exempt.Count} exempt"
                    : $"shaved-but-shouldn't-be: [{string.Join(", ", extraInProduction)}] " +
                      $"exempt-but-shouldn't-be: [{string.Join(", ", missingFromProduction)}]");

            Check("B3: the shave reaches 23 of the 40 ratings — the ruled split, stated as a " +
                  "number so a silent reclassification is loud",
                  livePublicInts.Count == 40 && expectedShaved.Count == 23 && exempt.Count == 17,
                  $"{livePublicInts.Count} ratings, {expectedShaved.Count} shaved, {exempt.Count} exempt");

            var readable = typeof(Player)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true })
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToArray();
            Check("B3: Player's readable public surface is the size B4 was written against " +
                  "(a NON-rating property added later must be classified, not inherited)",
                  readable.Length == PlayerReadablePropertyCount,
                  $"{readable.Length} readable properties, expected {PlayerReadablePropertyCount}");

            // ════════════════════════════════════════════════════════════════════
            //  B4 — exhaustive transformation and preservation.
            // ════════════════════════════════════════════════════════════════════
            //  The probe carries a DISTINCT value in every rating, so a clone that copied
            //  the right number from the wrong property is caught. The expected clone is
            //  hand-built here, by this check's own rules, and then compared property by
            //  property through reflection — including the read-only derived values,
            //  which are never skipped for being read-only.
            //  ★ The check's shave values are ITS OWN and deliberately NOT the ruled dial.
            //    Phase 86 tests the mechanism's contract, never the calibration, so a future
            //    tuning pass that moves config.json cannot turn this file red — and 2 keeps
            //    the documented 50→48 / 1→0 / 0→0 contract readable at a glance.
            const int probeShave = 2;
            var probe = HomeCourtProbePlayer();
            var expectedClone = HomeCourtExpectedClone(probe, probeShave);
            var actualClone = RoadShavedPlayer(probe, probeShave);

            var mismatches = new List<string>();
            foreach (var prop in readable)
            {
                var want = prop.GetValue(expectedClone);
                var got = prop.GetValue(actualClone);
                if (!HomeCourtValuesEqual(want, got))
                    mismatches.Add($"{prop.Name}: expected {HomeCourtShow(want)}, got {HomeCourtShow(got)}");
            }
            Check($"B4: every one of the {readable.Length} readable properties matches the " +
                  "independently-built expected clone (read-only and derived included)",
                  mismatches.Count == 0,
                  mismatches.Count == 0 ? $"{readable.Length} properties"
                                        : string.Join("  |  ", mismatches.Take(6)));

            // ★ THE RULING, AS AN ASSERTION. Athleticism is the mean of the six physicals
            //   and all six are exempt, so the road cannot cost a man one thousandth of it.
            //   This is the axis the S95 design conversation actually turned on, so it gets
            //   its own line rather than hiding inside the sweep above.
            Check("B4: ★ Athleticism is EXACTLY unchanged by the shave — skills, not bodies",
                  actualClone.Athleticism == probe.Athleticism,
                  $"source {probe.Athleticism.ToString("F6", CultureInfo.InvariantCulture)}, " +
                  $"clone {actualClone.Athleticism.ToString("F6", CultureInfo.InvariantCulture)}");

            //   ...and the other two derived values DO fall, because they are built from
            //   shooting and scoring ratings that are shaved by design. Asserting the
            //   direction is what makes the equality above mean something: a clone that
            //   simply copied everything would pass the Athleticism line too.
            Check("B4: Gravity and Spacing DO come down — the shave landing where it was " +
                  "sent (both are computed from shooting/scoring ratings), not leaking into the body",
                  actualClone.GravityContribution < probe.GravityContribution
                    && actualClone.SpacingContribution < probe.SpacingContribution,
                  $"gravity {probe.GravityContribution:F3}→{actualClone.GravityContribution:F3}, " +
                  $"spacing {probe.SpacingContribution:F3}→{actualClone.SpacingContribution:F3}");

            // The three contract values, named explicitly.
            var fifty = HomeCourtFlatPlayer(50);
            var one = HomeCourtFlatPlayer(1);
            var zero = HomeCourtFlatPlayer(0);
            Check("B4: the shave contract at the edges — 50→48, 1→0 (floored, never negative), 0→0",
                  RoadShavedPlayer(fifty, 2).Close == 48
                    && RoadShavedPlayer(one, 2).Close == 0
                    && RoadShavedPlayer(zero, 2).Close == 0
                    && RoadShavedPlayer(one, 2).Steals == 0
                    && RoadShavedPlayer(zero, 2).BasketballIQ == 0,
                  $"50→{RoadShavedPlayer(fifty, 2).Close}, 1→{RoadShavedPlayer(one, 2).Close}, " +
                  $"0→{RoadShavedPlayer(zero, 2).Close}");

            // The source is not written to. Asserted, not trusted to the absence of an
            // assignment: an init-only property cannot be reassigned, but a contained
            // collection could have been sorted or overwritten in place.
            var sourceBefore = readable.Select(p => HomeCourtShow(p.GetValue(probe))).ToArray();
            _ = RoadShavedPlayer(probe, probeShave);
            var sourceAfter = readable.Select(p => HomeCourtShow(p.GetValue(probe))).ToArray();
            Check("B4: the SOURCE player is byte-for-byte unchanged after cloning",
                  sourceBefore.SequenceEqual(sourceAfter, StringComparer.Ordinal));

            // ════════════════════════════════════════════════════════════════════
            //  B5 — side transformation, semantically.
            // ════════════════════════════════════════════════════════════════════
            var side = HomeCourtProbeSide();
            var shavedSide = ApplyRoadShave(side, probeShave, hasHost: true);

            var allTransformed =
                side.Starters.Select((p, i) => (p, c: shavedSide.Starters[i]))
                    .Concat(side.Reserves.Select((p, i) => (p, c: shavedSide.Reserves[i])))
                    .All(x => !ReferenceEquals(x.p, x.c)
                              && x.c.Close == Math.Max(0, x.p.Close - probeShave)
                              && x.c.PlayerId == x.p.PlayerId
                              && x.c.Height == x.p.Height);
            Check($"B5: EVERY rostered man is transformed — {side.Starters.Length} starters AND " +
                  $"{side.Reserves.Length} on the bench, so no coach substitutes his way out of it",
                  allTransformed && shavedSide.Starters.Length == side.Starters.Length
                                 && shavedSide.Reserves.Length == side.Reserves.Length,
                  $"{shavedSide.Starters.Length}+{shavedSide.Reserves.Length} men");

            Check("B5: the side's four non-player fields are the SAME references, order untouched",
                  ReferenceEquals(shavedSide.StarterPositions, side.StarterPositions)
                    && ReferenceEquals(shavedSide.StarterRanks, side.StarterRanks)
                    && ReferenceEquals(shavedSide.ReservePositions, side.ReservePositions)
                    && ReferenceEquals(shavedSide.ReserveRanks, side.ReserveRanks));

            Check("B5: the depth order survives — man k of the shaved side is man k of the source",
                  shavedSide.Starters.Select(p => p.PlayerId)
                    .SequenceEqual(side.Starters.Select(p => p.PlayerId))
                    && shavedSide.Reserves.Select(p => p.PlayerId)
                        .SequenceEqual(side.Reserves.Select(p => p.PlayerId)));

            Check("B5: the SOURCE side is untouched — its players are still the originals at " +
                  "their original ratings",
                  side.Starters.All(p => p.Close == HomeCourtProbeSideClose(p.PlayerId))
                    && side.Reserves.All(p => p.Close == HomeCourtProbeSideClose(p.PlayerId)));

            // ★ NON-ACCUMULATION — the season-decay failure mode, closed. If the applicator
            //   ever wrote back to its source, the second application would compound and a
            //   road team would decay across a season. Reference equality of the two RESULTS
            //   is explicitly NOT expected; semantic equivalence is the contract.
            var twice = ApplyRoadShave(side, probeShave, hasHost: true);
            var equivalent =
                !ReferenceEquals(twice, shavedSide)
                && twice.Starters.Select(p => p.Close).SequenceEqual(shavedSide.Starters.Select(p => p.Close))
                && twice.Reserves.Select(p => p.Close).SequenceEqual(shavedSide.Reserves.Select(p => p.Close))
                && twice.Starters.Select(p => p.PlayerId).SequenceEqual(shavedSide.Starters.Select(p => p.PlayerId));
            Check("B5: ★ two applications to the SAME source are semantically equivalent — the " +
                  "penalty cannot compound across a season",
                  equivalent);

            // ════════════════════════════════════════════════════════════════════
            //  B2 — neutral isolation, behaviourally.
            // ════════════════════════════════════════════════════════════════════
            Check("B2: a floor with no host tilts NOBODY — the applicator hands back the " +
                  "original side, not a copy of it",
                  ReferenceEquals(ApplyRoadShave(side, probeShave, hasHost: false), side));

            var (nHome, nAway, nFlag) = PrepareSeasonGameSides(side, side, probeShave, hasHost: false);
            Check("B2: the seam agrees — unhosted, both sides pass through and the flag is honest",
                  ReferenceEquals(nHome, side) && ReferenceEquals(nAway, side) && !nFlag);

            // ════════════════════════════════════════════════════════════════════
            //  B6 — hosted wiring asymmetry. RULING 3 AS AN ASSERTION.
            // ════════════════════════════════════════════════════════════════════
            var homeSide = HomeCourtProbeSide();
            var awaySide = HomeCourtProbeSide();
            var (pHome, pAway, pFlag) = PrepareSeasonGameSides(homeSide, awaySide, probeShave, hasHost: true);
            Check("B6: ★ hosted — the HOME side is the very same object it arrived as, and the " +
                  "AWAY side is transformed. A road penalty, never a home boost.",
                  ReferenceEquals(pHome, homeSide) && !ReferenceEquals(pAway, awaySide) && pFlag,
                  $"home passthrough {ReferenceEquals(pHome, homeSide)}, away shaved {pFlag}");

            var (zHome, zAway, zFlag) = PrepareSeasonGameSides(homeSide, awaySide, 0, hasHost: true);
            Check("B6: at shave 0 the seam is inert — both sides pass through, flag false",
                  ReferenceEquals(zHome, homeSide) && ReferenceEquals(zAway, awaySide) && !zFlag);

            // ════════════════════════════════════════════════════════════════════
            //  B1 — zero-path identity against the pre-S95 golden.
            // ════════════════════════════════════════════════════════════════════
            var zeroRun = RunSeasonCore(tiny, GoldenSeed, configPath, verbose: false,
                                        roadShaveOverride: 0);
            Check($"B1: the zero-shave season is the recorded shape ({GoldenGameCount} games) — " +
                  "asserted BEFORE the hash, so a mismatch names itself",
                  zeroRun.Results.Count == GoldenGameCount
                    && zeroRun.PossessionCounts.Count == GoldenGameCount,
                  $"{zeroRun.Results.Count} results, {zeroRun.PossessionCounts.Count} possession counts");

            var zeroFp = SeasonFingerprint(zeroRun.Results, zeroRun.PossessionCounts);
            Check("B1: ★ ZERO IS THE OLD ENGINE — every score and every possession count " +
                  "reproduces a fingerprint captured from the pre-S95 tree",
                  zeroFp == GoldenZeroSha256,
                  zeroFp == GoldenZeroSha256 ? zeroFp[..16] + "…" : $"got {zeroFp}, want {GoldenZeroSha256}");

            Check("B1: and at zero nothing was shaved — the counter agrees with the fingerprint",
                  zeroRun.HostedRoadSidesShaved == 0 && zeroRun.RoadShave == 0,
                  $"{zeroRun.HostedRoadSidesShaved} sides shaved");

            // ════════════════════════════════════════════════════════════════════
            //  B7 — determinism at a live shave.  B8 — coverage.
            // ════════════════════════════════════════════════════════════════════
            var onA = RunSeasonCore(tiny, GoldenSeed, configPath, verbose: false, roadShaveOverride: 2);
            var onB = RunSeasonCore(tiny, GoldenSeed, configPath, verbose: false, roadShaveOverride: 2);
            var fpA = SeasonFingerprint(onA.Results, onA.PossessionCounts);
            var fpB = SeasonFingerprint(onB.Results, onB.PossessionCounts);
            Check("B7: two shave-on seasons at the probe seed are identical — the shave adds no " +
                  "randomness of its own",
                  fpA == fpB, fpA == fpB ? fpA[..16] + "…" : $"{fpA[..16]}… vs {fpB[..16]}…");

            // The discriminating half: a change that did nothing at all would also produce
            // two identical fingerprints. This says the shave MOVED the basketball.
            Check("B7: ★ and the shave-on season is NOT the zero season — the dial does something",
                  fpA != zeroFp,
                  $"shave-on {fpA[..16]}… vs zero {zeroFp[..16]}…");

            Check("B8: every hosted game had its road side shaved — one assertion, one source " +
                  "of truth (the tournament layer replaces the expectation with its site fact)",
                  onA.HostedRoadSidesShaved == onA.Schedule.Count,
                  $"{onA.HostedRoadSidesShaved} shaved / {onA.Schedule.Count} scheduled");

            // ════════════════════════════════════════════════════════════════════
            //  B9 — retirement hygiene and the dial's guards.
            // ════════════════════════════════════════════════════════════════════
            var rollLProps = typeof(RollLConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name).ToArray();
            Check("B9: the dead road free-throw seam is gone from RollLConfig (it was a " +
                  "documented seam that never got wired, and S95 wired home court elsewhere)",
                  !rollLProps.Contains("RoadMakePenalty", StringComparer.Ordinal),
                  string.Join(", ", rollLProps));

            using (var doc = JsonDocument.Parse(File.ReadAllText(configPath)))
            {
                var hasOrphanKey = doc.RootElement.TryGetProperty("RollL", out var rollL)
                                   && rollL.TryGetProperty("RoadMakePenalty", out _);
                Check("B9: ...and gone from config.json's RollL section too",
                      !hasOrphanKey);

                var hasDial = doc.RootElement.TryGetProperty("HomeCourt", out var hc)
                              && hc.TryGetProperty("RoadShave", out _);
                Check("B9: the one dial is seated in config.json", hasDial);
            }

            Check("B9: the dial loads the ratified value from the live config",
                  HomeCourtConfig.Load(configPath).RoadShave >= 0,
                  $"RoadShave = {HomeCourtConfig.Load(configPath).RoadShave}");

            // Guards, exercised against TEMPORARY files. config.json is never rewritten by
            // a check — a suite that edits the production config is a suite that can leave
            // the repo dirty when it fails halfway.
            var tmpDir = Path.Combine(Path.GetTempPath(), "charm-s95-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                var missing = Path.Combine(tmpDir, "missing.json");
                File.WriteAllText(missing, "{ \"RollL\": { \"MakeProbability\": 0.72 } }");
                Check("B9: a missing HomeCourt section is QUIET at runtime — the compiled " +
                      "default applies and the game boots (Phase 71's ruling); Phase 71 is " +
                      "where the absence goes loud",
                      HomeCourtConfig.Load(missing).RoadShave == HomeCourtConfig.DefaultRoadShave,
                      $"defaulted to {HomeCourtConfig.Load(missing).RoadShave}");

                var negative = Path.Combine(tmpDir, "negative.json");
                File.WriteAllText(negative, "{ \"HomeCourt\": { \"RoadShave\": -1 } }");
                var refused = false; var refusalMsg = "";
                try { HomeCourtConfig.Load(negative); }
                catch (InvalidOperationException ex) { refused = true; refusalMsg = ex.Message; }
                Check("B9: a NEGATIVE shave is refused and names itself — a road boost is a " +
                      "different design, not a value of this dial",
                      refused && refusalMsg.Contains("RoadShave", StringComparison.Ordinal),
                      refusalMsg);

                var zeroCfg = Path.Combine(tmpDir, "zero.json");
                File.WriteAllText(zeroCfg, "{ \"HomeCourt\": { \"RoadShave\": 0 } }");
                Check("B9: ZERO is a legal value, not a refusal — B1 depends on it being " +
                      "reachable through the config, not only through the test override",
                      HomeCourtConfig.Load(zeroCfg).RoadShave == 0);

                var big = Path.Combine(tmpDir, "big.json");
                File.WriteAllText(big, "{ \"HomeCourt\": { \"RoadShave\": 500 } }");
                Check("B9: there is deliberately NO upper bound — an oversized shave floors " +
                      "every rating at 0 rather than producing ratings the engine would refuse",
                      HomeCourtConfig.Load(big).RoadShave == 500
                        && RoadShavedPlayer(fifty, 500).Close == 0
                        && RoadShavedPlayer(fifty, 500).Height == fifty.Height);
            }
            finally { try { Directory.Delete(tmpDir, recursive: true); } catch { /* best effort */ } }
        }
        catch (Exception ex)
        {
            Check($"Phase 86 threw: {ex.GetType().Name}", false, ex.Message);
        }

        Console.WriteLine(pass ? "  Phase 86: PASS" : "  Phase 86: FAIL");
        return pass;
    }

    // ── Probe construction ────────────────────────────────────────────────────

    /// <summary>A maximally populated probe carrying a DISTINCT value in every rating, so
    /// a clone that copied the right number from the wrong property cannot pass. Values
    /// walk 10, 11, 12 … which keeps every rating inside the authored 0–99 band and every
    /// tendency non-zero. The development state is populated too — it is the part of the
    /// card the sibling cloner drops, and B4 is where that divergence is proven
    /// deliberate.</summary>
    private static Player HomeCourtProbePlayer()
    {
        var n = 10;
        int V() => n++;
        return new Player("Probe Man")
        {
            PlayerId = 7, HierarchyRank = 3,
            Close = V(), Mid = V(), Outside = V(), Finishing = V(), FreeThrow = V(),
            FoulDrawing = V(), BallHandling = V(), Passing = V(), Playmaking = V(),
            SelfCreation = V(), PostMoves = V(), OffBallMovement = V(), Screening = V(),
            OffensiveRebounding = V(), PerimeterDefense = V(), PostDefense = V(),
            RimProtection = V(), DefensiveRebounding = V(), Steals = V(), HelpDefense = V(),
            OffBallDefense = V(), BasketballIQ = V(), Discipline = V(),
            Hustle = V(),
            RimTendency = V(), ShortTendency = V(), MidTendency = V(), LongTendency = V(),
            ThreeTendency = V(),
            Height = V(), Wingspan = V(), Weight = V(), Strength = V(), Speed = V(),
            Quickness = V(), FirstStep = V(), Vertical = V(), Endurance = V(),
            LatentSkills = new Dictionary<string, int>(StringComparer.Ordinal) { ["shooting"] = 88 },
            CurrentSkills = new Dictionary<string, int>(StringComparer.Ordinal) { ["shooting"] = 61 },
            Runway = new Dictionary<string, int>(StringComparer.Ordinal) { ["shooting"] = 27 },
            Arrival = 0.4,
            PlayerClass = "So",
        };
    }

    /// <summary>The expected clone, hand-built by THIS check's own rules: the twenty-three
    /// shaved ratings floored, everything else carried. Deliberately not produced by
    /// calling the thing under test.</summary>
    private static Player HomeCourtExpectedClone(Player p, int by)
    {
        int S(int v) => Math.Max(0, v - by);
        return new Player(p.Name)
        {
            PlayerId = p.PlayerId, HierarchyRank = p.HierarchyRank,
            Close = S(p.Close), Mid = S(p.Mid), Outside = S(p.Outside),
            Finishing = S(p.Finishing), FreeThrow = S(p.FreeThrow), FoulDrawing = S(p.FoulDrawing),
            BallHandling = S(p.BallHandling), Passing = S(p.Passing), Playmaking = S(p.Playmaking),
            SelfCreation = S(p.SelfCreation), PostMoves = S(p.PostMoves),
            OffBallMovement = S(p.OffBallMovement), Screening = S(p.Screening),
            OffensiveRebounding = S(p.OffensiveRebounding),
            PerimeterDefense = S(p.PerimeterDefense), PostDefense = S(p.PostDefense),
            RimProtection = S(p.RimProtection), DefensiveRebounding = S(p.DefensiveRebounding),
            Steals = S(p.Steals), HelpDefense = S(p.HelpDefense), OffBallDefense = S(p.OffBallDefense),
            BasketballIQ = S(p.BasketballIQ), Discipline = S(p.Discipline),
            Hustle = p.Hustle,
            RimTendency = p.RimTendency, ShortTendency = p.ShortTendency,
            MidTendency = p.MidTendency, LongTendency = p.LongTendency,
            ThreeTendency = p.ThreeTendency,
            Height = p.Height, Wingspan = p.Wingspan, Weight = p.Weight,
            Strength = p.Strength, Speed = p.Speed, Quickness = p.Quickness,
            FirstStep = p.FirstStep, Vertical = p.Vertical, Endurance = p.Endurance,
            LatentSkills = p.LatentSkills, CurrentSkills = p.CurrentSkills, Runway = p.Runway,
            Arrival = p.Arrival, PlayerClass = p.PlayerClass,
        };
    }

    /// <summary>A player whose every rating is one flat value — the edge-case probe for the
    /// 50→48 / 1→0 / 0→0 contract.</summary>
    private static Player HomeCourtFlatPlayer(int v) => new Player($"Flat {v}")
    {
        PlayerId = 1, HierarchyRank = 5,
        Close = v, Mid = v, Outside = v, Finishing = v, FreeThrow = v, FoulDrawing = v,
        BallHandling = v, Passing = v, Playmaking = v, SelfCreation = v, PostMoves = v,
        OffBallMovement = v, Screening = v, OffensiveRebounding = v, PerimeterDefense = v,
        PostDefense = v, RimProtection = v, DefensiveRebounding = v, Steals = v,
        HelpDefense = v, OffBallDefense = v, BasketballIQ = v, Discipline = v, Hustle = v,
        RimTendency = v, ShortTendency = v, MidTendency = v, LongTendency = v, ThreeTendency = v,
        Height = v, Wingspan = v, Weight = v, Strength = v, Speed = v, Quickness = v,
        FirstStep = v, Vertical = v, Endurance = v,
    };

    /// <summary>The probe side's Close rating for a given stamped id — the independent
    /// yardstick B5 uses to assert the SOURCE side never moved.</summary>
    private static int HomeCourtProbeSideClose(int playerId) => 20 + playerId;

    /// <summary>A full-shape probe side: a real starting five and a real bench, at the
    /// roster size the season actually seats, so "bench included" is tested against the
    /// live shape rather than a convenient two-man stand-in.</summary>
    private static GenSideData HomeCourtProbeSide()
    {
        Player Man(int id) => new Player($"Probe {id}")
        {
            PlayerId = id,
            Close = HomeCourtProbeSideClose(id), Mid = 40, Outside = 41, Finishing = 42,
            FreeThrow = 43, FoulDrawing = 44, BallHandling = 45, Passing = 46,
            Playmaking = 47, SelfCreation = 48, PostMoves = 49, OffBallMovement = 50,
            Screening = 51, OffensiveRebounding = 52, PerimeterDefense = 53,
            PostDefense = 54, RimProtection = 55, DefensiveRebounding = 56, Steals = 57,
            HelpDefense = 58, OffBallDefense = 59, BasketballIQ = 60, Discipline = 61,
            Hustle = 62,
            RimTendency = 20, ShortTendency = 20, MidTendency = 20, LongTendency = 20,
            ThreeTendency = 20,
            Height = 70, Wingspan = 71, Weight = 72, Strength = 73, Speed = 74,
            Quickness = 75, FirstStep = 76, Vertical = 77, Endurance = 78,
        };

        var starters = Enumerable.Range(1, Lineup.Size).Select(Man).ToArray();
        var reserves = Enumerable.Range(Lineup.Size + 1, RosterShape.Size - Lineup.Size)
                                 .Select(Man).ToArray();
        return new GenSideData(
            starters, starters.Select(_ => "G").ToArray(), starters.Select(p => (double)p.PlayerId).ToArray(),
            reserves, reserves.Select(_ => "W").ToArray(), reserves.Select(p => (double)p.PlayerId).ToArray());
    }

    // ── Value comparison for the reflection sweep ─────────────────────────────

    private static bool HomeCourtValuesEqual(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is IReadOnlyDictionary<string, int> da && b is IReadOnlyDictionary<string, int> db)
            return da.Count == db.Count
                && da.All(kv => db.TryGetValue(kv.Key, out var v) && v == kv.Value);
        return a.Equals(b);
    }

    private static string HomeCourtShow(object? v) => v switch
    {
        null => "null",
        IReadOnlyDictionary<string, int> d =>
            "{" + string.Join(",", d.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                    .Select(kv => $"{kv.Key}={kv.Value}")) + "}",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "null",
    };
}
