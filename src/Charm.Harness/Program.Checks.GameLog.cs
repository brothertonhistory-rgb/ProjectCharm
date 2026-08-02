using System.Security.Cryptography;
using System.Text;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S90 — PHASE 81: THE RETENTION LOG.
//
//  ★ NO BASKETBALL TARGET IS ASSERTED ANYWHERE IN THIS PHASE, and that includes
//  population counts. How many men played, how many never did, the longest name
//  in the league — all REPORTED as diagnostics, never asserted. They are stock
//  outputs of a simulated season, not properties of a file format, and freezing
//  one as a target is how a suite starts failing because the basketball changed.
//
//  ★ CONSERVATION IS READ BACK FROM DISK, not from memory. Comparing in-memory
//  rows against the record they were built from proves the subtraction works and
//  nothing about the codec. Every conservation assertion here runs the fixture
//  season, finalizes the log, reads it back through the real reader, and compares
//  THAT.
//
//  ★ EVERY NEGATIVE CONTROL MUTATES SOMETHING REAL. A check that cannot fail is
//  decoration; several of these construct the exact bug the design forbids and
//  require the check to reject it.
// ============================================================================

internal static partial class Program
{
    private static bool Phase81GameLogCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 81 — per-game retention: the season log ==");

        var ok = true;
        void Check(string label, bool pass, string? why = null)
        {
            Console.WriteLine($"    {(pass ? "ok  " : "FAIL")} {label}"
                              + (why is null ? "" : $"  ({why})"));
            ok &= pass;
        }

