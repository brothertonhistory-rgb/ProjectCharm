using Charm.History;
using System.Globalization;
using System.Text;

namespace Charm.Harness;

// ============================================================================
//  Session 116 — THE STATS SURVIVE UNDER HIS NUMBER.
//
//  A career's first player page: sports-reference's college page, read straight
//  off the chain of season logs by a man's permanent number. Four tables — per
//  game and totals, each for all games and conference games only — one row per
//  season he was on a roster, and a Career row under each.
//
//  ★ NOTHING HERE IS STORED, AND NOTHING HERE TOUCHES THE ENGINE. The page is
//  arithmetic over the 21 counters every game row already carries (the S90
//  counters-only ruling): points = 2·FGM + 3PM + FTM, 2P = FG − 3P, TRB = ORB +
//  DRB, PF = shooting + non-shooting + offensive fouls, G = rows, GS = rows marked
//  started (row schema 2, S116). No RNG, no config, no season loop.
//
//  ★ MINUTES ARE PER GAME, NOT THE SEASON PAGE'S CONVERSION. A man's minutes in
//  one game = his floor credits ÷ the game's possession count × the game's length
//  (40, plus 5 per overtime). A man who never sits has credits equal to the
//  possession count and reads exactly the game's length. The season page's
//  league-level `SeasonMinutesPerCredit` is a different, nominal figure; this
//  page does not use it.
//
//  ★ THE WALK IS ARITHMETIC (the host-memory rule): seasons 1 .. next − 1, one
//  computable path each. Three states per season, and the page shows which:
//    (1) the log is missing or unreadable  → a "no record" line, never skipped,
//        never fatal. A page reads; a turnover refuses.
//    (2) the log reads and he is not in its roster → NO ROW. Before a freshman
//        arrives and after a senior leaves, those years are outside his career.
//    (3) he is in it → a row (G may be 0 for a man who never got in).
//  Career is the sum of state-3 rows; if any state-1 season occurred anywhere in
//  the walk the row prints as `Career*` with a line naming those seasons, so a
//  clean-looking total never claims a completeness the files cannot back.
//
//  ★ THE CONFERENCE TABLES COUNT LEAGUE GAMES AND CONFERENCE TOURNAMENT GAMES
//  (Emmett, 2026-10-06: a tournament game is a conference game for the player,
//  never toward the standings). The log marks the two kinds separately (block
//  schema 2), so the standings' meaning of "conference game" never moves. A log
//  older than block schema 2 cannot tell a tournament game from any other, so
//  that season's conference row counts league games only and the page says so.
//
//  ★ THE CONFERENCE COLUMN IS READ FROM THE WORLD, and that is provably the world
//  the season was played in: every log is bound to its world's fingerprint and
//  refuses any other (GameLogReader.DecodeFileHeader, WorldDigestMismatch).
//  Realignment is the session that breaks that binding; the per-season conference
//  fact lands with that design (parked).
// ============================================================================

internal static partial class Program
{
    /// <summary>The 21 retained counters in the order the format pins them. Indices into
    /// <see cref="CareerLine.Counters"/>.</summary>
    private const int CcCredits = 0, CcFga = 2, CcFgm = 3, CcTpa = 4, CcTpm = 5, CcFta = 6, CcFtm = 7,
                      CcOReb = 8, CcDReb = 9, CcAst = 10, CcStl = 11, CcBlk = 12, CcTo = 13,
                      CcShFoul = 14, CcNsFoul = 15, CcOffFoul = 16;
    private const int CareerCounterCount = 21;

    /// <summary>One table line's raw material: games, games started, minutes (summed per game),
    /// and the 21 counters summed. Everything printed is derived from this at print time.</summary>
    private sealed class CareerLine
    {
        public long G;
        public long Gs;
        /// <summary>False when any game in this line came from a pre-S116 log (row schema 1),
        /// which never recorded who started: GS then prints blank, never a zero.</summary>
        public bool GsKnown = true;
        public double Minutes;
        public readonly long[] Counters = new long[CareerCounterCount];

        public void AddGame(PerGameStatRowV1 r, double minutes, bool startersRecorded)
        {
            G++;
            GsKnown &= startersRecorded;
            if (r.Started) Gs++;
            Minutes += minutes;
            var c = CareerCountersOf(r);
            for (var i = 0; i < CareerCounterCount; i++) Counters[i] += c[i];
        }

