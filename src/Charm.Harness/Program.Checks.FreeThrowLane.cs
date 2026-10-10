using Charm.Engine;
using System.Globalization;
using System.Text.Json;

namespace Charm.Harness;

// ============================================================================
//  Phase 111 — S120: THE FREE-THROW LANE (O-118).
//
//  Emmett's rulings (2026-10-09): six on the lane — four defenders, two offense — the shooter at
//  the line, one defender and two offense back. Lane spots go by size and rebounding; foul trouble
//  (two fouls in the first half, four with more than five minutes left; never in overtime) keeps a
//  man off. The board is the four against the two, in TOTALS, laid over a 20% default that stands
//  for a normal lane. The shooter gets 3% of his team's offensive boards, each offensive man back
//  0.5%, the defense's man back 2% of its defensive boards; the shooter draws and commits 0.5% of
//  the scramble fouls and the men back none. The ceiling stays shared with live rebounds.
//
//  What must be proven:
//    C1 oracle parity: the engine reproduces tools/ft_lane_golden.json (tools/ft_lane_oracle.py,
//       both on the frozen normal lane in config.json) — the eight archetype cases' lanes exactly,
//       their totals, splits and every draw's per-man shares within a ULP bound, the foul-trouble
//       orderings exactly. NEGATIVE CONTROL: the old ten-man split on the same cases fails the bound.
//    C2 the lane rules on constructed lanes: two and four, the shooter never on it, nobody back
//       outscores a lane man unless foul trouble put him there, ties by slot, the missing shooter
//       both ways.
//    C3 the rare shares, each against its own denominator: exact in the candidate lists, then
//       within 4 SE over 400,000 draws (the 0.5% cases expect 2,000), then over the stock season's
//       real trips.
//    C4 the lane never leaks into a live-ball roll over the stock season; NEGATIVE CONTROL: a lane
//       left on the state is counted.
//    C5 page-only: the stock season's free-throw boards.
//    C6 only possessions from a game's first missed last free throw on moved: in every fixed-pairing
//       game (the league slate and the buy games — an event or tournament game's pairing follows earlier
//       results, as Phase 109 found), every possession before it is byte-identical to the S119 engine
//       (frozen at the S120 build from the pre-change engine), with the possession holding it as the
//       control.
// ============================================================================

internal static partial class Program
{
    private const long FtLaneSeasonSeed = 20260720;

    // ── C1 — the ULP bound, set against the measured worst case (sandbox, S120: printed by C1). A
    //    real mis-wire (the old ten-man split) moves the board by tenths of a percent and more.
    private const long FtLaneMaxUlps = 64;

    // ── C6 — captured at the S120 build from the PRE-CHANGE engine (the S119 tree plus the clock
    //    and a read-only lane, its game digest equal to the S119 pin): for every fixed-pairing stock
    //    game, the possessions before its first missed last free throw, digested in order. Possession
    //    lines are integers and labels only (number|offense|end|points|half|FGA|FGM|FTA|FTM). The
    //    "plus one" digest also holds the possession with that first miss — the control.
    private const string FtLaneGoldenPrefixDigest =
        "d6258b687a32a6a1d9ec395f021742868f1ebc293c1ce4b54f81a27d5cd21ff1";
    private const string FtLaneGoldenPlusOneDigest =
        "2f66c02c9c30b3a8ba1d45ae854e6235fc047c7f8bb24923bd53d6b3b3ebb6e9";
    private const int FtLaneFixedPairingGames       = 4_988;
    private const int FtLaneGoldenPrefixPossessions = 132_831;
    private const int FtLaneGoldenGamesWithNone    = 7;

    // ── C5 — provenance, the same pre-change run (printed only, never asserted).
    private const int    FtLanePreS120Trips          = 40_793;
    private const double FtLanePreS120OffBoardRate   = 0.1904;
    private const int    FtLanePreS120TroubleTrips   = 18_128;

