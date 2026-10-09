using Charm.Engine;
using Charm.History;
using System.Globalization;
using System.Reflection;

namespace Charm.Harness;

// ============================================================================
//  Phase 109 — S118.1: THE SCRAMBLE FOUL GOES TO THE MAN IN THE SCRAMBLE (O-117).
//
//  Emmett's rulings (2026-10-08), in the bonus only:
//    1. a foul after the offense has secured the board (Roll K's DefensiveFoul) is shot by
//       the man who grabbed it;
//    2. a loose-ball foul (Roll I's LooseBallFoulOnDefense) goes to any of the five, drawn
//       the way the engine picks who grabs an offensive board — the missed jumper's or
//       three's shooter cut to about a third, a missed layup's shooter not cut ("keep it");
//    3. a loose ball off a missed free throw (Roll M's LooseBallFoulOnDefense): the same
//       draw, with the man who just missed at the stripe taking the cut.
//  Below the bonus nothing changes.
//
//  What must be proven:
//    C1 the man (constructed, through the real Resolver.Route, over a FIXED seed range; the
//       check refuses its own setup below 5,000 trips per arm and never searches). The five
//       offensive men are identical in every input to the rebound draw and differ only in
//       FreeThrow (20/40/60/80/95) — asserted before any share is tested. Every band is 4
//       standard errors computed from the arm's own counts. Negative controls: below the bonus
//       nothing is named; a reach-in bonus foul is never named in the scramble.
//    C2 ★ only the scramble games moved: the fixed-pairing games with no stamped trip are the
//       S118 season byte for byte; the games with one are counted. Control: the whole set differs.
//    C3 the counts: printed, never asserted as targets.
//    C4 the fingerprint wall at the S118.1 capture; the three schedule fingerprints at their
//       OLD values; the career's season one is the legacy season game for game.
// ============================================================================

internal static partial class Program
{
    private const long ScrambleCheckSeed = 20260720;   // the stock season every wall check reads

    // C1's seed range, chosen at the S118.1 gate and FIXED. Refuses its own setup if short.
    private const int ScrambleC1FirstSeed        = 0;
    private const int ScrambleC1LastSeed         = 9_999;
    private const int ScrambleC1ControlLastSeed  = 1_999;
    private const int ScrambleC1MinTrips         = 5_000;
    private static readonly int[] ScrambleC1FreeThrow = { 20, 40, 60, 80, 95 };   // slots 1..5

    // C2 — the fixed-pairing games with no stamped scramble trip, as the S118 tree played them
    // (lines ordinal|home|away|homeScore|awayScore|possessions). Captured at the S118.1 draft on
    // the S118 tree; declared in the prompt before the build.
    private const string ScrambleGoldenUntouchedDigest =
        "1b1d6e320fcbb2d3b86e74787e8945e7ad875562ff41a76cbdb64db055b3336e";
    private const int ScrambleGoldenUntouchedGames = 2_049;
    private const int ScrambleGoldenTouchedGames   = 2_939;
    private const int ScrambleFixedPairingGames    = 4_988;

    // C3 — provenance constants: the S118.1 draft measurement, the S118 tree against a throwaway
    // of this exact design (stock seed 20260720). Printed only, never asserted.
    private const int  ScrambleDraftLooseBallFg   = 2_542;
    private const int  ScrambleDraftAfterBoard    = 1_931;
    private const int  ScrambleDraftLooseBallFt   = 538;
    private const int  ScrambleDraftChangedFg     = 2_206;
    private const int  ScrambleDraftChangedBoard  = 1_707;
    private const int  ScrambleDraftChangedFt     = 298;
    private const int  ScrambleDraftFtWasPicker   = 207;
    private const double ScrambleDraftRatingBefore = 69.83;
    private const double ScrambleDraftRatingAfter  = 68.53;
    private const int  ScrambleDraftGamesWith     = 3_161;
    private const int  ScrambleDraftGamesMoved    = 2_433;
    private const int  ScrambleDraftWinnersChanged = 469;
    private const long ScrambleS118SeasonPoints   = 729_863;
    private const long ScrambleDraftSeasonPoints  = 730_487;

