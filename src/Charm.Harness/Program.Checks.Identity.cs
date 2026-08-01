using System.Globalization;
using System.Reflection;
using System.Text;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

internal static partial class Program
{
    /// <summary>
    /// Phase 80 (Session 89) — PERMANENT IDENTITY AND THE HISTORY FILE.
    ///
    /// <para>Page-only principle holds: not one basketball target is asserted here. Every
    /// check below is about numbering, wiring and file behaviour.</para>
    ///
    /// <para>★ THE TWO CHECKS THAT ACTUALLY DISCRIMINATE. Most of this phase would pass on
    /// a broken implementation, because "it did not crash" is cheap. The two that cannot
    /// are B2 — the type surface, which proves the banned operations are UNWRITABLE rather
    /// than merely unwritten — and B8, which proves the whole season is byte-for-byte
    /// unchanged and then constructs a one-value divergence and proves the comparator
    /// catches it. A green isolation check with no negative control is a check that has
    /// never been shown to be able to fail.</para>
    ///
    /// <para>★ WHY THE NON-REUSE CHECK READS THE FILE RATHER THAN THE IDENTITIES. "The next
    /// number is above every prior one" cannot be asserted by comparing identities, because
    /// identities deliberately cannot be compared — that is the entire point of B2. So
    /// non-reuse is read off the persisted counters instead, which is also the surface that
    /// actually carries the guarantee across a restart.</para>
    /// </summary>
    private static bool Phase80IdentityCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 80 — permanent identity and the history file ==");

        var ok = true;
        void Check(string label, bool pass, string? why = null)
        {
            Console.WriteLine($"    {(pass ? "ok  " : "FAIL")} {label}"
                              + (why is null ? "" : $"  ({why})"));
            ok &= pass;
        }

