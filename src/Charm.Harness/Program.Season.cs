using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Season — Pass 2 of the world-structure arc: the minimal season loop.
//
// A HARNESS-ONLY layer (no engine file changes). `season <world.json> <seed>`
// regenerates every school's divvied roster (world + seed — nothing persisted),
// builds a deterministic 30-game schedule (16 conference + 14 non-conference,
// neutral floors, exactly 15 home / 15 away), plays every game through the real
// engine via the extracted single-game body, and prints the standings page:
// all schools ranked by W-L, the prestige-band proof table, the overachievers
// with their leaked top-decile talent named, and the OT sanity pulse.
//
// THE SCHEDULE CONTRACT (mirrored bit-for-bit by the Python oracle,
// tools/schedule_oracle.py — the oracle's docstring is the authoritative spec;
// this header restates it):
//
//   RNG: WorldRng (SplitMix64) seeded with (seasonSeed ^ 0x5EA5C4ED) — the
//   schedule's own stream, decoupled from the divvy (the committed sample-sheet
//   XOR pattern). NextInt(n) = (int)(NextDouble() * n). Consumption order:
//   per construction attempt, one Fisher-Yates shuffle of ring positions
//   (n-1 draws, i = n-1 down to 1, j = NextInt(i+1)), then one draw per ACTUAL
//   conflict repair (the scan start offset) — stale queue entries consume
//   nothing. Conference slates and orientation consume NO randomness.
//
//   CONFERENCE SLATES (no RNG): conferences by id ascending, members by school
//   id ascending indexed 0..s-1. base = 16/(s-1); r = 16 - base*(s-1). The
//   extra-meeting graph is the canonical circulant on member indices: r even ->
//   offsets 1..r/2; r odd (s even, guaranteed by parity) -> offsets 1..(r-1)/2
//   plus the diameter matching (i, i+s/2). Emission order: for i in 0..s-2,
//   for j in i+1..s-1, emit (base + extra(i,j)) consecutive games (id_i, id_j).
//
//   NON-CONFERENCE (RNG): a 14-regular SIMPLE graph, no conference-mates.
//   Shuffle school indices into a ring; edges ring[i]—ring[(i+k)%n] for
//   i in 0..n-1, k in 1..7 (insertion order = the canonical edge-list order);
//   collect conference-mate conflicts in scan order (FIFO); repair each live
//   conflict (a,b) by a double-edge swap: start = NextInt(edgeCount), scan
//   forward, skip candidates sharing an endpoint, try rewiring R1 (a,c)+(b,d)
//   then R2 (a,d)+(b,c); legal iff both new pairs are non-mates and absent.
//   Apply the first legal one: slot of (a,b) <- first new edge, slot of (c,d)
//   <- second. If some conflict finds no legal swap across a full scan, the
//   ATTEMPT fails: construction restarts with a fresh shuffle drawn from the
//   same continuing RNG stream (nothing reseeded), up to 20 attempts; 20
//   failures -> fail loudly naming the last stuck pairing. (Oracle-measured:
//   stock always completes on attempt 1; the razor-tight fixture — 15 eligible,
//   14 needed — occasionally needs 2-3.) Edges stored (loId, hiId).
//
//   ORIENTATION (no RNG): the full multigraph (conf block then nonconf block,
//   game index ascending) has every degree 30 (even). Per component (components
//   by lowest school id, schools scanned id-ascending), iterative Hierholzer
//   with per-vertex adjacency in game-index order; each edge is oriented in its
//   consumption direction (from = HOME). A closed Eulerian circuit gives
//   out = in = 15 at every vertex: exactly 15 home / 15 away, always possible
//   because every degree is even. The road seam is 0 today (neutral floors),
//   but no school banks lopsided side assignments against the day it isn't.
//
//   FINGERPRINT: one record per game in schedule order (never re-sorted):
//   "{gameIndex}|{kind}|{homeSchoolId}|{awaySchoolId}\n", kind conf|nonconf,
//   UTF-8, SHA-256, lowercase hex. Printed on the page; asserted by Phase 55
//   against the oracle export at the fixed seed.
//
//   ENGINE SEEDS (no RNG; uniqueness asserted in Phase 55): base =
//   unchecked((int)seasonSeed) (the smoke sim's pattern); resolver =
//   base + 2*gameIndex, governor = base + 2*gameIndex + 1. Distinct within a
//   season by construction; the stride-2 scheme also keeps resolver and
//   governor seed sets disjoint (the committed gen runner's stride-1 shape lets
//   game i's governor seed equal game i+1's resolver seed — the season does not
//   inherit that wrinkle; `gen` itself is untouched behind the byte-for-byte
//   wall).
//
// THE HONEST WALL: the schedule is oracle-proven; game OUTCOMES are not
// oracle-mirrorable (SystemRng, no .NET in the sandbox) — they are proven by
// harness invariants (conservation, determinism, completeness) in Phase 55.
// The prestige-vs-wins climb is a page-level finding, never a suite assertion.
// ============================================================================

