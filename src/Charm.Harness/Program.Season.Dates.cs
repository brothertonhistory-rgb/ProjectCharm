using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  SESSION 94 — DATES FOR THE CONFERENCE SLATE
//
//  Locked contract: tools/schedule_oracle.py (the S94 section). That file is the
//  spec; this is the port, and Phase 85 asserts golden parity on the dated
//  fingerprints — exact, integers and dates, no tolerance.
//
//  THE MODEL (Emmett, 2026-08-02, LOOSE over tight on real Big East evidence):
//    * Three authored numbers a league: games, weeks, and the day its tournament
//      opens (days before Selection Sunday; null = none, walling at Selection
//      Sunday itself). wall = SelectionSunday - offset - 1.
//    * The week is MONDAY TO SUNDAY. ★ S105.2: a team plays AT MOST ONE weekday
//      (Mon-Fri) game and AT MOST ONE weekend (Sat-Sun) game in it — two ceilings
//      of one, which subsume the original never-three-in-a-week rule. A ceiling,
//      not a quota: zero of either is legal.
//    * The window's final week is the LATEST week ALL of whose active nights
//      fall on or before the wall — the real Big East finished Sat Mar 7
//      against a Tue Mar 10 wall, resting into its tournament; never a partial
//      week. The window is that week plus weeks-1 playing weeks before it,
//      skipping the Mon-Sun week containing December 25 (quiet, R10).
//    * Weekly totals are EXACT: base, extra = divmod(n*G/2, weeks); the LAST
//      `extra` playing weeks carry base+1 (the real league opens light).
//    * Active nights: even {D1,D2}, odd {D1,D2,D3}; a date seats at most
//      floor(n/2) games; dates fill in authored priority as a candidate order
//      inside backtracking. Capacity theorem (r13): valid nights make a
//      complete week seat exactly n, and weeks >= G/2 keeps every target <= n —
//      asserted internally; a failure indicts this file, never the world.
//    * ★ THE COMPLETED DATED WEEK IS THE ATOMIC UNIT OF CHRONOLOGICAL
//      EVALUATION (r14/r15): within-week placements test only week-stable
//      facts; rematch non-adjacency and quarter separation run on the week
//      sorted by real date and tentatively appended.
//    * The rotation supplies the spacing: base meetings ride circle-method
//      rounds; each extra meeting is interleaved half a rotation from its
//      pair's base position, so a doubled opponent's two meetings sit about
//      half a season apart BY CONSTRUCTION — zero same-quarter collisions on
//      the whole stock world falls out rather than being searched for.
//    * S93 emission order is the deterministic tie-break. No RNG anywhere.
//
//  Verdicts extend S93's: authored-data faults and the two-sided week bound are
//  InvalidConfiguration (thrown here as InvalidOperationException with the
//  SEASON PREFLIGHT prefix when reached through the whole-world path);
//  a wedged construction is SearchBudgetExhausted semantics — it proves
//  nothing — and no such wedge exists on any committed world.
// ============================================================================

internal static partial class Program
{
    /// <summary>The season being played. Stored, never hardcoded (R14): the CLI may
    /// override it, and C13 proves a different year yields the same structure with
    /// different dates.</summary>
    private const int SeasonDefaultStartYear = 2026;

    private static readonly string[] SeasonWeekdayNames =
        { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };

    private static int? SeasonWeekdayOf(string night)
    {
        var w = (night ?? "").Trim().ToLowerInvariant();
        var i = Array.IndexOf(SeasonWeekdayNames, w);
        return i < 0 ? null : i;                       // Monday = 0 .. Sunday = 6
    }

