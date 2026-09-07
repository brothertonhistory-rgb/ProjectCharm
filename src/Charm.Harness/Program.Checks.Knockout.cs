using System.Globalization;
using System.Reflection;

namespace Charm.Harness;

// ============================================================================
//  Phase 98 — S109: THE SINGLE-ELIMINATION PRIMITIVE, AND THE CITY ON THE GAME.
//
//  Two deliverables, both machinery, neither touching basketball:
//
//   1. A dormant knockout bracket generator. Twelve invariants, each its own
//      named check, across every supported size (2..64). The canonical seed
//      line asserted LITERALLY, not by property — because two outside reviews
//      each defeated a property-based contract with a legal-but-wrong bracket.
//      A negative control per hole, each proven to fire the rule it names.
//
//   2. PlaceId on the game record. Proven at both structural boundaries with
//      a negative control each, and — the free and brutal proof — ALL FIVE
//      existing fingerprints reproduce exactly on a run carrying the field.
//
//  ── How the invariants are exercised ───────────────────────────────────────
//  The production bracket cannot express a loser edge or a three-input node:
//  the side union is closed and a game has exactly two sides. That is better
//  than a validator that rejects them — but a control that cannot go red
//  discharges nothing. So the validator here runs over a TEST-ONLY RAW
//  TOPOLOGY (any number of inputs, any input kind) that the production
//  bracket is adapted INTO, and the negative controls are built raw. The
//  validator reports EVERY rule that fires, never the first — which is what
//  lets a control assert "rule 12 and nothing lower" rather than trusting
//  that it reached the rule it names (the S106 lesson).
//
//  Closure of the production types is DEMONSTRATED by reflection, not asserted
//  in a comment: the check goes red if a third side variant or a third side
//  slot ever appears.
//
//  ── What this phase does not prove ─────────────────────────────────────────
//  Any basketball value. Nothing here plays a game. Page-only calibration holds.
// ============================================================================

internal static partial class Program
{
    private const long KnockoutCheckSeed = 20260720;

    /// <summary>★ THE FIFTH FINGERPRINT — the stock season's non-conference DATED hash off
    /// <c>NonConDatedGame</c>, re-derived from the pristine pre-S109 tree at the gate (the page
    /// now prints it; Emmett's machine is the commit-of-record). It is the same value Phase 97
    /// C1d asserts against <c>nonconference_dates_golden.json</c>. ★ The build prompt carried
    /// <c>b75754bc…</c>, transcribed from the status board — that was S106's value, and S108's
    /// REACH moved it when the pairings moved. The S81.3 lesson, again: re-derive, never
    /// transcribe. The other four are the Phase 93 goldens, reused by name.</summary>
    private const string KnockoutGoldenNonConDatedFp =
        "7ace22ed8aa161c28e8af03bb2bb82b1ceb6a9b604fde1b6c35b2299094e1708";

    /// <summary>★ THE EVENT-GAME COUNT, re-derived at the gate from the pre-edit baseline:
    /// 128 tournament games (17 active four-team events × 4 + 5 eight-team × 12) plus 24
    /// showcase games. A seeded draw result, never a product of authored counts.</summary>
    private const int KnockoutGoldenEventGameCount = 152;
    private const int KnockoutGoldenConferenceGameCount = 2818;

    // ── The test-only raw topology ─────────────────────────────────────────────

    private sealed record RawSide(string Kind, int Ref);          // "seed" | "winner" | "loser"
    private sealed record RawGame(int Index, int Round, IReadOnlyList<RawSide> Sides, int? WinnerTo, int? WinnerToSlot);
    private sealed record RawBracket(int FieldSize, int Rounds, IReadOnlyList<RawGame> Games);

    private static RawBracket ToRaw(KnockoutBracket b) => new(
        b.FieldSize, b.Rounds,
        b.Games.Select(g => new RawGame(g.GameIndex, g.Round,
                                        new[] { RawOf(g.A), RawOf(g.B) },
                                        g.WinnerTo, g.WinnerToSlot)).ToList());

    private static RawSide RawOf(KnockoutSide s) => s switch
    {
        KnockoutSide.Seed sd => new RawSide("seed", sd.Number),
        KnockoutSide.WinnerOf w => new RawSide("winner", w.GameIndex),
        _ => throw new InvalidOperationException("a third side shape exists; the union is no longer closed"),
    };

    private static RawSide S(int seed) => new("seed", seed);
    private static RawSide W(int game) => new("winner", game);
    private static RawSide L(int game) => new("loser", game);

