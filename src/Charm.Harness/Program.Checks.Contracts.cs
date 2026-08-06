using System.Globalization;
using System.Text;
using System.Text.Json;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  Phase 94 (Session 103) — CONTRACTS AND THE NON-CONFERENCE LOG.
//
//  What this phase proves:
//    C1  ★ ORACLE PARITY — every trajectory in tools/contracts_golden.json
//        replayed through the pure C# season step, season by season: the forced
//        flag, the decision, the post-exercise count and the state written
//        forward. All integers, so parity is exact, never ULP-bounded. This is
//        the table that discriminates A4's two window conventions — completion
//        does not, since with slack both conventions complete.
//    C2  state transitions directly: optional decline; forced exercise
//        overriding a declining policy; the exact-window home-and-home forced
//        twice and completing; an invalid authored contract (4 legs, 3 window)
//        rejected at authoring; at most one leg per contract per season.
//    C3  ★ THE SPECIFIC-LEG CHOICE AS A PERSISTENCE TEST — author
//        [A home, B home, A home], exercise the B leg by injected choice,
//        SERIALIZE through the real record, and read the next season's state:
//        the outstanding legs are exactly the two A-home legs with their stable
//        ids and authored ordering intact. A stored gamesRemaining scalar
//        passes an in-memory check and fails exactly here (A3).
//    C4  ★ CROSS-SEASON INHERITANCE AT THE REAL READER/WRITER BOUNDARY — with
//        season 1's record DELETED, season 3 still loads the surviving state,
//        proving the read is exactly N-1 and never reaches an older record.
//    C5  malformed authoring, rejected by name — nine ways, each cheap, each
//        protecting the expensive stored shape.
//    C6  ★ the neutral fixture and the equal-host fixture — an explicit
//        executor with no host at all, neutral leg selection sitting between
//        home and away, and the executor field round-tripping through
//        serialization unchanged (a home-and-home forces immediately, which
//        demonstrates forcing but says nothing about the executor surviving).
//    C7  multi-contract: two forced on one school; forced never crowded by an
//        optional; capacity reached exactly; obligations exceeding capacity as
//        ONE hard failure with nothing committed and the windows UNFROZEN...
//        frozen — un-decremented; and enumeration order not changing the
//        canonical result.
//    C8  both deaths fail closed: conference mates terminate BEFORE exercise
//        even when equality-forced; a damaged record reports a
//        collection-level loss and names no pairing.
//    C9  integration on a real career: the contracted pairing bypasses
//        matching, the pair is unmatchable, both schools' request counts move
//        by the ruled buckets, and no contracted game is silently discarded.
//    C10 migration: a v1 record reads as EMPTY (never unknown), the current
//        version loads, a malformed record and an unknown future version are
//        collection-level losses, a missing record is its own quiet status.
//    C11 the charging chains as arithmetic, including the ruled fallback: an
//        away leg with no road games costs a HOME date.
//    C12 the pairing log on disk: always present, contracted and matched
//        entries both carried, normalised, and counted against the run.
//
//  ── What this phase deliberately does not prove ────────────────────────────
//  Any basketball value. No contract count, no home/road split, no distance —
//  page-only calibration holds. And nothing here signs a contract: every
//  contract in this file is fixture-authored, because the negotiation layer
//  does not exist yet and honouring is provable without it.
// ============================================================================

internal static partial class Program
{
    private static bool Phase94ContractsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 94 — Contracts and the non-conference log (S103: promises kept " +
                          "across seasons — oracle parity on the window machine, the specific-leg " +
                          "persistence test, real reader/writer inheritance, authoring refusals, the " +
                          "neutral and equal-host fixtures, multi-contract capacity, both deaths, " +
                          "matching integration, migration, the charging chains, the pairing log) ==");
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