        public void AddLine(CareerLine other)
        {
            G += other.G;
            Gs += other.Gs;
            GsKnown &= other.GsKnown;
            Minutes += other.Minutes;
            for (var i = 0; i < CareerCounterCount; i++) Counters[i] += other.Counters[i];
        }
    }

    private static long[] CareerCountersOf(PerGameStatRowV1 r) => new[]
    {
        r.Credits, r.OffensiveCredits, r.Fga, r.Fgm, r.Tpa, r.Tpm, r.Fta, r.Ftm,
        r.OReb, r.DReb, r.Ast, r.Stl, r.Blk, r.To, r.ShFoul, r.NsFoul, r.OffFoul,
        r.FbBlk, r.OpponentTwoPaOnFloor, r.SecuredBoardsOnFloor, r.OffensiveTeamFgmOnFloor,
    };

    /// <summary>A game's length in minutes: 40 regulation, 5 per overtime.</summary>
    private static int CareerGameLength(GameBlockFactsV1 f) => 40 + 5 * f.OvertimePeriods;

    /// <summary>His minutes in one game: credits ÷ possessions × the game's length.</summary>
    private static double CareerGameMinutes(PerGameStatRowV1 r, GameBlockFactsV1 f)
        => (double)r.Credits / f.PossessionCount * CareerGameLength(f);

    /// <summary>★ S116 — where HE played this game: H at home, A away, N on a neutral floor (no host
    /// — the home/away ids are then a box-score ordering only), or "?" for a pre-S116 log, which
    /// never recorded the site. The game log page (3c) prints it; this session proves it.</summary>
    private static string CareerSite(PerGameStatRowV1 r, GameBlockFactsV1 f)
        => f.HasHost switch
        {
            null => "?",
            false => "N",
            true => r.SchoolId == f.HomeSchoolId ? "H" : "A",
        };

    /// <summary>One season he was on a roster (state 3).</summary>
    private sealed record CareerSeasonRow(
        long SeasonId, int SchoolId, string School, string Conference, string Class, string Pos,
        CareerLine All, CareerLine Conf, bool ConfIncludesTournament = true);

    /// <summary>A conference game for the PLAYER: the league slate or the conference tournament.</summary>
    private static bool CareerCountsAsConference(GameBlockFactsV1 f)
        => f.IsConferenceGame || f.IsConferenceTournamentGame;

    /// <summary>One entry of the walk, in season order: a row (state 3) or a "no record" line
    /// (state 1). State 2 produces no entry at all.</summary>
    private sealed record CareerSeasonEntry(long SeasonId, CareerSeasonRow? Row, string? NoRecordReason);

    /// <summary>A man's whole career page, read off the files.</summary>
    private sealed record PlayerCareer(
        PersonId Person, string Name, CareerSeasonRow Latest,
        IReadOnlyList<CareerSeasonEntry> Entries, CareerLine CareerAll, CareerLine CareerConf)
    {
        public IReadOnlyList<CareerSeasonRow> Seasons => Entries.Where(e => e.Row is not null).Select(e => e.Row!).ToList();
        public IReadOnlyList<long> SeasonsWithoutRecord => Entries.Where(e => e.Row is null).Select(e => e.SeasonId).ToList();
        public bool Starred => SeasonsWithoutRecord.Count > 0;
    }

    /// <summary>The walk's result for a number no readable roster lists.</summary>
    private sealed class CareerNotFoundException : InvalidOperationException
    {
        public CareerNotFoundException(string message) : base(message) { }
    }

    private static string CareerClassLabel(RosterEntryV1 e)
        => e is RosterEntryV2 v2 ? v2.Class switch { 0 => "Fr", 1 => "So", 2 => "Jr", 3 => "Sr", _ => "?" } : "—";

    private static string CareerPosLabel(RosterPosition p)
        => p switch { RosterPosition.Guard => "G", RosterPosition.Wing => "W", RosterPosition.Big => "B", _ => "?" };

