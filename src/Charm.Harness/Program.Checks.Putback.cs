using Charm.Engine;
using Charm.History;
using System.Globalization;

namespace Charm.Harness;

// ============================================================================
//  Phase 108 — S118: THE PUTBACK BELONGS TO THE MAN WHO TOOK IT (O-115).
//
//  Emmett's ruling (2026-10-08): the man who secures the offensive rebound and goes
//  straight back up gets the field-goal attempt, the make if it drops, and any free
//  throws that come from it — he shoots them at his own rating and the foul is drawn
//  against him. A reset is not touched.
//
//  What must be proven:
//    C1 the man (constructed, through the real Resolver.Route, over a FIXED seed range):
//       a putback with the missed shooter in slot 1 (FT 20) and the rebounder in slot 3
//       (FT 95), and a bonus-trip putback with no missed shooter at all, credit every
//       FGA, FGM, FTA and FTM to slot 3 and none to slot 1; the foul is drawn against
//       slot 3; his made rate is 0.95 ± 0.03 (he SHOOTS them, not only owns them). The
//       range refuses its own setup unless it holds a made putback, a missed one and
//       1,000 free throws per arm — it never searches. Negative control: an ordinary
//       (non-putback) shot with the same two slots still credits slot 1.
//    C2 ★ only the putback games moved: the fixed-pairing games (league slate and buy
//       games) with no changed-shooter fouled putback are the pre-S118 capture, byte for
//       byte; the games with one are counted. Control: the whole fixed set differs.
//       ★ S118.1: re-scoped to the games touched by NEITHER S118 nor S118.1's scramble trip.
//       ★ RETIRED AT S120: the free-throw lane moves almost every game, so the untouched set this
//       compared no longer discriminates. Its proof stands in the journal; the shape (C2a) and the
//       counts still print. Phase 111 C6 is the live only-these-possessions-moved check.
//    C3 every point has its man: no side of any stock game holds a point no man is
//       credited with; every side's men sum to its team's FGA and FTA; nothing
//       unattributed anywhere.
//    C4 the counts: printed, never asserted as targets.
//    C5 the fingerprint wall at the S120 capture (★ S118.1: re-pinned again, O-117; ★ S120: again, O-118); the three schedule fingerprints at
//       their OLD values; the career's season one is the legacy season game for game.
// ============================================================================

internal static partial class Program
{
    private const long PutbackCheckSeed = 20260720;   // the stock season every wall check reads

    // C1's seed range, chosen at the S118 gate and FIXED. Refuses its own setup if short.
    private const int PutbackC1FirstSeed = 0;
    private const int PutbackC1LastSeed  = 19_999;
    private const int PutbackC1ControlLastSeed = 1_999;   // the non-putback control's range
    private const int PutbackC1MinFta = 1_000;

    // C2 — the fixed-pairing games touched by NEITHER change (no moved putback trip, no S118.1
    // scramble trip), as the PRE-S118 tree played them (lines ordinal|home|away|homeScore|
    // awayScore|possessions, the pre-S112 convention).
    // ★ S118.1 — re-scoped (O-117): some of S118's 1,586 untouched games now hold a scramble trip,
    // so the set shrinks to games neither session touched; the digest was captured from the
    // pre-S118 tree over that set at the S118.1 draft and declared in its prompt. Was 1,586 /
    // 3,402 / 8092781386cc125d40e1f9e388c7dbcc26ca341cdb6599d527c18003c6866cb2.
    private const string PutbackGoldenUntouchedDigest =
        "3feac3724f844abd22076f60a6302b7dcc1750bde85125bc96ca8a46650de35a";
    private const int PutbackGoldenUntouchedGames = 655;
    private const int PutbackGoldenTouchedGames   = 4_333;
    private const int PutbackFixedPairingGames    = 4_988;

