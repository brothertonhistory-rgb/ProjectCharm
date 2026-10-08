using Charm.History;
using System.Globalization;
using System.Text;

namespace Charm.Harness;

// ============================================================================
//  Session 117 — THE GAME LOG AND THE BOX SCORE (stacked-seasons arc, 3c).
//
//  The same game rows the player page sums, laid out two more ways, modelled on
//  sports-reference (Emmett's game-log screenshot, 2026-10-06):
//    `gamelog` — one man, one season, one line per game his TEAM played;
//    `box`     — one game, both teams, every man on both rosters.
//  Nothing here is stored and nothing here touches the engine: every line is the
//  season log's rows and block facts, read through the production reader.
//
//  ★ EVERY TEAM GAME IS ON HIS LOG (Emmett's ruling 1). A game he did not get into
//  is a DNP line: it has a team game number (Gtm) and no career game number (Gcar,
//  ruling 2). The DNP set is arithmetic — his school's games minus his rows — so a
//  walk-on who never played still has his team's whole schedule.
//
//  ★ DATE ORDER, GROUPED BY PHASE. A game's fixture number is NOT its place in the
//  calendar (tournaments are scheduled last and dated first), so the log sorts by
//  date and the fixture number is only the box score's reference (Ref). Rows group
//  by phase of the season — the regular season, conference and non-conference
//  interleaved by date, then the conference tournament — with the header repeated
//  between them, as the screenshot does. Two facts make that order exist, and the
//  page checks both rather than assuming them: a school plays at most one game per
//  date, and no regular-season game falls on or after its conference tournament's
//  first day. If a schedule ever breaks either, the page refuses by name; it never
//  invents an order or reorders a row to keep a section tidy.
//
//  ★ GCAR ACROSS A GAP. Career game numbers continue from earlier seasons only when
//  every earlier season of the career is on record. After a season with no record
//  (S116's state 1) they print — and the page says why: never restarted at 1, never
//  inferred through the gap.
//
//  ★ MINUTES PRINT AS MINUTES:SECONDS (Emmett, 2026-10-06): his share of the game's
//  possessions worked to the second. The bottom line's MP is the sum of the printed
//  game minutes; every other total is his season row on the player page.
//
//  ★ UNCREDITED POINTS (Emmett, 2026-10-07). In about one game in eight the final
//  holds points no man is credited with — a bonus trip whose last free throw is missed
//  and put back, where the engine never named a shooter (a known engine gap, boarded
//  for its own session). The box score's team totals stay the sum of the men, and a
//  line under them carries the difference to the final: points only, since the log
//  cannot say whether it was a basket or free throws. When the engine names that
//  shooter, the difference is zero and the line stops printing on its own.
// ============================================================================

internal static partial class Program
{
    /// <summary>Sports-reference's game types: the league slate, everything else in the regular
    /// season, and the conference tournament.</summary>
    private static string GameTypeLabel(GameBlockFactsV1 f)
        => f.IsConferenceTournamentGame ? "CTOURN" : f.IsConferenceGame ? "REG (Conf)" : "REG (Non-Conf)";

    /// <summary>The site as sports-reference prints it, for one school: blank at home, @ away, N neutral.</summary>
    private static string SiteMark(int schoolId, GameBlockFactsV1 f)
        => CareerSite(schoolId, f) switch { "A" => "@", "N" => "N", _ => "" };

    /// <summary>`W 76-57`, `L 92-98 (OT)`, `(2OT)` — the school's score first.</summary>
    private static string ResultLabel(int schoolId, GameBlockFactsV1 f)
    {
        var mine = schoolId == f.HomeSchoolId ? f.HomeScore : f.AwayScore;
        var theirs = schoolId == f.HomeSchoolId ? f.AwayScore : f.HomeScore;
        var ot = f.OvertimePeriods switch { 0 => "", 1 => " (OT)", var n => $" ({n.ToString(CultureInfo.InvariantCulture)}OT)" };
        return (mine > theirs ? "W " : "L ") + mine.ToString(CultureInfo.InvariantCulture) + "-"
               + theirs.ToString(CultureInfo.InvariantCulture) + ot;
    }

    private static bool SchoolWon(int schoolId, GameBlockFactsV1 f)
        => schoolId == f.HomeSchoolId ? f.HomeScore > f.AwayScore : f.AwayScore > f.HomeScore;