    /// <summary>Read every season of this career, once each, bound to this lineage and world.
    /// No schedule fingerprint: a page cannot know an old season's, and the binding is optional
    /// by design. Returns, per season, the log or the reader's reason it could not be read.</summary>
    private static List<(long SeasonId, GameLogV1? Log, string? Reason)> ReadCareerLogs(HistoryStore history)
    {
        var logs = new List<(long, GameLogV1?, string?)>();
        for (var season = 1L; season < history.PeekNextSeasonId; season++)
        {
            var path = GameLogWriter.FinalPathFor(history.Path, season);
            if (!File.Exists(path)) { logs.Add((season, null, "no season log was kept")); continue; }
            try
            {
                var log = GameLogReader.ReadFinalized(path,
                    new GameLogBindings(history.HistoryId, history.WorldFingerprint, season, ScheduleFingerprint: null));
                logs.Add((season, log, null));
            }
            catch (GameLogException gx)
            {
                logs.Add((season, null, $"the log could not be read ({gx.Error})"));
            }
        }
        return logs;
    }

    /// <summary>★ The player reader, by number. The number becomes an identity only through the
    /// history library's door (`GameLogV1.PersonNumbered`) — a roster must list it.</summary>
    private static PlayerCareer ReadPlayerCareer(HistoryStore history, WorldFile world, long number)
        => ReadPlayerCareer(ReadCareerLogs(history), world, log => log.PersonNumbered(number),
                            $"no season roster in this career lists number {number.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>The player reader, by identity (the suite holds identities off roster entries).</summary>
    private static PlayerCareer ReadPlayerCareer(HistoryStore history, WorldFile world, PersonId person)
        => ReadPlayerCareer(ReadCareerLogs(history), world,
                            log => log.Roster.Any(e => e.PersonId == person) ? person : null,
                            $"no season roster in this career lists {person}");

    private static PlayerCareer ReadPlayerCareer(
        List<(long SeasonId, GameLogV1? Log, string? Reason)> logs, WorldFile world,
        Func<GameLogV1, PersonId?> find, string notFound)
    {
        var schools = world.Schools.ToDictionary(s => s.Id);
        var conferences = world.Conferences.ToDictionary(c => c.Id);
        var entries = new List<CareerSeasonEntry>();
        PersonId? person = null;
        RosterEntryV1? latestEntry = null;
        CareerSeasonRow? latestRow = null;

        foreach (var (season, log, reason) in logs)
        {
            if (log is null) { entries.Add(new CareerSeasonEntry(season, null, reason)); continue; }   // state 1
            var hit = find(log);
            if (hit is not { } who) continue;                                                       // state 2
            if (person is { } known && known != who)
                throw new InvalidOperationException($"season {season} resolves the number to {who}, earlier seasons to {known}.");
            person = who;

            var entry = log.Roster.Single(e => e.PersonId == who);   // A4: at most one, by the writer's ascending rule
            var all = new CareerLine();
            var conf = new CareerLine();
            foreach (var block in log.Blocks)
                foreach (var r in block.Rows)
                {
                    if (r.PersonId != who) continue;
                    var minutes = CareerGameMinutes(r, block.Facts);
                    all.AddGame(r, minutes, log.RecordsStarters);
                    if (CareerCountsAsConference(block.Facts)) conf.AddGame(r, minutes, log.RecordsStarters);
                }
            var school = schools.TryGetValue(entry.SchoolId, out var ws) ? ws : null;
            var confName = school is not null && conferences.TryGetValue(school.ConferenceId, out var wc) ? wc.ShortName : "—";
            var row = new CareerSeasonRow(season, entry.SchoolId, school?.Name ?? $"school {entry.SchoolId}", confName,
                                          CareerClassLabel(entry), CareerPosLabel(entry.Position), all, conf,
                                          ConfIncludesTournament: log.RecordsConferenceTournaments);
            entries.Add(new CareerSeasonEntry(season, row, null));                                  // state 3
            latestEntry = entry;
            latestRow = row;
        }

        if (person is not { } found || latestEntry is null || latestRow is null)
            throw new CareerNotFoundException(notFound + ".");

        // Trailing no-record seasons after his last row are still in the walk: they print, and
        // they star the career — the files cannot prove he was not on those rosters.
        var careerAll = new CareerLine();
        var careerConf = new CareerLine();
        foreach (var e in entries.Where(e => e.Row is not null))
        {
            careerAll.AddLine(e.Row!.All);
            careerConf.AddLine(e.Row!.Conf);
        }
        return new PlayerCareer(found, latestEntry.Name, latestRow, entries, careerAll, careerConf);
    }

    // ── Printing ──────────────────────────────────────────────────────────────

    private static readonly string[] CareerStatHeaders =
    {
        "G", "GS", "MP", "FG", "FGA", "FG%", "3P", "3PA", "3P%", "2P", "2PA", "2P%", "FT", "FTA", "FT%",
        "ORB", "DRB", "TRB", "AST", "STL", "BLK", "TOV", "PF", "ShF", "NsF", "OffF", "PTS",
    };

    /// <summary>The printed cells from G to PTS for one line. Per-game values are totals ÷ G,
    /// one decimal; totals are integers (minutes rounded); percentages are made ÷ attempted over
    /// the TOTALS, three places, blank on zero attempts. This is the page's one rounding rule.</summary>
    private static string[] CareerCells(CareerLine l, bool perGame)
    {
        var c = l.Counters;
        var fg = c[CcFgm]; var fga = c[CcFga];
        var tp = c[CcTpm]; var tpa = c[CcTpa];
        var ft = c[CcFtm]; var fta = c[CcFta];
        var twoP = fg - tp; var twoPa = fga - tpa;
        var pf = c[CcShFoul] + c[CcNsFoul] + c[CcOffFoul];
        var pts = 2 * fg + tp + ft;

        string Count(long total) => perGame
            ? (l.G == 0 ? "" : CareerOneDecimal(total / (double)l.G))
            : total.ToString(CultureInfo.InvariantCulture);
        string Pct(long made, long att) => att == 0 ? "" : (made / (double)att).ToString(".000", CultureInfo.InvariantCulture);
        var mp = perGame
            ? (l.G == 0 ? "" : CareerOneDecimal(l.Minutes / l.G))
            : Math.Round(l.Minutes, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        return new[]
        {
            l.G.ToString(CultureInfo.InvariantCulture), l.GsKnown ? l.Gs.ToString(CultureInfo.InvariantCulture) : "", mp,
            Count(fg), Count(fga), Pct(fg, fga),
            Count(tp), Count(tpa), Pct(tp, tpa),
            Count(twoP), Count(twoPa), Pct(twoP, twoPa),
            Count(ft), Count(fta), Pct(ft, fta),
            Count(c[CcOReb]), Count(c[CcDReb]), Count(c[CcOReb] + c[CcDReb]),
            Count(c[CcAst]), Count(c[CcStl]), Count(c[CcBlk]), Count(c[CcTo]), Count(pf),
            Count(c[CcShFoul]), Count(c[CcNsFoul]), Count(c[CcOffFoul]),
            Count(pts),
        };
    }

    private static string CareerOneDecimal(double v)
        => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>The whole page as text — invariant culture, fixed width, no colour.</summary>
    private static string RenderPlayerPage(PlayerCareer career, string numberLabel)
    {
        var sb = new StringBuilder();
        var latest = career.Latest;
        sb.Append(CultureInfo.InvariantCulture,
            $"Player {numberLabel} — {career.Name} — {latest.School}, {latest.Class}, {latest.Pos} (season {latest.SeasonId})\n");
        sb.Append("Minutes are per game: his floor share of the game's possessions x the game's length (40, +5 per OT).\n");
        sb.Append("Conference is the school's conference in this career's world (every log is bound to that world).\n");
        sb.Append("Conference games are the league slate plus the conference tournament.\n");

        foreach (var (title, perGame, conf) in new[]
                 {
                     ("Per game, all games", true, false), ("Per game, conference games", true, true),
                     ("Totals, all games", false, false), ("Totals, conference games", false, true),
                 })
        {
            var lead = new[] { "Season", "School", "Conf", "Class", "Pos" };
            var lines = new List<string[]>();
            var notes = new List<(int At, string Text)>();
            foreach (var e in career.Entries)
            {
                if (e.Row is null)
                {
                    notes.Add((lines.Count, $"Season {e.SeasonId.ToString(CultureInfo.InvariantCulture)}: no record — {e.NoRecordReason}."));
                    continue;
                }
                var r = e.Row;
                lines.Add(new[] { r.SeasonId.ToString(CultureInfo.InvariantCulture), r.School, r.Conference, r.Class, r.Pos }
                          .Concat(CareerCells(conf ? r.Conf : r.All, perGame)).ToArray());
            }
            var careerLabel = career.Starred ? "Career*" : "Career";
            var careerLine = new[] { careerLabel, "", "", "", "" }
                .Concat(CareerCells(conf ? career.CareerConf : career.CareerAll, perGame)).ToArray();

            var header = lead.Concat(CareerStatHeaders).ToArray();
            var widths = header.Select((h, i) => Math.Max(h.Length,
                lines.Append(careerLine).Max(l => l[i].Length))).ToArray();
            string Format(string[] cells) => string.Join(" ", cells.Select((cell, i) =>
                i < lead.Length ? cell.PadRight(widths[i]) : cell.PadLeft(widths[i]))).TrimEnd();

            sb.Append('\n').Append(title).Append('\n');
            sb.Append(Format(header)).Append('\n');
            for (var i = 0; i <= lines.Count; i++)
            {
                foreach (var n in notes.Where(n => n.At == i)) sb.Append(n.Text).Append('\n');
                if (i < lines.Count) sb.Append(Format(lines[i])).Append('\n');
            }
            sb.Append(Format(careerLine)).Append('\n');
        }
        var leagueOnly = career.Seasons.Where(s => !s.ConfIncludesTournament).Select(s => s.SeasonId).ToList();
        if (leagueOnly.Count > 0)
            sb.Append('\n').Append("Conference rows for season")
              .Append(leagueOnly.Count == 1 ? " " : "s ")
              .Append(string.Join(", ", leagueOnly.Select(s => s.ToString(CultureInfo.InvariantCulture))))
              .Append(" count league games only: that log predates the conference tournament marking.\n");
        if (career.Starred)
            sb.Append('\n').Append("* Career sums only the seasons on record; no record exists for season")
              .Append(career.SeasonsWithoutRecord.Count == 1 ? " " : "s ")
              .Append(string.Join(", ", career.SeasonsWithoutRecord.Select(s => s.ToString(CultureInfo.InvariantCulture))))
              .Append(".\n");
        return sb.ToString();
    }

    /// <summary>The latest readable season's roster, for `people`. Returns the season it came
    /// from and the seasons after it that could not be read.</summary>
    private static string RenderPeopleList(HistoryStore history, WorldFile world, int? schoolId)
    {
        var logs = ReadCareerLogs(history);
        var latest = logs.LastOrDefault(l => l.Log is not null);
        if (latest.Log is null)
            throw new CareerNotFoundException("this career has no readable season log.");
        var schools = world.Schools.ToDictionary(s => s.Id);
        if (schoolId is { } sid && !schools.ContainsKey(sid))
            throw new CareerNotFoundException($"school {sid.ToString(CultureInfo.InvariantCulture)} is not a school in this world.");

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"People — season {latest.SeasonId} roster");
        if (schoolId is { } only) sb.Append(CultureInfo.InvariantCulture, $", {schools[only].Name}");
        sb.Append('\n');
        foreach (var skipped in logs.Where(l => l.SeasonId > latest.SeasonId))
            sb.Append(CultureInfo.InvariantCulture, $"(season {skipped.SeasonId}: no record — {skipped.Reason}.)\n");

        var rows = latest.Log.Roster
            .Where(e => schoolId is null || e.SchoolId == schoolId)
            .OrderBy(e => e.SchoolId).ThenBy(e => e.AcquisitionIndex)
            .Select(e => new[]
            {
                e.PersonId.ToString(), e.Name,
                schools.TryGetValue(e.SchoolId, out var s) ? s.Name : $"school {e.SchoolId}",
                CareerClassLabel(e), CareerPosLabel(e.Position),
            }).ToList();
        var header = new[] { "number", "name", "school", "class", "pos" };
        var widths = header.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        string Format(string[] cells) => string.Join("  ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd();
        sb.Append(Format(header)).Append('\n');
        foreach (var r in rows) sb.Append(Format(r)).Append('\n');
        return sb.ToString();
    }

    // ── The commands ──────────────────────────────────────────────────────────

    /// <summary>A number as typed: `2` or `person:2` (the form `people` prints). Either way it
    /// becomes an identity only through the roster door, never by being parsed into one.</summary>
    private static bool TryParsePlayerNumber(string text, out long number)
    {
        var t = text.StartsWith("person:", StringComparison.Ordinal) ? text["person:".Length..] : text;
        return long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 1;
    }

    /// <summary>Open a career for READING. A path with no career file is refused by name rather
    /// than handed to `HistoryStore.Open`, which would create one.</summary>
    private static HistoryStore? OpenCareerForReading(string command, WorldFile world, string historyPath)
    {
        if (!File.Exists(historyPath))
        {
            Console.WriteLine($"{command} ERROR: there is no career at '{historyPath}'. A page reads a career; it never starts one.");
            return null;
        }
        try { return HistoryStore.Open(historyPath, WorldFingerprint(world)); }
        catch (HistoryException hx)
        {
            Console.WriteLine($"{command} ERROR [{hx.Error}]: {hx.Message}");
            return null;
        }
    }

    private static WorldFile? LoadWorldFor(string command, string path)
    {
        try { return LoadWorld(path); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.WriteLine($"{command} ERROR: {ex.Message}");
            return null;
        }
    }

    /// <summary>`player &lt;world.json&gt; &lt;number&gt; --history &lt;career.json&gt;`.</summary>
    private static int RunPlayer(string[] args)
    {
        string? historyPath;
        try { historyPath = ParseHistoryArg(args, 1); }
        catch (HistoryException hx) { Console.WriteLine($"PLAYER ERROR: {hx.Message}"); return 1; }
        var positional = Enumerable.Range(1, args.Length - 1).Where(i => !IsHistoryArgAt(args, i)).Select(i => args[i]).ToList();
        if (positional.Count != 2)
        {
            Console.WriteLine("usage: player <world.json> <number> --history <career.json>");
            Console.WriteLine("  A career's player page, read off its season logs. Find a number with `people`.");
            return 1;
        }
        if (historyPath is null)
        {
            Console.WriteLine("PLAYER ERROR: a player page reads a career — --history <career.json> is required. " +
                              "A legacy season keeps no people.");
            return 1;
        }
        if (!TryParsePlayerNumber(positional[1], out var number))
        {
            Console.WriteLine($"PLAYER ERROR: '{positional[1]}' is not a player number (a whole number of 1 or more).");
            return 1;
        }
        var world = LoadWorldFor("PLAYER", positional[0]);
        if (world is null) return 1;
        using var history = OpenCareerForReading("PLAYER", world, historyPath);
        if (history is null) return 1;
        try
        {
            var career = ReadPlayerCareer(history, world, number);
            Console.Write(RenderPlayerPage(career, number.ToString(CultureInfo.InvariantCulture)));
            return 0;
        }
        catch (CareerNotFoundException nf)
        {
            Console.WriteLine($"PLAYER ERROR: {nf.Message}");
            return 1;
        }
    }

    /// <summary>`people &lt;world.json&gt; --history &lt;career.json&gt; [schoolId]`.</summary>
    private static int RunPeople(string[] args)
    {
        string? historyPath;
        try { historyPath = ParseHistoryArg(args, 1); }
        catch (HistoryException hx) { Console.WriteLine($"PEOPLE ERROR: {hx.Message}"); return 1; }
        var positional = Enumerable.Range(1, args.Length - 1).Where(i => !IsHistoryArgAt(args, i)).Select(i => args[i]).ToList();
        if (positional.Count is < 1 or > 2)
        {
            Console.WriteLine("usage: people <world.json> --history <career.json> [schoolId]");
            Console.WriteLine("  The latest season's roster: number, name, school, class, position.");
            return 1;
        }
        if (historyPath is null)
        {
            Console.WriteLine("PEOPLE ERROR: a people list reads a career — --history <career.json> is required. " +
                              "A legacy season keeps no people.");
            return 1;
        }
        int? schoolId = null;
        if (positional.Count == 2)
        {
            if (!int.TryParse(positional[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sid))
            {
                Console.WriteLine($"PEOPLE ERROR: '{positional[1]}' is not a school id.");
                return 1;
            }
            schoolId = sid;
        }
        var world = LoadWorldFor("PEOPLE", positional[0]);
        if (world is null) return 1;
        using var history = OpenCareerForReading("PEOPLE", world, historyPath);
        if (history is null) return 1;
        try
        {
            Console.Write(RenderPeopleList(history, world, schoolId));
            return 0;
        }
        catch (CareerNotFoundException nf)
        {
            Console.WriteLine($"PEOPLE ERROR: {nf.Message}");
            return 1;
        }
    }
}