internal static partial class Program
{
    private const long SeasonScheduleXor = 0x5EA5C4ED;
    private const int SeasonNonConfAttempts = 20;

    private sealed record SeasonGame(string Kind, int HomeId, int AwayId);

    private sealed record SeasonGameResult(
        int HomeId, int AwayId, int HomeScore, int AwayScore, int OvertimePeriods);

    private sealed class SeasonRunOutcome
    {
        public required List<SeasonGame> Schedule { get; init; }
        public required string Fingerprint { get; init; }
        public required List<SeasonGameResult> Results { get; init; }
        public required Dictionary<int, int> Wins { get; init; }
        public required Dictionary<int, int> Losses { get; init; }
        public required DivvyResult Divvy { get; init; }
        public int Ties { get; init; }
    }

    private static int SeasonNextInt(WorldRng rng, int n) => (int)(rng.NextDouble() * n);

    // ── Preflight (necessary conditions; the construction is the final ATTEMPT) ──

    private static void SeasonPreflight(WorldFile world)
    {
        var schools = world.Schools.OrderBy(s => s.Id).ToList();
        var n = schools.Count;
        var confNames = world.Conferences.ToDictionary(c => c.Id, c => c.Name);
        var byConf = new Dictionary<int, List<int>>();
        foreach (var s in schools)
        {
            if (!byConf.TryGetValue(s.ConferenceId, out var list))
                byConf[s.ConferenceId] = list = new List<int>();
            list.Add(s.Id);
        }
        foreach (var cid in byConf.Keys.OrderBy(x => x))
        {
            var size = byConf[cid].Count;
            var name = confNames.TryGetValue(cid, out var nm) ? nm : $"conference {cid}";
            if (size < 2)
                throw new InvalidOperationException(
                    $"SEASON PREFLIGHT INFEASIBLE: conference '{name}' (id {cid}) has {size} school(s) — " +
                    $"a 16-game conference slate needs an opponent (s-1 = 0).");
            var baseMeet = 16 / (size - 1);
            var r = 16 - baseMeet * (size - 1);
            if ((r * size) % 2 == 1)
                throw new InvalidOperationException(
                    $"SEASON PREFLIGHT INFEASIBLE: conference '{name}' (id {cid}, size {size}) — " +
                    $"extra-meeting condition violated (r={r}, r*s odd; no r-regular graph exists).");
        }
        foreach (var s in schools)
        {
            var eligible = n - byConf[s.ConferenceId].Count;
            if (eligible < 14)
                throw new InvalidOperationException(
                    $"SEASON PREFLIGHT INFEASIBLE: school id {s.Id} (conference id {s.ConferenceId}) has only " +
                    $"{eligible} eligible non-conference opponents — 14 required.");
        }
        if ((n * 14) % 2 == 1)
            throw new InvalidOperationException(
                $"SEASON PREFLIGHT INFEASIBLE: total non-conference degree {n}*14 is odd.");
    }