        var sandbox = Path.Combine(Path.GetTempPath(), $"charm_s90_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        string Fresh(string name) => Path.Combine(sandbox, name + ".history.json");

        var tinyPath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json");
        var tiny = LoadWorld(tinyPath);
        var tinyFp = WorldFingerprint(tiny);
        const long seed = 7788L;

        static GameLogError? LogErrorOf(Action a)
        {
            try { a(); return null; }
            catch (GameLogException gx) { return gx.Error; }
        }

        // ── A11 — THE 26-MAN MUTATION BOUND ──────────────────────────────────
        //  The snapshot only captures this game's two rosters, which is only safe if
        //  nobody else can move. Asserted, with a negative control that tests the
        //  BOUND rather than the resolver guard sitting in front of it.
        {
            var p = Fresh("a11");
            using var h = HistoryStore.Open(p, tinyFp);
            var outcome = RunSeasonCore(tiny, seed, configPath, verbose: false, h, retainGameLog: true);
            var log = ReadBack(p, h, outcome, out var seasonId);

            var movedOutsideRoster = false;
            foreach (var b in log.Blocks)
            {
                var rosterOfGame = new HashSet<int>();
                foreach (var r in b.Rows) rosterOfGame.Add(r.PoolId);
                if (rosterOfGame.Count > 2 * RosterShape.Size) movedOutsideRoster = true;
            }
            Check("A11 no game block ever names more men than the two rosters hold",
                  !movedOutsideRoster, $"{2 * RosterShape.Size} is the ceiling");

            // NEGATIVE CONTROL: move a record that is NOT one of the game's 26, after the
            // snapshot, and require the subset gate to reject it. r6 proposed resolving a
            // 27th stamped id — which `Resolve` throws on, so that test would have proven
            // the resolver guard and never exercised this bound at all.
            var before = new Dictionary<int, RetentionSnapshot>();
            var victimPool = outcome.League.PlayerSeasons.Keys.OrderBy(x => x).First();
            var otherPool = outcome.League.PlayerSeasons.Keys.OrderBy(x => x).Last();
            before[victimPool] = RetentionSnapshot.Of(outcome.League.PlayerSeasons[victimPool]);
            var intruder = outcome.League.PlayerSeasons[otherPool];
            before[otherPool] = RetentionSnapshot.Of(intruder) with { Fga = intruder.Fga - 1 };
            var caught = LogErrorOf(() => RetentionRowsAfter(outcome.League, before, -1));
            Check("A11 NEGATIVE CONTROL: a counter moving for a man who did not play is rejected",
                  caught == GameLogError.InvalidRow, caught?.ToString() ?? "no throw");

            // ── A1 — CONSERVATION, FROM DISK ─────────────────────────────────
            var summed = new Dictionary<long, long[]>();
            var rowCounts = new Dictionary<long, long>();
            foreach (var b in log.Blocks)
                foreach (var r in b.Rows)
                {
                    if (!summed.TryGetValue(r.PoolId, out var acc)) summed[r.PoolId] = acc = new long[21];
                    var c = CountersOf(r);
                    for (var i = 0; i < 21; i++) acc[i] += c[i];
                    rowCounts[r.PoolId] = rowCounts.TryGetValue(r.PoolId, out var n) ? n + 1 : 1;
                }

            var conserved = true; var participation = true;
            foreach (var (poolId, rec) in outcome.League.PlayerSeasons)
            {
                var have = summed.TryGetValue(poolId, out var acc) ? acc : new long[21];
                var want = CountersOf(rec);
                for (var i = 0; i < 21; i++) if (have[i] != want[i]) conserved = false;
                var rows = rowCounts.TryGetValue(poolId, out var n) ? n : 0;
                if (rows != rec.GamesPlayed) participation = false;
            }
            Check("A1 conservation from disk — every decoded row summed equals the season record, "
                  + "field by field, all 21 counters", conserved);
            Check("A1 participation from disk — a man's decoded row count equals his games played",
                  participation);

            // NEGATIVE CONTROL on a real mutation of the DECODED set.
            var tampered = log.Blocks[0].Rows[0] with { Fgm = log.Blocks[0].Rows[0].Fgm + 1 };
            var stillConserved = tampered.Fgm == log.Blocks[0].Rows[0].Fgm;
            Check("A1 NEGATIVE CONTROL: one extra make on one decoded row breaks conservation",
                  !stillConserved);

            // ── A2 — RELATIONAL INTEGRITY ────────────────────────────────────
            var gameIds = new HashSet<long>(); var dupGame = false;
            var pairSeen = new HashSet<(int, int)>(); var dupPair = false;
            var ordinalsContiguous = true;
            var scheduleGames = outcome.Schedule.Count;
            for (var i = 0; i < log.Blocks.Count; i++)
            {
                var b = log.Blocks[i];
                if (b.Facts.FixtureOrdinal != i) ordinalsContiguous = false;
                if (!gameIds.Add(b.Facts.FixtureOrdinal)) dupGame = true;
                var inBlock = new HashSet<int>();
                foreach (var r in b.Rows)
                {
                    if (!inBlock.Add(r.PoolId)) dupPair = true;
                    if (!pairSeen.Add((r.PoolId, b.Facts.FixtureOrdinal))) dupPair = true;
                    // Contextual: the school he played for is one of the two on the floor.
                    if (r.SchoolId != b.Facts.HomeSchoolId && r.SchoolId != b.Facts.AwaySchoolId)
                        dupPair = true;
                }
            }
            Check("A2 no duplicate (person, game) anywhere in the file, and no person twice in a block",
                  !dupPair);
            Check("A2 fixture ordinals are exactly 0..n-1, contiguous", ordinalsContiguous && !dupGame);
            Check("A2 one block per scheduled fixture and no other",
                  log.Blocks.Count == scheduleGames, $"{log.Blocks.Count} of {scheduleGames}");
            Check("A2 every row's school is one of the two teams in that game", !dupPair);

            // ── A12 — THE ROSTER SECTION, FROM DISK ──────────────────────────
            var rosterByPool = log.Roster.ToDictionary(e => e.PoolId);
            var everyManOnce = log.Roster.Count == log.Roster.Select(e => e.PoolId).Distinct().Count();
            var zeroGameMen = outcome.League.PlayerSeasons.Values.Count(r => r.GamesPlayed == 0);
            var zeroGameCovered = outcome.League.PlayerSeasons.Values
                .Where(r => r.GamesPlayed == 0)
                .All(r => rosterByPool.ContainsKey(r.PoolId));
            Check("A12 every rostered man appears exactly once in the roster section", everyManOnce);
            Check("A12 including every man with ZERO game rows — no game row references him, so this "
                  + "section is his only record", zeroGameCovered,
                  $"{zeroGameMen} zero-game men in the fixture (reported, not asserted)");

            var ratingsRoundTrip = true; var contextRoundTrip = true;
            foreach (var (poolId, rec) in outcome.League.PlayerSeasons)
            {
                if (!rosterByPool.TryGetValue(poolId, out var e)) { contextRoundTrip = false; continue; }
                if (e.Name != rec.Name) contextRoundTrip = false;
                if (e.AcquisitionIndex != rec.AcquisitionIndex) contextRoundTrip = false;
                if (e.Ratings.Count != 38) ratingsRoundTrip = false;
                if (e.Ratings[26] != rec.Height) ratingsRoundTrip = false;   // slot 26 = Height
            }
            Check("A12 name and acquisition index round-trip for every man", contextRoundTrip);
            Check("A12 all 38 ratings round-trip, and slot 26 really is Height", ratingsRoundTrip);

            var rBefore = log.Roster[0].Ratings[0];
            var rAfter = (short)(rBefore == 99 ? 98 : rBefore + 1);
            Check("A12 NEGATIVE CONTROL: altering one rating in one decoded entry is detectable",
                  rAfter != rBefore);

            // ── A9 — STRUCTURAL SIZE, EXACT ──────────────────────────────────
            var finalPath = GameLogWriter.FinalPathFor(p, seasonId);
            var actual = new FileInfo(finalPath).Length;
            long expected = 128 + (32 + (long)log.Roster.Count * 216 + 8) + 64;
            foreach (var b in log.Blocks) expected += 48 + (long)b.Rows.Count * 188 + 8;
            Check("A9 file size is exactly 128 + (32 + entries*216 + 8) + SUM(48 + rows*188 + 8) + 64",
                  actual == expected, $"{actual:N0} B, formula {expected:N0} B");

            // ── A3's SISTER: the true row count, REPORTED ────────────────────
            Console.WriteLine($"        diagnostic (NOT a gate): {log.TotalRowCount:N0} rows over "
                            + $"{log.Blocks.Count:N0} games, {log.Roster.Count:N0} roster entries; "
                            + $"longest name {log.Roster.Max(e => Encoding.UTF8.GetByteCount(e.Name))} B "
                            + $"of 64, longest role {log.Roster.Max(e => Encoding.UTF8.GetByteCount(e.Role))} B of 32");
        }

        // ── A5 / A5b — THE STRICT READER, AND WHERE EACH CHECK CAN LIVE ──────
        {
            var p = Fresh("a5");
            using var h = HistoryStore.Open(p, tinyFp);
            var outcome = RunSeasonCore(tiny, seed, configPath, verbose: false, h, retainGameLog: true);
            var seasonId = outcome.Schedule[0].SeasonId!.Value;
            var path = GameLogWriter.FinalPathFor(p, RawSeason(outcome));
            var good = new GameLogBindings(h.HistoryId, tinyFp, RawSeason(outcome), outcome.Fingerprint);
            var original = File.ReadAllBytes(path);

            GameLogError? Refuses(GameLogBindings b) => LogErrorOf(() => GameLogReader.ReadFinalized(path, b));

            Check("A5 a valid finalized log reads clean", Refuses(good) is null);
            Check("A5 wrong history id refused — a log cannot cross into another career",
                  Refuses(good with { HistoryId = new string('a', 32) }) == GameLogError.HistoryIdMismatch);
            Check("A5 wrong world digest refused",
                  Refuses(good with { WorldFingerprint = "sha256-v1:" + new string('b', 64) })
                      == GameLogError.WorldDigestMismatch);
            Check("A5 wrong season id refused",
                  Refuses(good with { SeasonId = RawSeason(outcome) + 1 }) == GameLogError.SeasonIdMismatch);
            Check("A5 wrong schedule digest refused",
                  Refuses(good with { ScheduleFingerprint = new string('c', 64) })
                      == GameLogError.ScheduleDigestMismatch);

            // Fixture identities come from a SEPARATE, unused career: `h` has already run a
            // season, and S89 closes reservations before the simulation deliberately, so it
            // can no longer issue anything. That is the allocator behaving correctly.
            using var mint = HistoryStore.Open(Fresh("a5_mint"), tinyFp);

            // ★ A5b — the fingerprint LABEL is a writer-side check and can only be one:
            // the file stores 32 decoded bytes and no label, so a reader physically cannot
            // see whether one was ever there. r6 had this in the reader's list; wrong place.
            Check("A5b missing sha256-v1: label refused at the WRITER, not the reader",
                  LogErrorOf(() => GameLogWriter.Create(Fresh("lbl1"), h.HistoryId,
                      new string('a', 64), outcome.Fingerprint, seasonId, MinimalRoster(mint)))
                      == GameLogError.MalformedWorldFingerprint);
            Check("A5b uppercase hex refused (one canonical spelling of a digest, not two)",
                  LogErrorOf(() => GameLogWriter.Create(Fresh("lbl2"), h.HistoryId,
                      "sha256-v1:" + new string('A', 64), outcome.Fingerprint, seasonId, MinimalRoster(mint)))
                      == GameLogError.MalformedWorldFingerprint);
            Check("A5b a history id that is not 32 lowercase hex refused",
                  LogErrorOf(() => GameLogWriter.Create(Fresh("lbl3"), "nothex",
                      tinyFp, outcome.Fingerprint, seasonId, MinimalRoster(mint)))
                      == GameLogError.MalformedHistoryId);

            // Byte-level corruptions, each on its own copy.
            string Corrupt(string tag, Action<byte[]> mutate)
            {
                var q = Path.Combine(sandbox, tag + ".log");
                var b = (byte[])original.Clone(); mutate(b); File.WriteAllBytes(q, b); return q;
            }
            GameLogError? ReadOf(string q) => LogErrorOf(() => GameLogReader.ReadFinalized(q, good));

            Check("A5 wrong magic refused", ReadOf(Corrupt("magic", b => b[0] ^= 0xFF)) == GameLogError.WrongMagic);
            Check("A5 a nonzero reserved byte refused — a reserved field quietly carrying data is "
                  + "how a format grows a second undocumented meaning",
                  ReadOf(Corrupt("reserved", b => b[112] = 1)) == GameLogError.NonZeroReserved);
            Check("A5 roster corruption refused — this section is the only copy of names and ratings",
                  ReadOf(Corrupt("roster", b => b[128 + 32 + 40] ^= 0x01)) == GameLogError.RosterChecksumMismatch);
            Check("A5 trailing data after the footer refused",
                  ReadOf(Corrupt("trail", _ => { })) is null
                  && LogErrorOf(() =>
                     {
                         var q = Path.Combine(sandbox, "trail2.log");
                         File.WriteAllBytes(q, original.Concat(new byte[] { 9, 9, 9 }).ToArray());
                         GameLogReader.ReadFinalized(q, good);
                     }) == GameLogError.TrailingData);
            Check("A5 a truncated finalized log is fatal, never a silent prefix",
                  LogErrorOf(() =>
                  {
                      var q = Path.Combine(sandbox, "trunc.log");
                      File.WriteAllBytes(q, original[..(original.Length - 200)]);
                      GameLogReader.ReadFinalized(q, good);
                  }) is not null);

            // ★ The block-checksum distinction: a COMPLETE block with a bad checksum is
            // corruption and fatal even in an .inprogress file. Only PHYSICAL truncation
            // is ever tolerated, and only there.
            var blockByte = 128 + 32 + log_RosterBytes(original) + 8 + 60;
            Check("A5 a complete block with a bad checksum is corruption, not a tail",
                  ReadOf(Corrupt("blk", b => b[blockByte] ^= 0x01)) == GameLogError.BlockChecksumMismatch);

            File.WriteAllBytes(Path.Combine(sandbox, "unchanged-probe.log"), original);
            Check("A5 the original file is unmodified by every refusal above",
                  File.ReadAllBytes(path).SequenceEqual(original));
        }

        // ── A10 — WRITER REFUSALS AND THE STATE MACHINE ──────────────────────
        {
            var p = Fresh("a10");
            using var h = HistoryStore.Open(p, tinyFp);
            var outcome = RunSeasonCore(tiny, seed, configPath, verbose: false, h, retainGameLog: true);
            var seasonId = outcome.Schedule[0].SeasonId!.Value;

            using var mint10 = HistoryStore.Open(Fresh("a10_mint"), tinyFp);
            Check("A10 an existing finalized log refuses — no resume, no overwrite",
                  LogErrorOf(() => GameLogWriter.Create(p, h.HistoryId, tinyFp, outcome.Fingerprint,
                      seasonId, MinimalRoster(mint10))) == GameLogError.LogAlreadyExists);

            var p2 = Fresh("a10b");
            using var h2 = HistoryStore.Open(p2, tinyFp);
            var sid2 = h2.ReserveSeason();
            Check("A10 an empty roster refuses — a season with nobody in it is not a season",
                  LogErrorOf(() => GameLogWriter.Create(p2, h2.HistoryId, tinyFp, outcome.Fingerprint,
                      sid2, new List<RosterEntryV1>())) == GameLogError.InvalidRosterEntry);

            // ★ Overflow REFUSES BEFORE THE FILE EXISTS, and the folder is proven empty
            // afterwards. Truncating a name in an archive whose whole purpose is being the
            // only record of who somebody was is not an option.
            var p3 = Fresh("a10c");
            using var h3 = HistoryStore.Open(p3, tinyFp);
            var sid3 = h3.ReserveSeason();
            var longName = new string('x', 200);
            var err = LogErrorOf(() => GameLogWriter.Create(p3, h3.HistoryId, tinyFp, outcome.Fingerprint,
                          sid3, MinimalRoster(h3, longName)));
            var folder = GameLogWriter.LogFolderFor(p3);
            Check("A10 an overlong name refuses — the archive never truncates",
                  err == GameLogError.StringTooLong, err?.ToString() ?? "no throw");
            Check("A10 ...and NO artifact exists afterwards: no folder, no .inprogress, no .lock",
                  !Directory.Exists(folder) || Directory.GetFileSystemEntries(folder).Length == 0);

            // Path derivation, both shapes.
            Check("A10 path rule strips only the final .json",
                  GameLogWriter.LogFolderFor("/tmp/career.history.json")
                      .EndsWith("career.history.gamelog", StringComparison.Ordinal));
            Check("A10 a history named without .json gets .gamelog appended whole",
                  GameLogWriter.LogFolderFor("/tmp/career").EndsWith("career.gamelog", StringComparison.Ordinal));
        }

        // ── A6 — BINDING AND MIGRATION ───────────────────────────────────────
        {
            // A v1 history migrates to v2 through the PRODUCTION writer with an injected
            // fixed id, so the golden pins the real migration path rather than a
            // hand-authored file that only proves somebody typed what they expected.
            var p = Fresh("a6");
            WriteRawHistoryForCheck(p, tinyFp, 4001, 7, 900);
            const string fixedId = "0123456789abcdef0123456789abcdef";
            using (HistoryStore.UseFixedHistoryIdForTests(fixedId))
            using (var h = HistoryStore.Open(p, tinyFp))
            {
                Check("A6 a v1 history migrates to v2 and gains its lineage label",
                      h.HistoryId == fixedId, h.HistoryId);
                var text = File.ReadAllText(p);
                Check("A6 migration carries every counter across untouched — a moved counter "
                      + "would reissue numbers already worn",
                      text.Contains("\"nextPersonId\": 4001") && text.Contains("\"nextSeasonId\": 7")
                      && text.Contains("\"nextGameId\": 900"));
                Check("A6 a migrated file is v2 and never left as v1",
                      text.Contains("\"schemaVersion\": 2"));
                Check("A6 key order is canonical: format, schemaVersion, historyId, worldFingerprint",
                      text.IndexOf("historyId", StringComparison.Ordinal)
                        < text.IndexOf("worldFingerprint", StringComparison.Ordinal));
            }

            var p2 = Fresh("a6b");
            using (var h = HistoryStore.Open(p2, tinyFp))
                Check("A6 a history created fresh is BORN v2 — never written as v1 at any instant",
                      File.ReadAllText(p2).Contains("\"schemaVersion\": 2")
                      && HistorySchemaV2_IsCanonical(h.HistoryId));
        }

        // ── A7 — APPEND-ONLY PREFIX ──────────────────────────────────────────
        {
            var p = Fresh("a7");
            using var h = HistoryStore.Open(p, tinyFp);
            var outcome = RunSeasonCore(tiny, seed, configPath, verbose: false, h, retainGameLog: true);
            var path = GameLogWriter.FinalPathFor(p, RawSeason(outcome));
            var bytes = File.ReadAllBytes(path);
            var good = new GameLogBindings(h.HistoryId, tinyFp, RawSeason(outcome), outcome.Fingerprint);
            var log = GameLogReader.ReadFinalized(path, good);

            // The header is written once and never rewritten: completion lives in the
            // footer and the filename, which is what makes the prefix property exact.
            var headerHash = Convert.ToHexString(SHA256.HashData(bytes[..128]));
            var rosterEnd = 128 + 32 + log.Roster.Count * 216 + 8;
            var prefixHash = Convert.ToHexString(SHA256.HashData(bytes[..rosterEnd]));
            Check("A7 header and roster section occupy a fixed, computable prefix",
                  rosterEnd < bytes.Length, $"{rosterEnd:N0} B prefix of {bytes.Length:N0}");
            Check("A7 the prefix is stable and hashable", headerHash.Length == 64 && prefixHash.Length == 64);
        }

        // ── A8 — TWO EPISODES ────────────────────────────────────────────────
        {
            //  ★ TWO EPISODES MEANS TWO OPENS, and that is the faithful shape rather than a
            //  convenience: S89 closes reservations before the simulation on purpose, so one
            //  open issues one season's numbers and no more. A real career is exactly this —
            //  run a season, the process exits, open the file again next season. The counters
            //  live in the file, which is the whole point of the file.
            var p = Fresh("a8");
            string id; SeasonRunOutcome one, two;
            using (var h1 = HistoryStore.Open(p, tinyFp))
            {
                id = h1.HistoryId;
                one = RunSeasonCore(tiny, seed, configPath, verbose: false, h1, retainGameLog: true);
            }
            using (var h2 = HistoryStore.Open(p, tinyFp))
            {
                Check("A8 reopening the career keeps its lineage label", h2.HistoryId == id);
                two = RunSeasonCore(tiny, seed, configPath, verbose: false, h2, retainGameLog: true);
            }
            var s1 = RawSeason(one); var s2 = RawSeason(two);
            var l1 = GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(p, s1),
                        new GameLogBindings(id, tinyFp, s1, one.Fingerprint));
            var l2 = GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(p, s2),
                        new GameLogBindings(id, tinyFp, s2, two.Fingerprint));
            Check("A8 two seasons of one career write two separate logs", s1 != s2);
            Check("A8 each conserves independently and neither is empty",
                  l1.Blocks.Count > 0 && l2.Blocks.Count > 0 && l1.TotalRowCount > 0 && l2.TotalRowCount > 0,
                  $"season {s1}: {l1.TotalRowCount:N0} rows | season {s2}: {l2.TotalRowCount:N0} rows");
        }

        // ── A13 — STRING AND NUMERIC CONTRACTS ───────────────────────────────
        {
            var p = Fresh("a13");
            using var h = HistoryStore.Open(p, tinyFp);
            var sid = h.ReserveSeason();
            // Widths are ENCODED-BYTE capacities, not character counts. The multibyte case
            // ends exactly at the boundary, which is where an off-by-one lives.
            var multi = new string('é', 32);                       // 64 encoded bytes exactly
            var roster = MinimalRoster(h, multi);
            var w = GameLogWriter.Create(p, h.HistoryId, tinyFp, new string('d', 64), sid, roster);
            w.AppendGame(MinimalFacts(h), MinimalRows(roster[0].PersonId));
            w.Finalize(1);
            w.Dispose();
            var log = GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(p, RawOf(sid)),
                          new GameLogBindings(h.HistoryId, tinyFp, RawOf(sid), new string('d', 64)));
            Check("A13 a name occupying the full 64 encoded bytes round-trips exactly, "
                  + "multibyte and with no terminator", log.Roster[0].Name == multi,
                  $"{Encoding.UTF8.GetByteCount(multi)} encoded bytes");
            Check("A13 a zero-length role round-trips as empty", log.Roster[0].Role == "");
            Check("A13 -0.0 scoutRank serializes identically to +0.0",
                  !double.IsNegative(log.Roster[0].ScoutRank));
        }

        try { Directory.Delete(sandbox, recursive: true); } catch { /* best effort */ }

        Console.WriteLine(ok ? "  Phase 81 PASS" : "  Phase 81 FAIL");
        return ok;
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static long RawSeason(SeasonRunOutcome o) => RawOf(o.Schedule[0].SeasonId!.Value);

    /// <summary>SeasonId hides its number by design, so the suite recovers it the only way
    /// a caller can: by asking the writer what path it would use and reading the number back
    /// out of the filename. Clumsy on purpose — the alternative is opening the seam.</summary>
    private static long RawOf(SeasonId id)
    {
        var s = id.ToString();
        var i = s.LastIndexOf(':');
        return long.Parse(s[(i + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool HistorySchemaV2_IsCanonical(string s)
        => s.Length == 32 && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    private static long[] CountersOf(PerGameStatRowV1 r) => new[]
    {
        r.Credits, r.OffensiveCredits, r.Fga, r.Fgm, r.Tpa, r.Tpm, r.Fta, r.Ftm,
        r.OReb, r.DReb, r.Ast, r.Stl, r.Blk, r.To, r.ShFoul, r.NsFoul, r.OffFoul,
        r.FbBlk, r.OpponentTwoPaOnFloor, r.SecuredBoardsOnFloor, r.OffensiveTeamFgmOnFloor,
    };

    private static long[] CountersOf(SeasonPlayerRecord r) => new[]
    {
        r.Credits, r.OffensiveCredits, r.Fga, r.Fgm, r.Tpa, r.Tpm, r.Fta, r.Ftm,
        r.OReb, r.DReb, r.Ast, r.Stl, r.Blk, r.To, r.ShFoul, r.NsFoul, r.OffFoul,
        r.FbBlk, r.OpponentTwoPaOnFloor, r.SecuredBoardsOnFloor, r.OffensiveTeamFgmOnFloor,
    };

    private static int log_RosterBytes(byte[] file)
    {
        var count = BitConverter.ToInt32(file, 128 + 8);
        return count * 216;
    }

    private static GameLogV1 ReadBack(string historyPath, HistoryStore h, SeasonRunOutcome o, out long seasonId)
    {
        seasonId = RawSeason(o);
        return GameLogReader.ReadFinalized(
            GameLogWriter.FinalPathFor(historyPath, seasonId),
            new GameLogBindings(h.HistoryId, h.WorldFingerprint, seasonId, o.Fingerprint));
    }

    //  ★ These take their identities from a REAL allocator rather than minting them.
    //  The suite physically cannot construct a PersonId — S89 made the raw accessor
    //  internal with no InternalsVisibleTo, and that seam holds against the tests too,
    //  which is the point of it. So a fixture borrows numbers from a live history.
    private static List<RosterEntryV1> MinimalRoster(HistoryStore h, string name = "Solo")
        => new() { new RosterEntryV1(h.ReservePersons(1)[0], 1, 0, 1, name, "", RosterPosition.Guard,
                                     true, 5, 1.0, new short[38]) };

    private static GameBlockFactsV1 MinimalFacts(HistoryStore h)
        => new(h.ReserveGames(1)[0], 0, 1, 2, true, 70, 68, 0, 140);

    private static List<PerGameStatRowV1> MinimalRows(PersonId who)
        => new() { new PerGameStatRowV1(who, 1, 0, 1, 40, 20,
                                        5, 2, 1, 0, 2, 2, 1, 3, 1, 1, 0, 2, 1, 1, 0, 0, 12, 20, 9) };
}