    // C4 — provenance constants: the S118 draft-time measurement on the unmodified tree (stock
    // seed 20260720), and its comparison against the throwaway of this exact design. Printed only.
    private const int PutbackPreS118Putbacks          = 35_868;
    private const int PutbackPreS118ToMissedShooter   = 29_137;
    private const int PutbackPreS118ToNobody          = 1_327;
    private const int PutbackPreS118UncreditedPoints  = 1_513;
    private const int PutbackPreS118WrongFtTrips      = 6_065;
    private const long PutbackPreS118SeasonPoints     = 730_430;
    private const int PutbackS118GamesMoved           = 1_553;
    private const int PutbackS118WinnersChanged       = 254;
    private const long PutbackS118SeasonPoints        = 729_863;   // ★ S118.1: S118's own season, printed beside the live one

    private sealed class AlwaysDefensiveBoard : IRollIPieGenerator, IRollMPieGenerator
    {
        public Pie<ReboundOutcome> Generate(PossessionState state, ReboundSource source) => Only(ReboundOutcome.DefensiveRebound);
        public Pie<FreeThrowReboundOutcome> Generate(PossessionState state) => Only(FreeThrowReboundOutcome.DefensiveRebound);
        private static Pie<T> Only<T>(T keep) where T : struct, Enum =>
            new(Enum.GetValues<T>().ToDictionary(o => o, o => EqualityComparer<T>.Default.Equals(o, keep) ? 1.0 : 0.0), 1e-9);
    }