    // ── Conference slates (deterministic, no RNG) ─────────────────────────────────

    private static List<(int Lo, int Hi)> BuildConferenceGames(Dictionary<int, List<int>> byConf)
    {
        var games = new List<(int, int)>();
        foreach (var cid in byConf.Keys.OrderBy(x => x))
        {
            var members = byConf[cid];   // sorted by school id (built from id-sorted scan)
            var s = members.Count;
            var baseMeet = 16 / (s - 1);
            var r = 16 - baseMeet * (s - 1);
            var extra = new HashSet<(int, int)>();
            if (r > 0)
            {
                int half; bool diameter;
                if (r % 2 == 0) { half = r / 2; diameter = false; }
                else { half = (r - 1) / 2; diameter = true; }   // r odd => s even (parity)
                for (var i = 0; i < s; i++)
                {
                    for (var k = 1; k <= half; k++)
                    {
                        var j = (i + k) % s;
                        extra.Add((Math.Min(i, j), Math.Max(i, j)));
                    }
                    if (diameter && i < s / 2) extra.Add((i, i + s / 2));
                }
            }
            for (var i = 0; i < s - 1; i++)
                for (var j = i + 1; j < s; j++)
                {
                    var m = baseMeet + (extra.Contains((i, j)) ? 1 : 0);
                    for (var t = 0; t < m; t++) games.Add((members[i], members[j]));
                }
        }
        return games;
    }

    // ── Non-conference slate (one attempt; the wrapper retries) ──────────────────

    private static bool TryBuildNonConferenceAttempt(
        List<WorldSchool> schools, WorldRng rng,
        out List<(int Lo, int Hi)> edgesOut, out string failure)
    {
        edgesOut = new List<(int, int)>();
        failure = "";
        var n = schools.Count;
        var ids = schools.Select(s => s.Id).ToArray();
        var conf = schools.ToDictionary(s => s.Id, s => s.ConferenceId);

        var ring = Enumerable.Range(0, n).ToArray();
        for (var i = n - 1; i >= 1; i--)
        {
            var j = SeasonNextInt(rng, i + 1);
            (ring[i], ring[j]) = (ring[j], ring[i]);
        }

        static (int, int) Norm(int a, int b) => a < b ? (a, b) : (b, a);

        var edges = new List<(int, int)>(7 * n);
        for (var i = 0; i < n; i++)
            for (var k = 1; k <= 7; k++)
                edges.Add(Norm(ids[ring[i]], ids[ring[(i + k) % n]]));

        var adj = new HashSet<(int, int)>(edges);
        if (adj.Count != edges.Count)
            throw new InvalidOperationException(
                "SEASON SCHEDULE BUG: circulant produced a duplicate edge.");
        var indexOf = new Dictionary<(int, int), int>(edges.Count);
        for (var i = 0; i < edges.Count; i++) indexOf[edges[i]] = i;

        var queue = edges.Where(e => conf[e.Item1] == conf[e.Item2]).ToList();

        foreach (var ab in queue)
        {
            if (!adj.Contains(ab)) continue;   // rewired away earlier; no RNG consumed
            var (a, b) = ab;
            var off = SeasonNextInt(rng, edges.Count);
            var repaired = false;
            for (var m = 0; m < edges.Count && !repaired; m++)
            {
                var cd = edges[(off + m) % edges.Count];
                var (cc, dd) = cd;
                if (cc == a || cc == b || dd == a || dd == b) continue;
                // R1: (a,c)+(b,d) then R2: (a,d)+(b,c) — first legal rewiring wins.
                foreach (var (n1, n2) in new[]
                         { ((a, cc), (b, dd)), ((a, dd), (b, cc)) })
                {
                    if (conf[n1.Item1] == conf[n1.Item2] || conf[n2.Item1] == conf[n2.Item2]) continue;
                    var p1 = Norm(n1.Item1, n1.Item2);
                    var p2 = Norm(n2.Item1, n2.Item2);
                    if (adj.Contains(p1) || adj.Contains(p2)) continue;
                    var iAb = indexOf[ab];
                    var iCd = indexOf[cd];
                    adj.Remove(ab); adj.Remove(cd);
                    adj.Add(p1); adj.Add(p2);
                    edges[iAb] = p1; edges[iCd] = p2;
                    indexOf.Remove(ab); indexOf.Remove(cd);
                    indexOf[p1] = iAb; indexOf[p2] = iCd;
                    repaired = true;
                    break;
                }
            }
            if (!repaired)
            {
                failure = $"non-conference repair found no legal swap for the " +
                          $"conference-mate pairing school {a} vs school {b}";
                return false;
            }
        }
        edgesOut = edges;
        return true;
    }

