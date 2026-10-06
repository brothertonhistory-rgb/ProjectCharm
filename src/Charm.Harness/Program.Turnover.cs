using Charm.Engine;
using System.Globalization;

namespace Charm.Harness;

// ============================================================================
//  Session 114 — THE TURNOVER, IN MEMORY.
//
//  A finished season's rosters become next season's: every senior leaves, every
//  other player advances one class and keeps his place in the school's acquisition
//  order, and a freshman class EXACTLY the size of the departures — position for
//  position — is handed out by prestige through the same draft loop the bootstrap
//  divvy uses, started from each school's vacancies.
//
//  Emmett's rulings (2026-10-04/05), settled:
//    1. departures are replaced position for position, no roster is ever short;
//    2. the freshman class is the same crop as the starting pool (same generator,
//       same scholarship line);
//    3. the freshman pool is exact — every freshman lands somewhere;
//    4. freshmen play if they are better (minutes are owned by rank within position;
//       the tipoff five stays the rank-blind walk — O-6);
//    5. prestige is frozen between seasons;
//    7. class is independent of talent — nothing here reads a rating to decide who
//       leaves or arrives.
//
//  Nothing is saved: the whole thing lives inside one `seasons` command. A returner
//  is the same pool row `with` its class advanced and a new index, so Player,
//  Ratings, Role, DefensivePlane, OffensiveRole and ScoutRank are the same objects
//  and values (Phase 104 C2). Position rides on the row — the season-two pool is NOT
//  block-ordered by index, and nothing season-side reads position from an index.
// ============================================================================

internal static partial class Program
{
    /// <summary>One school's line of the turnover report: who left and who arrived, by position.</summary>
    private sealed record TurnoverSchoolLine(int SchoolId, int LeftG, int LeftW, int LeftB, int ArrivedG, int ArrivedW, int ArrivedB)
    {
        public int Left => LeftG + LeftW + LeftB;
        public int Arrived => ArrivedG + ArrivedW + ArrivedB;
    }

    /// <summary>The turnover's whole output: season two's rosters, plus what the report prints.</summary>
    private sealed class TurnoverResult
    {
        public required DivvyResult SeasonTwo { get; init; }
        /// <summary>The freshman class as drafted — the rows of <see cref="SeasonTwo"/>'s pool from
        /// <see cref="ReturnerCount"/> onward, in the order they were generated.</summary>
        public required List<PoolPlayer> Freshmen { get; init; }
        public required int ReturnerCount { get; init; }
        public required List<TurnoverSchoolLine> Lines { get; init; }
        /// <summary>Schools whose returners held no lead guard / no wing defender — the freshman
        /// draft had to supply one.</summary>
        public required int SchoolsNeedingLead { get; init; }
        public required int SchoolsNeedingTdw { get; init; }
        public required int FreshmanLeadTarget { get; init; }
        public required int FreshmanTdwTarget { get; init; }
    }

    /// <summary>Fr→So→Jr→Sr. A senior has no next class; he has left (callers filter first).</summary>
    private static ClassYear NextClass(ClassYear c) => c switch
    {
        ClassYear.Fr => ClassYear.So,
        ClassYear.So => ClassYear.Jr,
        ClassYear.Jr => ClassYear.Sr,
        _ => throw new InvalidOperationException("TURNOVER: a senior does not advance — he leaves."),
    };

    /// <summary>★ Protected-role density for the freshman class: the bootstrap target scaled to the
    /// class's own position count, rounded half up. Fixed before the preflight runs.</summary>
    private static int FreshmanRoleTarget(int bootstrapTarget, int classCount, int bootstrapCount)
        => bootstrapCount == 0 ? 0
         : (int)Math.Floor(bootstrapTarget * (double)classCount / bootstrapCount + 0.5);