    private static bool Phase108PutbackCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 108 — S118: the putback belongs to the man who took it. The rebounder gets the field goal " +
                          "and its free throws, only the games with a moved trip moved, and every point has its man ==");
        var pass = true;
        var assertions = 0;

        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static string Line(SeasonRunOutcome run, int i)
        {
            var x = run.Results[i];
            return $"{i}|{x.HomeId}|{x.AwayId}|{x.HomeScore}|{x.AwayScore}|{run.PossessionCounts[i]}\n";
        }
        static string GamesDigest(SeasonRunOutcome run) =>
            RatingSha(string.Concat(Enumerable.Range(0, run.Results.Count).Select(i => Line(run, i))));

        // -- C1: the man -------------------------------------------------------------------
        try
        {
            var cfgA = RollAConfig.Load(configPath);   var cfgB = RollBConfig.Load(configPath);
            var cfgC = RollCConfig.Load(configPath);   var cfgD = RollDConfig.Load(configPath);
            var cfgE = RollEConfig.Load(configPath);   var cfgF = RollFConfig.Load(configPath);
            var cfgG = RollGConfig.Load(configPath);   var cfgH = RollHConfig.Load(configPath);
            var cfgJ = RollJConfig.Load(configPath);   var cfgK = RollKConfig.Load(configPath);
            var cfgL = RollLConfig.Load(configPath);   var cfgOff = RollOffensiveFoulConfig.Load(configPath);
            var cfgAtt = AttentionConfig.Load(configPath);
            var cfgMatch = MatchupConfig.Load(configPath);
            var board = new AlwaysDefensiveBoard();

            static Player MkP(int id, int ft)
                => new Player($"p{id}")
                {
                    PlayerId = id, HierarchyRank = 5,
                    Outside = 50, Mid = 50, Close = 50, Finishing = 50, FreeThrow = ft,
                    FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                    SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                    OffensiveRebounding = 50, PerimeterDefense = 50, PostDefense = 50, RimProtection = 50,
                    DefensiveRebounding = 50, Steals = 50, Height = 50, Wingspan = 50, Weight = 50,
                    Strength = 50, Speed = 50, Quickness = 50, FirstStep = 50, Vertical = 50, Endurance = 50,
                    Hustle = 50, BasketballIQ = 50, Discipline = 50, HelpDefense = 50, OffBallDefense = 50,
                    RimTendency = 50, ShortTendency = 50, MidTendency = 50, LongTendency = 50, ThreeTendency = 50,
                };
            // Slot 1 is the missed shooter (FT 20), slot 3 the rebounder (FT 95); the rest 50.
            GameState BuildGame()
            {
                var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                var ft = new[] { 20, 50, 95, 50, 50 };
                for (var i = 0; i < 5; i++)
                {
                    g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), MkP(i + 1, ft[i]));
                    g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), MkP(i + 6, 50));
                }
                return g;
            }
            // Real generators everywhere except the two rebound rolls, which always give the board
            // to the defense — so the possession ends on THIS shot (a second offensive board would
            // hand the ball to a new man, and that putback, correctly, would be his).
            Resolver Build(GameState g, IRng rng) => new Resolver(
                new RollAGenerator(cfgA, cfgMatch, g), cfgA,
                new RollBGenerator(cfgB, cfgMatch, g),
                new RollCGenerator(cfgC), cfgC,
                new RollDGenerator(cfgD),
                new RollEGenerator(cfgE, g),
                new AttentionGenerator(cfgAtt, g),
                new RollFGenerator(cfgF, cfgMatch, g),
                new RollGGenerator(cfgG, cfgMatch, g),
                new RollHGenerator(cfgH, cfgMatch, g),
                board,
                new RollJGenerator(cfgJ, cfgMatch, g),
                new RollKGenerator(cfgK, cfgMatch, g),
                new RollLGenerator(cfgL, g),
                board,
                new RollOffensiveFoulGenerator(cfgOff),
                cfgMatch, g, rng);

            // One arm: route the constructed shot once per seed; keep the routes whose only shot
            // was this one and that drew no other foul (a block or a ball out of bounds can hand the
            // offense a NEW play, which is someone else's — those routes are counted and set aside).
            (long fga, long fgm, long[] fgaBy, long[] fgmBy, long fta, long[] ftaBy, long[] ftmBy,
             int made, int missed, int fouledRoutes, int counterRoutes, int shooterNot3, int kept, int setAside)
                Arm(int? selected, bool putback, int lastSeed)
            {
                long fga = 0, fgm = 0, fta = 0;
                var fgaBy = new long[6]; var fgmBy = new long[6]; var ftaBy = new long[6]; var ftmBy = new long[6];
                int made = 0, missed = 0, fouled = 0, counter = 0, not3 = 0, kept = 0, aside = 0;
                for (var seed = PutbackC1FirstSeed; seed <= lastSeed; seed++)
                {
                    var g = BuildGame();
                    var st = new PossessionState(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                                                 Entry: EntryType.DeadBallInbound)
                    {
                        SelectedSlot = selected is int s ? g.HomeLineup.SlotAt(s) : null,
                        ReboundSlot  = g.HomeLineup.SlotAt(3),
                        ShotType     = ShotLocation.Rim,
                        Frontcourt   = true,
                    };
                    var o = Build(g, new SystemRng(seed)).Route(
                        new Continue(ContinuationKind.IntoShotResolution, st) { Putback = putback });
                    if (o.ShotResolutions != 1 || o.NonShootingFouls.Count != 0 || (o.OffensiveFouls?.Count ?? 0) != 0)
                    { aside++; continue; }
                    kept++;
                    fga += o.Fga; fgm += o.Fgm; fta += o.Fta;
                    int[] sf = { 0, o.Slot1Fga, o.Slot2Fga, o.Slot3Fga, o.Slot4Fga, o.Slot5Fga };
                    int[] sm = { 0, o.Slot1Fgm, o.Slot2Fgm, o.Slot3Fgm, o.Slot4Fgm, o.Slot5Fgm };
                    fgaBy[0] += o.SlotUnattributedFga; fgmBy[0] += o.SlotUnattributedFgm;
                    for (var k = 1; k <= 5; k++) { fgaBy[k] += sf[k]; fgmBy[k] += sm[k]; }
                    for (var k = 0; k <= 5; k++) { ftaBy[k] += o.FtaBySlot[k]; ftmBy[k] += o.FtmBySlot[k]; }
                    if (o.Fgm > 0) made++; else if (o.Fga > 0) missed++;
                    if (o.ShootingFouls.Count > 0)
                    {
                        fouled++;
                        if (o.ShootingFouls.Any(e => e.ShooterSlot != 3)) not3++;
                    }
                    if (o.PutbackFtShooterChanged > 0) counter++;
                }
                return (fga, fgm, fgaBy, fgmBy, fta, ftaBy, ftmBy, made, missed, fouled, counter, not3, kept, aside);
            }

            foreach (var (label, selected) in new (string, int?)[] { ("missed shooter in slot 1", 1), ("bonus trip, no missed shooter", null) })
            {
                var a = Arm(selected, putback: true, PutbackC1LastSeed);
                var setupOk = a.made >= 1 && a.missed >= 1 && a.fta >= PutbackC1MinFta;
                Check(Inv($"C1-setup ({label}): seeds {PutbackC1FirstSeed}..{PutbackC1LastSeed:N0} hold a made putback, a missed one and ≥ {PutbackC1MinFta:N0} free throws — the range is fixed, never searched"),
                      setupOk, Inv($"{a.kept:N0} routes kept ({a.setAside:N0} set aside: the offense got a new play); {a.made:N0} made, {a.missed:N0} missed, {a.fta:N0} FTA"));
                if (!setupOk) continue;
                Check(Inv($"C1a ({label}): every putback FGA and FGM is the rebounder's (slot 3), none slot 1's, none unattributed"),
                      a.fgaBy[3] == a.fga && a.fgmBy[3] == a.fgm && a.fgaBy[1] == 0 && a.fgmBy[1] == 0 && a.fgaBy[0] == 0 && a.fgmBy[0] == 0,
                      Inv($"FGA {a.fga:N0}: slot 3 {a.fgaBy[3]:N0}, slot 1 {a.fgaBy[1]:N0}, nobody {a.fgaBy[0]:N0}; FGM {a.fgm:N0}: slot 3 {a.fgmBy[3]:N0}"));
                Check(Inv($"C1b ({label}): every free throw from a fouled putback is credited to the rebounder, and the foul is drawn against him"),
                      a.ftaBy[3] == a.fta && a.ftaBy[1] == 0 && a.ftaBy[0] == 0 && a.shooterNot3 == 0,
                      Inv($"FTA {a.fta:N0}: slot 3 {a.ftaBy[3]:N0}, slot 1 {a.ftaBy[1]:N0}, nobody {a.ftaBy[0]:N0}; {a.fouledRoutes:N0} fouled, {a.shooterNot3} drawn against another man"));
                var rate = a.ftaBy[3] > 0 ? (double)a.ftmBy[3] / a.ftaBy[3] : 0.0;
                Check(Inv($"C1c ({label}): ★ he SHOOTS them — made rate within ±0.03 of his 0.95 (slot 1 would make 0.20)"),
                      Math.Abs(rate - 0.95) <= 0.03, Inv($"{a.ftmBy[3]:N0} of {a.ftaBy[3]:N0} = {rate:F3}"));
                Check(Inv($"C1d ({label}): the page-only counter marks exactly the fouled routes (the shooter moved on every one)"),
                      a.counterRoutes == a.fouledRoutes && a.fouledRoutes > 0, Inv($"{a.counterRoutes:N0} of {a.fouledRoutes:N0}"));
            }

            var ctl = Arm(1, putback: false, PutbackC1ControlLastSeed);
            Check("C1e: NEGATIVE CONTROL — an ordinary shot (no putback) with the same two men is still slot 1's, field goal and free throws",
                  ctl.kept > 0 && ctl.fga > 0 && ctl.fgaBy[1] == ctl.fga && ctl.fgaBy[3] == 0
                  && ctl.ftaBy[1] == ctl.fta && ctl.ftaBy[3] == 0 && ctl.counterRoutes == 0,
                  Inv($"seeds 0..{PutbackC1ControlLastSeed:N0}: FGA {ctl.fga:N0} (slot 1 {ctl.fgaBy[1]:N0}, slot 3 {ctl.fgaBy[3]:N0}); FTA {ctl.fta:N0} (slot 1 {ctl.ftaBy[1]:N0})"));
        }
        catch (Exception ex)
        {
            Check("C1 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        // -- The stock season: one career (with its game log) and the legacy run -----------
        var scratch = Path.Combine(Path.GetTempPath(), "charm-s118-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            var stock = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "stock-d1.world.json"));
            var careerPath = Path.Combine(scratch, "stock", "career.json");
            SeasonRunOutcome career;
            using (var store = HistoryStore.Open(careerPath, WorldFingerprint(stock)))
                career = RunSeasonCore(stock, PutbackCheckSeed, configPath, verbose: false, store, retainGameLog: true);
            GameLogV1 log;
            using (var store = HistoryStore.Open(careerPath, WorldFingerprint(stock)))
                log = GameLogReader.ReadFinalized(GameLogWriter.FinalPathFor(careerPath, 1),
                    new GameLogBindings(store.HistoryId, store.WorldFingerprint, 1));
            var n = career.Results.Count;
            if (career.PutbackAudits.Count != n || career.PlayedGames.Count != n || career.PossessionCounts.Count != n)
                throw new InvalidOperationException(
                    $"the putback audit is not index for index with the games: {career.PutbackAudits.Count} audits, " +
                    $"{career.PlayedGames.Count} played, {n} results.");

            // -- C2: only the putback games moved -------------------------------------------
            {
                // Fixed pairing = decided before the first tip: the league slate (the first
                // ConferenceGameCount ordinals) and the buy games. Event brackets and conference
                // tournaments pair on results, so they follow the moved games and are excluded.
                var fixedSet = Enumerable.Range(0, n)
                    .Where(i => i < career.ConferenceGameCount || career.PlayedGames[i].IsBuyGame).ToList();
                var shapeOk = fixedSet.Count == PutbackFixedPairingGames
                              && fixedSet.All(i => career.PlayedGames[i].EventId is null
                                                   && career.PlayedGames[i].ConferenceTournamentId is null);
                Check(Inv($"C2a: the fixed-pairing games are the league slate plus the buy games, {PutbackFixedPairingGames:N0} of them, none an event or tournament game"),
                      shapeOk, Inv($"{fixedSet.Count:N0} ({career.ConferenceGameCount:N0} league + {fixedSet.Count - career.ConferenceGameCount:N0} buy)"));
                // ★ RETIRED AT S120 (see the header): printed, no longer asserted.
                var untouched = fixedSet.Count(i => career.PutbackAudits[i].ShooterChanged == 0
                                                    && career.PutbackAudits[i].ScrambleStamped == 0);
                Console.WriteLine(Inv($"  (page) C2b/C2c retired at S120 (proven at S118.1: {PutbackGoldenUntouchedGames:N0} untouched games byte-identical to the pre-S118 season). This season: {untouched:N0} of {fixedSet.Count:N0} fixed-pairing games hold neither a moved putback trip nor a scramble trip."));
            }

            // -- C3: every point has its man -----------------------------------------------
            {
                var wrongGame = 0; var pointGaps = 0; var fgaGaps = 0; var ftaGaps = 0; var unattributed = 0L;
                if (log.Blocks.Count != n) throw new InvalidOperationException($"the log holds {log.Blocks.Count} games, the season {n}.");
                foreach (var b in log.Blocks)
                {
                    var f = b.Facts; var i = f.FixtureOrdinal;
                    var r = career.Results[i]; var a = career.PutbackAudits[i];
                    if (r.HomeId != f.HomeSchoolId || r.AwayId != f.AwaySchoolId || r.HomeScore != f.HomeScore || r.AwayScore != f.AwayScore)
                    { wrongGame++; continue; }
                    foreach (var (school, final, teamFga, teamFta) in new[]
                             { (f.HomeSchoolId, f.HomeScore, a.HomeFga, a.HomeFta), (f.AwaySchoolId, f.AwayScore, a.AwayFga, a.AwayFta) })
                    {
                        var rows = b.Rows.Where(x => x.SchoolId == school).ToList();
                        if (rows.Sum(x => 2 * x.Fgm + x.Tpm + x.Ftm) != final) pointGaps++;
                        if (rows.Sum(x => x.Fga) != teamFga) fgaGaps++;
                        if (rows.Sum(x => x.Fta) != teamFta) ftaGaps++;
                    }
                    unattributed += a.HomeUnattributedFga + a.AwayUnattributedFga + a.HomeUnattributedFta + a.AwayUnattributedFta;
                }
                Check("C3a: the log's games are the season's games (teams and finals, by ordinal)", wrongGame == 0, Inv($"{wrongGame} mismatched"));
                Check("C3b: ★ every point has its man — on every side of every stock game the men's points are the final",
                      pointGaps == 0, Inv($"{n:N0} games, {pointGaps} sides short"));
                Check("C3c: every side's men sum to its team's FGA and FTA, and no attempt is unattributed anywhere",
                      fgaGaps == 0 && ftaGaps == 0 && unattributed == 0,
                      Inv($"{fgaGaps} FGA gaps, {ftaGaps} FTA gaps, {unattributed} unattributed attempts"));
            }

            // -- C4: the counts (printed, never targets) -------------------------------------
            {
                var points = career.Results.Sum(r => (long)r.HomeScore + r.AwayScore);
                var moved = career.PutbackAudits.Sum(a => (long)a.ShooterChanged);
                var gamesWith = career.PutbackAudits.Count(a => a.ShooterChanged > 0);
                Console.WriteLine("  (page) the putback, stock season (seed 20260720):");
                Console.WriteLine(Inv($"    putbacks: {PutbackPreS118Putbacks:N0} (S118 draft measurement, pre-S118 tree)"));
                Console.WriteLine(Inv($"    credited to someone other than the man who took it — before: {PutbackPreS118ToMissedShooter + PutbackPreS118ToNobody:N0} ({PutbackPreS118ToMissedShooter:N0} to the missed shooter, {PutbackPreS118ToNobody:N0} to nobody); after: 0 (C1 proves the man, C3 that nothing is unattributed)"));
                Console.WriteLine(Inv($"    uncredited points — before: {PutbackPreS118UncreditedPoints:N0}; after: 0 (C3b)"));
                Console.WriteLine(Inv($"    fouled-putback free-throw trips shot by another man — before: {PutbackPreS118WrongFtTrips:N0}; trips S118 moved this season: {moved:N0}, in {gamesWith:N0} games"));
                Console.WriteLine(Inv($"    season points: {PutbackPreS118SeasonPoints:N0} → {PutbackS118SeasonPoints:N0} at S118 ({PutbackS118SeasonPoints - PutbackPreS118SeasonPoints:+#,0;-#,0;0}); live {points:N0} (S118.1 moved scramble trips too — Phase 109)"));
                Console.WriteLine(Inv($"    games moved {PutbackS118GamesMoved:N0}, winners changed {PutbackS118WinnersChanged:N0} (S118 draft comparison; C2 is the live proof)"));
            }

            // -- C5: the fingerprint wall, re-pinned at the S120 capture ----------------------
            {
                var legacy = RunSeasonCore(stock, PutbackCheckSeed, configPath, verbose: false);
                var prefix = legacy.ConferenceGameCount + legacy.TournamentGameCount;
                var resultsFp = SeasonFingerprint(legacy.Results.Take(prefix).ToList(), legacy.PossessionCounts.Take(prefix).ToList());
                Check("C5a: #1 conference schedule UNMOVED (its pre-S118 value)", legacy.Fingerprint == MatchGoldenConferenceFp);
                Check("C5b: #2 conference dated UNMOVED (its pre-S118 value)", legacy.DatedFingerprint == MatchGoldenDatedFp);
                Check("C5c: #5 non-conference dated UNMOVED (its pre-S118 value)", legacy.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C5d: #3 event games at the S120 capture", legacy.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C5e: #4 results+possessions at the S120 capture over its league-plus-event prefix", resultsFp == MatchGoldenResultsFp);
                Check("C5f: #6 conference tournaments at the S120 capture", legacy.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);
                Check("C5g: #7 buy games at the S120 capture", legacy.BuyGamesFingerprint == RatingGoldenBuyGamesFp);
                Check("C5h: ★ every legacy game at the S120 capture", GamesDigest(legacy) == RatingGoldenPreS112GameDigest,
                      Inv($"{legacy.Results.Count:N0} games, {GamesDigest(legacy)[..16]}…"));
                Check("C5i: the career's season one and the legacy season are the same games", GamesDigest(career) == GamesDigest(legacy));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 108 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 108 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }
}