    /// <summary>The three scramble rolls, forced: each returns its named outcome on its FIRST call
    /// in a route and the plain end afterwards (a defensive board, or an offensive foul for Roll K),
    /// so every route holds at most one forced foul — a missed last free throw would otherwise send
    /// Roll M straight back into another one. One instance per route.</summary>
    private sealed class ScrambleStub : IRollIPieGenerator, IRollKPieGenerator, IRollMPieGenerator
    {
        private readonly ReboundOutcome?          _iFirst;
        private readonly OffensiveReboundOutcome? _kFirst;
        private readonly FreeThrowReboundOutcome? _mFirst;
        private int _i, _k, _m;

        public ScrambleStub(ReboundOutcome? iFirst = null, OffensiveReboundOutcome? kFirst = null,
                            FreeThrowReboundOutcome? mFirst = null)
        { _iFirst = iFirst; _kFirst = kFirst; _mFirst = mFirst; }

        public Pie<ReboundOutcome> Generate(PossessionState state, ReboundSource source) =>
            Only(_i++ == 0 && _iFirst is { } f ? f : ReboundOutcome.DefensiveRebound);
        public Pie<OffensiveReboundOutcome> Generate(PossessionState state, OffensiveReboundSource source) =>
            Only(_k++ == 0 && _kFirst is { } f ? f : OffensiveReboundOutcome.OffensiveFoul);
        public Pie<FreeThrowReboundOutcome> Generate(PossessionState state) =>
            Only(_m++ == 0 && _mFirst is { } f ? f : FreeThrowReboundOutcome.DefensiveRebound);

        private static Pie<T> Only<T>(T keep) where T : struct, Enum =>
            new(Enum.GetValues<T>().ToDictionary(o => o, o => EqualityComparer<T>.Default.Equals(o, keep) ? 1.0 : 0.0), 1e-9);
    }

    private sealed record ScrambleRouteFacts(int Stamped, int Shooter, int BonusFta, int BonusFtm, int OrbSlot, int NonShootingFouls);