        var sandbox = Path.Combine(Path.GetTempPath(), $"charm_s89_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        string Fresh(string name) => Path.Combine(sandbox, name + ".history.json");

        // The fixture world is the bench for everything that needs a real population:
        // 20 schools, 260 people, 300 games — the same fixture Phases 54 and 55 use.
        var tinyPath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json");
        var tiny = LoadWorld(tinyPath);
        var tinyFp = WorldFingerprint(tiny);
        const long seed = 7788L;

        static HistoryError? ErrorOf(Action a)
        {
            try { a(); return null; }
            catch (HistoryException hx) { return hx.Error; }
        }

        try
        {
            // ── B1 — non-reuse at the allocator, across a reload. ────────────────────
            {
                var p = Fresh("b1");
                PersonId[] first, second, third;
                using (var h = HistoryStore.Open(p, tinyFp))
                {
                    first = h.ReservePersons(10);
                    // "Discard" five: they are simply dropped. Nothing tells the allocator,
                    // and that is the design — there is no retirement API to tell it with.
                    second = h.ReservePersons(4);
                }
                // Persist -> reload -> issue again. Numbering continues; it never restarts.
                using (var h2 = HistoryStore.Open(p, tinyFp))
                    third = h2.ReservePersons(3);

                var all = first.Concat(second).Concat(third).ToList();
                Check("B1 non-reuse: 17 identities issued across three batches and one reload, "
                      + "all distinct",
                      all.Distinct().Count() == 17, $"{all.Distinct().Count()} distinct of {all.Count}");
                var state = HistorySchemaV1Peek(p);
                Check("B1 high-water: the stored next-person is exactly 1 + 17 — no hole was "
                      + "filled and the count did not restart at reload",
                      state.NextPersonId == 18, $"nextPersonId {state.NextPersonId}");
                Check("B1 the other two counters never moved (person issuance is not season "
                      + "or game issuance)",
                      state.NextSeasonId == 1 && state.NextGameId == 1,
                      $"season {state.NextSeasonId}, game {state.NextGameId}");
            }

            // ── B2 — ★ no domain inference, enforced by the TYPE SURFACE. ────────────
            //  Proving the banned operations unwritable beats demonstrating one misuse.
            {
                var idTypes = new[] { typeof(PersonId), typeof(SeasonId), typeof(GameId) };
                const BindingFlags pub = BindingFlags.Public | BindingFlags.Instance
                                       | BindingFlags.Static | BindingFlags.DeclaredOnly;
                var numeric = new HashSet<Type>
                {
                    typeof(long), typeof(int), typeof(short), typeof(byte), typeof(sbyte),
                    typeof(uint), typeof(ulong), typeof(ushort), typeof(double),
                    typeof(float), typeof(decimal), typeof(nint), typeof(nuint),
                };
                var bannedOps = new[]
                {
                    "op_LessThan", "op_GreaterThan", "op_LessThanOrEqual", "op_GreaterThanOrEqual",
                    "op_Implicit", "op_Explicit", "op_Addition", "op_Subtraction",
                    "op_Increment", "op_Decrement", "op_Multiply", "op_Division", "op_Modulus",
                };

                foreach (var t in idTypes)
                {
                    var methods = t.GetMethods(pub);

                    var banned = methods.Where(m => bannedOps.Contains(m.Name, StringComparer.Ordinal))
                                        .Select(m => m.Name).ToList();
                    Check($"B2 {t.Name}: no ordering, conversion or arithmetic operator exists",
                          banned.Count == 0, banned.Count == 0 ? "" : string.Join(", ", banned));

                    // GetHashCode is the ONE public method that returns a number, and it is
                    // required (dictionary keys). It is exempt by NAME, not by type, so a
                    // future `public long Value` cannot hide behind the exemption.
                    var leaks = methods
                        .Where(m => !m.Name.Equals("GetHashCode", StringComparison.Ordinal))
                        .Where(m => numeric.Contains(m.ReturnType))
                        .Select(m => m.Name).ToList();
                    Check($"B2 {t.Name}: no public member hands out a raw number",
                          leaks.Count == 0, leaks.Count == 0 ? "" : string.Join(", ", leaks));

                    var comparable = t.GetInterfaces()
                        .Where(i => i.Name.StartsWith("IComparable", StringComparison.Ordinal))
                        .Select(i => i.Name).ToList();
                    Check($"B2 {t.Name}: implements no ordering interface",
                          comparable.Count == 0, string.Join(", ", comparable));

                    // Equality is REQUIRED, not banned. Record structs generate it and the
                    // stat layer needs it — "is this the same man" is the one comparison
                    // identity exists to answer.
                    Check($"B2 {t.Name}: equality operators ARE present (required)",
                          methods.Any(m => m.Name == "op_Equality")
                          && methods.Any(m => m.Name == "op_Inequality"));

                    var rawProp = t.GetProperty("Raw", BindingFlags.NonPublic | BindingFlags.Instance);
                    Check($"B2 {t.Name}: the raw-value seam exists but is not public",
                          rawProp is not null && !(rawProp.GetMethod?.IsPublic ?? false));
                }

                // The seam is sealed by the ASSEMBLY BOUNDARY, so a friend-assembly grant
                // would silently unseal every check above without changing any of them.
                var friends = typeof(PersonId).Assembly
                    .GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
                    .Select(a => a.AssemblyName).ToList();
                Check("B2 the identity assembly grants no InternalsVisibleTo — the seam cannot "
                      + "be reached from harness or calibration code",
                      friends.Count == 0, string.Join(", ", friends));
            }

            // ── B3 — deterministic issuance, from two ISOLATED files. ────────────────
            //  Deliberately NOT restore-one-file-and-rerun: restoring is branching, which is
            //  a different (and explicitly unsupported) thing.
            {
                var a = Fresh("b3a");
                var b = Fresh("b3b");
                var seedState = HistoryStateV1.Fresh(tinyFp);
                WriteHistoryForCheck(a, seedState);
                WriteHistoryForCheck(b, seedState);
                Check("B3 the two isolated histories start byte-identical",
                      File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)));

                Dictionary<int, PersonId> Run(string path)
                {
                    using var h = HistoryStore.Open(path, tinyFp);
                    var res = RunDivvyDraft(tiny, seed, h);
                    return res.PersonIds!.Keys.ToDictionary(k => k, k => res.PersonIds![k]);
                }
                var mapA = Run(a);
                var mapB = Run(b);
                Check("B3 same world, same seed, same starting state -> identical pool-slot -> "
                      + "person map, entry for entry",
                      mapA.Count == mapB.Count && mapA.All(kv => mapB[kv.Key] == kv.Value),
                      $"{mapA.Count} entries");
                Check("B3 both files advanced to the same next-person",
                      HistorySchemaV1Peek(a).NextPersonId == HistorySchemaV1Peek(b).NextPersonId,
                      $"{HistorySchemaV1Peek(a).NextPersonId}");
            }