    /// <summary>Build a raw bracket from opening pairs and later rounds given as side pairs;
    /// destinations are derived so the control isolates the rule it names.</summary>
    private static RawBracket Raw(int fieldSize, params (int Round, RawSide[] Sides)[] games)
    {
        var list = new List<RawGame>();
        for (var i = 0; i < games.Length; i++)
            list.Add(new RawGame(i, games[i].Round, games[i].Sides, null, null));
        // Destinations: the first later game that references W(i), if any.
        for (var i = 0; i < list.Count; i++)
        {
            int? to = null, slot = null;
            for (var j = 0; j < list.Count && to is null; j++)
                for (var k = 0; k < list[j].Sides.Count; k++)
                    if (list[j].Sides[k] is { Kind: "winner" } side && side.Ref == i) { to = j; slot = k; break; }
            list[i] = list[i] with { WinnerTo = to, WinnerToSlot = slot };
        }
        return new RawBracket(fieldSize, KnockoutRounds(fieldSize), list);
    }

    // ── The validator: twelve rules, ALL reported ──────────────────────────────

    /// <summary>Returns the set of rule numbers (2..12) that the raw bracket violates, with
    /// a message per hit. Rule 1 (stable identity) is a property of two builds, asserted
    /// directly. Every rule is evaluated independently so a control can prove it fired the
    /// rule it names and no lower one.</summary>
    private static SortedDictionary<int, string> KnockoutViolations(RawBracket b)
    {
        var v = new SortedDictionary<int, string>();
        void Hit(int rule, string msg) { if (!v.ContainsKey(rule)) v[rule] = msg; }
        var n = b.FieldSize;
        var games = b.Games;

        // 2 — exactly two sides, distinct.
        foreach (var g in games)
        {
            if (g.Sides.Count != 2)
                Hit(2, $"game {g.Index} has {g.Sides.Count} inputs; a basketball game has exactly two");
            else if (g.Sides[0] == g.Sides[1])
                Hit(2, $"game {g.Index} has the same input on both sides ({g.Sides[0].Kind} {g.Sides[0].Ref})");
        }

        // 3 — every side is a seed in range or the winner of a valid game.
        foreach (var g in games)
            foreach (var s in g.Sides)
            {
                var ok = s.Kind switch
                {
                    "seed" => s.Ref >= 1 && s.Ref <= n,
                    "winner" => s.Ref >= 0 && s.Ref < games.Count,
                    _ => false,
                };
                if (!ok) Hit(3, $"game {g.Index} takes '{s.Kind} {s.Ref}', which is neither a seed nor an earlier winner");
            }

        // 4 — no forward references.
        foreach (var g in games)
            foreach (var s in g.Sides)
                if (s.Kind is "winner" or "loser" && s.Ref >= g.Index)
                    Hit(4, $"game {g.Index} takes the result of game {s.Ref}, which has not been played");

        // 5 — no winner feeds two slots. (A non-final winner feeding ZERO slots is a second
        //     final, and rule 6 names it as such — so 5 and 6 together say "exactly one".)
        var feeds = new int[games.Count];
        foreach (var g in games) foreach (var s in g.Sides) if (s.Kind == "winner" && s.Ref >= 0 && s.Ref < games.Count) feeds[s.Ref]++;
        var finals = Enumerable.Range(0, games.Count).Where(i => feeds[i] == 0).ToList();
        for (var i = 0; i < games.Count; i++)
            if (feeds[i] > 1) Hit(5, $"the winner of game {i} is fed into {feeds[i]} later slots");

        // 6 — exactly one final, and it is the last game.
        if (finals.Count != 1)
            Hit(6, $"{finals.Count} games have an unfed winner (games {string.Join(",", finals)}); single elimination has exactly one final");
        else if (finals[0] != games.Count - 1)
            Hit(6, $"the only unfed game is {finals[0]}, but the final must be the last game");

        // Propagation: the seeds that can possibly occupy each game's slots.
        var possible = new List<HashSet<int>>[games.Count];
        for (var i = 0; i < games.Count; i++)
        {
            possible[i] = new List<HashSet<int>>();
            foreach (var s in games[i].Sides)
            {
                var set = new HashSet<int>();
                if (s.Kind == "seed") set.Add(s.Ref);
                else if (s.Ref >= 0 && s.Ref < i)
                    foreach (var slot in possible[s.Ref]) set.UnionWith(slot);
                possible[i].Add(set);
            }
        }
        HashSet<int> All(int i) { var u = new HashSet<int>(); foreach (var set in possible[i]) u.UnionWith(set); return u; }

        // 9 — every seed enters exactly one game, at its entry round (0).
        var entries = new Dictionary<int, List<int>>();
        for (var s = 1; s <= n; s++) entries[s] = new List<int>();
        foreach (var g in games) foreach (var s in g.Sides) if (s.Kind == "seed" && entries.ContainsKey(s.Ref)) entries[s.Ref].Add(g.Index);
        for (var s = 1; s <= n; s++)
        {
            if (entries[s].Count != 1) Hit(9, $"seed {s} enters {entries[s].Count} games; it must enter exactly one");
            else if (games[entries[s][0]].Round != KnockoutEntryRound(n, s))
                Hit(9, $"seed {s} enters at round {games[entries[s][0]].Round}, not its entry round {KnockoutEntryRound(n, s)}");
        }

        // 7 — no eliminated team re-enters: every game a seed can reach lies on the winner
        //     chain out of its single entry game.
        for (var s = 1; s <= n; s++)
        {
            if (entries[s].Count != 1) continue;
            var chain = new HashSet<int>();
            for (int? g = entries[s][0]; g is not null && chain.Add(g.Value); g = games[g.Value].WinnerTo) { }
            for (var i = 0; i < games.Count; i++)
                if (All(i).Contains(s) && !chain.Contains(i))
                { Hit(7, $"eliminated participant (seed {s}) re-enters at game {i}"); break; }
        }

        // 8 — no team on two live branches: a game's slots are disjoint.
        foreach (var g in games)
            if (possible[g.Index].Count == 2 && possible[g.Index][0].Overlaps(possible[g.Index][1]))
                Hit(8, $"game {g.Index} could see the same team on both sides " +
                       $"(seed {possible[g.Index][0].Intersect(possible[g.Index][1]).Min()})");

        // 10 — exactly one champion path and its length is the number of rounds: every
        //      root-to-seed chain through the final has depth == Rounds.
        if (finals.Count == 1)
        {
            var depths = new List<int>();
            void Walk(int i, int depth)
            {
                foreach (var s in games[i].Sides)
                    if (s.Kind == "winner" && s.Ref >= 0 && s.Ref < i) Walk(s.Ref, depth + 1);
                    else depths.Add(depth);
            }
            Walk(finals[0], 1);
            if (depths.Count == 0 || depths.Min() != b.Rounds || depths.Max() != b.Rounds)
                Hit(10, $"champion paths run {(depths.Count == 0 ? 0 : depths.Min())}..{(depths.Count == 0 ? 0 : depths.Max())} rounds; " +
                        $"every path must be exactly {b.Rounds}");
        }

        // 11 — N − 1 games.
        if (games.Count != n - 1)
            Hit(11, $"a field of {n} built {games.Count} games; single elimination is exactly {n - 1}");

        // 12 — THE CANONICAL LINE, and the fold. Openings are consecutive pairs down
        //      line(N); game j of round r takes W(2j) then W(2j+1) of round r−1.
        {
            var line = KnockoutSeedLine(n);
            var round0 = games.Where(g => g.Round == 0).OrderBy(g => g.Index).ToList();
            var flat = round0.SelectMany(g => g.Sides).ToList();
            var opensRight = round0.Count == n / 2
                && flat.Count == n
                && flat.Select((s, k) => s.Kind == "seed" && s.Ref == line[k]).All(x => x);
            if (!opensRight)
                Hit(12, "the opening round does not read down the canonical seed line " +
                        $"[{string.Join(",", line)}]; got [{string.Join(",", flat.Select(s => s.Kind == "seed" ? s.Ref.ToString(CultureInfo.InvariantCulture) : s.Kind))}]");
            else
            {
                var prev = round0.Select(g => g.Index).ToList();
                for (var r = 1; r < b.Rounds && !v.ContainsKey(12); r++)
                {
                    var thisRound = games.Where(g => g.Round == r).OrderBy(g => g.Index).ToList();
                    if (thisRound.Count != prev.Count / 2)
                    { Hit(12, $"round {r} has {thisRound.Count} games; the fold of {prev.Count} winners is {prev.Count / 2}"); break; }
                    for (var j = 0; j < thisRound.Count; j++)
                    {
                        var g = thisRound[j];
                        if (g.Sides.Count != 2 || g.Sides[0] != W(prev[2 * j]) || g.Sides[1] != W(prev[2 * j + 1]))
                        {
                            Hit(12, $"round {r} game {g.Index} is [{string.Join(" v ", g.Sides.Select(s => s.Kind[0] + s.Ref.ToString(CultureInfo.InvariantCulture)))}]; " +
                                    $"the fold says [w{prev[2 * j]} v w{prev[2 * j + 1]}] — regions or sides are miswired");
                            break;
                        }
                    }
                    prev = thisRound.Select(g => g.Index).ToList();
                }
            }
        }

        return v;
    }