    /// <summary>★ The freshman preflight. Positional quotas hold by construction (the class is cut
    /// to the vacancies); what the source cannot promise is that the class carries enough lead
    /// guards and wing defenders for every school whose returners lost theirs. Refused by name.</summary>
    private static void ValidateFreshmanClass(IReadOnlyList<PoolPlayer> freshmen, int needLeadSchools, int needTdwSchools)
    {
        var lead = freshmen.Count(p => GenLeadRoles.Contains(p.Role));
        var tdw = freshmen.Count(p => p.Role == GenWingDefenderRole);
        if (lead < needLeadSchools)
            throw new InvalidOperationException(
                $"TURNOVER INFEASIBLE: the freshman class carries {lead} lead guard(s) but {needLeadSchools} " +
                $"school(s) lost every lead guard they had.");
        if (tdw < needTdwSchools)
            throw new InvalidOperationException(
                $"TURNOVER INFEASIBLE: the freshman class carries {tdw} wing defender(s) but {needTdwSchools} " +
                $"school(s) lost every wing defender they had.");
    }

    /// <summary>★ 3a — the turnover. A pure function of (world, season one's rosters, season-two seed).</summary>
    private static TurnoverResult RunTurnover(WorldFile world, DivvyResult seasonOne, long seasonTwoSeed)
    {
        var n = world.Schools.Count;
        var pool1 = seasonOne.Pool;
        var schools = world.Schools.OrderBy(s => s.Id).ToList();

        // ── Departures and returners. Returners keep their acquisition order; the
        //    season-two pool is returners first, in school order, then the freshmen. ──
        var pool2 = new List<PoolPlayer>(pool1.Count);
        var rosters2 = schools.ToDictionary(s => s.Id, _ => new List<int>());
        var caps = new Dictionary<int, Dictionary<string, int>>(n);
        var needLead = new Dictionary<int, bool>(n);
        var needTdw = new Dictionary<int, bool>(n);
        var left = new Dictionary<int, (int G, int W, int B)>(n);
        foreach (var s in schools)
        {
            int lg = 0, lw = 0, lb = 0;
            var holdsLead = false; var holdsTdw = false;
            foreach (var pid in seasonOne.Rosters[s.Id])
            {
                var p = pool1[pid];
                if (p.Class == ClassYear.Sr)
                {
                    if (p.Pos == "G") lg++; else if (p.Pos == "W") lw++; else lb++;
                    continue;
                }
                var row = p with { PoolId = pool2.Count, Class = NextClass(p.Class) };
                pool2.Add(row);
                rosters2[s.Id].Add(row.PoolId);
                holdsLead |= GenLeadRoles.Contains(row.Role);
                holdsTdw |= row.Role == GenWingDefenderRole;
            }
            left[s.Id] = (lg, lw, lb);
            caps[s.Id] = new Dictionary<string, int>
            {
                [PositionalEligibility.Guard] = lg,
                [PositionalEligibility.Wing]  = lw,
                [PositionalEligibility.Big]   = lb,
            };
            needLead[s.Id] = !holdsLead;
            needTdw[s.Id] = !holdsTdw;
        }
        var returnerCount = pool2.Count;
        var gG = left.Values.Sum(x => x.G);
        var gW = left.Values.Sum(x => x.W);
        var gB = left.Values.Sum(x => x.B);
        var F = gG + gW + gB;

        // ── The freshman class: the same crop, cut to exactly the vacancies (rulings 2, 3).
        //    Same generator, same scholarship line, cohort seed by the bootstrap's own xor
        //    rule from the season-two seed. Position by defensive-plane rank at the CLASS's
        //    quotas (A4) — never from an index. ──────────────────────────────────────────
        var cohortSeed = unchecked((int)(seasonTwoSeed ^ DivvyCohortSeedXor));
        var cohort = F == 0 ? Array.Empty<PlayerGenPass3Live.LivePlayer>() : BuildRecruitedCohort(cohortSeed, F, 2 * F);
        var order = Enumerable.Range(0, F).OrderBy(i => cohort[i].Result.DPlane).ThenBy(i => i).ToArray();
        var fpos = new string[F];
        var cards = new Dictionary<string, int>[F];
        var players = new Player[F];
        for (var k = 0; k < F; k++)
        {
            fpos[k] = k < gG ? PositionalEligibility.Guard : k < gG + gW ? PositionalEligibility.Wing : PositionalEligibility.Big;
            var v = new Dictionary<string, int>(cohort[order[k]].Result.Card, StringComparer.Ordinal);
            DeriveAndStampTendencies(v);
            // The name is a label, not an identity (S89's person number is history-mode only;
            // cross-season identity is session 3's design). Returners keep their season-one
            // names ("Pool_<old id>"), so a freshman is numbered PAST the whole season-one pool —
            // otherwise a freshman named for his new index would collide with a returner who
            // held that index last season, and the S77 same-name guard refuses a school with two.
            players[k] = GenMapToPlayer(v, $"Pool_{pool1.Count + k}");
            var errs = players[k].Validate();
            if (errs.Count > 0)
                throw new InvalidOperationException(
                    $"turnover bridge bug — freshman {k} failed Player.Validate():\n  " + string.Join("\n  ", errs));
            cards[k] = v;
        }

        // Protected roles by rank within position at the bootstrap density, the integers fixed first.
        var leadTarget = FreshmanRoleTarget(DivvyLeadRoleTarget(n), gG, RosterShape.GuardCount(n));
        var tdwTarget = FreshmanRoleTarget(DivvyTdwRoleTarget(n), gW, RosterShape.WingCount(n));
        var leadSet = new HashSet<int>(
            Enumerable.Range(0, gG)
                .OrderByDescending(k => cards[k]["BallHandling"] + cards[k]["Playmaking"]).ThenBy(k => k)
                .Take(leadTarget));
        var tdwSet = new HashSet<int>(
            Enumerable.Range(gG, gW)
                .OrderByDescending(k => cards[k]["PerimeterDefense"]).ThenBy(k => k)
                .Take(tdwTarget));

        var freshmen = new List<PoolPlayer>(F);
        for (var k = 0; k < F; k++)
        {
            var v = cards[k];
            var role = DivvyRoleFor(fpos[k], leadSet.Contains(k), tdwSet.Contains(k), v);
            // ★ Class = Fr BY CONSTRUCTION — never InitialPlayerClass, which is the bootstrap draw.
            freshmen.Add(new PoolPlayer(returnerCount + k, fpos[k], role,
                cohort[order[k]].Result.DPlane, cohort[order[k]].Result.Role,
                v, players[k], DivvyScoutRank(v, fpos[k]), ClassYear.Fr));
        }

        var needLeadSchools = needLead.Count(kv => kv.Value);
        var needTdwSchools = needTdw.Count(kv => kv.Value);
        ValidateFreshmanClass(freshmen, needLeadSchools, needTdwSchools);

        // ── The freshman draft: the generalized loop, started from the vacancies. ──
        var draft = RunDraftLoop(world, freshmen, seasonTwoSeed, caps, needLead, needTdw, rosters2, personIds: null);
        pool2.AddRange(freshmen);

        var lines = new List<TurnoverSchoolLine>(n);
        foreach (var s in schools)
        {
            var arrived = rosters2[s.Id].Skip(rosters2[s.Id].Count - left[s.Id].G - left[s.Id].W - left[s.Id].B)
                                        .Select(pid => pool2[pid]).ToList();
            lines.Add(new TurnoverSchoolLine(s.Id, left[s.Id].G, left[s.Id].W, left[s.Id].B,
                arrived.Count(p => p.Pos == "G"), arrived.Count(p => p.Pos == "W"), arrived.Count(p => p.Pos == "B")));
        }

        var seasonTwo = new DivvyResult
        {
            Pool = pool2, Rosters = rosters2, Picks = draft.Picks, NoiseScale = draft.NoiseScale,
            MinSlackLead = draft.MinSlackLead, MinSlackTdw = draft.MinSlackTdw, PersonIds = null,
        };
        var result = new TurnoverResult
        {
            SeasonTwo = seasonTwo, Freshmen = freshmen, ReturnerCount = returnerCount, Lines = lines,
            SchoolsNeedingLead = needLeadSchools, SchoolsNeedingTdw = needTdwSchools,
            FreshmanLeadTarget = leadTarget, FreshmanTdwTarget = tdwTarget,
        };
        ValidateTurnover(world, seasonOne, result);
        return result;
    }