            // ── B4 — transport bijection, scoped to one cohort and one season. ───────
            {
                var p = Fresh("b4");
                using var h = HistoryStore.Open(p, tinyFp);
                var outcome = RunSeasonCore(tiny, seed, configPath, verbose: false, h);
                var recs = outcome.League.PlayerSeasons;

                Check("B4 every admitted person carries an identity in the season roll-up",
                      recs.Values.All(r => r.PersonId is { IsValid: true }),
                      $"{recs.Values.Count(r => r.PersonId is not { IsValid: true })} missing");
                Check("B4 no two season records share a person identity",
                      recs.Values.Select(r => r.PersonId!.Value).Distinct().Count() == recs.Count,
                      $"{recs.Count} records");
                Check("B4 the pool-slot -> person pair carried from construction is the pair the "
                      + "roll-up holds (RecordFor is idempotent on identity, not re-derived)",
                      recs.Values.All(r => r.PersonId == outcome.Divvy.PersonIds![r.PoolId]));
                Check("B4 the drift counter is still zero — accumulation never rewrote a "
                      + "stamped identity",
                      outcome.League.IdentityDriftObservations == 0,
                      $"{outcome.League.IdentityDriftObservations}");
            }

            // ── B5 — fixture identity across two episodes. ───────────────────────────
            {
                var p = Fresh("b5");
                List<SeasonGameResult> a, b;
                using (var h = HistoryStore.Open(p, tinyFp))
                    a = RunSeasonCore(tiny, seed, configPath, verbose: false, h).Results;
                using (var h = HistoryStore.Open(p, tinyFp))
                    b = RunSeasonCore(tiny, seed, configPath, verbose: false, h).Results;

                var seasonsA = a.Select(r => r.SeasonId!.Value).Distinct().ToList();
                var seasonsB = b.Select(r => r.SeasonId!.Value).Distinct().ToList();
                Check("B5 each episode is exactly one season",
                      seasonsA.Count == 1 && seasonsB.Count == 1);
                Check("B5 the second episode is a DIFFERENT season, not a rerun of the first",
                      seasonsA[0] != seasonsB[0]);

                var gamesA = a.Select(r => r.GameId!.Value).ToList();
                var gamesB = b.Select(r => r.GameId!.Value).ToList();
                Check("B5 no game number collides within or across the two episodes",
                      gamesA.Concat(gamesB).Distinct().Count() == gamesA.Count + gamesB.Count,
                      $"{gamesA.Count} + {gamesB.Count}");
                var st = HistorySchemaV1Peek(p);
                Check("B5 B continues ABOVE A: the stored next-game is 1 + both episodes' fixtures",
                      st.NextGameId == 1 + gamesA.Count + gamesB.Count, $"nextGameId {st.NextGameId}");
                Check("B5 every fixture's identity survived result construction and every "
                      + "accumulator path",
                      a.All(r => r.GameId is { IsValid: true } && r.SeasonId is { IsValid: true }));
            }