    private static DateOnly SeasonMonday(DateOnly d)
        => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));   // DayOfWeek: Sunday = 0

    /// <summary>★ S105.2 — THE definition of the weekend, once (A1): Saturday and
    /// Sunday. Friday is a weekday — the Ivy Friday/Saturday pair is legal BECAUSE
    /// Friday is the weekday game. One line to change, here only; the oracle's
    /// is_weekend is its counterpart and Phase 85 C16 pins the two together.</summary>
    private static bool SeasonIsWeekend(DateOnly d)
        => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateOnly SeasonNightDate(DateOnly weekMonday, int weekday)
        => weekMonday.AddDays(weekday);

    /// <summary>Case-normalised active nights: even leagues the first two, odd all
    /// three. Throws the named InvalidConfiguration reasons from the oracle.</summary>
    private static List<int> SeasonActiveNights(
        IReadOnlyList<string> nights, int n, string label)
    {
        var norm = new List<int>();
        foreach (var raw in nights)
        {
            var w = SeasonWeekdayOf(raw);
            if (w is null)
                throw new InvalidOperationException(
                    $"INVALID CONFIGURATION: {label}unrecognised authored night '{raw}'.");
            norm.Add(w.Value);
        }
        if (norm.Distinct().Count() != norm.Count)
            throw new InvalidOperationException(
                $"INVALID CONFIGURATION: {label}duplicate authored night.");
        var need = n % 2 == 0 ? 2 : 3;
        if (norm.Count < need)
            throw new InvalidOperationException(
                $"INVALID CONFIGURATION: {label}needs {need} distinct nights, has {norm.Count}.");
        return norm.Take(need).ToList();
    }

    /// <summary>The ordered playing-week Mondays (ascending) and the wall. The final
    /// week is the latest Mon-Sun week ALL of whose active nights fall on or before
    /// the wall; the Christmas week is skipped and counts for nothing (R10).</summary>
    private static (List<DateOnly> Window, DateOnly Wall) SeasonLeagueWindow(
        int startYear, int weeks, int? offsetDays, List<int> active, string label)
    {
        var ss = CharmCalendar.ThirdSundayInMarch(startYear + 1);
        if (offsetDays is < 0)
            throw new InvalidOperationException(
                $"INVALID CONFIGURATION: {label}tournament offset {offsetDays} is negative.");
        var wall = offsetDays is null ? ss : ss.AddDays(-(offsetDays.Value + 1));
        var wk = SeasonMonday(wall);
        while (active.Any(a => SeasonNightDate(wk, a) > wall))
            wk = wk.AddDays(-7);
        var xmas = SeasonMonday(new DateOnly(startYear, 12, 25));
        var nov1 = new DateOnly(startYear, 11, 1);
        var outWeeks = new List<DateOnly>();
        while (outWeeks.Count < weeks)
        {
            if (wk != xmas) outWeeks.Add(wk);
            wk = wk.AddDays(-7);
        }
        if (outWeeks.Count > 0 && outWeeks[^1] < nov1)
            throw new InvalidOperationException(
                $"INVALID CONFIGURATION: {label}window would open {outWeeks[^1]:yyyy-MM-dd}, "
                + "before the November 1 floor.");
        outWeeks.Reverse();
        return (outWeeks, wall);
    }

    /// <summary>Exact totals: [base]*(weeks-extra) + [base+1]*extra over the ordered
    /// playing weeks — heavier weeks LATEST, evidenced by the real league opening
    /// light. Two-sided bound refused here; the r13 capacity theorem asserted.</summary>
    private static int[] SeasonWeeklyTargets(int n, int g, int weeks, string label)
    {
        if (weeks < g / 2)
            throw new InvalidOperationException(
                $"INVALID CONFIGURATION: {label}Weeks {weeks} cannot seat Games {g} at two a "
                + $"week (needs at least {g / 2}).");
        var leagueGames = n * g / 2;
        if (weeks > leagueGames)
            throw new InvalidOperationException(
                $"INVALID CONFIGURATION: {label}Weeks {weeks} exceeds the league's {leagueGames} "
                + "games — an empty week is forced (base = 0).");
        var baseGames = leagueGames / weeks;
        var extra = leagueGames - baseGames * weeks;
        var targets = new int[weeks];
        for (var w = 0; w < weeks; w++)
            targets[w] = w >= weeks - extra ? baseGames + 1 : baseGames;
        if (targets.Max() > n)
            throw new InvalidOperationException(
                $"{label}weekly target {targets.Max()} exceeds capacity {n} — implementation "
                + "error, not authored data (r13 theorem)");
        return targets;
    }

    private static int SeasonQuarterOf(int seqIndex, int g)
    {
        var q = g / 4;
        var m = g - q * 4;
        var start = 0;
        for (var qi = 0; qi < 4; qi++)
        {
            var size = qi < m ? q + 1 : q;
            if (seqIndex < start + size) return qi;
            start += size;
        }
        throw new InvalidOperationException("sequence index outside its own season");
    }

    /// <summary>The heart of S94: dates one conference's oriented games (emission
    /// order). Returns dates aligned to <paramref name="games"/>. Mirrors the oracle's
    /// date_conference exactly — rotation stream, interleaved extras, urgency-sorted
    /// take/skip week search, completed-week evaluation.</summary>
    private static (DateOnly[] Dates, List<DateOnly> Window, DateOnly Wall) SeasonDateConference(
        List<int> members, int g, int weeks, int? offsetDays, IReadOnlyList<string> nights,
        List<(int Home, int Away)> games, int startYear, string label)
    {
        var n = members.Count;
        if (n - 1 <= 1 && g >= 2)
            throw new InvalidOperationException(
                $"INVALID CONFIGURATION: {label}a dated conference season needs at least two "
                + $"played opponents (size {n} gives one; every game would be a back-to-back "
                + "rematch).");
        var active = SeasonActiveNights(nights, n, label);
        var (window, wall) = SeasonLeagueWindow(startYear, weeks, offsetDays, active, label);
        var targets = SeasonWeeklyTargets(n, g, weeks, label);
        var cap = n / 2;

        // ── the rotation stream: circle-method base rounds, extras half a rotation
        //    from their pair's base position, S93 emission the tie-break ──
        var pairGames = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < games.Count; i++)
        {
            var key = (Math.Min(games[i].Home, games[i].Away),
                       Math.Max(games[i].Home, games[i].Away));
            if (!pairGames.TryGetValue(key, out var list)) pairGames[key] = list = new();
            list.Add(i);
        }
        var qBase = pairGames.Count == 0 ? 0 : pairGames.Values.Min(v => v.Count);
        var m2 = n % 2 == 0 ? n : n + 1;
        var baseRounds = SeasonCircleRounds(n, m2);
        var R = Math.Max(1, baseRounds.Count);
        var roundOfPair = new Dictionary<(int, int), int>();
        for (var ri = 0; ri < baseRounds.Count; ri++)
            foreach (var (a, b) in baseRounds[ri])
            {
                var key = (members[a], members[b]);
                var norm = (Math.Min(key.Item1, key.Item2), Math.Max(key.Item1, key.Item2));
                roundOfPair.TryAdd(norm, ri);
            }
        var entries = new List<(long Key, int Gi)>();
        foreach (var (p, gis) in pairGames)
        {
            var rp = roundOfPair.TryGetValue(p, out var v) ? v : 0;
            for (var e = 0; e < gis.Count; e++)
            {
                long key;
                if (e < qBase)
                    key = ((long)e * R + rp) * 2;
                else
                {
                    var off = (rp + (R / 2) * (e - qBase + 1)) % R;
                    key = ((long)(qBase / 2) * R + off) * 2 + 1;
                }
                entries.Add((key, gis[e]));
            }
        }
        entries.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Gi.CompareTo(y.Gi));
        var stream = entries.Select(e => e.Gi).ToArray();
        if (stream.Length != games.Count)
            throw new InvalidOperationException(
                $"{label}rotation stream lost games — implementation error");

        // ── urgency-sorted take/skip week search with completed-week evaluation ──
        var datesOut = new DateOnly?[games.Count];
        var seq = members.ToDictionary(s => s, _ => new List<(int Opp, int Idx)>());
        var left = members.ToDictionary(s => s, _ => g);
        var used = new bool[stream.Length];
        long nodes = 0;
        const long Budget = 5_000_000;

        bool Build(int w)
        {
            if (w == window.Count) return used.All(u => u);
            var target = targets[w];
            var dts = active.Select(a => SeasonNightDate(window[w], a)).ToArray();
            var weeksLeft = window.Count - w;
            foreach (var s in members)
                if (left[s] > 2 * weeksLeft) return false;

            int Urgency(int gi)
            {
                var (h, a) = games[gi];
                return Math.Max(left[h] - 2 * (weeksLeft - 1), left[a] - 2 * (weeksLeft - 1));
            }
            var order = Enumerable.Range(0, stream.Length).Where(sp => !used[sp])
                .OrderByDescending(sp => Urgency(stream[sp])).ThenBy(sp => sp).ToArray();

            var playedWk = members.ToDictionary(s => s, _ => 0);
            // ★ S105.2 — the weekday/weekend rule, PRUNED not validated: per-team
            //   occupancy of each half of the week, kept beside playedWk,
            //   incremented on take and unwound on backtrack in the same places.
            var wdWk = members.ToDictionary(s => s, _ => 0);
            var weWk = members.ToDictionary(s => s, _ => 0);
            var dateIsWeekend = dts.Select(SeasonIsWeekend).ToArray();
            var pairsWk = new HashSet<(int, int)>();
            var onDate = dts.Select(_ => new HashSet<int>()).ToArray();
            var chosen = new List<(int Sp, int Gi, int Di)>();

            bool EvalAndDescend()
            {
                // ── COMPLETED-WEEK EVALUATION (r14/r15): sort by real date, append
                //    tentatively, run every chronological-sequence rule, descend. ──
                var weekSorted = chosen.OrderBy(t => dts[t.Di]).ThenBy(t => t.Gi).ToList();
                var appended = new List<int>();
                var ok = true;
                foreach (var (sp, gi, di) in weekSorted)
                {
                    var (h, a) = games[gi];
                    foreach (var (team, opp) in new[] { (h, a), (a, h) })
                    {
                        var sq = seq[team];
                        if (sq.Count > 0 && sq[^1].Opp == opp) { ok = false; break; }
                        var prev = -1;
                        for (var k = sq.Count - 1; k >= 0; k--)
                            if (sq[k].Opp == opp) { prev = sq[k].Idx; break; }
                        if (prev >= 0 && SeasonQuarterOf(prev, g) == SeasonQuarterOf(sq.Count, g))
                        { ok = false; break; }
                        sq.Add((opp, sq.Count));
                        appended.Add(team);
                    }
                    if (!ok) break;
                }
                if (ok)
                {
                    foreach (var (sp, gi, di) in weekSorted)
                    {
                        datesOut[gi] = dts[di];
                        used[sp] = true;
                        left[games[gi].Home]--; left[games[gi].Away]--;
                    }
                    if (Build(w + 1)) return true;
                    foreach (var (sp, gi, di) in weekSorted)
                    {
                        datesOut[gi] = null;
                        used[sp] = false;
                        left[games[gi].Home]++; left[games[gi].Away]++;
                    }
                }
                for (var k = appended.Count - 1; k >= 0; k--)
                    seq[appended[k]].RemoveAt(seq[appended[k]].Count - 1);
                return false;
            }

            bool Pick(int pos, int count)
            {
                if (++nodes > Budget)
                    throw new InvalidOperationException(
                        $"SEARCH BUDGET EXHAUSTED: {label}date-search budget {Budget} "
                        + $"exhausted in week {w + 1} — proves nothing about feasibility.");
                if (count == target) return EvalAndDescend();
                if (pos == order.Length || count + (order.Length - pos) < target) return false;
                var sp = order[pos];
                var gi = stream[sp];
                var (h, a) = games[gi];
                var key = (Math.Min(h, a), Math.Max(h, a));
                if (playedWk[h] < 2 && playedWk[a] < 2 && !pairsWk.Contains(key))
                {
                    for (var di = 0; di < dts.Length; di++)   // authored priority order
                    {
                        if (onDate[di].Contains(h) || onDate[di].Contains(a)) continue;
                        if (onDate[di].Count >= 2 * cap) continue;
                        // ★ S105.2 — at most one weekday and one weekend game a week:
                        //   a date whose half is already occupied by EITHER team is
                        //   rejected here, before it is ever taken.
                        var occ = dateIsWeekend[di] ? weWk : wdWk;
                        if (occ[h] >= 1 || occ[a] >= 1) continue;
                        onDate[di].Add(h); onDate[di].Add(a);
                        playedWk[h]++; playedWk[a]++;
                        occ[h]++; occ[a]++;
                        pairsWk.Add(key);
                        chosen.Add((sp, gi, di));
                        if (Pick(pos + 1, count + 1)) return true;
                        chosen.RemoveAt(chosen.Count - 1);
                        pairsWk.Remove(key);
                        occ[h]--; occ[a]--;
                        playedWk[h]--; playedWk[a]--;
                        onDate[di].Remove(h); onDate[di].Remove(a);
                        break;      // the date choice is priority-forced, not branched
                    }
                    if (left[h] - playedWk[h] > 2 * (weeksLeft - 1)
                        || left[a] - playedWk[a] > 2 * (weeksLeft - 1))
                        return false;    // a must-play team's game may not be skipped
                }
                return Pick(pos + 1, count);
            }

            return Pick(0, 0);
        }

        if (!Build(0))
            throw new InvalidOperationException(
                $"INFEASIBLE UNDER CONSTRAINTS: {label}no legal date assignment exists under "
                + "the week cap, exact weekly totals, rematch spacing and quarter separation.");
        return (datesOut.Select(d => d!.Value).ToArray(), window, wall);
    }

    /// <summary>Circle-method rounds on 0..n-1 (odd n gets a bye vertex m2-1).</summary>
    private static List<List<(int, int)>> SeasonCircleRounds(int n, int m2)
    {
        var ring = Enumerable.Range(0, m2).ToList();
        var rounds = new List<List<(int, int)>>();
        for (var r = 0; r < m2 - 1; r++)
        {
            var rnd = new List<(int, int)>();
            for (var i = 0; i < m2 / 2; i++)
            {
                int a = ring[i], b = ring[m2 - 1 - i];
                if (a < n && b < n) rnd.Add((Math.Min(a, b), Math.Max(a, b)));
            }
            rnd.Sort();
            rounds.Add(rnd);
            var next = new List<int> { ring[0], ring[^1] };
            next.AddRange(ring.GetRange(1, m2 - 2));
            ring = next;
        }
        return rounds;
    }

    /// <summary>Dates every game of a built season schedule in place. The structural
    /// fingerprint provably ignores the date (it hashes four fields by name); this
    /// returns the DATED fingerprint over index|date|home|away.</summary>
    private static string SeasonDateSchedule(
        WorldFile world, List<SeasonGame> games, int startYear)
    {
        var byConf = new Dictionary<int, List<int>>();
        foreach (var s in world.Schools) {
            if (!byConf.TryGetValue(s.ConferenceId, out var l)) byConf[s.ConferenceId] = l = new();
            l.Add(s.Id);
        }
        var confOfSchool = world.Schools.ToDictionary(s => s.Id, s => s.ConferenceId);
        var perConfIdx = new Dictionary<int, List<int>>();
        for (var i = 0; i < games.Count; i++)
        {
            var cid = confOfSchool[games[i].HomeId];
            if (!perConfIdx.TryGetValue(cid, out var l)) perConfIdx[cid] = l = new();
            l.Add(i);
        }
        foreach (var c in world.Conferences.OrderBy(c => c.Id))
        {
            if (c.Games == 0 || !byConf.ContainsKey(c.Id)) continue;
            var members = byConf[c.Id].OrderBy(x => x).ToList();
            var label = $"conference '{c.Name}' (id {c.Id}) ";
            var idxs = perConfIdx.TryGetValue(c.Id, out var l) ? l : new List<int>();
            var sub = idxs.Select(i => (games[i].HomeId, games[i].AwayId)).ToList();
            var (dates, _, _) = SeasonDateConference(
                members, c.Games, c.Weeks, c.TourneyOffsetDays, c.Nights, sub, startYear, label);
            for (var k = 0; k < idxs.Count; k++)
                games[idxs[k]] = games[idxs[k]] with { Date = dates[k] };
        }
        var sb = new StringBuilder();
        for (var i = 0; i < games.Count; i++)
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(games[i].Date is { } d
                          ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-")
              .Append('|')
              .Append(games[i].HomeId.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(games[i].AwayId.ToString(CultureInfo.InvariantCulture)).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>★ THE SHOWCASE SKELETON (R13/r15 C11): a named, reachable seam that may
    /// mark at most one game per league per week as the showcase. It selects NOTHING
    /// today — no prestige proxy, no heuristic — exactly as FixedResidualHost shipped
    /// empty in S93; a later session fills it without reopening the date layer. It does
    /// not make D3 active for an even league (it runs after dating, touching no date).</summary>
    private static List<int> SelectShowcaseGames(WorldFile world, List<SeasonGame> games)
        => new();
}