    /// <summary>One game's stat cells, GS to PTS, as a single-game line prints them: counts as whole
    /// numbers, percentages three places (blank on no attempts), minutes as minutes:seconds.</summary>
    private static string[] GameStatCells(PerGameStatRowV1 r, GameBlockFactsV1 f)
    {
        var line = new CareerLine();
        line.AddGame(r, CareerGameMinutes(r, f));
        var cells = CareerCells(line, perGame: false).Skip(1).ToArray();   // drop G
        cells[1] = GameClock(CareerGameSeconds(r, f));                      // MP
        return cells;
    }

    private static readonly string[] GameStatHeaders = CareerStatHeaders.Skip(1).ToArray();   // GS .. PTS

    /// <summary>A plain fixed-width table: lead cells left-aligned, the rest right-aligned.</summary>
    private static Func<string[], string> TableFormat(string[] header, int leadCount, IEnumerable<string[]> rows)
        => TableFormat(header, leadCount, rows, out _);

    private static Func<string[], string> TableFormat(string[] header, int leadCount, IEnumerable<string[]> rows, out int[] widths)
    {
        var all = rows.Prepend(header).ToList();
        var w = header.Select((_, i) => all.Max(r => i < r.Length ? r[i].Length : 0)).ToArray();
        widths = w;
        return cells => string.Join(" ", cells.Select((c, i) => i < leadCount ? c.PadRight(w[i]) : c.PadLeft(w[i]))).TrimEnd();
    }

    // ── The game log ─────────────────────────────────────────────────────────

    /// <summary>One line of a man's game log: a game his team played. `Row` is null for a DNP;
    /// `Gcar` is null for a DNP and for every played game once career numbering is incomplete.</summary>
    private sealed record GameLogLine(int Gtm, long? Gcar, GameBlockFactsV1 Facts, PerGameStatRowV1? Row, string Opponent);

    private sealed record SeasonGameLog(
        PersonId Person, string Name, long SeasonId, int SchoolId, string School,
        IReadOnlyList<GameLogLine> Lines, CareerLine Totals, long TotalSeconds, int Wins, int Losses,
        IReadOnlyList<long> GapSeasons);

    /// <summary>★ S117 — a man's game log for one season, read off the career's logs. Refuses by name:
    /// a season the career does not have, a season with no record, a season he was not on a roster
    /// for, a school that plays twice on one date, and a regular-season game inside the tournament.</summary>
    private static SeasonGameLog ReadSeasonGameLog(
        List<(long SeasonId, GameLogV1? Log, string? Reason)> logs, WorldFile world,
        Func<GameLogV1, PersonId?> find, string who, long season)
    {
        var at = logs.FindIndex(l => l.SeasonId == season);
        if (at < 0)
            throw new CareerRefusedException(logs.Count == 0
                ? $"this career has played no season yet, so it has no season {season.ToString(CultureInfo.InvariantCulture)}."
                : $"this career has seasons 1..{logs[^1].SeasonId.ToString(CultureInfo.InvariantCulture)}; there is no season {season.ToString(CultureInfo.InvariantCulture)}.");
        var (_, log, reason) = logs[at];
        if (log is null)
            throw new CareerRefusedException($"season {season.ToString(CultureInfo.InvariantCulture)} has no record — {reason}.");
        if (find(log) is not { } person)
            throw new CareerRefusedException($"{who} was not on a roster in season {season.ToString(CultureInfo.InvariantCulture)}.");

        var schools = world.Schools.ToDictionary(s => s.Id);
        string NameOf(int id) => schools.TryGetValue(id, out var s) ? s.Name : $"school {id.ToString(CultureInfo.InvariantCulture)}";
        var entry = log.Roster.Single(e => e.PersonId == person);
        var school = entry.SchoolId;

        var teamGames = log.Blocks.Where(b => b.Facts.HomeSchoolId == school || b.Facts.AwaySchoolId == school).ToList();
        var twice = teamGames.GroupBy(b => b.Facts.Date!.Value).FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
            throw new CareerRefusedException(
                $"{NameOf(school)} plays {twice.Count()} games on {twice.Key:yyyy-MM-dd} in season {season.ToString(CultureInfo.InvariantCulture)}; " +
                "the game log orders games by date and will not invent an order between them.");
        teamGames = teamGames.OrderBy(b => b.Facts.Date!.Value).ToList();
        var firstTourney = teamGames.FindIndex(b => b.Facts.IsConferenceTournamentGame);
        if (firstTourney >= 0)
        {
            var late = teamGames.Skip(firstTourney).FirstOrDefault(b => !b.Facts.IsConferenceTournamentGame);
            if (late is not null)
                throw new CareerRefusedException(
                    $"{NameOf(school)} plays a regular-season game on {late.Facts.Date!.Value:yyyy-MM-dd}, after its conference " +
                    $"tournament began on {teamGames[firstTourney].Facts.Date!.Value:yyyy-MM-dd} (season {season.ToString(CultureInfo.InvariantCulture)}); " +
                    "the game log groups by phase of the season and will not reorder a game to keep a section tidy.");
        }

        // Career game numbers: only through seasons that are all on record.
        var gaps = logs.Take(at).Where(l => l.Log is null).Select(l => l.SeasonId).ToList();
        long played = 0;
        if (gaps.Count == 0)
            foreach (var (_, earlier, _) in logs.Take(at))
                played += earlier!.Blocks.Sum(b => b.Rows.Count(r => r.PersonId == person));

        var lines = new List<GameLogLine>(teamGames.Count);
        var totals = new CareerLine();
        long seconds = 0;
        int wins = 0, losses = 0;
        foreach (var b in teamGames)
        {
            var row = b.Rows.FirstOrDefault(r => r.PersonId == person);
            long? gcar = null;
            if (row is not null)
            {
                played++;
                if (gaps.Count == 0) gcar = played;
                totals.AddGame(row, CareerGameMinutes(row, b.Facts));
                seconds += CareerGameSeconds(row, b.Facts);
            }
            if (SchoolWon(school, b.Facts)) wins++; else losses++;
            var opp = b.Facts.HomeSchoolId == school ? b.Facts.AwaySchoolId : b.Facts.HomeSchoolId;
            lines.Add(new GameLogLine(lines.Count + 1, gcar, b.Facts, row, NameOf(opp)));
        }
        return new SeasonGameLog(person, entry.Name, season, school, NameOf(school), lines, totals, seconds, wins, losses, gaps);
    }