    private static bool Phase109ScrambleCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 109 — S118.1: the scramble foul goes to the man in the scramble. The rebounder shoots " +
                          "a foul after the board, a rebound-weighted draw names the loose-ball man, and only the games " +
                          "with a stamped trip moved ==");
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
            var cfgJ = RollJConfig.Load(configPath);   var cfgL = RollLConfig.Load(configPath);
            var cfgOff = RollOffensiveFoulConfig.Load(configPath);
            var cfgAtt = AttentionConfig.Load(configPath);
            var cfgMatch = MatchupConfig.Load(configPath);
            var nerf = cfgMatch.ReboundShooterNerf;

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
            GameState BuildGame(bool inBonus)
            {
                var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
                for (var i = 0; i < 5; i++)
                {
                    g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), MkP(i + 1, ScrambleC1FreeThrow[i]));
                    g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), MkP(i + 6, 50));
                }
                // The defense (Away) pre-loaded into the DOUBLE bonus: every trip is two shots.
                if (inBonus)
                    for (var f = 0; f < cfgD.DoubleBonusThreshold; f++) g.Fouls.Increment(TeamSide.Away);
                return g;
            }
            Resolver Build(GameState g, ScrambleStub stub, IRng rng) => new Resolver(
                new RollAGenerator(cfgA, cfgMatch, g), cfgA,
                new RollBGenerator(cfgB, cfgMatch, g),
                new RollCGenerator(cfgC), cfgC,
                new RollDGenerator(cfgD),
                new RollEGenerator(cfgE, g),
                new AttentionGenerator(cfgAtt, g),
                new RollFGenerator(cfgF, cfgMatch, g),
                new RollGGenerator(cfgG, cfgMatch, g),
                new RollHGenerator(cfgH, cfgMatch, g),
                stub,
                new RollJGenerator(cfgJ, cfgMatch, g),
                stub,
                new RollLGenerator(cfgL, g),
                stub,
                new RollOffensiveFoulGenerator(cfgOff),
                cfgMatch, g, rng);

            // ── C1-0: the fixture is what the expectation assumes — the five men identical in
            //    every input except FreeThrow. Every public property compared, not a hand-picked list.
            {
                var men = Enumerable.Range(0, 5).Select(i => MkP(i + 1, ScrambleC1FreeThrow[i])).ToArray();
                var skip = new HashSet<string> { nameof(Player.Name), nameof(Player.PlayerId), nameof(Player.FreeThrow) };
                static string Show(object? v) => v switch
                {
                    null => "null",
                    IReadOnlyDictionary<string, int> d => string.Join(",", d.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")),
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => v.ToString() ?? "",
                };
                var props = typeof(Player).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !skip.Contains(p.Name)).ToList();
                var differing = props.Where(p => men.Select(m => Show(p.GetValue(m))).Distinct().Count() > 1)
                                     .Select(p => p.Name).ToList();
                var ftOk = men.Select(m => m.FreeThrow).SequenceEqual(ScrambleC1FreeThrow);
                Check(Inv($"C1-0: the five offensive men are identical in all {props.Count} other properties and differ only in FreeThrow (20/40/60/80/95)"),
                      differing.Count == 0 && ftOk && props.Count > 30,
                      differing.Count == 0 ? Inv($"{props.Count} properties compared") : "differ: " + string.Join(", ", differing));
            }

            // One route's bonus-trip facts, read off the possession outcome.
            //   shooter: the one slot holding the bonus trip's attempts (0 when the attempts were
            //   spread or unattributed — a failure); bonusFta / bonusFtm: that trip's attempts and makes
            //   (bonusFtm is -1 when it cannot be separated from another trip by the same man).
            ScrambleRouteFacts Facts(RoutingOutcome o, int priorSlot, int priorFta)
            {
                var fta = new int[6]; var ftm = new int[6];
                for (var k = 0; k <= 5; k++) { fta[k] = o.FtaBySlot[k]; ftm[k] = o.FtmBySlot[k]; }
                if (priorSlot > 0) fta[priorSlot] -= priorFta;   // the earlier shooting trip (M arm)
                var holders = Enumerable.Range(0, 6).Where(k => fta[k] != 0).ToList();
                var shooter = holders.Count == 1 && holders[0] != 0 ? holders[0] : 0;
                var bonusFta = fta.Sum();
                var bonusFtm = shooter == 0 ? -1 : (shooter == priorSlot ? -1 : ftm[shooter]);
                var orbs = Enumerable.Range(1, 5).Where(k => o.OrbBySlot[k] > 0).ToList();
                return new ScrambleRouteFacts(o.ScrambleFtShooterStamped, shooter, bonusFta, bonusFtm,
                                      orbs.Count == 1 ? orbs[0] : 0, o.NonShootingFouls.Count);
            }

            List<ScrambleRouteFacts> RunArm(Func<GameState, Continue> start, Func<ScrambleStub> stub, bool inBonus,
                                    int lastSeed, int priorSlot = 0, int priorFta = 0)
            {
                var list = new List<ScrambleRouteFacts>(lastSeed + 1);
                for (var seed = ScrambleC1FirstSeed; seed <= lastSeed; seed++)
                {
                    var g = BuildGame(inBonus);
                    var o = Build(g, stub(), new SystemRng(seed)).Route(start(g));
                    list.Add(Facts(o, priorSlot, priorFta));
                }
                return list;
            }

            PossessionState St(GameState g, int? selected, ShotLocation? zone) =>
                new(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away, Entry: EntryType.DeadBallInbound)
                {
                    SelectedSlot = selected is int s ? g.HomeLineup.SlotAt(s) : null,
                    ShotType     = zone,
                    Result       = zone is null ? null : ShotResult.Miss,
                    Frontcourt   = true,
                };

            // Shares and the made rate, every band 4 SE from the arm's own counts.
            void Shares(string arm, List<ScrambleRouteFacts> trips, double[] w)
            {
                var n = trips.Count; var tot = w.Sum();
                for (var k = 1; k <= 5; k++)
                {
                    var p = w[k - 1] / tot; var obs = (double)trips.Count(t => t.Shooter == k) / n;
                    var se = Math.Sqrt(p * (1 - p) / n);
                    Check(Inv($"{arm}: slot {k}'s share of the trips at {p:F3} ± 4 SE"),
                          Math.Abs(obs - p) <= 4 * se, Inv($"{obs:F4} (SE {se:F4}, {n:N0} trips)"));
                }
            }
            void MadeRate(string arm, IEnumerable<ScrambleRouteFacts> trips)
            {
                double att = 0, made = 0, exp = 0, v = 0;
                foreach (var t in trips.Where(t => t.BonusFtm >= 0))
                {
                    var p = ScrambleC1FreeThrow[t.Shooter - 1] / 100.0;
                    att += t.BonusFta; made += t.BonusFtm; exp += t.BonusFta * p; v += t.BonusFta * p * (1 - p);
                }
                var sd = Math.Sqrt(v);
                Check(Inv($"{arm}: ★ he SHOOTS them — the made count equals the attempt-weighted sum of the named men's ratings ± 4 SE"),
                      att > 0 && Math.Abs(made - exp) <= 4 * sd,
                      Inv($"{made:N0} made of {att:N0}, expected {exp:N1} (SD {sd:F1}) — {made / Math.Max(1, att):F4} vs {exp / Math.Max(1, att):F4}"));
            }
            bool Setup(string arm, List<ScrambleRouteFacts> all, List<ScrambleRouteFacts> trips, int expectedStampedRoutes)
            {
                var ok = trips.Count >= ScrambleC1MinTrips && all.Count(r => r.Stamped == 1) == expectedStampedRoutes
                         && all.All(r => r.Stamped <= 1);
                Check(Inv($"{arm} setup: seeds {ScrambleC1FirstSeed}..{ScrambleC1LastSeed:N0} hold ≥ {ScrambleC1MinTrips:N0} stamped trips, at most one per route — the range is fixed, never searched"),
                      ok, Inv($"{trips.Count:N0} trips over {all.Count:N0} routes"));
                return ok;
            }
            // Every attempt of the trip is the one named man's, two shots each (the double bonus).
            void Credited(string arm, List<ScrambleRouteFacts> trips) =>
                Check(Inv($"{arm}: every bonus attempt is credited to the one named man — two shots, one slot, nothing unattributed"),
                      trips.All(t => t.Shooter > 0 && t.BonusFta == 2),
                      Inv($"{trips.Count(t => t.Shooter == 0)} trips spread or unattributed, {trips.Count(t => t.BonusFta != 2)} not two shots"));

            var even = new double[] { 1, 1, 1, 1, 1 };

            // ── K: a foul after the board — the rebounder shoots it.
            {
                var all = RunArm(g => new Continue(ContinuationKind.ResolveRebound, St(g, 1, ShotLocation.Three)),
                                 () => new ScrambleStub(iFirst: ReboundOutcome.OffensiveRebound,
                                                        kFirst: OffensiveReboundOutcome.DefensiveFoul),
                                 inBonus: true, ScrambleC1LastSeed);
                var trips = all.Where(r => r.Stamped == 1).ToList();
                if (Setup("C1-K", all, trips, all.Count))
                {
                    Credited("C1-K", trips);
                    Check("C1-K: ★ every trip is shot by the man the rebound draw named — the board and the free throws, same slot, every route",
                          trips.All(t => t.OrbSlot > 0 && t.OrbSlot == t.Shooter),
                          Inv($"{trips.Count(t => t.OrbSlot != t.Shooter):N0} of {trips.Count:N0} differ"));
                    MadeRate("C1-K", trips);
                }
            }

            // ── I: a loose ball off a missed three by slot 1 — slot 1 cut to the nerf, the other four even.
            {
                var all = RunArm(g => new Continue(ContinuationKind.ResolveRebound, St(g, 1, ShotLocation.Three)),
                                 () => new ScrambleStub(iFirst: ReboundOutcome.LooseBallFoulOnDefense),
                                 inBonus: true, ScrambleC1LastSeed);
                var trips = all.Where(r => r.Stamped == 1).ToList();
                if (Setup("C1-I three", all, trips, all.Count))
                {
                    Credited("C1-I three", trips);
                    Shares("C1-I three", trips, new[] { nerf, 1, 1, 1, 1 });
                    var share = Enumerable.Range(1, 5).Select(k => trips.Count(t => t.Shooter == k)).ToArray();
                    Check("C1-I three: the man who missed the three is strictly the least likely to be fouled",
                          share.Skip(1).All(c => share[0] < c), string.Join(" / ", share.Select(c => c.ToString("N0"))));
                    MadeRate("C1-I three", trips);
                }
            }

            // ── I control: a missed LAYUP by slot 1 — not cut ("keep it"); all five even.
            {
                var all = RunArm(g => new Continue(ContinuationKind.ResolveRebound, St(g, 1, ShotLocation.Rim)),
                                 () => new ScrambleStub(iFirst: ReboundOutcome.LooseBallFoulOnDefense),
                                 inBonus: true, ScrambleC1LastSeed);
                var trips = all.Where(r => r.Stamped == 1).ToList();
                if (Setup("C1-I layup", all, trips, all.Count))
                {
                    Credited("C1-I layup", trips);
                    Shares("C1-I layup (control: the layup's shooter is not cut)", trips, even);
                }
            }

            // ── M: slot 2 goes to the line after slot 1's missed three was fouled (a real shooting
            //    trip of three, shot by slot 2 through the S118 stamp), misses the last, and the loose
            //    ball is fouled. Slot 2 — the man at the stripe — is cut; slot 1 — the plain rebound
            //    draw's man after a missed three — is NOT. Tells the two rules apart in both directions.
            {
                var all = RunArm(g =>
                    {
                        var st = St(g, 1, ShotLocation.Three) with
                        {
                            Result = ShotResult.MissFouled,
                            FreeThrowShooterSlot = g.HomeLineup.SlotAt(2),
                        };
                        return new Continue(ContinuationKind.ResolveShootingFreeThrows, st);
                    },
                    () => new ScrambleStub(mFirst: FreeThrowReboundOutcome.LooseBallFoulOnDefense),
                    inBonus: true, ScrambleC1LastSeed, priorSlot: 2, priorFta: 3);
                var trips = all.Where(r => r.Stamped == 1).ToList();
                // A stamped trip exists exactly when slot 2 missed his third — no route is set aside.
                if (Setup("C1-M", all, trips, trips.Count))
                {
                    Credited("C1-M", trips);
                    Shares("C1-M", trips, new[] { 1, nerf, 1, 1, 1 });
                    MadeRate("C1-M (trips not shot by slot 2, whose makes cannot be split from his own trip)", trips);
                }
            }

            // ── Negative controls.
            {
                var below = RunArm(g => new Continue(ContinuationKind.ResolveRebound, St(g, 1, ShotLocation.Three)),
                                   () => new ScrambleStub(iFirst: ReboundOutcome.LooseBallFoulOnDefense),
                                   inBonus: false, ScrambleC1ControlLastSeed);
                Check(Inv($"C1-ctl below: NEGATIVE CONTROL — below the bonus the loose-ball foul is charged and nobody is named (seeds 0..{ScrambleC1ControlLastSeed:N0})"),
                      below.All(r => r.Stamped == 0 && r.NonShootingFouls >= 1),
                      Inv($"{below.Count(r => r.Stamped != 0)} stamped, {below.Count(r => r.NonShootingFouls == 0)} with no foul charged"));

                foreach (var (label, sel) in new (string, int?)[] { ("after selection, slot 1", 1), ("before selection (the foul-draw picker)", null) })
                {
                    var reach = RunArm(g => new Continue(ContinuationKind.ResolveFoulType, St(g, sel, null)),
                                       () => new ScrambleStub(), inBonus: true, ScrambleC1ControlLastSeed);
                    var ok = reach.All(r => r.Stamped == 0 && r.Shooter > 0 && r.BonusFta == 2
                                            && (sel is not int s || r.Shooter == s));
                    Check(Inv($"C1-ctl reach-in ({label}): NEGATIVE CONTROL — a reach-in bonus foul is never named in the scramble; it goes to {(sel is null ? "the foul-draw picker's man" : "the selected man")} as before"),
                          ok, Inv($"{reach.Count(r => r.Stamped != 0)} stamped, {reach.Count(r => r.Shooter == 0)} unattributed, {(sel is int s2 ? reach.Count(r => r.Shooter != s2) : 0)} to another man"));
                }
            }
        }
        catch (Exception ex)
        {
            Check("C1 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        // -- The stock season: one career and the legacy run --------------------------------
        var scratch = Path.Combine(Path.GetTempPath(), "charm-s1181-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            var stock = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "stock-d1.world.json"));
            var careerPath = Path.Combine(scratch, "stock", "career.json");
            SeasonRunOutcome career;
            using (var store = HistoryStore.Open(careerPath, WorldFingerprint(stock)))
                career = RunSeasonCore(stock, ScrambleCheckSeed, configPath, verbose: false, store, retainGameLog: true);
            var n = career.Results.Count;
            if (career.PutbackAudits.Count != n || career.PlayedGames.Count != n || career.PossessionCounts.Count != n)
                throw new InvalidOperationException(
                    $"the audit is not index for index with the games: {career.PutbackAudits.Count} audits, " +
                    $"{career.PlayedGames.Count} played, {n} results.");

            // -- C2: only the scramble games moved -------------------------------------------
            {
                var fixedSet = Enumerable.Range(0, n)
                    .Where(i => i < career.ConferenceGameCount || career.PlayedGames[i].IsBuyGame).ToList();
                var shapeOk = fixedSet.Count == ScrambleFixedPairingGames
                              && fixedSet.All(i => career.PlayedGames[i].EventId is null
                                                   && career.PlayedGames[i].ConferenceTournamentId is null);
                Check(Inv($"C2a: the fixed-pairing games are the league slate plus the buy games, {ScrambleFixedPairingGames:N0} of them, none an event or tournament game"),
                      shapeOk, Inv($"{fixedSet.Count:N0} ({career.ConferenceGameCount:N0} league + {fixedSet.Count - career.ConferenceGameCount:N0} buy)"));
                var untouched = fixedSet.Where(i => career.PutbackAudits[i].ScrambleStamped == 0).ToList();
                var touched = fixedSet.Count - untouched.Count;
                var digest = RatingSha(string.Concat(untouched.Select(i => Line(career, i))));
                Check(Inv($"C2b: ★ the {ScrambleGoldenUntouchedGames:N0} fixed-pairing games with no scramble trip are the S118 season, byte for byte; {ScrambleGoldenTouchedGames:N0} have one"),
                      untouched.Count == ScrambleGoldenUntouchedGames && touched == ScrambleGoldenTouchedGames
                      && digest == ScrambleGoldenUntouchedDigest,
                      Inv($"{untouched.Count:N0} untouched, {touched:N0} with a scramble trip, {digest[..16]}…"));
                var whole = RatingSha(string.Concat(fixedSet.Select(i => Line(career, i))));
                Check("C2c: NEGATIVE CONTROL — the same digest over every fixed-pairing game differs (the touched games really moved, and the digest can see it)",
                      whole != ScrambleGoldenUntouchedDigest, whole[..16] + "…");
            }

            // -- C3: the counts (printed, never targets) -------------------------------------
            {
                var points = career.Results.Sum(r => (long)r.HomeScore + r.AwayScore);
                var stamped = career.PutbackAudits.Sum(a => (long)a.ScrambleStamped);
                var gamesWith = career.PutbackAudits.Count(a => a.ScrambleStamped > 0);
                var draftTrips = ScrambleDraftLooseBallFg + ScrambleDraftAfterBoard + ScrambleDraftLooseBallFt;
                Console.WriteLine("  (page) the scramble foul, stock season (seed 20260720):");
                Console.WriteLine(Inv($"    bonus trips named in the scramble this season: {stamped:N0} (draft: {draftTrips:N0} — {ScrambleDraftLooseBallFg:N0} loose balls off a missed shot, {ScrambleDraftAfterBoard:N0} after the board, {ScrambleDraftLooseBallFt:N0} loose balls off a missed free throw)"));
                Console.WriteLine(Inv($"    man at the line changed (draft): {ScrambleDraftChangedFg:N0} / {ScrambleDraftChangedBoard:N0} / {ScrambleDraftChangedFt:N0}; {ScrambleDraftFtWasPicker:N0} free-throw loose balls were the foul-draw picker's"));
                Console.WriteLine(Inv($"    average FreeThrow at the line (draft, trips that had a shooter before): {ScrambleDraftRatingBefore:F2} → {ScrambleDraftRatingAfter:F2}"));
                Console.WriteLine(Inv($"    games holding a scramble trip: {gamesWith:N0} (draft {ScrambleDraftGamesWith:N0}); games moved {ScrambleDraftGamesMoved:N0}, winners changed {ScrambleDraftWinnersChanged:N0} (draft comparison; C2 is the live proof)"));
                Console.WriteLine(Inv($"    season points: {ScrambleS118SeasonPoints:N0} at S118 → {points:N0} ({points - ScrambleS118SeasonPoints:+#,0;-#,0;0}; draft {ScrambleDraftSeasonPoints:N0})"));
            }

            // -- C4: the fingerprint wall, re-pinned at the S118.1 capture ----------------------
            {
                var legacy = RunSeasonCore(stock, ScrambleCheckSeed, configPath, verbose: false);
                var prefix = legacy.ConferenceGameCount + legacy.TournamentGameCount;
                var resultsFp = SeasonFingerprint(legacy.Results.Take(prefix).ToList(), legacy.PossessionCounts.Take(prefix).ToList());
                Check("C4a: #1 conference schedule UNMOVED (its pre-S118.1 value)", legacy.Fingerprint == MatchGoldenConferenceFp);
                Check("C4b: #2 conference dated UNMOVED (its pre-S118.1 value)", legacy.DatedFingerprint == MatchGoldenDatedFp);
                Check("C4c: #5 non-conference dated UNMOVED (its pre-S118.1 value)", legacy.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp);
                Check("C4d: #3 event games at the S118.1 capture", legacy.EventGamesFingerprint == MatchGoldenEventGamesFp);
                Check("C4e: #4 results+possessions at the S118.1 capture over its league-plus-event prefix", resultsFp == MatchGoldenResultsFp);
                Check("C4f: #6 conference tournaments at the S118.1 capture", legacy.ConferenceTournamentFingerprint == BuyGoldenConfTourneyFp);
                Check("C4g: #7 buy games at the S118.1 capture", legacy.BuyGamesFingerprint == RatingGoldenBuyGamesFp);
                Check("C4h: ★ every legacy game at the S118.1 capture", GamesDigest(legacy) == RatingGoldenPreS112GameDigest,
                      Inv($"{legacy.Results.Count:N0} games, {GamesDigest(legacy)[..16]}…"));
                Check("C4i: the career's season one and the legacy season are the same games", GamesDigest(career) == GamesDigest(legacy));
            }
        }
        catch (Exception ex)
        {
            Check("Phase 109 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        Console.WriteLine($"  Phase 109 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }
}