        var scratch = Path.Combine(Path.GetTempPath(), "charm-s103-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string WorldPath(string file) =>
                Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var tiny = LoadWorld(WorldPath("fixture-tiny.world.json"));

            // ── Shared builders ─────────────────────────────────────────────────
            ContractLeg Leg(int id, int order, int? host, bool neutral = false, bool done = false)
                => new(id, order, neutral, neutral ? null : host, done);
            LiveContract C(int id, int a, int b, int exec, int window, params ContractLeg[] legs)
                => new(id, a, b, exec, window, legs.ToList());
            ContractLoad Loaded(params LiveContract[] cs)
                => new(ContractLoadStatus.Loaded, cs.ToList(), null);
            ContractSeasonOutcome Step(ContractLoad load, Func<int, bool>? wants = null,
                                       Func<int, int>? open = null,
                                       IReadOnlyDictionary<int, int>? choice = null,
                                       Func<int, int, bool>? league = null)
                => ContractSeasonStep(load, league ?? ((_, _) => false), open ?? (_ => 99),
                                      choice, wants ?? (_ => false));

            // ════════════════════════════════════════════════════════════════════
            //  C1 — ORACLE PARITY, trajectory for trajectory.
            // ════════════════════════════════════════════════════════════════════
            {
                var goldenPath = Path.Combine(AppContext.BaseDirectory, "tools", "contracts_golden.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(goldenPath));
                var trajectories = doc.RootElement.GetProperty("trajectories").EnumerateArray().ToList();
                var mismatches = new List<string>();
                var seasonsChecked = 0;
                foreach (var t in trajectories)
                {
                    var name = t.GetProperty("name").GetString()!;
                    var games = t.GetProperty("games").GetInt32();
                    var window = t.GetProperty("window").GetInt32();
                    var policy = t.GetProperty("policy").GetString()!;
                    var legs = Enumerable.Range(1, games)
                        .Select(i => Leg(i, i, null, neutral: true)).ToArray();
                    var live = new List<LiveContract> { C(1, 101, 202, 101, window, legs) };
                    foreach (var row in t.GetProperty("seasons").EnumerateArray())
                    {
                        var season = row.GetProperty("season").GetInt32();
                        bool Wants(int _) => policy switch
                        {
                            "decline" => false,
                            "exercise-season-1" => season == 1,
                            "always" => true,
                            _ => throw new InvalidOperationException($"unknown policy '{policy}'"),
                        };
                        var current = live.Single();
                        var forcedExpected = row.GetProperty("forced").GetBoolean();
                        var decision = row.GetProperty("decision").GetString()!;
                        var startOk = current.GamesRemaining == row.GetProperty("startGames").GetInt32()
                                      && current.WindowRemaining == row.GetProperty("startWindow").GetInt32();
                        var forcedGot = current.GamesRemaining == current.WindowRemaining;
                        var o = Step(Loaded(current), Wants);
                        var exercisedGot = o.Exercised.Count == 1;
                        var exercisedExpected = decision != "decline";
                        var rollWindow = row.GetProperty("rollWindow");
                        bool rollOk;
                        if (rollWindow.ValueKind == JsonValueKind.Null)
                            rollOk = o.Survivors.Count == 0;   // complete — never written forward
                        else
                            rollOk = o.Survivors.Count == 1
                                     && o.Survivors[0].GamesRemaining == row.GetProperty("rollGames").GetInt32()
                                     && o.Survivors[0].WindowRemaining == rollWindow.GetInt32();
                        if (!startOk || forcedGot != forcedExpected || exercisedGot != exercisedExpected || !rollOk)
                            mismatches.Add($"{name} season {season}");
                        seasonsChecked++;
                        if (o.Survivors.Count == 0) break;
                        live = o.Survivors.ToList();
                    }
                }
                Check("C1: ★ every golden trajectory replays through the pure season step exactly — " +
                      "forced flag, decision, and the state written forward",
                      mismatches.Count == 0 && trajectories.Count == 97,
                      mismatches.Count == 0
                          ? $"{trajectories.Count} trajectories, {seasonsChecked} season rows"
                          : string.Join("; ", mismatches.Take(4)));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — STATE TRANSITIONS, directly.
            // ════════════════════════════════════════════════════════════════════
            {
                var threeFour = C(1, 11, 22, 11, 4, Leg(1, 1, 11), Leg(2, 2, 22), Leg(3, 3, 11));
                var declined = Step(Loaded(threeFour));
                Check("C2a: an optional contract declines under the placeholder policy and rolls 3/4 → 3/3",
                      declined.Exercised.Count == 0 && declined.PolicyDeclined.SequenceEqual(new[] { 1 })
                      && declined.Survivors.Single().GamesRemaining == 3
                      && declined.Survivors.Single().WindowRemaining == 3);

                var atWall = C(1, 11, 22, 11, 3, Leg(1, 1, 11), Leg(2, 2, 22), Leg(3, 3, 11));
                var forcedRun = Step(Loaded(atWall), wants: _ => false);
                Check("C2b: ★ forced exercise OVERRIDES a declining policy at games == window",
                      forcedRun.Exercised.Count == 1 && forcedRun.PolicyDeclined.Count == 0
                      && forcedRun.Survivors.Single().GamesRemaining == 2);

                var hh = C(2, 11, 22, 11, 2, Leg(1, 1, 11), Leg(2, 2, 22));
                var y1 = Step(Loaded(hh), wants: _ => false);
                var y2 = Step(Loaded(y1.Survivors.ToArray()), wants: _ => false);
                Check("C2c: the exact-window home-and-home is forced immediately, forced again, and " +
                      "completes — never written forward",
                      y1.Exercised.Count == 1 && y1.Survivors.Single() is { GamesRemaining: 1, WindowRemaining: 1 }
                      && y2.Exercised.Count == 1 && y2.Survivors.Count == 0);

                var overfull = C(3, 11, 22, 11, 3,
                                 Leg(1, 1, 11), Leg(2, 2, 22), Leg(3, 3, 11), Leg(4, 4, 22));
                var msg = Refusal(() => ValidateContractCollection(new[] { overfull }));
                Check("C2d: an authored contract with 4 legs in a 3-season window is rejected AT AUTHORING",
                      msg is not null && msg.Contains("outstanding legs"),
                      msg is null ? "NO REFUSAL" : "refused");

                var chosen = Step(Loaded(atWall), wants: _ => true,
                                  choice: new Dictionary<int, int> { [1] = 2 });
                Check("C2e: at most one leg per contract per season, even when forced, optional policy " +
                      "and an injected choice all point at the same contract",
                      chosen.Exercised.Count == 1
                      && chosen.Survivors.Single().Legs.Count(l => l.Completed) == 1);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3/C4/C9/C10/C12 — THE REAL CAREER. One disk lifecycle serves five
            //  check groups: season 1 writes an empty v2 record, the record is
            //  doctored to author [A home, B home, A home], season 2 exercises the
            //  B leg by injected choice, and season 3 inherits with season 1's
            //  record DELETED.
            // ════════════════════════════════════════════════════════════════════
            var playing = tiny.Schools
                .Where(s => tiny.Conferences.Single(c => c.Id == s.ConferenceId).Games > 0)
                .OrderBy(s => s.Id).ToList();
            var schoolA = playing.First();
            var schoolB = playing.First(s => s.ConferenceId != schoolA.ConferenceId);
            var careerPath = Path.Combine(scratch, "career.json");
            var tinyFp = WorldFingerprint(tiny);

            SeasonRunOutcome season1;
            using (var store = HistoryStore.Open(careerPath, tinyFp))
                season1 = RunSeasonCore(tiny, MteCheckSeed, configPath, verbose: false, store);
            var rec1 = MteRecordPathFor(careerPath, 1);
            var rec1Text = File.ReadAllText(rec1);
            Check("C12a: a v2 record carries BOTH collections even when empty — absence can never " +
                  "be mistaken for emptiness",
                  rec1Text.Contains("\"formatVersion\": 2") && rec1Text.Contains("\"liveContracts\": []")
                  && rec1Text.Contains("\"nonConferencePairings\": ["),
                  season1.Contracts.Load.Status.ToString());

            var authored =
                "\"liveContracts\": [\n" +
                "    {\n" +
                $"      \"contractId\": 7,\n      \"schoolAId\": {schoolA.Id},\n" +
                $"      \"schoolBId\": {schoolB.Id},\n      \"executorId\": {schoolA.Id},\n" +
                "      \"windowRemaining\": 6,\n      \"legs\": [\n" +
                $"        {{ \"legId\": 1, \"order\": 1, \"site\": \"Home\", \"hostId\": {schoolA.Id}, \"status\": \"Outstanding\" }},\n" +
                $"        {{ \"legId\": 2, \"order\": 2, \"site\": \"Home\", \"hostId\": {schoolB.Id}, \"status\": \"Outstanding\" }},\n" +
                $"        {{ \"legId\": 3, \"order\": 3, \"site\": \"Home\", \"hostId\": {schoolA.Id}, \"status\": \"Outstanding\" }}\n" +
                "      ]\n    }\n  ]";
            File.WriteAllText(rec1, rec1Text.Replace("\"liveContracts\": []", authored));

            SeasonRunOutcome season2;
            using (var store = HistoryStore.Open(careerPath, tinyFp))
                season2 = RunSeasonCore(tiny, MteCheckSeed, configPath, verbose: false, store,
                                        contractChoiceOverride: new Dictionary<int, int> { [7] = 2 });
            {
                var o = season2.Contracts;
                var g = o.Exercised.SingleOrDefault();
                Check("C9a: ★ the injected choice exercises the named leg — the B-hosted game, " +
                      "played as a fixed pairing that never entered matching",
                      o.Load.Status == ContractLoadStatus.Loaded && g is not null
                      && g.LegId == 2 && !g.IsNeutral && g.HostId == schoolB.Id,
                      g is null ? "nothing exercised" : $"leg {g.LegId}, host {g.HostId}");
                var pairLo = Math.Min(schoolA.Id, schoolB.Id);
                var pairHi = Math.Max(schoolA.Id, schoolB.Id);
                Check("C9b: ★ the matcher cannot rematch the contracted pair — it appears in no " +
                      "phase's output",
                      season2.Matching.Pairs.All(p =>
                          (Math.Min(p.HostSchoolId, p.VisitorSchoolId),
                           Math.Max(p.HostSchoolId, p.VisitorSchoolId)) != (pairLo, pairHi)));
                var control = BuildNonConferenceRequests(tiny, season2.Events.Seating);
                var liveRep = season2.NonConference;
                var hostRow = liveRep.Schools.Single(s => s.SchoolId == schoolB.Id);
                var hostCtl = control.Schools.Single(s => s.SchoolId == schoolB.Id);
                var visRow = liveRep.Schools.Single(s => s.SchoolId == schoolA.Id);
                var visCtl = control.Schools.Single(s => s.SchoolId == schoolA.Id);
                var hostCharged =
                    hostCtl.Home > 0 ? hostRow.Home == hostCtl.Home - 1 && hostRow.Road == hostCtl.Road
                                     : hostRow.Road == hostCtl.Road - 1;
                var visCharged =
                    visCtl.Road > 0 ? visRow.Road == visCtl.Road - 1 && visRow.Home == visCtl.Home
                                    : visRow.Home == visCtl.Home - 1;
                Check("C9c: ★ both schools lose an opening in the RULED bucket — the host a home " +
                      "game, the visitor a road game (or a home date when it has no road)",
                      hostCharged && visCharged,
                      $"host home {hostCtl.Home}→{hostRow.Home}, visitor road {visCtl.Road}→{visRow.Road} " +
                      $"home {visCtl.Home}→{visRow.Home}");
                Check("C9d: total open games is conserved through the charge — the contracted game " +
                      "replaces a request instead of stacking on top of one",
                      hostRow.Home + hostRow.Neutral + hostRow.Road
                          == hostCtl.Home + hostCtl.Neutral + hostCtl.Road - 1
                      && visRow.Home + visRow.Neutral + visRow.Road
                          == visCtl.Home + visCtl.Neutral + visCtl.Road - 1);
            }

            var rec2 = MteRecordPathFor(careerPath, 2);
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(rec2));
                var contracts = doc.RootElement.GetProperty("liveContracts").EnumerateArray().ToList();
                var legs = contracts.Count == 1
                    ? contracts[0].GetProperty("legs").EnumerateArray().ToList()
                    : new List<JsonElement>();
                var outstanding = legs.Where(l => l.GetProperty("status").GetString() == "Outstanding").ToList();
                Check("C3: ★ THE SPECIFIC-LEG PERSISTENCE TEST — the record written forward holds " +
                      "exactly the two A-home legs outstanding, stable ids 1 and 3, authored order " +
                      "intact, the B leg Completed, the window decremented to 5",
                      contracts.Count == 1
                      && contracts[0].GetProperty("windowRemaining").GetInt32() == 5
                      && outstanding.Select(l => l.GetProperty("legId").GetInt32()).SequenceEqual(new[] { 1, 3 })
                      && outstanding.Select(l => l.GetProperty("order").GetInt32()).SequenceEqual(new[] { 1, 3 })
                      && outstanding.All(l => l.GetProperty("hostId").GetInt32() == schoolA.Id)
                      && legs.Single(l => l.GetProperty("legId").GetInt32() == 2)
                             .GetProperty("status").GetString() == "Completed",
                      $"{contracts.Count} contract(s), {outstanding.Count} outstanding");

                var pairings = doc.RootElement.GetProperty("nonConferencePairings").EnumerateArray().ToList();
                var contracted = pairings.Where(p => p.GetProperty("source").GetString() == "Contracted").ToList();
                var matched = pairings.Count(p => p.GetProperty("source").GetString() == "Matched");
                Check("C12b: ★ the pairing log carries the season — one Contracted entry (the pair " +
                      "normalised, site Hosted, the host named) beside every Matched pairing, and " +
                      "no contracted game is silently discarded",
                      contracted.Count == season2.Contracts.Exercised.Count
                      && contracted.Count == 1
                      && contracted[0].GetProperty("schoolAId").GetInt32() == Math.Min(schoolA.Id, schoolB.Id)
                      && contracted[0].GetProperty("schoolBId").GetInt32() == Math.Max(schoolA.Id, schoolB.Id)
                      && contracted[0].GetProperty("site").GetString() == "Hosted"
                      && contracted[0].GetProperty("hostId").GetInt32() == schoolB.Id
                      && matched == season2.Matching.Pairs.Count,
                      $"{contracted.Count} contracted + {matched} matched");
            }

            // C10 — migration, against record 2, each variant on its own doctored copy.
            var rec2Pristine = File.ReadAllText(rec2);
            {
                ContractLoad Read()
                {
                    using var store = HistoryStore.Open(careerPath, tinyFp);
                    return ReadLiveContracts(store, 3);
                }
                Check("C10a: the current version loads whole",
                      Read().Status == ContractLoadStatus.Loaded && Read().Contracts.Count == 1);
                File.WriteAllText(rec2, rec2Pristine.Replace("\"formatVersion\": 2", "\"formatVersion\": 1"));
                var v1 = Read();
                Check("C10b: ★ a v1 record is a PRE-CONTRACT career and reads as EMPTY, never as " +
                      "unknown — no debt is ever manufactured from the past",
                      v1.Status == ContractLoadStatus.PreContractFormat && v1.Contracts.Count == 0);
                File.WriteAllText(rec2, rec2Pristine.Replace("\"formatVersion\": 2", "\"formatVersion\": 99"));
                var future = Read();
                Check("C10c: an unknown future version is a collection-level loss, per the existing policy",
                      future.Status == ContractLoadStatus.CollectionLost);
                File.WriteAllText(rec2, "{ this is not json");
                var damaged = Read();
                Check("C8b/C10d: ★ a damaged record reports a COLLECTION-LEVEL loss and names no " +
                      "pairing it could not read",
                      damaged.Status == ContractLoadStatus.CollectionLost
                      && damaged.Contracts.Count == 0
                      && damaged.Diagnostic is not null
                      && !damaged.Diagnostic.Contains(schoolA.Name) && !damaged.Diagnostic.Contains(schoolB.Name),
                      damaged.Diagnostic ?? "no diagnostic");
                File.WriteAllText(rec2, rec2Pristine);
                File.Move(rec2, rec2 + ".away");
                Check("C10e: a missing record is its own quiet status — nothing was lost, there is " +
                      "simply no file",
                      Read().Status == ContractLoadStatus.NoRecord);
                File.Move(rec2 + ".away", rec2);
            }

            // C4 — inheritance with season 1's record deleted: the read is EXACTLY N-1.
            File.Delete(rec1);
            SeasonRunOutcome season3;
            using (var store = HistoryStore.Open(careerPath, tinyFp))
                season3 = RunSeasonCore(tiny, MteCheckSeed, configPath, verbose: false, store);
            Check("C4: ★ season 3 inherits the exact surviving state from season 2's record with " +
                  "season 1's record GONE — the boundary is the real reader/writer pair and the " +
                  "read never opens an earlier season",
                  season3.Contracts.Load.Status == ContractLoadStatus.Loaded
                  && season3.Contracts.Load.Contracts.Single().GamesRemaining == 2
                  && season3.Contracts.Load.Contracts.Single().WindowRemaining == 5
                  && season3.Contracts.Load.Contracts.Single().Legs
                         .Where(l => !l.Completed).All(l => l.HostId == schoolA.Id)
                  && season3.Contracts.PolicyDeclined.SequenceEqual(new[] { 7 })
                  && season3.Contracts.Survivors.Single().WindowRemaining == 4);

            // ════════════════════════════════════════════════════════════════════
            //  C5 — MALFORMED AUTHORING, nine refusals by name.
            // ════════════════════════════════════════════════════════════════════
            {
                var good = C(1, 11, 22, 11, 4, Leg(1, 1, 11), Leg(2, 2, 22));
                var cases = new (string Name, Action Act, string Fragment)[]
                {
                    ("duplicate ContractId",
                     () => ValidateContractCollection(new[] { good, C(1, 33, 44, 33, 4, Leg(1, 1, 33)) }),
                     "duplicates another contract's id"),
                    ("duplicate leg id within a contract",
                     () => ValidateContractCollection(new[] { C(2, 11, 22, 11, 4, Leg(1, 1, 11), Leg(1, 2, 22)) }),
                     "duplicates leg id"),
                    ("executor not one of the two schools",
                     () => ValidateContractCollection(new[] { C(3, 11, 22, 33, 4, Leg(1, 1, 11)) }),
                     "executor"),
                    ("host not one of the two schools",
                     () => ValidateContractCollection(new[] { C(4, 11, 22, 11, 4, Leg(1, 1, 33)) }),
                     "hosted by neither school"),
                    ("the same school on both sides",
                     () => ValidateContractCollection(new[] { C(5, 11, 11, 11, 4, Leg(1, 1, 11)) }),
                     "same school on both sides"),
                    ("no legs",
                     () => ValidateContractCollection(new[] { C(6, 11, 22, 11, 4) }),
                     "no legs"),
                    ("zero window",
                     () => ValidateContractCollection(new[] { C(7, 11, 22, 11, 0, Leg(1, 1, 11)) }),
                     "zero or negative window"),
                    ("negative window",
                     () => ValidateContractCollection(new[] { C(8, 11, 22, 11, -2, Leg(1, 1, 11)) }),
                     "zero or negative window"),
                    ("outstanding legs exceeding the window",
                     () => ValidateContractCollection(new[] { C(9, 11, 22, 11, 1, Leg(1, 1, 11), Leg(2, 2, 22)) }),
                     "outstanding legs"),
                };
                var failed = new List<string>();
                foreach (var (name, act, fragment) in cases)
                {
                    var m = Refusal(act);
                    if (m is null || !m.Contains(fragment)) failed.Add(name);
                }
                Check("C5a: ★ nine malformed authorings, each refused by name — cheap, and they " +
                      "protect the expensive stored shape",
                      failed.Count == 0, failed.Count == 0 ? "9 refusals" : string.Join("; ", failed));

                string ParseError(string legJson)
                {
                    var json = "[ { \"contractId\": 1, \"schoolAId\": 11, \"schoolBId\": 22, " +
                               "\"executorId\": 11, \"windowRemaining\": 4, \"legs\": [ " + legJson + " ] } ]";
                    using var doc = JsonDocument.Parse(json);
                    var arr = doc.RootElement;
                    return Refusal(() => ParseLiveContracts(arr)) ?? "";
                }
                Check("C5b: an unknown status word, an unknown site word, a Home leg with no host and " +
                      "a Neutral leg WITH one are all parse refusals — words, never guesses",
                      ParseError("{ \"legId\": 1, \"order\": 1, \"site\": \"Home\", \"hostId\": 11, \"status\": \"Pending\" }")
                          .Contains("unknown status")
                      && ParseError("{ \"legId\": 1, \"order\": 1, \"site\": \"SemiHome\", \"hostId\": 11, \"status\": \"Outstanding\" }")
                          .Contains("unknown site")
                      && ParseError("{ \"legId\": 1, \"order\": 1, \"site\": \"Home\", \"status\": \"Outstanding\" }")
                          .Contains("must name its host")
                      && ParseError("{ \"legId\": 1, \"order\": 1, \"site\": \"Neutral\", \"hostId\": 11, \"status\": \"Outstanding\" }")
                          .Contains("must carry no host"));

                var completedOnDisk = C(10, 11, 22, 11, 3, Leg(1, 1, 11, done: true));
                var loadedMsg = Refusal(() => ValidateContractCollection(new[] { completedOnDisk }, loaded: true));
                Check("C5c: a fully-completed contract ON DISK is damage — rollover omits them, so " +
                      "one in a record can only be a bug",
                      loadedMsg is not null && loadedMsg.Contains("fully completed")
                      && Refusal(() => ValidateContractCollection(new[] { completedOnDisk })) is null);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — THE NEUTRAL FIXTURE AND THE EQUAL-HOST FIXTURE.
            // ════════════════════════════════════════════════════════════════════
            {
                var series = C(1, 11, 22, 22, 6,
                               Leg(1, 1, null, neutral: true), Leg(2, 2, null, neutral: true));
                var o = Step(Loaded(series), wants: _ => true);
                var g = o.Exercised.Single();
                Check("C6a: ★ a neutral series exercises with an explicit executor and NO HOST AT " +
                      "ALL — the site is the word, never a blank host — and both schools are " +
                      "charged a neutral opening",
                      g.IsNeutral && g.HostId is null
                      && o.Charges[11] == new ContractChargeSet(0, 0, 1)
                      && o.Charges[22] == new ContractChargeSet(0, 0, 1));

                var mixed = C(2, 11, 22, 11, 9,
                              Leg(1, 1, 22), Leg(2, 2, null, neutral: true), Leg(3, 3, 11));
                var pick1 = Step(Loaded(mixed), wants: _ => true).Exercised.Single();
                var noHome = C(3, 11, 22, 11, 9, Leg(1, 1, 22), Leg(2, 2, null, neutral: true));
                var pick2 = Step(Loaded(noHome), wants: _ => true).Exercised.Single();
                Check("C6b: ★ neutral sits BETWEEN home and away in the placeholder order — the " +
                      "executor takes its home leg over a neutral, and a neutral over the trip",
                      pick1.LegId == 3 && pick2.LegId == 2);

                var equalHost = C(4, 11, 22, 22, 5, Leg(1, 1, 11), Leg(2, 2, 22));
                using var stream = new MemoryStream();
                using (var w = new Utf8JsonWriter(stream))
                {
                    w.WriteStartObject();
                    WriteLiveContracts(w, new[] { equalHost });
                    w.WriteEndObject();
                }
                using var round = JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));
                var back = ParseLiveContracts(round.RootElement.GetProperty("liveContracts")).Single();
                Check("C6c: ★ the equal-host contract round-trips through the real serializer with " +
                      "the executor UNCHANGED — a home-and-home forces immediately, which proves " +
                      "forcing and nothing about this field",
                      back.ExecutorId == 22 && back.ContractId == 4
                      && back.Legs.Select(l => (l.LegId, l.Order, l.IsNeutral, l.HostId, l.Completed))
                             .SequenceEqual(equalHost.Legs.Select(l => (l.LegId, l.Order, l.IsNeutral, l.HostId, l.Completed))));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — MULTI-CONTRACT CAPACITY.
            // ════════════════════════════════════════════════════════════════════
            {
                var withY = C(1, 11, 22, 11, 1, Leg(1, 1, 11));
                var withZ = C(2, 11, 33, 11, 1, Leg(1, 1, 33));
                var both = Step(Loaded(withY, withZ), open: _ => 2);
                Check("C7a: two forced contracts on one school both place when capacity holds them",
                      both.Exercised.Count == 2 && !both.ForcedCapacityFailure);

                var exact = Step(Loaded(withY, withZ), open: id => id == 11 ? 2 : 9);
                Check("C7b: capacity reached EXACTLY commits — the bound is open games, not open " +
                      "games minus a margin",
                      exact.Exercised.Count == 2 && !exact.ForcedCapacityFailure);

                var overload = Step(Loaded(withY, withZ), open: id => id == 11 ? 1 : 9);
                Check("C7c: ★ forced obligations exceeding capacity is ONE hard world-state " +
                      "failure — nothing committed, and the collection rides forward FROZEN, " +
                      "un-decremented, so a broken world cannot also corrupt the record",
                      overload.ForcedCapacityFailure && overload.Exercised.Count == 0
                      && overload.Charges.Count == 0
                      && overload.Survivors.Count == 2
                      && overload.Survivors.All(c => c.WindowRemaining == 1)
                      && overload.ForcedCapacityDetail is not null
                      && overload.ForcedCapacityDetail.Contains("11"),
                      overload.ForcedCapacityDetail ?? "no detail");

                var forcedC = C(1, 11, 22, 11, 1, Leg(1, 1, 11));
                var optionC = C(2, 11, 33, 11, 3, Leg(1, 1, 33));
                var crowd = Step(Loaded(optionC, forcedC), wants: _ => true, open: id => id == 11 ? 1 : 9);
                Check("C7d: ★ an optional can never crowd out a forced leg — forced reserves first, " +
                      "the option is capacity-blocked, stays live, and still rolls its window",
                      crowd.Exercised.Single().ContractId == 1
                      && crowd.CapacityBlocked.SequenceEqual(new[] { 2 })
                      && crowd.Survivors.Single().ContractId == 2
                      && crowd.Survivors.Single() is { GamesRemaining: 1, WindowRemaining: 2 });

                var optA = C(5, 11, 22, 11, 3, Leg(1, 1, 11));
                var optB = C(10, 11, 33, 11, 3, Leg(1, 1, 11));
                var fwd = Step(Loaded(optA, optB), wants: _ => true, open: id => id == 11 ? 1 : 9);
                var rev = Step(Loaded(optB, optA), wants: _ => true, open: id => id == 11 ? 1 : 9);
                Check("C7e: ★ two optionals wanting one last opening resolve CANONICALLY — ascending " +
                      "ContractId wins — and enumeration order of the source collection does not " +
                      "change the result",
                      fwd.Exercised.Single().ContractId == 5 && rev.Exercised.Single().ContractId == 5
                      && fwd.CapacityBlocked.SequenceEqual(new[] { 10 })
                      && rev.CapacityBlocked.SequenceEqual(new[] { 10 }));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — THE CONFERENCE WALL.
            // ════════════════════════════════════════════════════════════════════
            {
                var wall = C(1, 11, 22, 11, 2, Leg(1, 1, 11), Leg(2, 2, 22));   // equality-forced
                var o = Step(Loaded(wall), league: (_, _) => true);
                Check("C8a: ★ conference mates terminate BEFORE exercise — an equality-forced " +
                      "contract is never exercised through the wall; remaining legs are void, " +
                      "reported once, and the contract is not written forward",
                      o.Terminated.Single() is { ContractId: 1, LegsLost: 2 }
                      && o.Exercised.Count == 0 && o.Survivors.Count == 0);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C11 — THE CHARGING CHAINS, as arithmetic.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C11a: a hosted leg comes out of home; an away leg out of road; a neutral leg " +
                      "out of neutral — a contract year keeps the shape of a normal year",
                      ApplyContractCharges(10, 2, 1, new ContractChargeSet(1, 0, 0)) == (9, 2, 1)
                      && ApplyContractCharges(7, 1, 5, new ContractChargeSet(0, 1, 0)) == (7, 1, 4)
                      && ApplyContractCharges(7, 1, 2, new ContractChargeSet(0, 0, 1)) == (7, 0, 2));
                Check("C11b: ★ THE RULED FALLBACK — the road-less power school pays a HOME date for " +
                      "its away leg: that date was spent traveling instead of hosting",
                      ApplyContractCharges(10, 1, 0, new ContractChargeSet(0, 1, 0)) == (9, 1, 0));
                Check("C11c: the chain tails — hosted falls to road when home is empty; neutral falls " +
                      "to road, then home",
                      ApplyContractCharges(0, 1, 3, new ContractChargeSet(1, 0, 0)) == (0, 1, 2)
                      && ApplyContractCharges(4, 0, 3, new ContractChargeSet(0, 0, 1)) == (4, 0, 2)
                      && ApplyContractCharges(5, 0, 0, new ContractChargeSet(0, 0, 1)) == (4, 0, 0));
                Check("C11d: a charge with every bucket empty is an INVARIANT VIOLATION, not a case — " +
                      "the capacity gate should have refused the world first",
                      Refusal(() => ApplyContractCharges(0, 0, 0, new ContractChargeSet(1, 0, 0))) is { } m
                      && m.Contains("nothing to charge"));
            }

            Console.WriteLine(pass ? "  Phase 94 PASS" : "  Phase 94 FAIL");
            return pass;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] Phase 94 threw: {ex.Message}");
            return false;
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        }
    }
}