    private static List<(int Lo, int Hi)> BuildNonConferenceSlate(
        List<WorldSchool> schools, WorldRng rng, long seasonSeed)
    {
        var last = "";
        for (var attempt = 0; attempt < SeasonNonConfAttempts; attempt++)
            if (TryBuildNonConferenceAttempt(schools, rng, out var edges, out last))
                return edges;
        throw new InvalidOperationException(
            $"SEASON SCHEDULE BUILD FAILED at seed {seasonSeed}: {SeasonNonConfAttempts} construction " +
            $"attempts exhausted; last failure: {last}.");
    }

    // ── Orientation (Hierholzer; no RNG; out = in = 15 at every vertex) ──────────

    private static List<(int Home, int Away)> OrientSchedule(
        List<(int Lo, int Hi)> allGames, List<int> schoolIds)
    {
        var adj = schoolIds.ToDictionary(id => id, _ => new List<(int Nbr, int G)>());
        for (var g = 0; g < allGames.Count; g++)
        {
            var (x, y) = allGames[g];
            adj[x].Add((y, g));
            adj[y].Add((x, g));
        }
        var used = new bool[allGames.Count];
        var home = new int[allGames.Count];
        var ptr = schoolIds.ToDictionary(id => id, _ => 0);
        var visited = new HashSet<int>();
        foreach (var start in schoolIds)   // id ascending
        {
            if (visited.Contains(start) || adj[start].Count == 0) continue;
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var v = stack.Peek();
                visited.Add(v);
                var a = adj[v];
                while (ptr[v] < a.Count && used[a[ptr[v]].G]) ptr[v]++;
                if (ptr[v] == a.Count) { stack.Pop(); }
                else
                {
                    var (w, g) = a[ptr[v]];
                    used[g] = true;
                    home[g] = v;   // oriented in the consumption direction: from = HOME
                    stack.Push(w);
                }
            }
        }
        var oriented = new List<(int, int)>(allGames.Count);
        for (var g = 0; g < allGames.Count; g++)
        {
            var (x, y) = allGames[g];
            oriented.Add(home[g] == x ? (x, y) : (y, x));
        }
        return oriented;
    }

    // ── The schedule builder (preflight -> conf -> nonconf -> orient) ────────────

    private static List<SeasonGame> BuildSeasonSchedule(WorldFile world, long seasonSeed)
    {
        SeasonPreflight(world);
        var schools = world.Schools.OrderBy(s => s.Id).ToList();
        var byConf = new Dictionary<int, List<int>>();
        foreach (var s in schools)
        {
            if (!byConf.TryGetValue(s.ConferenceId, out var list))
                byConf[s.ConferenceId] = list = new List<int>();
            list.Add(s.Id);
        }
        var rng = new WorldRng(unchecked(seasonSeed ^ SeasonScheduleXor));
        var confGames = BuildConferenceGames(byConf);
        var nonconfGames = BuildNonConferenceSlate(schools, rng, seasonSeed);
        var all = new List<(int, int)>(confGames.Count + nonconfGames.Count);
        all.AddRange(confGames);
        all.AddRange(nonconfGames);
        var oriented = OrientSchedule(all, schools.Select(s => s.Id).ToList());
        var games = new List<SeasonGame>(all.Count);
        for (var g = 0; g < all.Count; g++)
            games.Add(new SeasonGame(
                g < confGames.Count ? "conf" : "nonconf", oriented[g].Item1, oriented[g].Item2));
        return games;
    }

    private static string ScheduleFingerprint(List<SeasonGame> games)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < games.Count; i++)
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(games[i].Kind).Append('|')
              .Append(games[i].HomeId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(games[i].AwayId.ToString(CultureInfo.InvariantCulture)).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── Season preparation: divvied rosters -> per-school depth rows, built once ──

    private static Dictionary<int, List<GenPlayerRow>> BuildSeasonRows(
        DivvyResult res, WorldFile world, bool verbose)
    {
        var rows = new Dictionary<int, List<GenPlayerRow>>();
        foreach (var s in world.Schools.OrderBy(x => x.Id))
        {
            var roster = res.Rosters[s.Id];
            var five = new HashSet<int>(BuildOpeningFive(roster, pid => res.Pool[pid].Pos));
            var list = roster.Select((pid, i) =>
            {
                var p = res.Pool[pid];
                return new GenPlayerRow(i + 1, p.Pos, p.Role, five.Contains(pid), p.LegCount,
                                        p.PlusLegs, p.Ratings, p.Player);
            }).ToList();
            if (verbose)
            {
                // Session 29.1 residual, kept visible: exactly one line per affected
                // side listing its absent positions (only W can currently be absent).
                var onFloor = new HashSet<string>(list.Where(r => r.Starter).Select(r => r.Pos));
                var absent = new[] { "G", "W", "B" }.Where(p => !onFloor.Contains(p)).ToList();
                if (absent.Count > 0)
                    Console.WriteLine($"  NOTE {s.Name}: no {string.Join("/", absent)} on the floor — " +
                                      $"benched {string.Join("/", absent)} cannot enter under the same-position fence.");
            }
            rows[s.Id] = list;
        }
        return rows;
    }

    private static GenSideData BuildSeasonSide(List<GenPlayerRow> rows, int idOffset)
    {
        var stamped = new Player[10];
        for (var i = 0; i < 10; i++) stamped[i] = StampPlayerId(rows[i].Player, rows[i].Slot + idOffset);
        return BuildGenSideData(rows, stamped);
    }

    // ── The season runner (shared by the CLI page and Phase 55) ───────────────────

    private static SeasonRunOutcome RunSeasonCore(
        WorldFile world, long seasonSeed, string engineConfigPath, bool verbose)
    {
        var schedule = BuildSeasonSchedule(world, seasonSeed);
        var fingerprint = ScheduleFingerprint(schedule);
        var divvy = RunDivvyDraft(world, seasonSeed);
        var rowsBySchool = BuildSeasonRows(divvy, world, verbose);
        var cfgs = LoadGenEngineConfigs(engineConfigPath);

        var wins = world.Schools.ToDictionary(s => s.Id, _ => 0);
        var losses = world.Schools.ToDictionary(s => s.Id, _ => 0);
        var results = new List<SeasonGameResult>(schedule.Count);
        var ties = 0;
        var baseSeed = unchecked((int)seasonSeed);

        for (var g = 0; g < schedule.Count; g++)
        {
            var sg = schedule[g];
            // HomeSchool -> the engine's Home side, AwaySchool -> Away: the §1b
            // invariant. Home rows stamp PlayerIds 1-10, away rows 11-20 (ids only
            // need uniqueness within a game; sides are stamped per matchup, never
            // cached across games where a school flips sides).
            var sideHome = BuildSeasonSide(rowsBySchool[sg.HomeId], 0);
            var sideAway = BuildSeasonSide(rowsBySchool[sg.AwayId], 10);
            var (game, result, _) = RunSingleGenGame(
                cfgs, sideHome, sideAway, TeamSide.Home, TeamSide.Away,
                resolverSeed: unchecked(baseSeed + 2 * g),
                governorSeed: unchecked(baseSeed + 2 * g + 1));

            // GameState.HomeScore is credited to HomeSchool, AwayScore to AwaySchool,
            // full stop (a flipped attribution passes conservation and determinism —
            // Phase 55's replay check exists to catch exactly that).
            results.Add(new SeasonGameResult(
                sg.HomeId, sg.AwayId, game.HomeScore, game.AwayScore, result.OvertimePeriods));
            if (game.HomeScore > game.AwayScore) { wins[sg.HomeId]++; losses[sg.AwayId]++; }
            else if (game.AwayScore > game.HomeScore) { wins[sg.AwayId]++; losses[sg.HomeId]++; }
            else ties++;   // assumption-1 says impossible; counted so it can never hide

            if (verbose && (g + 1) % 500 == 0)
                Console.WriteLine($"  ... {g + 1}/{schedule.Count} games played");
        }

        return new SeasonRunOutcome
        {
            Schedule = schedule, Fingerprint = fingerprint, Results = results,
            Wins = wins, Losses = losses, Divvy = divvy, Ties = ties,
        };
    }

    // ── The page ──────────────────────────────────────────────────────────────────

    private static readonly (int Lo, int Hi)[] SeasonBands =
        { (0, 19), (20, 39), (40, 59), (60, 79), (80, 99) };   // the divvy's exact five bands

    private static void RunSeason(string engineConfigPath, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage: season <world.json> <seed>");
            return;
        }
        if (!long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            Console.WriteLine($"SEASON ERROR: seed '{args[2]}' is not a valid integer.");
            return;
        }
        WorldFile world;
        try
        {
            world = LoadWorld(args[1]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.WriteLine($"SEASON ERROR: {ex.Message}");
            return;
        }

        List<SeasonGame> schedule;
        SeasonRunOutcome run;
        try
        {
            schedule = BuildSeasonSchedule(world, seed);   // preflight + build (fails loudly)
            Console.WriteLine("=== Project Charm :: Season (Pass 2: minimal season loop) ===");
            Console.WriteLine($"World: {args[1]} ({world.Schools.Count} schools, {world.Conferences.Count} conferences)");
            Console.WriteLine($"Season seed: {seed}");
            Console.WriteLine($"Schedule fingerprint: {ScheduleFingerprint(schedule)}");
            Console.WriteLine($"Schedule: {schedule.Count} games — 16 conference + 14 non-conference per team, " +
                              $"15 home / 15 away, neutral floors (the road seam is 0).");
            Console.WriteLine();
            Console.WriteLine($"Regenerating divvied rosters (world + seed; nothing persisted) and playing " +
                              $"{schedule.Count} real engine games ...");
            run = RunSeasonCore(world, seed, engineConfigPath, verbose: true);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"SEASON ERROR: {ex.Message}");
            return;
        }
        Console.WriteLine();

        var prestige = world.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
        var names = world.Schools.ToDictionary(s => s.Id, s => s.Name);
        var abbrs = world.Schools.ToDictionary(s => s.Id, s => s.Abbr.Trim());
        var confShort = world.Conferences.ToDictionary(c => c.Id, c => c.ShortName);
        var confOf = world.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);

        // (i) the full ranked table — every school by W-L, ties broken by school id.
        Console.WriteLine($"--- STANDINGS (all {world.Schools.Count}, ranked by W-L; ties broken by school id) ---");
        var ranked = world.Schools.Select(s => s.Id)
            .OrderByDescending(id => run.Wins[id]).ThenBy(id => id).ToList();
        Console.WriteLine($"  {"rk",-5}{"school",-26}{"conf",-10}{"pres",5}   W-L");
        for (var i = 0; i < ranked.Count; i++)
        {
            var id = ranked[i];
            Console.WriteLine($"  {i + 1,-5}{names[id] + " (" + abbrs[id] + ")",-26}" +
                              $"{confShort[confOf[id]],-10}{prestige[id],5}   {run.Wins[id]}-{run.Losses[id]}");
        }
        Console.WriteLine();

        // (ii) the proof table — average wins by prestige band (the divvy's bands).
        Console.WriteLine("--- PROOF TABLE (prestige buys access; does access buy wins?) ---");
        var bandAvg = new Dictionary<(int, int), double>();
        foreach (var band in SeasonBands)
        {
            var members = world.Schools.Where(s => s.CurrentPrestige >= band.Lo && s.CurrentPrestige <= band.Hi)
                                       .Select(s => s.Id).ToList();
            if (members.Count == 0) continue;
            var avg = members.Average(id => (double)run.Wins[id]);
            bandAvg[band] = avg;
            Console.WriteLine($"  prestige {band.Lo,2}-{band.Hi,-2}  avg wins {avg,5:F1}   (n={members.Count})");
        }
        Console.WriteLine();

        // (iii) the escapes — top 10 by wins over band average, leaked talent named.
        Console.WriteLine("--- ESCAPES (top 10 by wins over band average; leaked top-decile talent named) ---");
        var ranks = run.Divvy.Pool.Select(p => p.ScoutRank).ToArray();
        var topDecile = new HashSet<int>(
            Enumerable.Range(0, ranks.Length).OrderByDescending(i => ranks[i]).Take(ranks.Length / 10));
        (int, int) BandOf(int p) => SeasonBands.First(b => p >= b.Lo && p <= b.Hi);
        var escapes = world.Schools
            .Select(s => (s.Id, Dev: run.Wins[s.Id] - bandAvg[BandOf(s.CurrentPrestige)]))
            .OrderByDescending(t => t.Dev).ThenBy(t => t.Id).Take(10);
        foreach (var (id, dev) in escapes)
        {
            var leaked = run.Divvy.Rosters[id].Where(pid => topDecile.Contains(pid)).Select(pid =>
            {
                var p = run.Divvy.Pool[pid];
                var tier = p.LegCount == 3 ? "three-leg"
                         : p.LegCount == 2 ? $"two-leg {p.GradientTier}" : "one-leg";
                return $"{p.Pos} {p.Role} (pool #{pid}, rank {p.ScoutRank:F1}, {tier})";
            }).ToList();
            var band = BandOf(prestige[id]);
            Console.WriteLine($"  {names[id]} (prestige {prestige[id]}, band {band.Item1}-{band.Item2}): " +
                              $"{run.Wins[id]}-{run.Losses[id]}, {dev:+0.0;-0.0} over band | " +
                              (leaked.Count > 0 ? "leaks: " + string.Join("; ", leaked)
                                                : "no top-decile players (overperformed without a leaked star)"));
        }
        Console.WriteLine();

        // (iv) the OT / scoring sanity pulse.
        var otGames = run.Results.Count(r => r.OvertimePeriods > 0);
        var otPeriods = run.Results.Sum(r => r.OvertimePeriods);
        var avgTotal = run.Results.Average(r => (double)(r.HomeScore + r.AwayScore));
        Console.WriteLine($"--- SANITY PULSE ---");
        Console.WriteLine($"  OT: {otGames} of {run.Results.Count} games needed overtime " +
                          $"({otPeriods} OT periods total); average total score {avgTotal:F1}" +
                          (run.Ties > 0 ? $"; ANOMALY: {run.Ties} unresolved ties" : ""));
    }
}