    /// <summary>★ The turnover's own contract, refused by name — run on every real turnover and
    /// fed the Phase 104 C11 negative controls: every senior left and nobody else did, every
    /// returner is the same man one class on, every freshman is a freshman, and every roster is
    /// full at the roster shape with its coverage intact.</summary>
    private static void ValidateTurnover(WorldFile world, DivvyResult seasonOne, TurnoverResult t)
    {
        var pool1 = seasonOne.Pool; var pool2 = t.SeasonTwo.Pool;
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        if (pool2.Count != pool1.Count)
            throw new InvalidOperationException($"TURNOVER: pool size moved {pool1.Count} -> {pool2.Count}; replacement is exact.");
        for (var i = 0; i < pool2.Count; i++)
            if (pool2[i].PoolId != i)
                throw new InvalidOperationException($"TURNOVER: season-two pool is not dense (row {i} carries id {pool2[i].PoolId}).");
        var seniorsLeft = pool1.Count(p => p.Class == ClassYear.Sr);
        if (t.ReturnerCount != pool1.Count - seniorsLeft)
            throw new InvalidOperationException($"TURNOVER: {t.ReturnerCount} returners for {pool1.Count - seniorsLeft} non-seniors.");
        // Every senior left: no season-two row is a season-one senior (the same man, by reference).
        var seniors = new HashSet<Player>(pool1.Where(p => p.Class == ClassYear.Sr).Select(p => p.Player), ReferenceEqualityComparer.Instance);
        foreach (var row in pool2)
            if (seniors.Contains(row.Player))
                throw new InvalidOperationException(Inv($"TURNOVER: a senior stayed — season-two pool #{row.PoolId} ({row.Pos}, rank {row.ScoutRank:F1}) is a season-one senior."));
        foreach (var s in world.Schools)
        {
            var r1 = seasonOne.Rosters[s.Id]; var r2 = t.SeasonTwo.Rosters[s.Id];
            if (r2.Count != RosterShape.Size)
                throw new InvalidOperationException($"TURNOVER: school {s.Id} has {r2.Count} players after the turnover, not {RosterShape.Size}.");
            var ret1 = r1.Where(pid => pool1[pid].Class != ClassYear.Sr).Select(pid => pool1[pid]).ToList();
            var ret2 = r2.Take(ret1.Count).Select(pid => pool2[pid]).ToList();
            for (var i = 0; i < ret1.Count; i++)
            {
                var a = ret1[i]; var b = ret2[i];
                if (!ReferenceEquals(a.Player, b.Player) || a.Pos != b.Pos || a.Role != b.Role)
                    throw new InvalidOperationException($"TURNOVER: school {s.Id} returner #{i + 1} is not the same man.");
                if (b.Class != NextClass(a.Class))
                    throw new InvalidOperationException($"TURNOVER: school {s.Id} returner #{i + 1} went {a.Class} -> {b.Class}.");
            }
            foreach (var pid in r2.Skip(ret1.Count))
            {
                var f = pool2[pid];
                if (pid < t.ReturnerCount)
                    throw new InvalidOperationException($"TURNOVER: school {s.Id} drafted a returner (pool #{pid}) as a freshman.");
                if (f.Class != ClassYear.Fr)
                    throw new InvalidOperationException($"TURNOVER: a freshman is not Fr — pool #{pid} is {f.Class} (classed through the bootstrap draw?).");
            }
            foreach (var pos in new[] { "G", "W", "B" })
            {
                var want = pos == "G" ? RosterShape.Guards : pos == "W" ? RosterShape.Wings : RosterShape.Bigs;
                var have = r2.Count(pid => pool2[pid].Pos == pos);
                if (have != want)
                    throw new InvalidOperationException($"TURNOVER: school {s.Id} holds {have} {pos}, not {want}, after the turnover.");
            }
            if (!r2.Any(pid => GenLeadRoles.Contains(pool2[pid].Role)))
                throw new InvalidOperationException($"TURNOVER: school {s.Id} has no lead guard after the turnover.");
            if (!r2.Any(pid => pool2[pid].Role == GenWingDefenderRole))
                throw new InvalidOperationException($"TURNOVER: school {s.Id} has no wing defender after the turnover.");
        }
        var drafted = t.SeasonTwo.Rosters.Values.SelectMany(r => r).ToList();
        if (drafted.Count != pool2.Count || drafted.Distinct().Count() != pool2.Count)
            throw new InvalidOperationException($"TURNOVER: {drafted.Distinct().Count()} distinct people on rosters for a pool of {pool2.Count}.");
    }