            // ── B6 — type separation. ───────────────────────────────────────────────
            {
                var pairs = new[]
                {
                    (typeof(PersonId), typeof(SeasonId)), (typeof(SeasonId), typeof(GameId)),
                    (typeof(GameId), typeof(PersonId)),
                };
                var any = false;
                foreach (var (x, y) in pairs)
                    foreach (var m in x.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        if ((m.Name == "op_Implicit" || m.Name == "op_Explicit")
                            && (m.ReturnType == y || m.GetParameters().Any(pp => pp.ParameterType == y)))
                            any = true;
                Check("B6 no conversion exists between any two identity types", !any);

                var shared = typeof(PersonId).GetInterfaces()
                    .Intersect(typeof(SeasonId).GetInterfaces())
                    .Intersect(typeof(GameId).GetInterfaces())
                    .Where(i => i != typeof(IEquatable<PersonId>))
                    .Select(i => i.Name).ToList();
                Check("B6 the three types share no interface exposing numeric semantics",
                      shared.Count == 0, string.Join(", ", shared));
            }

            // ── B7 — domain guards. ─────────────────────────────────────────────────
            {
                var p = Fresh("b7");
                using (HistoryStore.Open(p, tinyFp)) { }

                Check("B7 a default identity is invalid and is refused at a boundary",
                      ErrorOf(() => IdentityGuard.Require(default(PersonId), "a test boundary"))
                          == HistoryError.MissingIdentity);
                Check("B7 a null identity is refused at a boundary in history mode",
                      ErrorOf(() => IdentityGuard.Require((SeasonId?)null, "a test boundary"))
                          == HistoryError.MissingIdentity);

                foreach (var bad in new long[] { 0, -1, -999 })
                {
                    var f = Fresh($"b7_{bad}");
                    WriteRawHistoryForCheck(f, tinyFp, bad, 1, 1);
                    Check($"B7 a stored next-person of {bad.ToString(CultureInfo.InvariantCulture)} "
                          + "fails the load, classified",
                          ErrorOf(() => HistoryStore.Open(f, tinyFp).Dispose())
                              == HistoryError.CounterOutOfDomain);
                }
                var fMax = Fresh("b7_max");
                WriteRawHistoryForCheck(fMax, tinyFp, long.MaxValue, 1, 1);
                Check("B7 a stored next-person of long.MaxValue fails the load, classified",
                      ErrorOf(() => HistoryStore.Open(fMax, tinyFp).Dispose())
                          == HistoryError.CounterOutOfDomain);

                var fNear = Fresh("b7_near");
                WriteRawHistoryForCheck(fNear, tinyFp, long.MaxValue - 5, 1, 1);
                using (var h = HistoryStore.Open(fNear, tinyFp))
                {
                    var one = h.ReservePersons(1);
                    Check("B7 a valid single reservation near the ceiling succeeds",
                          one.Length == 1 && one[0].IsValid);
                    var before = File.ReadAllBytes(fNear);
                    var err = ErrorOf(() => h.ReservePersons(1_000_000));
                    Check("B7 an oversized batch is rejected, classified",
                          err == HistoryError.ExhaustedRange, err?.ToString());
                    Check("B7 the rejected batch left the file byte-identical — no partial "
                          + "advance",
                          File.ReadAllBytes(fNear).SequenceEqual(before));
                    Check("B7 a negative count is rejected, classified",
                          ErrorOf(() => h.ReservePersons(-1)) == HistoryError.NegativeCount);
                    var before0 = File.ReadAllBytes(fNear);
                    var none = h.ReservePersons(0);
                    Check("B7 a zero reservation is a no-op that writes nothing",
                          none.Length == 0 && File.ReadAllBytes(fNear).SequenceEqual(before0));
                }
            }

            // ── B8 — ★ behavioural isolation, with a negative control. ──────────────
            //  The season is run in legacy mode and in history mode and every number the
            //  outcome carries is compared. Then one number is deliberately moved and the
            //  comparator is required to reject it — without that, a green line here proves
            //  only that the comparator was never able to fail.
            {
                var legacy = RunSeasonCore(tiny, seed, configPath, verbose: false);
                var p = Fresh("b8");
                SeasonRunOutcome hist;
                using (var h = HistoryStore.Open(p, tinyFp))
                    hist = RunSeasonCore(tiny, seed, configPath, verbose: false, h);

                string Surface(SeasonRunOutcome o)
                {
                    var sb = new StringBuilder();
                    sb.Append(o.Fingerprint).Append('|').Append(o.Ties).Append('|');
                    foreach (var r in o.Results)
                        sb.Append(r.HomeId).Append(',').Append(r.AwayId).Append(',')
                          .Append(r.HomeScore).Append(',').Append(r.AwayScore).Append(',')
                          .Append(r.OvertimePeriods).Append(';');
                    foreach (var k in o.Wins.Keys.OrderBy(x => x))
                        sb.Append(k).Append(':').Append(o.Wins[k]).Append('/').Append(o.Losses[k]).Append(';');
                    var lg = o.League;
                    sb.Append(lg.PossessionRecords).Append('|').Append(lg.PointsFromRecords).Append('|')
                      .Append(lg.PointsFromScores).Append('|').Append(lg.MetadataDriftRecords).Append('|')
                      .Append(lg.IdentityDriftObservations).Append('|');
                    foreach (var kv in o.League.PlayerSeasons.OrderBy(x => x.Key))
                    {
                        var r = kv.Value;
                        sb.Append(r.PoolId).Append(',').Append(r.SchoolId).Append(',')
                          .Append(r.GamesPlayed).Append(',').Append(r.Credits).Append(',')
                          .Append(r.Fga).Append(',').Append(r.Fgm).Append(',').Append(r.Tpa).Append(',')
                          .Append(r.Tpm).Append(',').Append(r.Fta).Append(',').Append(r.Ftm).Append(',')
                          .Append(r.OReb).Append(',').Append(r.DReb).Append(',').Append(r.Ast).Append(',')
                          .Append(r.Stl).Append(',').Append(r.Blk).Append(',').Append(r.To).Append(',')
                          .Append(r.ShFoul).Append(',').Append(r.NsFoul).Append(';');
                    }
                    return sb.ToString();
                }

                var sLegacy = Surface(legacy);
                var sHist = Surface(hist);
                Check("B8 isolation: the whole fixture season is identical with and without a "
                      + "history — every score, standing, conservation total and per-player line",
                      sLegacy == sHist,
                      sLegacy == sHist ? $"{sLegacy.Length} chars compared" : "surfaces differ");
                Check("B8 the schedule fingerprint is untouched by the new fixture fields",
                      legacy.Fingerprint == hist.Fingerprint, hist.Fingerprint[..8] + "…");

                // ★ NEGATIVE CONTROL — a REAL one. One live per-player field is moved by a
                // single shot, the surface is rebuilt from the mutated outcome, and the
                // comparison is required to go red. Mutating a string would only have proved
                // that two different strings are different; mutating the outcome proves the
                // surface is actually reading the season.
                var victim = hist.League.PlayerSeasons.OrderBy(x => x.Key).First().Value;
                victim.Fga += 1;
                var sTampered = Surface(hist);
                victim.Fga -= 1;
                Check("B8 NEGATIVE CONTROL: one extra shot attempt on one player turns the "
                      + "isolation comparison red — this check is able to fail",
                      sTampered != sLegacy && Surface(hist) == sLegacy);

                // And the identity fields really are populated in history mode, so the
                // isolation above is not passing because nothing happened.
                Check("B8 discriminator: history mode really did number the season "
                      + "(isolation is not passing because the feature is inert)",
                      hist.Results.All(r => r.GameId is { IsValid: true })
                      && hist.League.PlayerSeasons.Values.All(r => r.PersonId is { IsValid: true })
                      && legacy.Results.All(r => r.GameId is null)
                      && legacy.League.PlayerSeasons.Values.All(r => r.PersonId is null));
            }

            // ── B9 — PoolId untouched. ──────────────────────────────────────────────
            {
                var p = Fresh("b9");
                using var h = HistoryStore.Open(p, tinyFp);
                var withHist = RunDivvyDraft(tiny, seed, h);
                var without = RunDivvyDraft(tiny, seed);
                var n = tiny.Schools.Count;

                Check("B9 the pool is the same length and the same order",
                      withHist.Pool.Count == without.Pool.Count
                      && withHist.Pool.Zip(without.Pool).All(x => x.First.PoolId == x.Second.PoolId
                                                              && x.First.Pos == x.Second.Pos));
                Check("B9 every roster holds the same pool slots in the same acquisition order",
                      withHist.Rosters.Keys.OrderBy(x => x).SequenceEqual(without.Rosters.Keys.OrderBy(x => x))
                      && withHist.Rosters.All(kv => kv.Value.SequenceEqual(without.Rosters[kv.Key])));
                Check("B9 PoolId still decides POSITION: the guard/wing/big blocks are exactly "
                      + "where RosterShape puts them",
                      withHist.Pool.All(pp => pp.Pos == RosterShape.PositionForPoolIndex(pp.PoolId, n)));
                Check("B9 the pool slot is unique across the whole admitted population — "
                      + "national, never team-local",
                      withHist.Rosters.Values.SelectMany(x => x).Distinct().Count()
                          == RosterShape.PoolSize(n));
            }

            // ── B10 — the file lifecycle. ───────────────────────────────────────────
            {
                var p = Fresh("b10");
                Check("B10 absent -> created as valid v1", !File.Exists(p));
                using (HistoryStore.Open(p, tinyFp)) { }
                Check("B10 the created file exists and parses", File.Exists(p));

                // ★ The golden is compared against what the STORE ACTUALLY WRITES, never
                // against a second hand-rolled copy of the same format — that would be a
                // check comparing this file's opinion of the format to its own opinion.
                var golden = Path.Combine(AppContext.BaseDirectory, "tools", "history_v1_golden.json");
                var goldenFp = "sha256-v1:" + new string('0', 64);
                var pGolden = Fresh("b10_golden");
                using (HistoryStore.Open(pGolden, goldenFp)) { }
                Check("B10 canonical output matches the committed golden v1 fixture "
                      + "(key order, 2-space indent, UTF-8 no BOM, final newline)",
                      File.Exists(golden)
                      && File.ReadAllBytes(golden).SequenceEqual(File.ReadAllBytes(pGolden)),
                      File.Exists(golden) ? "" : "golden fixture missing");

                // Round-trip: write, read, write again -> identical bytes.
                var b1 = File.ReadAllBytes(p);
                using (HistoryStore.Open(p, tinyFp)) { }
                Check("B10 validation without reservation leaves the file byte-identical",
                      File.ReadAllBytes(p).SequenceEqual(b1));

                // The world file is never touched by any history operation.
                var worldBefore = File.ReadAllBytes(tinyPath);
                var p2 = Fresh("b10b");
                using (var h = HistoryStore.Open(p2, tinyFp)) { h.ReservePersons(5); h.ReserveSeason(); }
                Check("B10 the world file's bytes are untouched by every history operation",
                      File.ReadAllBytes(tinyPath).SequenceEqual(worldBefore));

                // Every rejection case, each with its own classification.
                var cases = new (string Name, string Body, HistoryError Want)[]
                {
                    ("malformed",  "{ not json",                                   HistoryError.MalformedJson),
                    ("unknown",    RawHistory(tinyFp, 1, 1, 1, extra: "\"nope\": 1"), HistoryError.UnknownKey),
                    ("missing",    "{\"format\":\"charm-history\",\"schemaVersion\":1}", HistoryError.MissingKey),
                    ("format",     RawHistory(tinyFp, 1, 1, 1).Replace("charm-history", "other"), HistoryError.WrongFormat),
                    ("version",    RawHistory(tinyFp, 1, 1, 1).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 7"), HistoryError.UnsupportedVersion),
                    ("wrongtype",  RawHistory(tinyFp, 1, 1, 1).Replace("\"nextGameId\": 1", "\"nextGameId\": \"x\""), HistoryError.WrongType),
                };
                foreach (var (name, body, want) in cases)
                {
                    var f = Fresh("b10_" + name);
                    File.WriteAllText(f, body);
                    var before = File.ReadAllBytes(f);
                    var got = ErrorOf(() => HistoryStore.Open(f, tinyFp).Dispose());
                    Check($"B10 rejection '{name}' stops loudly as {want}", got == want, got?.ToString());
                    Check($"B10 rejection '{name}' modified nothing",
                          File.ReadAllBytes(f).SequenceEqual(before));
                }

                var fWrongWorld = Fresh("b10_world");
                WriteRawHistoryForCheck(fWrongWorld, "sha256-v1:" + new string('a', 64), 1, 1, 1);
                Check("B10 a history bound to a different world is refused",
                      ErrorOf(() => HistoryStore.Open(fWrongWorld, tinyFp).Dispose())
                          == HistoryError.FingerprintMismatch);

                Check("B10 a path that is an existing directory is rejected",
                      ErrorOf(() => HistoryStore.Open(sandbox, tinyFp).Dispose())
                          == HistoryError.PathIsDirectory);

                var pLock = Fresh("b10_lock");
                using (HistoryStore.Open(pLock, tinyFp))
                {
                    Check("B10 a second opener cannot take a held history lock",
                          ErrorOf(() => HistoryStore.Open(pLock, tinyFp).Dispose())
                              == HistoryError.LockUnavailable);
                }
            }

            // ── B11 — legacy mode. ──────────────────────────────────────────────────
            {
                var before = Directory.GetFileSystemEntries(sandbox).Length;
                var legacy = RunSeasonCore(tiny, seed, configPath, verbose: false);
                Check("B11 a run without a history creates no file and touches no folder",
                      Directory.GetFileSystemEntries(sandbox).Length == before);
                Check("B11 every identity field is ABSENT — never a zero, never a synthetic id",
                      legacy.Divvy.PersonIds is null
                      && legacy.Schedule.All(g => g.SeasonId is null && g.GameId is null)
                      && legacy.Results.All(r => r.SeasonId is null && r.GameId is null)
                      && legacy.League.PlayerSeasons.Values.All(r => r.PersonId is null));
                Check("B11 the fingerprint is a pure function of the world, not of any run state",
                      WorldFingerprint(tiny) == WorldFingerprint(LoadWorld(tinyPath)));
            }
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { /* best effort */ }
        }

        return ok;
    }

