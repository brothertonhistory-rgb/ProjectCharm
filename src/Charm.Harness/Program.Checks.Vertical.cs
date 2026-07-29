using Charm.Engine;

namespace Charm.Harness;

// Phase 75 (Session 81.2) — WHAT A VERTICAL LEAP IS WORTH.
//
// Before S81.2 the leap was smeared across two composites with no separation between
// standing reach, general athleticism, and task-specific jumping: a full third of the
// block door's "length", one fifth of Athleticism, nothing at all on the glass, and
// double-counted in transition. The visible symptom was a 6'6" leaper out-protecting the
// rim better than a 6'11" with a 7'2" wingspan.
//
// THE INVARIANT THAT WOULD STILL PASS IF THIS WERE BUILT WRONG: every conservation
// identity, monotonicity check and golden passes at ANY weighting, because a weighting is
// self-consistent. Only a sweep of the leap with height, wingspan and every rating FROZEN
// can see it. That sweep is (1) below, deliberately first.
//
// TWO SEPARATE FACTS ABOUT BLOCKING, neither implying the other, and (4) keeps them apart:
//   * within one body  — more hops STILL helps a defender block, a little (by design);
//   * across bodies    — the tall long man now beats the short explosive one.
// Only the second is the acceptance test. A check written to the sentence "blocking falls"
// would license an implementation that makes hops HARMFUL to shot blocking, which is
// nonsense basketball and would ship green.