    // ── The report (ruling 6): per school who left / who arrived by position; league
    //    totals; the season-two class census. Page-only. ──────────────────────────────
    private static void PrintTurnoverReport(WorldFile world, DivvyResult seasonOne, TurnoverResult t)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        var names = world.Schools.ToDictionary(s => s.Id, s => s.Name);
        var abbrs = world.Schools.ToDictionary(s => s.Id, s => s.Abbr.Trim());
        Console.WriteLine("=== THE TURNOVER (seniors leave, everyone else advances a class, freshmen arrive position for position) ===");
        Console.WriteLine($"  {"school",-30}{"left G/W/B",-14}{"arrived G/W/B",-16}note");
        foreach (var l in t.Lines)
        {
            var note = l.Left == 0 ? "nobody left, nobody arrived" : "";
            Console.WriteLine($"  {names[l.SchoolId] + " (" + abbrs[l.SchoolId] + ")",-30}" +
                              $"{$"{l.LeftG}/{l.LeftW}/{l.LeftB}",-14}{$"{l.ArrivedG}/{l.ArrivedW}/{l.ArrivedB}",-16}{note}");
        }
        var lg = t.Lines.Sum(l => l.LeftG); var lw = t.Lines.Sum(l => l.LeftW); var lb = t.Lines.Sum(l => l.LeftB);
        Console.WriteLine($"  league: {lg + lw + lb} seniors left ({lg} G, {lw} W, {lb} B); {t.Freshmen.Count} freshmen arrived " +
                          $"({t.Freshmen.Count(p => p.Pos == "G")} G, {t.Freshmen.Count(p => p.Pos == "W")} W, {t.Freshmen.Count(p => p.Pos == "B")} B); " +
                          $"{t.Lines.Count(l => l.Left == 0)} school(s) lost nobody");
        Console.WriteLine($"  coverage: {t.SchoolsNeedingLead} school(s) needed a lead guard from the class and " +
                          $"{t.SchoolsNeedingTdw} a wing defender; the class carries " +
                          $"{t.Freshmen.Count(p => GenLeadRoles.Contains(p.Role))} lead guards (target {t.FreshmanLeadTarget}) and " +
                          $"{t.Freshmen.Count(p => p.Role == GenWingDefenderRole)} wing defenders (target {t.FreshmanTdwTarget})");
        var ci = CultureInfo.InvariantCulture;
        foreach (var pos in new[] { "G", "W", "B" })
        {
            var b = seasonOne.Pool.Where(p => p.Pos == pos).Select(p => p.ScoutRank).ToList();
            var f = t.Freshmen.Where(p => p.Pos == pos).Select(p => p.ScoutRank).ToList();
            if (f.Count < 2) continue;
            Console.WriteLine(string.Format(ci,
                "  scout rank {0}: freshmen mean {1:F1} sd {2:F1} (n={3}) vs the starting pool {4:F1} sd {5:F1}",
                pos, f.Average(), SampleSd(f), f.Count, b.Average(), SampleSd(b)));
        }
        var pool2 = t.SeasonTwo.Pool;
        Console.WriteLine(Inv($"  season-two classes (pool of {pool2.Count}): ") + string.Join("  ",
            new[] { ClassYear.Fr, ClassYear.So, ClassYear.Jr, ClassYear.Sr }
                .Select(c => $"{c} {pool2.Count(p => p.Class == c)}")));
        Console.WriteLine();
    }

    private static double SampleSd(IReadOnlyList<double> xs)
    {
        if (xs.Count < 2) return 0.0;
        var m = xs.Average();
        return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / (xs.Count - 1));
    }
}