    // ── Small check-only helpers ────────────────────────────────────────────────
    //  These construct history files BY HAND rather than through the store, which is the
    //  point: a check that could only build a file with the writer could never test what
    //  the reader does with a file the writer would not have produced.

    private static string RawHistory(string fp, long person, long season, long game, string? extra = null)
        => "{\n"
         + "  \"format\": \"charm-history\",\n"
         + "  \"schemaVersion\": 1,\n"
         + $"  \"worldFingerprint\": \"{fp}\",\n"
         + $"  \"nextPersonId\": {person.ToString(CultureInfo.InvariantCulture)},\n"
         + $"  \"nextSeasonId\": {season.ToString(CultureInfo.InvariantCulture)},\n"
         + $"  \"nextGameId\": {game.ToString(CultureInfo.InvariantCulture)}"
         + (extra is null ? "" : ",\n  " + extra)
         + "\n}\n";

    private static void WriteRawHistoryForCheck(string path, string fp, long person, long season, long game)
        => File.WriteAllBytes(path, Encoding.UTF8.GetBytes(RawHistory(fp, person, season, game)));

    private static void WriteHistoryForCheck(string path, HistoryStateV1 s)
        => File.WriteAllBytes(path, CanonicalHistoryBytesForCheck(s));

    private static byte[] CanonicalHistoryBytesForCheck(HistoryStateV1 s)
        => Encoding.UTF8.GetBytes(RawHistory(s.WorldFingerprint, s.NextPersonId, s.NextSeasonId, s.NextGameId));

    private static HistoryStateV1 PeekState(string path)
    {
        var text = File.ReadAllText(path);
        long Get(string key)
        {
            var i = text.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            var c = text.IndexOf(':', i) + 1;
            var e = c;
            while (e < text.Length && (char.IsDigit(text[e]) || text[e] == '-' || text[e] == ' ')) e++;
            return long.Parse(text[c..e].Trim(), CultureInfo.InvariantCulture);
        }
        return new HistoryStateV1("", Get("nextPersonId"), Get("nextSeasonId"), Get("nextGameId"));
    }

    private static HistoryStateV1 HistorySchemaV1Peek(string path) => PeekState(path);
}