    private static readonly string[] GameLogLeadHeaders = { "Rk", "Gcar", "Gtm", "Date", "School", "Site", "Opp", "Type", "Result" };

    /// <summary>The game log as text — invariant culture, fixed width, no colour.</summary>
    private static string RenderSeasonGameLog(SeasonGameLog g, string numberLabel)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"Game log — {g.Name} (number {numberLabel}) — {g.School}, season {g.SeasonId}\n");
        sb.Append("Every game his team played, in date order. Gtm counts the team's games; Gcar counts only the games he played.\n");
        sb.Append("MP is minutes:seconds, his floor share of the game's possessions. Ref opens the box score: box <world> <season> <Ref>.\n");
        if (g.GapSeasons.Count > 0)
            sb.Append("Career game numbers are incomplete: no record exists for season")
              .Append(g.GapSeasons.Count == 1 ? " " : "s ")
              .Append(string.Join(", ", g.GapSeasons.Select(s => s.ToString(CultureInfo.InvariantCulture))))
              .Append(", so Gcar prints —.\n");

        var header = GameLogLeadHeaders.Concat(GameStatHeaders).Append("Ref").ToArray();
        var statCount = GameStatHeaders.Length;
        string[] Line(GameLogLine l)
        {
            var lead = new[]
            {
                l.Gtm.ToString(CultureInfo.InvariantCulture),
                l.Row is null ? "" : l.Gcar is { } n ? n.ToString(CultureInfo.InvariantCulture) : "—",
                l.Gtm.ToString(CultureInfo.InvariantCulture),
                l.Facts.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                g.School, SiteMark(g.SchoolId, l.Facts), l.Opponent, GameTypeLabel(l.Facts), ResultLabel(g.SchoolId, l.Facts),
            };
            var stats = l.Row is null ? Enumerable.Repeat("", statCount).ToArray() : GameStatCells(l.Row, l.Facts);
            return lead.Concat(stats).Append(l.Facts.FixtureOrdinal.ToString(CultureInfo.InvariantCulture)).ToArray();
        }
        var rows = g.Lines.Select(Line).ToList();
        var bottomStats = CareerCells(g.Totals, perGame: false).Skip(1).ToArray();
        bottomStats[1] = GameClock(g.TotalSeconds);
        var bottom = new[] { "", "", "", "Totals", "", "", "", "",
                             g.Wins.ToString(CultureInfo.InvariantCulture) + "-" + g.Losses.ToString(CultureInfo.InvariantCulture) }
                     .Concat(bottomStats).Append("").ToArray();
        var leadCount = GameLogLeadHeaders.Length;
        var format = TableFormat(header, leadCount, rows.Append(bottom), out var widths);
        var probe = format(header);

        // A DNP line: the lead cells, then "Did Not Play" across the stat columns, then Ref.
        var statsWidth = widths.Skip(leadCount).Take(statCount).Sum() + statCount - 1;
        string Dnp(string[] cells)
            => string.Join(" ", cells.Take(leadCount).Select((c, i) => c.PadRight(widths[i])))
               + " " + "Did Not Play".PadRight(statsWidth) + " " + cells[^1].PadLeft(widths[^1]);

        var phases = new[]
        {
            ("Regular season", g.Lines.Where(l => !l.Facts.IsConferenceTournamentGame).ToList()),
            ("Conference tournament", g.Lines.Where(l => l.Facts.IsConferenceTournamentGame).ToList()),
        };
        foreach (var (title, phase) in phases)
        {
            if (phase.Count == 0) continue;
            sb.Append('\n').Append(title).Append('\n').Append(probe).Append('\n');
            foreach (var l in phase)
            {
                var cells = rows[l.Gtm - 1];
                sb.Append(l.Row is null ? Dnp(cells) : format(cells)).Append('\n');
            }
        }
        sb.Append('\n').Append(format(bottom)).Append('\n');
        return sb.ToString();
    }

    // ── The box score ────────────────────────────────────────────────────────

    private sealed record BoxLine(RosterEntryV1 Man, PerGameStatRowV1 Row, long Seconds);

    /// <summary>`Points` is the men's points; `Uncredited` is the final minus that — points the
    /// scoreboard holds and no man is credited with (never negative: the page refuses otherwise).</summary>
    private sealed record BoxTeam(
        int SchoolId, string School, IReadOnlyList<BoxLine> Starters, IReadOnlyList<BoxLine> Reserves,
        IReadOnlyList<RosterEntryV1> DidNotPlay, CareerLine Totals, long Points, long Uncredited);

    private sealed record BoxScore(long SeasonId, GameBlockFactsV1 Facts, BoxTeam Visitor, BoxTeam Home);

    /// <summary>★ S117 — one game's box score, read off its season's log by its fixture number (the
    /// game log's Ref). The visitor is listed first, the home school second (Emmett, 2026-10-06); on a
    /// neutral floor the school the fixture lists as away goes first.</summary>
    private static BoxScore ReadBoxScore(GameLogV1 log, WorldFile world, long season, int reference)
    {
        if (reference < 0 || reference >= log.Blocks.Count)
            throw new CareerRefusedException(
                $"season {season.ToString(CultureInfo.InvariantCulture)} has no game {reference.ToString(CultureInfo.InvariantCulture)}; " +
                $"its games are 0..{(log.Blocks.Count - 1).ToString(CultureInfo.InvariantCulture)}.");
        var block = log.Blocks[reference];
        var schools = world.Schools.ToDictionary(s => s.Id);
        BoxTeam Team(int schoolId, int final)
        {
            var rows = block.Rows.Where(r => r.SchoolId == schoolId).ToList();
            var men = log.Roster.Where(e => e.SchoolId == schoolId).ToDictionary(e => e.PersonId);
            var lines = rows.Select(r => new BoxLine(men[r.PersonId], r, CareerGameSeconds(r, block.Facts)))
                            .OrderByDescending(l => l.Seconds).ThenBy(l => l.Man.AcquisitionIndex).ToList();
            var played = rows.Select(r => r.PersonId).ToHashSet();
            var totals = new CareerLine();
            foreach (var r in rows) totals.AddGame(r, CareerGameMinutes(r, block.Facts));
            var name = schools.TryGetValue(schoolId, out var s) ? s.Name : $"school {schoolId}";
            var points = rows.Sum(r => 2 * r.Fgm + r.Tpm + r.Ftm);
            if (points > final)
                throw new CareerRefusedException(
                    $"season {season.ToString(CultureInfo.InvariantCulture)} game {reference.ToString(CultureInfo.InvariantCulture)}: " +
                    $"{name}'s men are credited with {points} points, more than its final {final}; the box score will not print a game that does not add up.");
            return new BoxTeam(schoolId, name,
                lines.Where(l => l.Row.Started).ToList(), lines.Where(l => !l.Row.Started).ToList(),
                men.Values.Where(e => !played.Contains(e.PersonId)).OrderBy(e => e.AcquisitionIndex).ToList(),
                totals, points, final - points);
        }
        return new BoxScore(season, block.Facts, Team(block.Facts.AwaySchoolId, block.Facts.AwayScore),
                            Team(block.Facts.HomeSchoolId, block.Facts.HomeScore));
    }

    private static string RenderBoxScore(BoxScore box)
    {
        var f = box.Facts;
        var sb = new StringBuilder();
        var where = f.HasHost!.Value ? $"at {box.Home.School}" : "neutral site";
        sb.Append(CultureInfo.InvariantCulture,
            $"Box score — season {box.SeasonId}, game {f.FixtureOrdinal} — {f.Date!.Value:yyyy-MM-dd} — {GameTypeLabel(f)} — {where}\n");
        var otNote = f.OvertimePeriods switch { 0 => "", 1 => " (OT)", var n => $" ({n.ToString(CultureInfo.InvariantCulture)}OT)" };
        sb.Append(CultureInfo.InvariantCulture, $"{box.Visitor.School} {f.AwayScore}, {box.Home.School} {f.HomeScore}{otNote}\n");

        // The line score: 1st, 2nd, each overtime, final.
        var periods = f.Periods!;
        var lsHeader = new[] { "" }.Concat(periods.Select((_, k) => k switch
            { 0 => "1st", 1 => "2nd", 2 => "OT", _ => $"{k - 1}OT" })).Append("Final").ToArray();
        var lsRows = new[]
        {
            new[] { box.Visitor.School }.Concat(periods.Select(p => p.Away.ToString(CultureInfo.InvariantCulture)))
                .Append(f.AwayScore.ToString(CultureInfo.InvariantCulture)).ToArray(),
            new[] { box.Home.School }.Concat(periods.Select(p => p.Home.ToString(CultureInfo.InvariantCulture)))
                .Append(f.HomeScore.ToString(CultureInfo.InvariantCulture)).ToArray(),
        };
        var ls = TableFormat(lsHeader, 1, lsRows);
        sb.Append('\n').Append("Line score").Append('\n').Append(ls(lsHeader)).Append('\n');
        foreach (var r in lsRows) sb.Append(ls(r)).Append('\n');

        var header = new[] { "No.", "Player" }.Concat(GameStatHeaders.Skip(1)).ToArray();   // MP .. PTS
        foreach (var team in new[] { box.Visitor, box.Home })
        {
            string[] Cells(BoxLine l) => new[] { l.Man.PersonId.ToString(), l.Man.Name }
                .Concat(GameStatCells(l.Row, f).Skip(1)).ToArray();
            var totalCells = CareerCells(team.Totals, perGame: false).Skip(2).ToArray();
            totalCells[0] = GameClock(5L * CareerGameLength(f) * 60);
            var totalRow = new[] { "", "Team totals" }.Concat(totalCells).ToArray();
            var starters = team.Starters.Select(Cells).ToList();
            var reserves = team.Reserves.Select(Cells).ToList();
            var format = TableFormat(header, 2, starters.Concat(reserves).Append(totalRow));
            sb.Append('\n').Append(team.School).Append('\n');
            sb.Append("Starters").Append('\n').Append(format(header)).Append('\n');
            foreach (var c in starters) sb.Append(format(c)).Append('\n');
            sb.Append("Reserves").Append('\n');
            foreach (var c in reserves) sb.Append(format(c)).Append('\n');
            sb.Append("Did not play: ")
              .Append(team.DidNotPlay.Count == 0 ? "—" : string.Join(", ", team.DidNotPlay.Select(e => $"{e.Name} ({e.PersonId})")))
              .Append('\n');
            sb.Append(format(totalRow)).Append('\n');
            if (team.Uncredited > 0)
                sb.Append(CultureInfo.InvariantCulture,
                    $"Uncredited: {team.Uncredited} point{(team.Uncredited == 1 ? "" : "s")} (scored, no player named)\n");
        }
        return sb.ToString();
    }

    // ── The commands ─────────────────────────────────────────────────────────

    private static bool TryParseSeason(string text, out long season)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out season) && season >= 1;

    /// <summary>`gamelog &lt;world.json&gt; &lt;number&gt; &lt;season&gt; --history &lt;career.json&gt;`.</summary>
    private static int RunGameLog(string[] args)
    {
        string? historyPath;
        try { historyPath = ParseHistoryArg(args, 1); }
        catch (HistoryException hx) { Console.WriteLine($"GAMELOG ERROR: {hx.Message}"); return 1; }
        var positional = Enumerable.Range(1, args.Length - 1).Where(i => !IsHistoryArgAt(args, i)).Select(i => args[i]).ToList();
        if (positional.Count != 3)
        {
            Console.WriteLine("usage: gamelog <world.json> <number> <season> --history <career.json>");
            Console.WriteLine("  One man's season, one line per game his team played. Find a number with `people`.");
            return 1;
        }
        if (historyPath is null)
        {
            Console.WriteLine("GAMELOG ERROR: a game log reads a career — --history <career.json> is required. " +
                              "A legacy season keeps no people.");
            return 1;
        }
        if (!TryParsePlayerNumber(positional[1], out var number))
        {
            Console.WriteLine($"GAMELOG ERROR: '{positional[1]}' is not a player number (a whole number of 1 or more).");
            return 1;
        }
        if (!TryParseSeason(positional[2], out var season))
        {
            Console.WriteLine($"GAMELOG ERROR: '{positional[2]}' is not a season number (a whole number of 1 or more).");
            return 1;
        }
        var world = LoadWorldFor("GAMELOG", positional[0]);
        if (world is null) return 1;
        using var history = OpenCareerForReading("GAMELOG", world, historyPath);
        if (history is null) return 1;
        try
        {
            var label = number.ToString(CultureInfo.InvariantCulture);
            var page = ReadSeasonGameLog(ReadCareerLogs(history), world, log => log.PersonNumbered(number), $"number {label}", season);
            Console.Write(RenderSeasonGameLog(page, label));
            return 0;
        }
        catch (CareerRefusedException rx)
        {
            Console.WriteLine($"GAMELOG ERROR: {rx.Message}");
            return 1;
        }
    }

    /// <summary>`box &lt;world.json&gt; &lt;season&gt; &lt;ref&gt; --history &lt;career.json&gt;`.</summary>
    private static int RunBox(string[] args)
    {
        string? historyPath;
        try { historyPath = ParseHistoryArg(args, 1); }
        catch (HistoryException hx) { Console.WriteLine($"BOX ERROR: {hx.Message}"); return 1; }
        var positional = Enumerable.Range(1, args.Length - 1).Where(i => !IsHistoryArgAt(args, i)).Select(i => args[i]).ToList();
        if (positional.Count != 3)
        {
            Console.WriteLine("usage: box <world.json> <season> <ref> --history <career.json>");
            Console.WriteLine("  One game's box score. Ref is the game log's Ref column.");
            return 1;
        }
        if (historyPath is null)
        {
            Console.WriteLine("BOX ERROR: a box score reads a career — --history <career.json> is required. " +
                              "A legacy season keeps no games.");
            return 1;
        }
        if (!TryParseSeason(positional[1], out var season))
        {
            Console.WriteLine($"BOX ERROR: '{positional[1]}' is not a season number (a whole number of 1 or more).");
            return 1;
        }
        if (!int.TryParse(positional[2], NumberStyles.None, CultureInfo.InvariantCulture, out var reference))
        {
            Console.WriteLine($"BOX ERROR: '{positional[2]}' is not a game reference (the game log's Ref, a whole number).");
            return 1;
        }
        var world = LoadWorldFor("BOX", positional[0]);
        if (world is null) return 1;
        using var history = OpenCareerForReading("BOX", world, historyPath);
        if (history is null) return 1;
        try
        {
            var logs = ReadCareerLogs(history);
            var entry = logs.FirstOrDefault(l => l.SeasonId == season);
            if (entry.SeasonId != season)
                throw new CareerRefusedException(logs.Count == 0
                    ? $"this career has played no season yet, so it has no season {season.ToString(CultureInfo.InvariantCulture)}."
                    : $"this career has seasons 1..{logs[^1].SeasonId.ToString(CultureInfo.InvariantCulture)}; there is no season {season.ToString(CultureInfo.InvariantCulture)}.");
            if (entry.Log is null)
                throw new CareerRefusedException($"season {season.ToString(CultureInfo.InvariantCulture)} has no record — {entry.Reason}.");
            Console.Write(RenderBoxScore(ReadBoxScore(entry.Log, world, season, reference)));
            return 0;
        }
        catch (CareerRefusedException rx)
        {
            Console.WriteLine($"BOX ERROR: {rx.Message}");
            return 1;
        }
    }
}