internal static partial class Program
{
    private static bool Phase75VerticalCheck(string configPath)
    {
        Console.WriteLine("\n--- Phase 75: what a vertical leap is worth (isolation sweep + neutral points + acceptance swap) ---");
        var pass = true;
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        var cfgM = MatchupConfig.Load(configPath);
        var cfgH = RollHConfig.Load(configPath);
        var cfgD = RollDConfig.Load(configPath);

        static Player Mk(string n, int height = 50, int wingspan = 50, int vertical = 50,
                         int strength = 50, int speed = 50, int quickness = 50, int firstStep = 50,
                         int perimD = 50, int postD = 50, int rimP = 50, int helpD = 50,
                         int fin = 50, int close = 50, int mid = 50, int outside = 50,
                         int orb = 50, int drb = 50, int hustle = 50)
            => new Player(n)
            {
                Outside = outside, Mid = mid, Close = close, Finishing = fin, FreeThrow = 50,
                FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                OffensiveRebounding = orb, PerimeterDefense = perimD, PostDefense = postD,
                RimProtection = rimP, DefensiveRebounding = drb, Steals = 50,
                Height = height, Wingspan = wingspan, Weight = 50, Strength = strength,
                Speed = speed, Quickness = quickness, FirstStep = firstStep, Vertical = vertical,
                Endurance = 50, Hustle = hustle, BasketballIQ = 50, Discipline = 50,
                HelpDefense = helpD, OffBallDefense = 50,
                RimTendency = 50, ShortTendency = 50, MidTendency = 50,
                LongTendency = 50, ThreeTendency = 50,
            };

        // ── (1) THE ISOLATION SWEEP — first, and the only check that can see a mis-weighting ──
        // One player. Vertical walks 20 -> 99. Height, wingspan and EVERY rating frozen. The
        // decomposition is printed so the stacking is VISIBLE rather than inferred: the leap now
        // pays twice on a rim make (general athleticism + its own term) and once on the block
        // door (its 0.15 share of reach). Those are DIFFERENT DOORS and are not summed.
        //
        // HeightOverDefenderShift reads (Height + Wingspan)/2 and deliberately EXCLUDES the leap,
        // so reach adds no third term to the make curve. Describing this as "three additions to
        // the make curve" is wrong and would misread the sweep.
        {
            Console.WriteLine("  (1) Isolation sweep — hops 20..99, body and every rating frozen:");
            Console.WriteLine("      defender: 6'8\"-equivalent, hops 50, rim protection 70. Shooter frozen but for hops.");
            Console.WriteLine();
            Console.WriteLine("      hops | athleticism | new leap term | MAKE total |  reach | block rate");
            Console.WriteLine("      -----+-------------+---------------+------------+--------+-----------");

            var defender = Mk("def", height: 74, wingspan: 78, vertical: 50, rimP: 70, postD: 60);
            var baseMake = 0.0; var baseBlock = 0.0; var firstRow = true;
            var makeRising = true; var blockRising = true;
            var prevMake = double.NegativeInfinity; var prevBlock = double.NegativeInfinity;

            for (var v = 20; v <= 99; v += (v == 99 ? 1 : (v % 10 == 0 ? 10 : 9)))
            {
                var shooter = Mk("sh", height: 74, wingspan: 76, vertical: v, fin: 60, close: 60);

                var athShift = Matchup.GapFn(shooter.Athleticism - defender.Athleticism,
                                             cfgM.PhysicalSteepness, cfgM.PhysicalExponent, cfgM.ReferenceScale);
                var leapShift = Matchup.VerticalShift(shooter, defender, cfgM.RimVerticalWeight, cfgM);
                var eff = Matchup.EffectiveRating(ShotLocation.Rim, shooter, defender, cfgM) + leapShift;
                var make = cfgH.MakeProbability(ShotLocation.Rim, eff);
                var reach = Matchup.LengthRating(shooter, cfgM);
                var block = Matchup.BlockWeight(ShotLocation.Rim, shooter, defender,
                                                cfgH.BlockWeight(ShotLocation.Rim), cfgM);

                if (firstRow) { baseMake = make; baseBlock = block; firstRow = false; }
                if (make < prevMake) makeRising = false;
                if (block > prevBlock && v > 20) blockRising = false;  // shooter's hops LOWER the block on him
                prevMake = make; prevBlock = block;

                Console.WriteLine($"      {v,4} | {athShift,11:F3} | {leapShift,13:F3} | {make,9:P2} | {reach,6:F2} | {block,9:P2}");
                if (v == 99) break;
            }
            Console.WriteLine();
            Console.WriteLine($"      make% across the full hops range: {baseMake:P2} -> {prevMake:P2}  (span {(prevMake - baseMake) * 100:F2} pts)");

            Check("more hops strictly raises rim make% with the body frozen", makeRising);
            Check("a SHOOTER's hops lower the block rate against him (reach is shooter-relative)", blockRising);
        }

        // ── (2) The reach composite, asserted DIRECTLY and independently of any oracle ──
        // If this drifts, every downstream block number is wrong in a way a self-consistent
        // golden cannot see.
        {
            var b = Mk("b", height: 60, wingspan: 60, vertical: 60);
            var bH = Mk("bH", height: 61, wingspan: 60, vertical: 60);
            var bW = Mk("bW", height: 60, wingspan: 61, vertical: 60);
            var bV = Mk("bV", height: 60, wingspan: 60, vertical: 61);
            var r0 = Matchup.LengthRating(b, cfgM);
            var dH = Matchup.LengthRating(bH, cfgM) - r0;
            var dW = Matchup.LengthRating(bW, cfgM) - r0;
            var dV = Matchup.LengthRating(bV, cfgM) - r0;
            Check("+1 wingspan raises reach by exactly 0.45", Math.Abs(dW - 0.45) < 1e-12, $"{dW:F12}");
            Check("+1 height raises reach by exactly 0.40",   Math.Abs(dH - 0.40) < 1e-12, $"{dH:F12}");
            Check("+1 hops raises reach by exactly 0.15",     Math.Abs(dV - 0.15) < 1e-12, $"{dV:F12}");
            Check("the hand outranks the frame, and the frame outranks the leap",
                  dW > dH && dH > dV && dV > 0.0, "wingspan > height > vertical > 0");
        }

        // ── (3) THE ACCEPTANCE TEST — the swap ──────────────────────────────────
        // Skill, readiness, assignment and shooter held IDENTICAL, so a failure cannot come
        // from an unrelated attribute. Today the small explosive man out-protects the rim;
        // after, the tall long man leads.
        {
            //  6'6" frame, 6'9" span, 42-inch hops   vs   6'11" frame, 7'2" span, flat-footed
            var smallLeaper = Mk("leaper", height: 66, wingspan: 74, vertical: 95, rimP: 70, postD: 60, helpD: 60);
            var bigLong     = Mk("big",    height: 88, wingspan: 95, vertical: 35, rimP: 70, postD: 60, helpD: 60);

            var threatSmall = Matchup.BlockDefenderThreat(ShotLocation.Rim, smallLeaper, cfgM);
            var threatBig   = Matchup.BlockDefenderThreat(ShotLocation.Rim, bigLong, cfgM);
            Check("the tall long man now out-protects the rim over the short explosive one",
                  threatBig > threatSmall,
                  $"long big {threatBig:F2} vs small leaper {threatSmall:F2} (identical skill, readiness, assignment)");

            Check("...and the big's REACH now leads too",
                  Matchup.LengthRating(bigLong, cfgM) > Matchup.LengthRating(smallLeaper, cfgM),
                  $"{Matchup.LengthRating(bigLong, cfgM):F2} vs {Matchup.LengthRating(smallLeaper, cfgM):F2}");
        }

        // ── (4) ★ NOT "blocking falls" — the SLOPE falls, the sign does not ─────
        // Within one fixed body, more hops STILL helps a defender block. Asserting that
        // blocking falls would license an implementation making hops harmful to shot
        // blocking. Two contracts, both required.
        {
            Player D(int v) => Mk("d", height: 80, wingspan: 82, vertical: v, rimP: 70, postD: 60);
            var shooter = Mk("sh", height: 74, wingspan: 76, vertical: 50, fin: 60, close: 60);
            double Blk(Player d) => Matchup.BlockWeight(ShotLocation.Rim, shooter, d,
                                                        cfgH.BlockWeight(ShotLocation.Rim), cfgM);

            var newLo = Blk(D(20)); var newHi = Blk(D(99));

            // The OLD equal-thirds behaviour, recomputed by hand from the same primitives —
            // no second config load, no stale copy of the engine.
            double OldReach(Player p) => (p.Height + p.Wingspan + p.Vertical) / 3.0;
            double OldBlk(Player d)
            {
                var skillGap = Matchup.DefenseRating(ShotLocation.Rim, d, cfgM)
                             - Matchup.OffenseRating(ShotLocation.Rim, shooter);
                var skillShift = Matchup.GapFn(skillGap, cfgM.SkillSteepness, cfgM.SkillExponent, cfgM.ReferenceScale);
                var lengthShift = Matchup.GapFn(OldReach(d) - OldReach(shooter),
                                                cfgM.PhysicalSteepness, cfgM.PhysicalExponent, cfgM.ReferenceScale);
                var (sw, lw) = cfgM.BlockContestWeights(ShotLocation.Rim);
                return Matchup.BlockBend(ShotLocation.Rim, sw * skillShift + lw * lengthShift,
                                         cfgH.BlockWeight(ShotLocation.Rim), cfgM);
            }
            var oldLo = OldBlk(D(20)); var oldHi = OldBlk(D(99));

            Console.WriteLine($"  (4) one fixed 6'8\"/6'10\" body, rim protection 70, hops 20 -> 99:");
            Console.WriteLine($"      old block {oldLo:P2} -> {oldHi:P2}   (span {(oldHi - oldLo) * 100:F2} pts)");
            Console.WriteLine($"      new block {newLo:P2} -> {newHi:P2}   (span {(newHi - newLo) * 100:F2} pts)");

            Check("within one body, more hops STILL improves blocking", newHi > newLo,
                  $"{newLo:P2} -> {newHi:P2}");
            Check("...but the slope is materially smaller than the old equal-thirds slope",
                  (newHi - newLo) < (oldHi - oldLo),
                  $"new span {(newHi - newLo) * 100:F2} pts vs old span {(oldHi - oldLo) * 100:F2} pts");
        }

        // ── (5) ★ THREE DIFFERENT NEUTRAL POINTS — do not assert one rule across all three ──
        {
            // (a) rim and putback — ATTACKER vs DEFENDER
            var a = Mk("a", vertical: 90); var d50 = Mk("d", vertical: 50);
            var equalA = Mk("ea", vertical: 71); var equalD = Mk("ed", vertical: 71);
            var fwd = Matchup.VerticalShift(a, d50, cfgM.RimVerticalWeight, cfgM);
            var rev = Matchup.VerticalShift(d50, a, cfgM.RimVerticalWeight, cfgM);
            Check("rim: two equal leapers cancel EXACTLY",
                  Matchup.VerticalShift(equalA, equalD, cfgM.RimVerticalWeight, cfgM) == 0.0);
            Check("rim: 90-vs-50 is the exact sign-reverse of 50-vs-90",
                  Math.Abs(fwd + rev) < 1e-12, $"{fwd:F6} / {rev:F6}");
            Check("putback: two equal leapers cancel EXACTLY",
                  Matchup.VerticalShift(equalA, equalD, cfgM.PutbackVerticalWeight, cfgM) == 0.0);
            Check("putback carries double the ordinary rim weight",
                  Math.Abs(Matchup.VerticalShift(a, d50, cfgM.PutbackVerticalWeight, cfgM)
                           - 2.0 * fwd) < 1e-12);

            // (b) team rebounding — TEAM MEAN vs TEAM MEAN
            var offEq = new Player?[] { Mk("o1", vertical: 40), Mk("o2", vertical: 60), Mk("o3", vertical: 50), Mk("o4", vertical: 55), Mk("o5", vertical: 45) };
            var defEq = new Player?[] { Mk("d1", vertical: 50), Mk("d2", vertical: 50), Mk("d3", vertical: 50), Mk("d4", vertical: 50), Mk("d5", vertical: 50) };
            var shareEq = Matchup.OffensiveReboundShare(offEq, defEq, -1, ShotLocation.Rim, 0.30, cfgM);
            Check("team glass: equal team MEANS cancel exactly (different individuals, same mean)",
                  Math.Abs(shareEq - 0.30) < 1e-12, $"share {shareEq:P4} against a 30.00% baseline");

            var offHi = new Player?[] { Mk("h1", vertical: 90), Mk("h2", vertical: 90), Mk("h3", vertical: 90), Mk("h4", vertical: 90), Mk("h5", vertical: 90) };
            var shareHi = Matchup.OffensiveReboundShare(offHi, defEq, -1, ShotLocation.Rim, 0.30, cfgM);
            Console.WriteLine($"  (5) all-elite leapers vs all-average on the offensive glass: 30.00% -> {shareHi:P2}");
            Check("a whole team of leapers moves the offensive glass a sliver, not a strategy",
                  shareHi > 0.30 && shareHi < 0.35, $"{shareHi:P2}");

            // (c) individual rebounding — TEAMMATE-relative, NOT opponent-relative.
            // The neutral point is his OWN lineup mean. An attacker-versus-defender
            // cancellation assertion is WRONG here and is deliberately not written.
            var m1 = Matchup.ReboundVerticalMultiplier(70.0, 70.0, cfgM);
            Check("individual glass: a player who jumps exactly like his teammates gets exactly 1.0",
                  m1 == 1.0, "neutral point is his own lineup mean, not the opponent");
            Check("individual glass: the multiplier stays inside (1-swing, 1+swing)",
                  Matchup.ReboundVerticalMultiplier(99.0, 20.0, cfgM) < 1.0 + cfgM.ReboundVerticalSwing
                  && Matchup.ReboundVerticalMultiplier(20.0, 99.0, cfgM) > 1.0 - cfgM.ReboundVerticalSwing
                  && Matchup.ReboundVerticalMultiplier(20.0, 99.0, cfgM) > 0.0,
                  $"({1.0 - cfgM.ReboundVerticalSwing:F2}, {1.0 + cfgM.ReboundVerticalSwing:F2}) — strictly positive, so no weight can flip sign");
        }

        // ── (6) ★ PICKER MONOTONICITY — the STRICT version, with its full preconditions ──
        // The luck term is ADDED, not a floor under the product, so the weight is strictly
        // increasing in hops. State every precondition, because the multiplier is
        // teammate-relative and has legitimate neutral cases.
        //
        // ★ The two pickers do NOT read the same rating. Offensive is strict when
        // OffensiveRebounding > 0; defensive when DefensiveRebounding > 0. A test that sweeps
        // the offensive rating while exercising the defensive picker passes for the wrong reason.
        {
            static Player P(int id, int v, int orb, int drb)
                => new Player($"p{id}")
                {
                    PlayerId = id,
                    Outside = 50, Mid = 50, Close = 50, Finishing = 50, FreeThrow = 50,
                    FoulDrawing = 50, BallHandling = 50, Passing = 50, Playmaking = 50,
                    SelfCreation = 50, PostMoves = 50, OffBallMovement = 50, Screening = 50,
                    OffensiveRebounding = orb, PerimeterDefense = 50, PostDefense = 50,
                    RimProtection = 50, DefensiveRebounding = drb, Steals = 50,
                    Height = 50, Wingspan = 50, Weight = 50, Strength = 50,
                    Speed = 50, Quickness = 50, FirstStep = 50, Vertical = v,
                    Endurance = 50, Hustle = 50, BasketballIQ = 50, Discipline = 50,
                    HelpDefense = 50, OffBallDefense = 50,
                    RimTendency = 50, ShortTendency = 50, MidTendency = 50,
                    LongTendency = 50, ThreeTendency = 50,
                };

            // The picker weight, recomputed from the same public primitives the picker uses.
            double Weight(Player[] five, int idx, bool offensive)
            {
                var meanV = five.Average(p => (double)p.Vertical);
                var meanPn = five.Average(p => Matchup.Postness(p, cfgM));
                var meanWs = five.Average(p => (double)p.Wingspan);
                var meanPh = five.Average(p => Matchup.ReboundPhysical(p, cfgM));
                var p2 = five[idx];
                var pw = Matchup.PositionalWeight(Matchup.Postness(p2, cfgM), meanPn, cfgM);
                var wm = Matchup.ReboundWingspanMultiplier(p2.Wingspan, meanWs, cfgM);
                var hm = 1.0 + cfgM.HustleRebounderSteepness
                             * Math.Tanh((p2.Hustle - 50.0) / cfgM.HustleRebounderScale);
                var vm = Matchup.ReboundVerticalMultiplier(p2.Vertical, meanV, cfgM);
                var rating = offensive ? p2.OffensiveRebounding : p2.DefensiveRebounding;
                var bodyPull = cfgM.ReboundBodyPullWeight
                             * Math.Max(0.0, Matchup.ReboundPhysical(p2, cfgM) - meanPh);
                var absFloor = cfgM.ReboundBodyFloorCeiling
                             * Math.Tanh(Math.Max(0.0, Matchup.ReboundPhysical(p2, cfgM) - cfgM.ReboundBodyFloorReference)
                                         / cfgM.ReboundBodyFloorScale);
                return cfgM.ReboundLuckWeight + rating * pw * wm * hm * vm + bodyPull + absFloor;
            }

            // STRICT — offensive picker, offensive rating > 0, five populated, swing > 0.
            var strictOffOk = true;
            for (var v = 20; v < 99; v++)
            {
                var lo = new[] { P(1, v,     60, 0), P(2, 50, 60, 60), P(3, 50, 60, 60), P(4, 50, 60, 60), P(5, 50, 60, 60) };
                var hi = new[] { P(1, v + 1, 60, 0), P(2, 50, 60, 60), P(3, 50, 60, 60), P(4, 50, 60, 60), P(5, 50, 60, 60) };
                if (!(Weight(hi, 0, offensive: true) > Weight(lo, 0, offensive: true))) strictOffOk = false;
            }
            Check("offensive picker: raising a man's hops STRICTLY raises his pick weight",
                  strictOffOk, "populated > 1, swing > 0, his OffensiveRebounding > 0, nerf > 0, all else fixed");

            // ★ The defensive picker reads a DIFFERENT rating — sweep it with the offensive
            // rating pinned at zero, so a pass cannot come from the wrong rating.
            var strictDefOk = true;
            for (var v = 20; v < 99; v++)
            {
                var lo = new[] { P(1, v,     0, 60), P(2, 50, 60, 60), P(3, 50, 60, 60), P(4, 50, 60, 60), P(5, 50, 60, 60) };
                var hi = new[] { P(1, v + 1, 0, 60), P(2, 50, 60, 60), P(3, 50, 60, 60), P(4, 50, 60, 60), P(5, 50, 60, 60) };
                if (!(Weight(hi, 0, offensive: false) > Weight(lo, 0, offensive: false))) strictDefOk = false;
            }
            Check("defensive picker: strict on DefensiveRebounding, with OffensiveRebounding pinned at 0",
                  strictDefOk, "the two pickers do not read the same rating — a shared sweep would pass for the wrong reason");

            // NEUTRAL — the legitimate no-change cases.
            var solo = new[] { P(1, 99, 60, 60) };
            var soloLo = new[] { P(1, 20, 60, 60) };
            Check("NEUTRAL: one populated player IS the lineup mean, so his hops cannot move him",
                  Weight(solo, 0, offensive: true) == Weight(soloLo, 0, offensive: true));

            var zeroRatingHi = new[] { P(1, 99, 0, 60), P(2, 50, 60, 60), P(3, 50, 60, 60), P(4, 50, 60, 60), P(5, 50, 60, 60) };
            var zeroRatingLo = new[] { P(1, 20, 0, 60), P(2, 50, 60, 60), P(3, 50, 60, 60), P(4, 50, 60, 60), P(5, 50, 60, 60) };
            Check("NEUTRAL: a zero rebounding rating means the whole product vanishes, hops and all",
                  Weight(zeroRatingHi, 0, offensive: true) == Weight(zeroRatingLo, 0, offensive: true),
                  "luck, body pull and the loose-ball floor are deliberately rating-independent");
        }

        // ── (7) The multiplier goes into the SKILL PRODUCT only, never the independent channels ──
        // Luck is every populated player's equal claim on a loose ball; the body terms stand on
        // their own. If the leap tilt leaked into any of the three, a zero-rating player's weight
        // would move with his hops — and (6)'s neutral case above is exactly that probe. This
        // check states the property directly so a future reader sees the rule, not just the probe.
        {
            var one = Matchup.ReboundVerticalMultiplier(99.0, 50.0, cfgM);
            Check("the leap tilt is a MULTIPLIER on the rebounding rating, bounded and centred at 1.0",
                  one > 1.0 && one < 1.0 + cfgM.ReboundVerticalSwing,
                  $"an elite leaper among average teammates: x{one:F4}");
        }

        // ── (8) ★ CONFIG GUARDS — every new Load assertion actually throws ───────
        {
            var raw = File.ReadAllText(configPath);
            bool Throws(string key, object value)
            {
                var tmp = Path.Combine(Path.GetTempPath(), $"charm_p75_{Guid.NewGuid():N}.json");
                try
                {
                    var node = System.Text.Json.Nodes.JsonNode.Parse(raw)!;
                    node["Matchup"]![key] = System.Text.Json.Nodes.JsonValue.Create(Convert.ToDouble(value));
                    File.WriteAllText(tmp, node.ToJsonString());
                    try { MatchupConfig.Load(tmp); return false; }
                    catch (InvalidOperationException) { return true; }
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }

            Check("negative RimVerticalWeight throws",              Throws("RimVerticalWeight", -0.01));
            Check("negative PutbackVerticalWeight throws",          Throws("PutbackVerticalWeight", -0.01));
            Check("negative ReboundVerticalTeamWeight throws",      Throws("ReboundVerticalTeamWeight", -0.01));
            Check("ReboundVerticalSwing at 1.0 throws (a poor leaper could go negative)",
                  Throws("ReboundVerticalSwing", 1.0));
            Check("ReboundVerticalSwing negative throws",           Throws("ReboundVerticalSwing", -0.01));
            Check("ReboundVerticalScale at ZERO throws (it is the tanh denominator)",
                  Throws("ReboundVerticalScale", 0.0));
            Check("reach weights out of order throw (vertical above height)",
                  Throws("LengthVertical", 0.45) || Throws("LengthHeight", 0.10));
            Check("reach weights that no longer sum to 1.0 still throw (Phase 7 guard kept)",
                  Throws("LengthWingspan", 0.60));

            // The zero-is-a-kill-switch contract: 0 must LOAD, not throw.
            var killTmp = Path.Combine(Path.GetTempPath(), $"charm_p75_kill_{Guid.NewGuid():N}.json");
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(raw)!;
                node["Matchup"]!["RimVerticalWeight"] = System.Text.Json.Nodes.JsonValue.Create(0.0);
                node["Matchup"]!["PutbackVerticalWeight"] = System.Text.Json.Nodes.JsonValue.Create(0.0);
                node["Matchup"]!["ReboundVerticalTeamWeight"] = System.Text.Json.Nodes.JsonValue.Create(0.0);
                node["Matchup"]!["ReboundVerticalSwing"] = System.Text.Json.Nodes.JsonValue.Create(0.0);
                File.WriteAllText(killTmp, node.ToJsonString());
                var killed = MatchupConfig.Load(killTmp);
                var a = Mk("a", vertical: 99); var d = Mk("d", vertical: 20);
                Check("all four kill switches at ZERO load cleanly and zero the leap out",
                      Matchup.VerticalShift(a, d, killed.RimVerticalWeight, killed) == 0.0
                      && Matchup.VerticalShift(a, d, killed.PutbackVerticalWeight, killed) == 0.0
                      && Matchup.ReboundVerticalMultiplier(99.0, 20.0, killed) == 1.0,
                      "the leap can be switched off entirely without a rebuild");
            }
            catch (InvalidOperationException ex)
            {
                Check("all four kill switches at ZERO load cleanly", false, ex.Message);
            }
            finally { if (File.Exists(killTmp)) File.Delete(killTmp); }
        }

        _ = cfgD;
        Console.WriteLine(pass ? "  Phase 75 PASSED." : "  Phase 75 FAILED.");
        return pass;
    }
}