    private static bool Phase111FreeThrowLaneCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 111 — S120: the free-throw lane. Two offense and four defense on the lane, the board " +
                          "decided four against two in totals over a normal lane, the shooter and the men back rare, the " +
                          "scramble fouls on the lane ==");
        var pass = true;
        var assertions = 0;
        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static long Ulps(double a, double b)
        {
            if (a == b) return 0;
            if (double.IsNaN(a) || double.IsNaN(b)) return long.MaxValue;
            var ai = BitConverter.DoubleToInt64Bits(a);
            var bi = BitConverter.DoubleToInt64Bits(b);
            if ((ai < 0) != (bi < 0)) return long.MaxValue;
            return Math.Abs(ai - bi);
        }

        var cfgM = RollMConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);
        var match = MatchupConfig.Load(configPath);
        var gen = (GameState g) => new RollMGenerator(cfgM, match, g);

        Player Man(int id, IReadOnlyDictionary<string, int> r)
        {
            int A(string k) => r.TryGetValue(k, out var v) ? v : 50;
            return new Player($"p{id}")
            {
                PlayerId = id, HierarchyRank = 5,
                Outside = 50, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 70,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                OffensiveRebounding = A("OffensiveRebounding"), PerimeterDefense = 50, PostDefense = A("PostDefense"),
                RimProtection = 50, DefensiveRebounding = A("DefensiveRebounding"), Steals = 50,
                Height = A("Height"), Wingspan = A("Wingspan"), Weight = 50, Strength = A("Strength"),
                Speed = 50, Quickness = 50, FirstStep = 50, Vertical = A("Vertical"), Endurance = 50,
                Hustle = A("Hustle"), BasketballIQ = 50, Discipline = A("Discipline"), HelpDefense = 50, OffBallDefense = 50,
                RimTendency = 50, ShortTendency = 50, MidTendency = 50, LongTendency = 50, ThreeTendency = 50,
            };
        }
        GameState Game(IReadOnlyList<IReadOnlyDictionary<string, int>> off, IReadOnlyList<IReadOnlyDictionary<string, int>> def)
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), Man(i + 1, off[i]));
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), Man(i + 6, def[i]));
            }
            return g;
        }
        static PossessionState St() => new(PossessionNumber: 1, Offense: TeamSide.Home, Defense: TeamSide.Away,
                                           Entry: EntryType.DeadBallInbound);
        static int[] Nums(Slot[] s) => s.Select(x => x.Number).ToArray();
        static int[] JInts(JsonElement e) => e.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        static IReadOnlyDictionary<string, int> JMan(JsonElement e) =>
            e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number)
             .ToDictionary(p => p.Name, p => p.Value.GetInt32());
        var mass = cfgM.OffensiveRebound + cfgM.DefensiveRebound;
        var baseOff = cfgM.OffensiveRebound / mass;

        // ── C1: oracle parity ──────────────────────────────────────────────────────────────
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "tools", "ft_lane_golden.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var nl = root.GetProperty("normal_lane");
            Check("C1-0: the golden was emitted on the frozen normal lane this config holds (the same four numbers, never the oracle's own)",
                  nl.GetProperty("off_body").GetDouble() == cfgM.LaneNormalOffenseBody
                  && nl.GetProperty("def_body").GetDouble() == cfgM.LaneNormalDefenseBody
                  && nl.GetProperty("off_reb").GetDouble() == cfgM.LaneNormalOffenseRebounding
                  && nl.GetProperty("def_reb").GetDouble() == cfgM.LaneNormalDefenseRebounding
                  && root.GetProperty("base_off_share").GetDouble() == baseOff,
                  Inv($"body {cfgM.LaneNormalOffenseBody:F3} v {cfgM.LaneNormalDefenseBody:F3}, rebounding {cfgM.LaneNormalOffenseRebounding:F3} v {cfgM.LaneNormalDefenseRebounding:F3}"));

            long worst = 0; var compared = 0; var lanesOk = true; var laneDetail = new List<string>();
            long controlBest = long.MaxValue; var controlFails = 0;
            var page = new List<string>();
            void Cmp(double engine, double oracle) { worst = Math.Max(worst, Ulps(engine, oracle)); compared++; }

            foreach (var c in root.GetProperty("cases").EnumerateArray())
            {
                var id = c.GetProperty("id").GetInt32();
                var off = c.GetProperty("offense").EnumerateArray().Select(JMan).ToList();
                var def = c.GetProperty("defense").EnumerateArray().Select(JMan).ToList();
                var g = Game(off, def);
                if (c.GetProperty("clock") is { ValueKind: JsonValueKind.Array } ck)
                    g.PublishClock(ck[0].GetInt32(), ck[1].GetDouble());
                foreach (var f in c.GetProperty("def_fouls").EnumerateObject())
                    for (var k = 0; k < f.Value.GetInt32(); k++)
                        g.PersonalFouls.Increment(g.AwayRoster.PlayerAt(g.AwayLineup.SlotAt(int.Parse(f.Name)))!.PlayerId);
                var shooter = g.HomeLineup.SlotAt(c.GetProperty("shooter").GetInt32());
                var st = St();
                var lane = FreeThrowLane.Build(g, st, shooter, cfgM, match);
                var gl = c.GetProperty("lane");
                var same = Nums(lane.OffenseLane).SequenceEqual(JInts(gl.GetProperty("off_lane")))
                        && Nums(lane.OffenseBack).SequenceEqual(JInts(gl.GetProperty("off_back")))
                        && Nums(lane.DefenseLane).SequenceEqual(JInts(gl.GetProperty("def_lane")))
                        && Nums(lane.DefenseBack).SequenceEqual(JInts(gl.GetProperty("def_back")));
                lanesOk &= same;
                if (!same) laneDetail.Add(Inv($"case {id}"));

                var (ob, db, or, dr) = lane.Totals(g, match);
                var gt = c.GetProperty("totals");
                Cmp(ob, gt.GetProperty("off_body").GetDouble()); Cmp(db, gt.GetProperty("def_body").GetDouble());
                Cmp(or, gt.GetProperty("off_reb").GetDouble()); Cmp(dr, gt.GetProperty("def_reb").GetDouble());

                var laneState = st with { FreeThrowLane = lane };
                var pie = gen(g).Generate(laneState);
                var board = pie.Slices.First(x => x.Outcome == FreeThrowReboundOutcome.OffensiveRebound).Weight;
                var oracleBoard = c.GetProperty("board").GetDouble();
                Cmp(board, oracleBoard);
                Cmp(FreeThrowLane.OffensiveShare(ob, db, or, dr, baseOff, cfgM, match), c.GetProperty("off_share").GetDouble());

                var lists = new Dictionary<string, LaneCandidate[]>
                {
                    ["offensive_board"]           = lane.OffensiveBoard(laneState, g, match, cfgM),
                    ["defensive_board"]           = lane.DefensiveBoard(laneState, g, match, cfgM),
                    ["scramble_fouled"]           = lane.ScrambleFouled(laneState, g, match, cfgM),
                    ["scramble_offensive_fouler"] = lane.ScrambleOffensiveFouler(laneState, g, match, cfgM),
                    ["scramble_defensive_fouler"] = lane.ScrambleDefensiveFouler(g, match),
                };
                foreach (var (name, list) in lists)
                {
                    var ol = c.GetProperty("draws").GetProperty(name).EnumerateArray().ToList();
                    if (ol.Count != list.Length) { lanesOk = false; laneDetail.Add(Inv($"case {id} {name}: {list.Length} vs {ol.Count} men")); continue; }
                    for (var i = 0; i < list.Length; i++)
                    {
                        if (ol[i].GetProperty("slot").GetInt32() != list[i].Slot.Number
                            || ol[i].GetProperty("spot").GetString() != list[i].Spot.ToString())
                        { lanesOk = false; laneDetail.Add(Inv($"case {id} {name} #{i}")); }
                        Cmp(list[i].Share, ol[i].GetProperty("share").GetDouble());
                    }
                }

                // The NEGATIVE CONTROL: the old ten-man split on the same men.
                var offP = Enumerable.Range(1, 5).Select(n => g.HomeRoster.PlayerAt(g.HomeLineup.SlotAt(n))).ToArray();
                var defP = Enumerable.Range(1, 5).Select(n => g.AwayRoster.PlayerAt(g.AwayLineup.SlotAt(n))).ToArray();
                var old = mass * Matchup.OffensiveReboundShare(offP, defP, -1, ShotLocation.Rim, baseOff, match);
                var u = Ulps(old, oracleBoard);
                controlBest = Math.Min(controlBest, u);
                if (u > FtLaneMaxUlps) controlFails++;

                page.Add(Inv($"      {id}. {c.GetProperty("label").GetString(),-62} {100 * board,5:F1}% (approved {100 * c.GetProperty("approved_board").GetDouble():F1}% on the table's own normal pair; ten-man model {100 * old:F1}%)"));
            }
            Check("C1a: ★ the eight archetype cases' lanes and every draw's men and spots match the oracle exactly",
                  lanesOk, lanesOk ? "8 cases, 5 draws each" : string.Join(", ", laneDetail));
            Check(Inv($"C1b: ★ every total, split and per-man share within {FtLaneMaxUlps} ULPs of the oracle"),
                  worst <= FtLaneMaxUlps, Inv($"{compared:N0} values, worst {worst} ULPs"));
            Check(Inv($"C1c: NEGATIVE CONTROL — the old ten-man split on the same eight cases fails the bound in every case"),
                  controlFails == 8, Inv($"{controlFails} of 8 fail; the closest is {controlBest:N0} ULPs away"));
            var row8 = root.GetProperty("cases").EnumerateArray().First(x => x.GetProperty("id").GetInt32() == 8)
                          .GetProperty("board").GetDouble();
            Check("C1d: case 8 (two elite bigs against four men who cannot rebound) sits at or below the shared ceiling",
                  row8 <= mass * match.ReboundOffShareCeiling + 1e-12,
                  Inv($"{100 * row8:F2}% against the ceiling's {100 * mass * match.ReboundOffShareCeiling:F2}%"));
            Console.WriteLine("    (page) offensive boards on a missed last free throw, the eight cases on the league's normal lane:");
            foreach (var l in page) Console.WriteLine(l);

            // The foul-trouble orderings.
            var trOk = true; var trDetail = new List<string>();
            foreach (var t in root.GetProperty("trouble").EnumerateArray())
            {
                var c1 = root.GetProperty("cases")[0];
                var g = Game(c1.GetProperty("offense").EnumerateArray().Select(JMan).ToList(),
                             c1.GetProperty("defense").EnumerateArray().Select(JMan).ToList());
                if (t.GetProperty("clock") is { ValueKind: JsonValueKind.Array } ck)
                    g.PublishClock(ck[0].GetInt32(), ck[1].GetDouble());
                foreach (var f in t.GetProperty("def_fouls").EnumerateObject())
                    for (var k = 0; k < f.Value.GetInt32(); k++)
                        g.PersonalFouls.Increment(g.AwayRoster.PlayerAt(g.AwayLineup.SlotAt(int.Parse(f.Name)))!.PlayerId);
                var lane = FreeThrowLane.Build(g, St(), g.HomeLineup.SlotAt(1), cfgM, match);
                var ok = Nums(lane.DefenseLane).SequenceEqual(JInts(t.GetProperty("def_lane")))
                      && Nums(lane.DefenseBack).SequenceEqual(JInts(t.GetProperty("def_back")));
                trOk &= ok;
                trDetail.Add(Inv($"{t.GetProperty("label").GetString()}: back {string.Join(",", Nums(lane.DefenseBack))}{(ok ? "" : " ✗")}"));
            }
            Check("C1e: ★ the foul-trouble orderings match the oracle — first half at two fouls, second half at four with 301 seconds left but not 300, never in overtime, never without a clock",
                  trOk, string.Join("; ", trDetail));
        }
        catch (Exception ex)
        {
            Check("C1 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        // ── C2: the lane rules ───────────────────────────────────────────────────────────
        try
        {
            // C2a/b: constructed lanes — 2,000 random lineups, seeded, with random foul counts and clocks.
            var rng = new Random(111);
            Dictionary<string, int> R() => new()
            {
                ["Height"] = rng.Next(20, 95), ["Wingspan"] = rng.Next(20, 95), ["Strength"] = rng.Next(20, 95),
                ["Vertical"] = rng.Next(20, 95), ["PostDefense"] = rng.Next(20, 95),
                ["OffensiveRebounding"] = rng.Next(5, 95), ["DefensiveRebounding"] = rng.Next(5, 95),
                ["Hustle"] = rng.Next(20, 95), ["Discipline"] = rng.Next(20, 95),
            };
            int bad = 0, trials = 2_000, troubled = 0;
            for (var t = 0; t < trials; t++)
            {
                var g = Game(Enumerable.Range(0, 5).Select(_ => (IReadOnlyDictionary<string, int>)R()).ToList(),
                             Enumerable.Range(0, 5).Select(_ => (IReadOnlyDictionary<string, int>)R()).ToList());
                var period = rng.Next(1, 4);
                g.PublishClock(period, rng.Next(0, 1201));
                for (var id = 1; id <= 10; id++)
                    for (var k = rng.Next(0, 5); k > 0; k--) g.PersonalFouls.Increment(id);
                var shooter = g.HomeLineup.SlotAt(rng.Next(1, 6));
                var lane = FreeThrowLane.Build(g, St(), shooter, cfgM, match);
                troubled += lane.FoulTroubleMoves > 0 ? 1 : 0;
                bool Trouble(TeamSide side, Slot s) => FreeThrowLane.InFoulTrouble(
                    g.PersonalFouls.CountFor(g.RosterFor(side).PlayerAt(s)!.PlayerId), g.Clock, cfgM);
                double Score(TeamSide side, Slot s) => FreeThrowLane.LaneScore(g.RosterFor(side).PlayerAt(s)!, side == TeamSide.Home, match);
                bool Ordered(TeamSide side, Slot[] onLane, Slot[] back) =>
                    onLane.All(l => back.All(b =>
                        Trouble(side, b) && !Trouble(side, l) ? true
                        : Trouble(side, l) && !Trouble(side, b) ? false
                        : Score(side, l) > Score(side, b) || (Score(side, l) == Score(side, b) && l.Number < b.Number)));
                var ok = lane.OffenseLane.Length == 2 && lane.OffenseBack.Length == 2
                      && lane.DefenseLane.Length == 4 && lane.DefenseBack.Length == 1
                      && !lane.OffenseLane.Contains(shooter) && !lane.OffenseBack.Contains(shooter)
                      && Ordered(TeamSide.Home, lane.OffenseLane, lane.OffenseBack)
                      && Ordered(TeamSide.Away, lane.DefenseLane, lane.DefenseBack);
                if (!ok) bad++;
            }
            Check(Inv($"C2a: ★ {trials:N0} constructed lanes: two offense and four defense on the lane, two and one back, the shooter never on it, and nobody back outscores a lane man unless foul trouble sent him back"),
                  bad == 0, Inv($"{bad} wrong; foul trouble moved a man in {troubled:N0}"));

            // C2b: the non-rebounding point guard stays back (case 1's defense).
            var std = new IReadOnlyDictionary<string, int>[]
            {
                new Dictionary<string, int> { ["Height"] = 51, ["Wingspan"] = 50, ["Strength"] = 40, ["OffensiveRebounding"] = 22, ["DefensiveRebounding"] = 32 },
                new Dictionary<string, int> { ["Height"] = 62, ["Wingspan"] = 58, ["Strength"] = 48, ["OffensiveRebounding"] = 30, ["DefensiveRebounding"] = 40 },
                new Dictionary<string, int> { ["Height"] = 71, ["Wingspan"] = 68, ["Strength"] = 58, ["OffensiveRebounding"] = 45, ["DefensiveRebounding"] = 55 },
                new Dictionary<string, int> { ["Height"] = 76, ["Wingspan"] = 74, ["Strength"] = 70, ["OffensiveRebounding"] = 65, ["DefensiveRebounding"] = 70 },
                new Dictionary<string, int> { ["Height"] = 84, ["Wingspan"] = 82, ["Strength"] = 78, ["OffensiveRebounding"] = 70, ["DefensiveRebounding"] = 78 },
            };
            {
                var g = Game(std, std);
                var lane = FreeThrowLane.Build(g, St(), g.HomeLineup.SlotAt(5), cfgM, match);
                Check("C2b: the 6'0\" point guard who can't rebound stays back on defense, and with the center at the line the offense's lane is the power forward and the wing",
                      Nums(lane.DefenseBack).SequenceEqual(new[] { 1 }) && Nums(lane.OffenseLane).SequenceEqual(new[] { 3, 4 }),
                      Inv($"defense back {string.Join(",", Nums(lane.DefenseBack))}; offense lane {string.Join(",", Nums(lane.OffenseLane))}"));
            }

            // C2c: ties by slot — five identical men.
            {
                var same = Enumerable.Range(0, 5).Select(_ => (IReadOnlyDictionary<string, int>)new Dictionary<string, int>()).ToList();
                var g = Game(same, same);
                var lane = FreeThrowLane.Build(g, St(), g.HomeLineup.SlotAt(1), cfgM, match);
                Check("C2c: ties go by slot — identical men put slots 2 and 3 on the offense's lane (1 at the line) and 1–4 on the defense's, 5 back",
                      Nums(lane.OffenseLane).SequenceEqual(new[] { 2, 3 }) && Nums(lane.OffenseBack).SequenceEqual(new[] { 4, 5 })
                      && Nums(lane.DefenseLane).SequenceEqual(new[] { 1, 2, 3, 4 }) && Nums(lane.DefenseBack).SequenceEqual(new[] { 5 }));
            }

            // C2d/e: the missing shooter, both ways.
            {
                var g = Game(std, std);
                var lane = FreeThrowLane.Build(g, St(), shooter: null, cfgM, match);
                var board = lane.OffensiveBoard(St() with { FreeThrowLane = lane }, g, match, cfgM);
                Check("C2d: no clock and no shooter (a hand-built state): the offense's lane is the best two of all five, three back, and no shooter share — the shares still sum to 1",
                      Nums(lane.OffenseLane).SequenceEqual(new[] { 4, 5 }) && lane.OffenseBack.Length == 3
                      && board.All(x => x.Spot != LaneSpot.Shooter) && Math.Abs(board.Sum(x => x.Share) - 1.0) < 1e-12,
                      Inv($"lane {string.Join(",", Nums(lane.OffenseLane))}, back {string.Join(",", Nums(lane.OffenseBack))}"));
                g.PublishClock(1, 900);
                var threw = false;
                try { FreeThrowLane.Build(g, St(), shooter: null, cfgM, match); } catch (InvalidOperationException) { threw = true; }
                Check("C2e: with a clock (a real game) a missed free throw with no shooter refuses — a real game always knows who missed", threw);
            }
        }
        catch (Exception ex)
        {
            Check("C2 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        // ── C3: the rare shares ──────────────────────────────────────────────────────────
        try
        {
            var std = new IReadOnlyDictionary<string, int>[]
            {
                new Dictionary<string, int> { ["Height"] = 51, ["Wingspan"] = 50, ["Strength"] = 40, ["OffensiveRebounding"] = 22, ["DefensiveRebounding"] = 32 },
                new Dictionary<string, int> { ["Height"] = 62, ["Wingspan"] = 58, ["Strength"] = 48, ["OffensiveRebounding"] = 30, ["DefensiveRebounding"] = 40 },
                new Dictionary<string, int> { ["Height"] = 71, ["Wingspan"] = 68, ["Strength"] = 58, ["OffensiveRebounding"] = 45, ["DefensiveRebounding"] = 55 },
                new Dictionary<string, int> { ["Height"] = 76, ["Wingspan"] = 74, ["Strength"] = 70, ["OffensiveRebounding"] = 65, ["DefensiveRebounding"] = 70 },
                new Dictionary<string, int> { ["Height"] = 84, ["Wingspan"] = 82, ["Strength"] = 78, ["OffensiveRebounding"] = 70, ["DefensiveRebounding"] = 78 },
            };
            var g = Game(std, std);
            var lane = FreeThrowLane.Build(g, St(), g.HomeLineup.SlotAt(1), cfgM, match);
            var ls = St() with { FreeThrowLane = lane };
            var lists = new (string Name, LaneCandidate[] List, double Shooter, double Back)[]
            {
                ("offensive board",            lane.OffensiveBoard(ls, g, match, cfgM),          cfgM.LaneShooterBoardShare,    cfgM.LaneOffenseBackBoardShare),
                ("defensive board",            lane.DefensiveBoard(ls, g, match, cfgM),          double.NaN,                    cfgM.LaneDefenseBackBoardShare),
                ("man fouled on the scramble", lane.ScrambleFouled(ls, g, match, cfgM),          cfgM.LaneShooterScrambleShare, 0.0),
                ("offense's scramble fouler",  lane.ScrambleOffensiveFouler(ls, g, match, cfgM), cfgM.LaneShooterScrambleShare, 0.0),
                ("defense's scramble fouler",  lane.ScrambleDefensiveFouler(g, match),           double.NaN,                    0.0),
            };
            foreach (var (name, list, sh, back) in lists)
            {
                var sum = list.Sum(x => x.Share);
                var shOk = double.IsNaN(sh) ? list.All(x => x.Spot != LaneSpot.Shooter)
                                            : list.Single(x => x.Spot == LaneSpot.Shooter).Share == sh;
                var backOk = list.Where(x => x.Spot == LaneSpot.Back).All(x => x.Share == back);
                Check(Inv($"C3a: {name}: the shares sum to 1, the shooter's is exactly {(double.IsNaN(sh) ? "absent" : sh.ToString("0.###", CultureInfo.InvariantCulture))}, each man back's exactly {back.ToString("0.###", CultureInfo.InvariantCulture)}"),
                      Math.Abs(sum - 1.0) < 1e-12 && shOk && backOk,
                      string.Join(" ", list.Select(x => Inv($"{x.Slot.Number}:{x.Spot}={x.Share:F4}"))));
            }

            const int draws = 400_000;   // the 0.5% cases expect 2,000 each
            foreach (var (name, list, _, _) in lists)
            {
                var counts = new Dictionary<int, int>();
                var r = new SystemRng(111_000 + name.Length);
                for (var i = 0; i < draws; i++)
                {
                    var s = FreeThrowLane.Draw(list, r).Number;
                    counts[s] = counts.GetValueOrDefault(s) + 1;
                }
                var worstZ = 0.0; var zeroDrawn = 0;
                foreach (var c in list)
                {
                    var obs = counts.GetValueOrDefault(c.Slot.Number);
                    if (c.Share == 0.0) { zeroDrawn += obs; continue; }
                    var se = Math.Sqrt(draws * c.Share * (1 - c.Share));
                    worstZ = Math.Max(worstZ, Math.Abs(obs - draws * c.Share) / se);
                }
                Check(Inv($"C3b: {name}: {draws:N0} draws land within 4 SE of every man's share, and a man at zero is never named"),
                      worstZ <= 4.0 && zeroDrawn == 0,
                      Inv($"worst {worstZ:F2} SE; ") + string.Join(" ", list.Select(x => Inv($"{x.Slot.Number}:{counts.GetValueOrDefault(x.Slot.Number):N0}"))));
            }
        }
        catch (Exception ex)
        {
            Check("C3 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        // ── The stock season ─────────────────────────────────────────────────────────────
        try
        {
            var stock = LoadWorld(Path.Combine(AppContext.BaseDirectory, "worlds", "stock-d1.world.json"));
            var season = RunSeasonCore(stock, FtLaneSeasonSeed, configPath, verbose: false);
            var n = season.Results.Count;
            if (season.LaneAudits.Count != n)
                throw new InvalidOperationException($"the lane audit is not index for index with the games: {season.LaneAudits.Count} vs {n}.");
            var trips = season.LaneAudits.SelectMany(a => a.Lanes).ToList();

            // C3c: the rare shares over the season's real trips, each against its own denominator.
            {
                var offBoards = trips.Where(t => t.BoardToOffense == true).ToList();
                var defBoards = trips.Where(t => t.BoardToOffense == false).ToList();
                var fouled = trips.Where(t => t.ScrambleFouledSpot is not null).ToList();
                var offFoulers = trips.Where(t => t.ScrambleOffensiveFoulerSpot is not null).ToList();
                var defFoulers = trips.Where(t => t.ScrambleDefensiveFoulerSpot is not null).ToList();
                void Band(string label, int hits, int denom, double p)
                {
                    var se = Math.Sqrt(denom * p * (1 - p));
                    var ok = p == 0.0 ? hits == 0 : denom > 0 && Math.Abs(hits - denom * p) <= 4 * se;
                    Check(Inv($"C3c: the season — {label}"), ok,
                          Inv($"{hits:N0} of {denom:N0} ({(denom > 0 ? (double)hits / denom : 0):P2}; expected {p:P2}{(p > 0 ? Inv($", 4 SE = {4 * se / Math.Max(1, denom):P2}") : "")})"));
                }
                Band("the shooter takes 3% of his team's offensive boards", offBoards.Count(t => t.BoardSpot == LaneSpot.Shooter), offBoards.Count, cfgM.LaneShooterBoardShare);
                Band("the two offensive men back take 0.5% each", offBoards.Count(t => t.BoardSpot == LaneSpot.Back), offBoards.Count, 2 * cfgM.LaneOffenseBackBoardShare);
                Band("the defense's man back takes 2% of its boards", defBoards.Count(t => t.BoardSpot == LaneSpot.Back), defBoards.Count, cfgM.LaneDefenseBackBoardShare);
                Band("the shooter is the man fouled on 0.5% of scramble fouls", fouled.Count(t => t.ScrambleFouledSpot == LaneSpot.Shooter), fouled.Count, cfgM.LaneShooterScrambleShare);
                Band("a man back is never fouled on the scramble", fouled.Count(t => t.ScrambleFouledSpot == LaneSpot.Back), fouled.Count, 0.0);
                Band("the shooter commits 0.5% of the offense's scramble fouls", offFoulers.Count(t => t.ScrambleOffensiveFoulerSpot == LaneSpot.Shooter), offFoulers.Count, cfgM.LaneShooterScrambleShare);
                Band("a man back never commits the offense's scramble foul", offFoulers.Count(t => t.ScrambleOffensiveFoulerSpot == LaneSpot.Back), offFoulers.Count, 0.0);
                Band("the defense's man back never commits its scramble foul", defFoulers.Count(t => t.ScrambleDefensiveFoulerSpot == LaneSpot.Back), defFoulers.Count, 0.0);
            }

            // C4: the lane never leaks.
            {
                var leaks = season.LaneAudits.Sum(a => a.Leaks);
                Check("C4a: ★ over the stock season, no Roll I, Roll J or Roll K resolution starts with a free-throw lane on the state",
                      leaks == 0, Inv($"{leaks} leaks over {season.PossessionCounts.Sum():N0} possessions and {trips.Count:N0} lanes"));
                Check("C4b: no missed last free throw in a real game lacked a shooter or a clock",
                      trips.All(t => !t.ShooterMissing && t.HadClock),
                      Inv($"{trips.Count(t => t.ShooterMissing)} without a shooter, {trips.Count(t => !t.HadClock)} without a clock"));
            }

            // C5: page-only.
            {
                var offBoards = trips.Count(t => t.BoardToOffense == true);
                Console.WriteLine("  (page) the free-throw lane, stock season (seed 20260720):");
                Console.WriteLine(Inv($"    missed last free throws: {trips.Count:N0} (before S120: {FtLanePreS120Trips:N0})"));
                Console.WriteLine(Inv($"    offensive boards on them: {(double)offBoards / trips.Count:P2} (before S120: {FtLanePreS120OffBoardRate:P2}; the ruled default 20%; Ken Pomeroy's NCAA-wide estimate 20.3%)"));
                Console.WriteLine(Inv($"    mean offensive slice as rolled: {trips.Average(t => t.OffensiveBoardWeight):P2}; Roll M arms: ") +
                                  string.Join(", ", trips.GroupBy(t => t.Outcome).OrderByDescending(x => x.Count()).Select(x => Inv($"{x.Key} {x.Count():N0}"))));
                Console.WriteLine(Inv($"    trips where foul trouble moved a man off the lane: {trips.Count(t => t.FoulTroubleMoves > 0):N0} ({(double)trips.Count(t => t.FoulTroubleMoves > 0) / trips.Count:P1}); men moved {trips.Sum(t => t.FoulTroubleMoves):N0} (before S120, read-only: {FtLanePreS120TroubleTrips:N0} trips)"));
                foreach (var side in new[] { true, false })
                {
                    var b = trips.Where(t => t.BoardToOffense == side).ToList();
                    Console.WriteLine(Inv($"    {(side ? "offensive" : "defensive")} boards ({b.Count:N0}) by where the man stood: ") +
                                      string.Join(", ", b.GroupBy(t => t.BoardSpot).OrderBy(x => x.Key).Select(x => Inv($"{x.Key} {x.Count():N0} ({(double)x.Count() / b.Count:P1})"))));
                    Console.WriteLine(Inv($"      by the man's rebounding rank among his five (1 = best): ") +
                                      string.Join(", ", b.GroupBy(t => t.BoardReboundingRank).OrderBy(x => x.Key).Select(x => Inv($"#{x.Key} {(double)x.Count() / b.Count:P1}"))));
                }
                var f = trips.Where(t => t.ScrambleFouledSpot is not null).ToList();
                var o = trips.Where(t => t.ScrambleOffensiveFoulerSpot is not null).ToList();
                var d = trips.Where(t => t.ScrambleDefensiveFoulerSpot is not null).ToList();
                string Spots(IEnumerable<LaneSpot?> s) => string.Join(", ", s.GroupBy(x => x).OrderBy(x => x.Key).Select(x => Inv($"{x.Key} {x.Count():N0}")));
                Console.WriteLine(Inv($"    scramble fouls — man fouled in the bonus ({f.Count:N0}): {Spots(f.Select(t => t.ScrambleFouledSpot))}; offense's fouler ({o.Count:N0}): {Spots(o.Select(t => t.ScrambleOffensiveFoulerSpot))}; defense's fouler ({d.Count:N0}): {Spots(d.Select(t => t.ScrambleDefensiveFoulerSpot))}"));
                var means = Inv($"lane totals this season, mean: body {trips.Average(t => t.OffBody):F2} v {trips.Average(t => t.DefBody):F2}, rebounding {trips.Average(t => t.OffRebounding):F2} v {trips.Average(t => t.DefRebounding):F2}");
                Console.WriteLine($"    {means} (the frozen normal lane: body {cfgM.LaneNormalOffenseBody:F2} v {cfgM.LaneNormalDefenseBody:F2}, rebounding {cfgM.LaneNormalOffenseRebounding:F2} v {cfgM.LaneNormalDefenseRebounding:F2})");
                Console.WriteLine(Inv($"    season points: {season.Results.Sum(r => (long)r.HomeScore + r.AwayScore):N0}"));
            }

            // C6: only possessions from a game's first missed last free throw on moved.
            {
                var fixedSet = Enumerable.Range(0, n)
                    .Where(i => i < season.ConferenceGameCount || season.PlayedGames[i].IsBuyGame).ToList();
                Check(Inv($"C6a: the fixed-pairing games are the league slate plus the buy games, {FtLaneFixedPairingGames:N0} of them, none an event or tournament game"),
                      fixedSet.Count == FtLaneFixedPairingGames
                      && fixedSet.All(i => season.PlayedGames[i].EventId is null && season.PlayedGames[i].ConferenceTournamentId is null),
                      Inv($"{fixedSet.Count:N0}"));
                var none = fixedSet.Count(i => season.LaneAudits[i].Lanes.Count == 0);
                var prefixPoss = fixedSet.Sum(i => season.LaneAudits[i].PrefixPossessions);
                var digest = RatingSha(string.Concat(fixedSet.Select(i => $"{i}|{season.LaneAudits[i].PrefixPossessions}|{season.LaneAudits[i].Prefix}\n")));
                Check(Inv($"C6b: ★ in every fixed-pairing game, every possession before its first missed last free throw is the S119 engine's, byte for byte — {FtLaneGoldenPrefixPossessions:N0} possessions, and the {FtLaneGoldenGamesWithNone} games with none whole"),
                      digest == FtLaneGoldenPrefixDigest && prefixPoss == FtLaneGoldenPrefixPossessions && none == FtLaneGoldenGamesWithNone,
                      Inv($"{prefixPoss:N0} possessions, {none} games with no missed last free throw, {digest[..16]}…"));
                var plusOne = RatingSha(string.Concat(fixedSet.Select(i => $"{i}|{season.LaneAudits[i].PlusOne}\n")));
                Check("C6c: NEGATIVE CONTROL — taken one possession further (the possession holding each game's first miss), the same digest differs from the S119 engine's: the lane moved them, and the digest can see it",
                      plusOne != FtLaneGoldenPlusOneDigest, plusOne[..16] + "…");
            }
        }
        catch (Exception ex)
        {
            Check("Phase 111 season completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        // C4c: the leak counter can see a leak — a possession handed to Roll J with a lane still on it.
        //    (The resolver clears the lane on every continuation that leaves the Roll M resolution, so a
        //    stale lane cannot be routed INTO Roll I or Roll K from outside the engine; the counter is
        //    shown firing where a caller can still hand one in.)
        try
        {
            var g = new GameState(new FoulTracker(cfgD.BonusThreshold, cfgD.DoubleBonusThreshold));
            for (var i = 0; i < 5; i++)
            {
                g.HomeRoster.SetStarter(g.HomeLineup.SlotAt(i + 1), Man(i + 1, new Dictionary<string, int>()));
                g.AwayRoster.SetStarter(g.AwayLineup.SlotAt(i + 1), Man(i + 6, new Dictionary<string, int>()));
            }
            var resolver = FtLaneResolver(configPath, g);
            var lane = FreeThrowLane.Build(g, St(), g.HomeLineup.SlotAt(1), cfgM, match);
            var stale = new PossessionState(PossessionNumber: 2, Offense: TeamSide.Away, Defense: TeamSide.Home,
                                            Entry: EntryType.Transition, TransitionContext: TransitionContext.FreeThrowRebound)
                        { FreeThrowLane = lane };
            var o = resolver.RunPossession(stale);
            var clean = resolver.RunPossession(stale with { FreeThrowLane = null });
            Check("C4c: NEGATIVE CONTROL — a possession handed to Roll J with a lane still on it is counted as a leak; the same possession without one is not",
                  o.FreeThrowLaneLeaks == 1 && clean.FreeThrowLaneLeaks == 0,
                  Inv($"{o.FreeThrowLaneLeaks} with the stale lane, {clean.FreeThrowLaneLeaks} without"));
        }
        catch (Exception ex)
        {
            Check("C4c completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine($"  Phase 111 {(pass ? "PASS" : "FAIL")} ({assertions} assertions)");
        return pass;
    }

    /// <summary>A real resolver over one hand-built game (C4c).</summary>
    private static Resolver FtLaneResolver(string configPath, GameState g)
    {
        var cfgA = RollAConfig.Load(configPath);   var cfgB = RollBConfig.Load(configPath);
        var cfgC = RollCConfig.Load(configPath);   var cfgD = RollDConfig.Load(configPath);
        var cfgE = RollEConfig.Load(configPath);   var cfgF = RollFConfig.Load(configPath);
        var cfgG = RollGConfig.Load(configPath);   var cfgH = RollHConfig.Load(configPath);
        var cfgI = RollIConfig.Load(configPath);   var cfgJ = RollJConfig.Load(configPath);
        var cfgK = RollKConfig.Load(configPath);   var cfgL = RollLConfig.Load(configPath);
        var cfgM = RollMConfig.Load(configPath);
        var cfgOff = RollOffensiveFoulConfig.Load(configPath);
        var cfgAtt = AttentionConfig.Load(configPath);
        var m = MatchupConfig.Load(configPath);
        return new Resolver(
            new RollAGenerator(cfgA, m, g), cfgA, new RollBGenerator(cfgB, m, g),
            new RollCGenerator(cfgC), cfgC, new RollDGenerator(cfgD), new RollEGenerator(cfgE, g),
            new AttentionGenerator(cfgAtt, g), new RollFGenerator(cfgF, m, g), new RollGGenerator(cfgG, m, g),
            new RollHGenerator(cfgH, m, g), new RollIGenerator(cfgI, m, g), new RollJGenerator(cfgJ, m, g),
            new RollKGenerator(cfgK, m, g), new RollLGenerator(cfgL, g), new RollMGenerator(cfgM, m, g),
            new RollOffensiveFoulGenerator(cfgOff), m, g, new SystemRng(111));
    }
}