    private static bool Phase98KnockoutCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 98 — S109: the single-elimination bracket primitive (dormant) and the " +
                          "city on the game record. Twelve invariants at every supported size, the canonical " +
                          "line literally, a negative control per hole, the input refusals, closure of the " +
                          "production types by reflection, both place boundaries with controls, the " +
                          "dormancy proof, and ★ ALL FIVE FINGERPRINTS UNMOVED ==");
        var pass = true;
        var assertions = 0;

        void Check(string name, bool ok, string detail = "")
        {
            assertions++;
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        string? Refusal(Action act)
        {
            try { act(); return null; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }

        static IReadOnlyList<int> Order(int n) => Enumerable.Range(1, n).Select(i => 1000 + i).ToList();

        try
        {
            // ════════════════════════════════════════════════════════════════════
            //  C1 — THE TWELVE INVARIANTS AT EVERY SUPPORTED SIZE.
            // ════════════════════════════════════════════════════════════════════
            foreach (var n in KnockoutSupportedFields)
            {
                var a = BuildKnockoutBracket(n, Order(n));
                var b = BuildKnockoutBracket(n, Order(n));
                Check($"C1.{n}.1: stable identity and order — two builds of {n} are structurally identical, game for game",
                      a.Games.Count == b.Games.Count && a.Games.Zip(b.Games).All(p => p.First == p.Second)
                      && a.ParticipantBySeed.SequenceEqual(b.ParticipantBySeed));
                var raw = ToRaw(a);
                var viol = KnockoutViolations(raw);
                for (var rule = 2; rule <= 12; rule++)
                {
                    var label = rule switch
                    {
                        2 => "every game has exactly two distinct inputs",
                        3 => "every input is a seed or an earlier winner",
                        4 => "no forward references",
                        5 => "every non-final winner has exactly one destination",
                        6 => "exactly one final and it is last",
                        7 => "★ no eliminated team re-enters (the discriminator against the consolation tables)",
                        8 => "no team occupies two live branches",
                        9 => "every seed enters exactly one game at round one",
                        10 => $"exactly one champion path, {a.Rounds} rounds long",
                        11 => $"game count is N − 1 = {n - 1}",
                        _ => "★ THE CANONICAL LINE and its fold, literally",
                    };
                    Check($"C1.{n}.{rule}: {label}", !viol.ContainsKey(rule), viol.TryGetValue(rule, out var m) ? m : "");
                }
            }

            // ════════════════════════════════════════════════════════════════════
            //  C2 — THE LINE ASSERTED LITERALLY, and the recursion at 16/32/64.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C2a: line(2) == [1,2]", KnockoutSeedLine(2).SequenceEqual(new[] { 1, 2 }));
                Check("C2b: line(4) == [1,4,2,3]", KnockoutSeedLine(4).SequenceEqual(new[] { 1, 4, 2, 3 }));
                Check("C2c: line(8) == [1,8,4,5,2,7,3,6]", KnockoutSeedLine(8).SequenceEqual(new[] { 1, 8, 4, 5, 2, 7, 3, 6 }));
                foreach (var n in new[] { 16, 32, 64 })
                {
                    var half = KnockoutSeedLine(n / 2);
                    var expect = half.SelectMany(s => new[] { s, n + 1 - s }).ToList();
                    var line = KnockoutSeedLine(n);
                    Check($"C2d: line({n}) is the recursion applied to line({n / 2}) — every seed present once, complements paired",
                          line.SequenceEqual(expect) && line.OrderBy(x => x).SequenceEqual(Enumerable.Range(1, n)),
                          $"opens {line[0]} v {line[1]}, …, ends {line[^2]} v {line[^1]}");
                }
                var eight = BuildKnockoutBracket(8, Order(8));
                HashSet<int> Region(int semi) => new(eight.Games.Where(g => g.Round == 0 && g.WinnerTo == semi)
                                                        .SelectMany(g => new[] { ((KnockoutSide.Seed)g.A).Number, ((KnockoutSide.Seed)g.B).Number }));
                Check("C2e: eight-team semifinal regions are exactly {1,8,4,5} and {2,7,3,6}",
                      Region(4).SetEquals(new[] { 1, 8, 4, 5 }) && Region(5).SetEquals(new[] { 2, 7, 3, 6 }));
                Check("C2f: seed 1 is on the top line and 1 and 2 can meet only in the final",
                      eight.Games[0].A is KnockoutSide.Seed { Number: 1 }
                      && eight.Games[6].A is KnockoutSide.WinnerOf { GameIndex: 4 }
                      && eight.Games[6].B is KnockoutSide.WinnerOf { GameIndex: 5 });
                Check("C2g: participant identities ride at their seed positions — seed 1 is the first participant, seed 8 the last",
                      eight.ParticipantOfSeed(1) == 1001 && eight.ParticipantOfSeed(8) == 1008);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C3 — ★ A NEGATIVE CONTROL PER HOLE. Each must fire the rule it names,
            //       and the topology controls must fire NOTHING lower — a control that
            //       trips an earlier rule exercises nothing.
            // ════════════════════════════════════════════════════════════════════
            {
                // Three strengths. "only": the named rule and no other — the topology controls
                // are built to this bar. "noLower": the named rule fires and nothing below it
                // does, so the control demonstrably reached the rule it names; higher rules may
                // follow as consequences. "fires": the named rule fires — used only where an
                // earlier rule is entailed by construction (a loser edge is also not-a-winner)
                // and the honest claim is that THIS rule also sees it, in its own words.
                void Control(string name, RawBracket raw, int rule, string strength = "only")
                {
                    var viol = KnockoutViolations(raw);
                    var fired = viol.ContainsKey(rule);
                    var lower = viol.Keys.Where(k => k < rule).ToList();
                    var ok = strength switch
                    {
                        "only" => fired && viol.Count == 1,
                        "noLower" => fired && lower.Count == 0,
                        _ => fired,
                    };
                    var bar = strength switch { "only" => " and by nothing else", "noLower" => ", with no lower rule tripped", _ => " (in its own words)" };
                    Check($"C3: {name} — REJECTED by rule {rule}{bar}", ok,
                          fired ? $"fired {string.Join(",", viol.Keys)}: {viol[rule]}" : $"rule {rule} DID NOT FIRE (fired: {string.Join(",", viol.Keys)})");
                }

                // The correct eight, raw, as the base the wrong ones are built from.
                RawBracket Eight(RawSide[] g0, RawSide[] g1, RawSide[] g2, RawSide[] g3, RawSide[] g4, RawSide[] g5, RawSide[] g6)
                    => Raw(8, (0, g0), (0, g1), (0, g2), (0, g3), (1, g4), (1, g5), (2, g6));

                var goodEight = Eight(new[] { S(1), S(8) }, new[] { S(4), S(5) }, new[] { S(2), S(7) }, new[] { S(3), S(6) },
                                      new[] { W(0), W(1) }, new[] { W(2), W(3) }, new[] { W(4), W(5) });
                Check("C3: the hand-built correct eight passes all twelve (the controls' base is clean)",
                      KnockoutViolations(goodEight).Count == 0);

                // ★ The prompt's four named controls.
                Control("1v2, 3v4, 5v6, 7v8 — a legal tree that opens down the wrong line",
                        Eight(new[] { S(1), S(2) }, new[] { S(3), S(4) }, new[] { S(5), S(6) }, new[] { S(7), S(8) },
                              new[] { W(0), W(1) }, new[] { W(2), W(3) }, new[] { W(4), W(5) }), 12);
                Control("region-swapped {1,8,3,6} / {2,7,4,5} — every opening correct, the fold wrong",
                        Eight(new[] { S(1), S(8) }, new[] { S(4), S(5) }, new[] { S(2), S(7) }, new[] { S(3), S(6) },
                              new[] { W(0), W(3) }, new[] { W(2), W(1) }, new[] { W(4), W(5) }), 12);
                Control("1 and 2 wired into a semifinal — openings correct, protected-round clause satisfied for the top four, still wrong",
                        Eight(new[] { S(1), S(8) }, new[] { S(4), S(5) }, new[] { S(2), S(7) }, new[] { S(3), S(6) },
                              new[] { W(0), W(2) }, new[] { W(1), W(3) }, new[] { W(4), W(5) }), 12);

                // ★ The existing consolation table, raw: losers re-enter. Rule 7 must fire.
                //   It legitimately also trips 3 (a loser edge is neither seed nor winner), 6
                //   (four placement finals) and 11 (twelve games) — those are reported, and
                //   the assertion is that 7 fires, which only the re-entry rule can say.
                var routes = BracketRoutesFor(8);
                var consolation = new RawBracket(8, 3, routes.Select(r =>
                {
                    var sides = new List<RawSide>();
                    if (r.Round == 0) { sides.Add(S(r.SeedA)); sides.Add(S(r.SeedB)); }
                    else
                        foreach (var src in routes.Where(x => x.WinnerToGame == r.GameIndex || x.LoserToGame == r.GameIndex)
                                                  .OrderBy(x => x.WinnerToGame == r.GameIndex ? x.WinnerToSlot : x.LoserToSlot))
                            sides.Add(src.WinnerToGame == r.GameIndex ? W(src.GameIndex) : L(src.GameIndex));
                    var winTo = r.WinnerToGame >= 0 ? r.WinnerToGame : (int?)null;
                    return new RawGame(r.GameIndex, r.Round, sides, winTo, winTo is null ? null : r.WinnerToSlot);
                }).ToList());
                Control("the existing BracketRoutes8 CONSOLATION table — a loser re-enters", consolation, 7, "fires");
                Check("C3: and rule 7 is what says so — 'eliminated participant re-enters' names the game",
                      KnockoutViolations(consolation).TryGetValue(7, out var seven) && seven.Contains("re-enters at game"),
                      seven ?? "");

                // One control per remaining structural rule.
                Control("a three-input node", Raw(4, (0, new[] { S(1), S(4) }), (0, new[] { S(2), S(3) }), (1, new[] { W(0), W(1), S(2) })), 2, "noLower");
                Control("a forward reference — a final that takes a game not yet played",
                        Raw(4, (0, new[] { S(1), S(4) }), (1, new[] { W(0), W(2) }), (0, new[] { S(2), S(3) })), 4, "noLower");
                Control("a winner fed into two later slots",
                        Raw(8, (0, new[] { S(1), S(8) }), (0, new[] { S(4), S(5) }), (0, new[] { S(2), S(7) }), (0, new[] { S(3), S(6) }),
                            (1, new[] { W(0), W(1) }), (1, new[] { W(0), W(3) }), (2, new[] { W(4), W(5) })), 5, "noLower");
                Control("two finals — a bracket that never converges",
                        Raw(4, (0, new[] { S(1), S(4) }), (0, new[] { S(2), S(3) })), 6, "noLower");
                Control("the same seed entering twice — two live branches",
                        Raw(4, (0, new[] { S(1), S(4) }), (0, new[] { S(2), S(1) }), (1, new[] { W(0), W(1) })), 8, "noLower");
                Control("a seed entering at round two (a bye) — refused by the entry-round rule",
                        Raw(4, (0, new[] { S(2), S(3) }), (1, new[] { S(1), W(0) })), 9, "noLower");
                Control("an unbalanced tree with everyone entering round one — champion paths of unequal length",
                        Raw(8, (0, new[] { S(1), S(8) }), (0, new[] { S(4), S(5) }), (0, new[] { S(2), S(7) }), (0, new[] { S(3), S(6) }),
                            (1, new[] { W(0), W(1) }), (2, new[] { W(4), W(2) }), (3, new[] { W(5), W(3) })), 10, "noLower");
                // Rule 11 cannot be isolated: any bracket short a game has a second final (6)
                // and any bracket with an extra game re-enters someone (9). The honest claim is
                // that 11 sees the short bracket in its own words, which is what locates it.
                Control("the wrong game count — a bracket that stops one game short",
                        Raw(8, (0, new[] { S(1), S(8) }), (0, new[] { S(4), S(5) }), (0, new[] { S(2), S(7) }), (0, new[] { S(3), S(6) }),
                            (1, new[] { W(0), W(1) }), (1, new[] { W(2), W(3) })), 11, "fires");
            }

            // ════════════════════════════════════════════════════════════════════
            //  C4 — ★ CLOSURE OF THE PRODUCTION TYPES, DEMONSTRATED. A loser edge
            //       and a three-input node are unconstructible; this goes red the
            //       day either becomes constructible.
            // ════════════════════════════════════════════════════════════════════
            {
                var sideType = typeof(KnockoutSide);
                var variants = sideType.Assembly.GetTypes().Where(t => t != sideType && sideType.IsAssignableFrom(t)).ToList();
                Check("C4a: the side union has exactly two variants — Seed and WinnerOf — and no LoserOf (discharges rules 3 and 7 by type)",
                      variants.Count == 2 && variants.All(t => t.Name is "Seed" or "WinnerOf") && variants.All(t => t.IsSealed),
                      string.Join(", ", variants.Select(t => t.Name)));
                // A record also carries a protected COPY constructor (the `with` machinery), which
                // only a derived type can reach and which cannot create a new shape; the creating
                // constructor is the private one and there is no public way in.
                var ctors = sideType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var creating = ctors.Where(c => c.GetParameters().Length == 0).ToList();
                Check("C4b: the union's creating constructor is private and it has no public constructor, so nothing outside can add a third shape",
                      creating.Count == 1 && creating[0].IsPrivate && ctors.All(c => !c.IsPublic),
                      $"{ctors.Length} constructors: {string.Join(", ", ctors.Select(c => (c.IsPrivate ? "private" : c.IsFamily ? "protected" : "other") + "(" + c.GetParameters().Length + ")"))}");
                var sideSlots = typeof(KnockoutGame).GetProperties().Where(p => p.PropertyType == sideType).ToList();
                Check("C4c: a game has exactly two side slots and no collection of sides (discharges rule 2 by type)",
                      sideSlots.Count == 2 && typeof(KnockoutGame).GetProperties().All(p => !typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) || p.PropertyType == typeof(string)),
                      string.Join(", ", sideSlots.Select(p => p.Name)));
                Check("C4d: a game has no loser destination at all — an eliminated team has nowhere to go",
                      typeof(KnockoutGame).GetProperties().All(p => !p.Name.Contains("Loser", StringComparison.OrdinalIgnoreCase)));
            }

            // ════════════════════════════════════════════════════════════════════
            //  C5 — THE REFUSALS, loud and by name.
            // ════════════════════════════════════════════════════════════════════
            {
                foreach (var n in new[] { 0, 1, 3, 5, 6, 9, 11, 12, 128 })
                {
                    var msg = Refusal(() => BuildKnockoutBracket(n, Order(Math.Max(n, 0))));
                    Check($"C5a: a field of {n} is refused by name",
                          msg is not null && msg.StartsWith("KNOCKOUT:") && msg.Contains($"field of {n} ") && msg.Contains("only 2, 4, 8, 16, 32 and 64"),
                          msg is null ? "NO REFUSAL" : "");
                }
                Check("C5b: the seed line itself refuses an unsupported size (nothing downstream can be reached with one)",
                      Refusal(() => KnockoutSeedLine(12)) is { } m12 && m12.Contains("field of 12"));

                var seven = Refusal(() => BuildKnockoutBracket(8, Order(7)));
                var nine = Refusal(() => BuildKnockoutBracket(8, Order(9)));
                Check("C5c: wrong cardinality, short — a field of 8 handed 7", seven is not null && seven.Contains("handed 7 participants"), seven ?? "NO REFUSAL");
                Check("C5d: wrong cardinality, long — a field of 8 handed 9", nine is not null && nine.Contains("handed 9 participants"), nine ?? "NO REFUSAL");
                var dup = Refusal(() => BuildKnockoutBracket(4, new[] { 1001, 1002, 1001, 1004 }));
                Check("C5e: ★ a duplicated participant is refused before any topology exists (invariant 8 would already be false)",
                      dup is not null && dup.Contains("participant 1001 appears twice") && dup.Contains("seed 3"), dup ?? "NO REFUSAL");
                var empty = Refusal(() => BuildKnockoutBracket(4, Array.Empty<int>()));
                Check("C5f: an empty order is refused, by its own message", empty is not null && empty.Contains("is empty"), empty ?? "NO REFUSAL");
                var nul = Refusal(() => BuildKnockoutBracket(4, null!));
                Check("C5g: ★ a NULL order is refused at runtime, not left to the annotation", nul is not null && nul.Contains("is null"), nul ?? "NO REFUSAL");
                Check("C5h: the refusals fire before any topology — an invalid order with a valid size produces no bracket",
                      Refusal(() => BuildKnockoutBracket(2, new[] { 5, 5 })) is not null);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C6 — THE PLACE. Both boundaries, each with a negative control, and the
            //       stock season carrying a city on every game that played.
            // ════════════════════════════════════════════════════════════════════
            string WorldPath(string file) => Path.Combine(AppContext.BaseDirectory, "worlds", file);
            var stock = LoadWorld(WorldPath("stock-d1.world.json"));
            var stockRun = RunSeasonCore(stock, KnockoutCheckSeed, configPath, verbose: false);
            {
                var placeOf = stock.Schools.ToDictionary(s => s.Id, s => s.PlaceId);
                var eventPlace = stock.Events.ToDictionary(e => e.Id, e => e.PlaceId);
                Check("C6a: boundary one — every game in the assembled league schedule carries a city",
                      stockRun.Schedule.All(g => g.PlaceId is > 0), $"{stockRun.Schedule.Count} games");
                Check("C6b: and it is the HOME school's city on every hosted game",
                      stockRun.Schedule.All(g => g.HasHost && g.PlaceId == placeOf[g.HomeId]));
                Check("C6c: boundary two — every game in the completed season result carries a city",
                      stockRun.PlayedGames.All(p => p.Game.PlaceId is > 0), $"{stockRun.PlayedGames.Count} games");
                Check("C6d: and every event game carries its EVENT's city, never a school's",
                      stockRun.PlayedGames.Where(p => p.IsEventGame).All(p => !p.Game.HasHost && p.Game.PlaceId == eventPlace[p.EventId!.Value]),
                      $"{stockRun.PlayedGames.Count(p => p.IsEventGame)} event games");

                var unresolved = new[] { new SeasonGame("conf", 1, 2, PlaceId: 7), new SeasonGame("conf", 3, 4) };
                var r1 = Refusal(() => AssertEveryGamePlaced(unresolved, "BuildSeasonSchedule"));
                var r2 = Refusal(() => AssertEveryGamePlaced(unresolved, "SeasonRunOutcome"));
                Check("C6e: negative control — boundary one rejects an unresolved place and names the game",
                      r1 is not null && r1.Contains("at BuildSeasonSchedule") && r1.Contains("game 1 (conf 3 v 4) has no city"), r1 ?? "NO REFUSAL");
                Check("C6f: negative control — boundary two rejects an unresolved place and names the game",
                      r2 is not null && r2.Contains("at SeasonRunOutcome") && r2.Contains("game 1"), r2 ?? "NO REFUSAL");
                var zero = Refusal(() => AssertEveryGamePlaced(new[] { new SeasonGame("conf", 1, 2, PlaceId: 0) }, "x"));
                Check("C6g: a zero place is as unresolved as a missing one (world ids are positive)", zero is not null);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C7 — ★ THE FINGERPRINT WALL, ALL FIVE, ASSERTED RATHER THAN TRUSTED.
            // ════════════════════════════════════════════════════════════════════
            {
                var resultsFp = SeasonFingerprint(stockRun.Results, stockRun.PossessionCounts);
                Check("C7a: #1 the conference schedule fingerprint is unmoved", stockRun.Fingerprint == MatchGoldenConferenceFp, stockRun.Fingerprint[..8] + "…");
                Check("C7b: #2 the conference DATED fingerprint is unmoved (the one the brief missed)", stockRun.DatedFingerprint == MatchGoldenDatedFp, stockRun.DatedFingerprint[..8] + "…");
                Check("C7c: #3 the event-games fingerprint is unmoved", stockRun.EventGamesFingerprint == MatchGoldenEventGamesFp, stockRun.EventGamesFingerprint[..8] + "…");
                Check("C7d: #4 the results+possessions fingerprint is unmoved", resultsFp == MatchGoldenResultsFp, resultsFp[..8] + "…");
                Check("C7e: #5 the non-conference DATED fingerprint is unmoved", stockRun.NonConferenceDates.DatedFingerprint == KnockoutGoldenNonConDatedFp,
                      stockRun.NonConferenceDates.DatedFingerprint[..8] + "…");
                Check("C7f: ★ and the place field is invisible to every hash BY CONSTRUCTION — the same schedule with every city rewritten hashes identically",
                      ScheduleFingerprint(stockRun.Schedule.Select(g => g with { PlaceId = 999999 }).ToList()) == stockRun.Fingerprint);
            }

            // ════════════════════════════════════════════════════════════════════
            //  C8 — ★ DORMANCY. The generator contributes zero games; every active
            //       consolation event still plays its full table.
            // ════════════════════════════════════════════════════════════════════
            {
                Check("C8a: conference games unchanged", stockRun.ConferenceGameCount == KnockoutGoldenConferenceGameCount, stockRun.ConferenceGameCount.ToString(CultureInfo.InvariantCulture));
                Check("C8b: event games unchanged at the pre-edit baseline", stockRun.TournamentGameCount == KnockoutGoldenEventGameCount,
                      $"{stockRun.TournamentGameCount} = {stockRun.TournamentGameCount - stockRun.ShowcaseGameCount} tournament + {stockRun.ShowcaseGameCount} showcase");
                var byEvent = stockRun.PlayedGames.Where(p => p.IsEventGame).GroupBy(p => p.EventId!.Value).ToDictionary(g => g.Key, g => g.Count());
                var events = stock.Events.ToDictionary(e => e.Id);
                var fours = byEvent.Where(kv => !events[kv.Key].IsShowcase && events[kv.Key].FieldSize == 4).ToList();
                var eights = byEvent.Where(kv => !events[kv.Key].IsShowcase && events[kv.Key].FieldSize == 8).ToList();
                var shows = byEvent.Where(kv => events[kv.Key].IsShowcase).ToList();
                Check("C8c: ★ every active FOUR-team event plays 4 games (a knockout would play 3)",
                      fours.Count > 0 && fours.All(kv => kv.Value == 4), $"{fours.Count} events");
                Check("C8d: ★ every active EIGHT-team event plays 12 games (a knockout would play 7)",
                      eights.Count > 0 && eights.All(kv => kv.Value == 12), $"{eights.Count} events");
                Check("C8e: every active showcase plays 2", shows.All(kv => kv.Value == 2), $"{shows.Count} events");
            }
        }
        catch (Exception ex)
        {
            Check("Phase 98 completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine(pass ? $"  Phase 98 PASS ({assertions} assertions)" : $"  Phase 98 FAIL ({assertions} assertions)");
        return pass;
    }
}
