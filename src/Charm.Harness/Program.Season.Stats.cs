using System.Globalization;
using Charm.Engine;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
// Session 77 — THE SEASON STAT PAGE.
//
// Every stat a basketball person reads already existed, per game: PlayerBoxTotals
// carries points, shooting splits, boards, assists, steals, blocks, turnovers and
// both foul types for every player in every game. Nothing added them up across a
// season and attached them to a PERSON. The season page reported team rates only.
//
// Until S76 that gap did not matter — five men took ~88% of the floor, so a stat
// leaderboard would have been fiction. S76 fixed the rotation. This is the first
// session in which a season's individual statistics mean anything.
//
// WHAT THIS IS FOR: the cheapest bug-finder available. A human reading a
// leaderboard catches what no aggregate check can — a 6'2" guard leading the
// league in blocks, nobody averaging a double-double, every top scorer a wing.
// That is the deliverable. Phase 73 proves the roll-up is HONEST; whether the
// numbers are good basketball is Emmett's read, on the page, and is the entire
// point of the session.
//
// ★ THE KEY IS THE PERSON, NOT THE SEAT. Records are keyed by pool id, never by
// (school, acquisition index). See SeasonLeagueStats.PlayerSeasons for why.
//
// ★ TWO STAT FAMILIES ARE HARNESS DRAWS, NOT ENGINE DECISIONS. Shooting fouls
// (seed+3) and non-shooting fouls (seed+4) are drawn post-hoc in AttributeGame;
// NonShootingFoulEvent says so in its own doc comment — "the committer is NOT
// chosen here." Foul totals per player are therefore a weighted allocation, not
// a record of who committed anything, and every surface here labels them so. A
// leaderboard reading "league leader in fouls" would imply a fidelity the engine
// does not have. Assists, by contrast, are engine-stamped on-walk (Phase 39).
// ============================================================================

internal static partial class Program
{
    /// <summary>One player's season, keyed by the person (pool id) with the school carried as
    /// data. Public mutable fields, matching the PlayerBoxTotals / SeasonLeagueStats siblings.
    /// New counting fields may be appended here without touching any consumer.</summary>
    private sealed class SeasonPlayerRecord
    {
        // Identity — stamped once, then only re-verified (see IdentityDriftObservations).
        public int PoolId;
        /// <summary>★ S89 — the man's permanent number, or absent in legacy mode. `PoolId`
        /// keeps every consumer it had: it is this season's roster slot, it encodes position,
        /// and the roll-up is still keyed by it. This is the number that will still mean the
        /// same person in four seasons' time, when the pool slot means somebody else.</summary>
        public PersonId? PersonId;
        public int SchoolId;
        public int AcquisitionIndex;
        public string Name = "";
        public string Pos = "";
        public int Height;
        public double ScoutRank;

        // Participation. Credits are player-possession credits, the unit the season page
        // already prints and asserts; GamesPlayed counts team-games with POSITIVE credit.
        public long GamesPlayed;
        public long Credits;

        // The box.
        public long Fga, Fgm, Tpa, Tpm, Fta, Ftm;
        public long OReb, DReb, Ast, Stl, Blk, To;
        public long ShFoul, NsFoul;
        //  ★ S90 — OFFENSIVE FOULS COMMITTED. S87 made the engine name the man who takes a
        //  charge (`PlayerBoxTotals.OffFoul`), but this record never picked the column up:
        //  it was summed league-wide only, so the season line dropped a counter the engine
        //  already attributed. S90 retains every game forever, and a primitive not tracked
        //  at write time can never be recreated for a game already written — so it is added
        //  here BEFORE the first season is archived rather than after.
        //  ★ Deliberately NOT printed anywhere. The page must stay byte-identical (Phase 81
        //  A3); the missing printed column remains O-67.
        public long OffFoul;
        //  Session 85, PAGE-ONLY: the fast-break SUBSET of Blk. Fed from the same box column,
        //  through the same seat-to-man translation, so FbBlk <= Blk for every man. Exists so
        //  the page can report how concentrated a team's break blocks are on one defender —
        //  the baseline for any later change that widens that spread. Never asserted as a
        //  basketball target.
        public long FbBlk;

        //  Session 79.3 — THE ON-FLOOR DENOMINATORS. Four exact counts, fed by NoteOccupancy
        //  from the same walk that credits floor time, so every rate below is measured against
        //  what this man was actually on the floor for rather than against his playing time.
        //  ★ Published BLK%/STL%/AST%/TRB% ESTIMATE these from minutes share. These are counted.
        public long OffensiveCredits;          // his possessions on offence; defensive = Credits - this
        public long OpponentTwoPaOnFloor;      // opponent two-point attempts he faced
        public long SecuredBoardsOnFloor;      // secured boards contested with him on — BOTH ends
        //  ★ The name is load-bearing: this is NOT "teammate makes". It accumulates the WHOLE
        //  offensive team's makes for each of the five men on the floor, which is why the league
        //  identity is 5 x league FGM. The subtraction to teammate-only happens at the board.
        public long OffensiveTeamFgmOnFloor;

        public long Points => 2 * Fgm + Tpm + Ftm;
        public long Reb    => OReb + DReb;
        public long DefensiveCredits => Credits - OffensiveCredits;

        /// <summary>Per-game average, or 0 for a man who never played. ★ The zero guard is not
        /// defensive tidiness: a NaN or an infinity reaching a sort comparator reorders a
        /// leaderboard quietly, and the reader has no way to see it happened.</summary>
        public double PerGame(long total) => GamesPlayed > 0 ? total / (double)GamesPlayed : 0.0;
    }

    // ── The minutes floor: reporting only ────────────────────────────────────────
    //  ★ Deliberately NOT in config.json — that file's key names are parity-locked against a
    //  registry by Phase 71 (S74), so a reporting toggle there would either break that check or
    //  drag registry work into a page-only session. It is a CLI argument, applied after the
    //  roll-up is complete, and it touches neither simulation nor accumulation.
    private static readonly int[] SeasonMinuteTiers = { 100, 250, 500, 900 };
    private const int SeasonDefaultMinuteFloor = 100;

    // Attempts-per-game thresholds for the percentage boards (Emmett's ruling, SETTLED).
    private const double SeasonFgAttemptFloor = 4.0;
    private const double SeasonTpAttemptFloor = 1.5;
    private const double SeasonFtAttemptFloor = 1.5;

    /// <summary>The school printed in full. Named by ID, never by string match — S76's page
    /// has it 51st at 21-9, a real rotation rather than an outlier at either end.</summary>
    private const int SeasonFullTeamSchoolId = 200;   // Oklahoma State (OK ST), Big 12, prestige 87

    /// <summary>Nominal minutes per player-possession credit. Records per team-game × 40 minutes
    /// / records = the 200-minute pie, so a credit is worth 40 / (records per team-game). This is
    /// the S76 conversion, reused verbatim so the two readouts cannot drift apart.</summary>
    private static double SeasonMinutesPerCredit(SeasonLeagueStats s)
    {
        if (s.RotationTeamGames == 0) return 0.0;
        var recordsPerTeamGame = s.RotationRecords / (double)s.RotationTeamGames;
        return recordsPerTeamGame > 0 ? 40.0 / recordsPerTeamGame : 0.0;
    }

    private static double SeasonMinutes(SeasonPlayerRecord r, double minutesPerCredit)
        => r.Credits * minutesPerCredit;

    // ────────────────────────────────────────────────────────────────────────────
    //  THE PAGE
    // ────────────────────────────────────────────────────────────────────────────

    private static void PrintSeasonStatPage(
        SeasonLeagueStats s, WorldFile world, int minuteFloor)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);

        Console.WriteLine("--- SEASON STAT PAGE (Session 77; page-only, never asserted) ---");
        if (s.PlayerSeasons.Count == 0)
        {
            Console.WriteLine("  no per-player records accumulated (instrument not wired) — " +
                              "treat every number on this page as unproven.");
            Console.WriteLine();
            return;
        }

        var mpc     = SeasonMinutesPerCredit(s);
        var all     = s.PlayerSeasons.Values.ToList();
        var names   = world.Schools.ToDictionary(x => x.Id, x => x.Abbr.Trim());
        string Team(SeasonPlayerRecord r) => names.TryGetValue(r.SchoolId, out var a) ? a : "?";

        // The qualifier counts for ALL four tiers, always — a season run is 5,205 games, and
        // making Emmett re-simulate the league to find out how the pool changes is a bad trade
        // for one line of output.
        var tierCounts = SeasonMinuteTiers
            .Select(t => Inv($">={t} {all.Count(r => SeasonMinutes(r, mpc) >= t)}"))
            .ToList();
        Console.WriteLine(Inv($"  {all.Count} player-seasons; {all.Count(r => r.GamesPlayed == 0)} never played a minute."));
        Console.WriteLine("  qualifiers: " + string.Join(" | ", tierCounts));
        Console.WriteLine(Inv($"  minutes floor in force: >={minuteFloor} (reporting only — the roll-up is complete before any filter)"));
        Console.WriteLine();

        var qualified = all.Where(r => SeasonMinutes(r, mpc) >= minuteFloor).ToList();

        // ── (a) Leaderboards ────────────────────────────────────────────────────
        Console.WriteLine(Inv($"  LEADERS — per-game averages, {qualified.Count} men clearing the >={minuteFloor} minute floor"));

        void CountingBoard(string label, Func<SeasonPlayerRecord, long> stat)
        {
            var top = qualified
                .OrderByDescending(r => r.PerGame(stat(r)))
                .ThenBy(r => r.PoolId)
                .Take(10).ToList();
            Console.WriteLine($"    {label}");
            for (var i = 0; i < top.Count; i++)
            {
                var r = top[i];
                Console.WriteLine(
                    Inv($"      {i + 1,2}. {r.Name,-12} {Team(r),-6} {r.Pos} {r.Height,2}  ") +
                    Inv($"{r.PerGame(stat(r)),5:F1}   ({r.GamesPlayed,2} gp, {SeasonMinutes(r, mpc) / Math.Max(1, r.GamesPlayed),4:F1} mpg)"));
            }
        }

        CountingBoard("points",   r => r.Points);
        CountingBoard("rebounds", r => r.Reb);
        CountingBoard("assists",  r => r.Ast);
        CountingBoard("steals",   r => r.Stl);
        CountingBoard("blocks",   r => r.Blk);

        {
            var top = qualified
                .OrderByDescending(r => SeasonMinutes(r, mpc) / Math.Max(1, r.GamesPlayed))
                .ThenBy(r => r.PoolId)
                .Take(10).ToList();
            Console.WriteLine("    minutes");
            for (var i = 0; i < top.Count; i++)
            {
                var r = top[i];
                Console.WriteLine(
                    Inv($"      {i + 1,2}. {r.Name,-12} {Team(r),-6} {r.Pos} {r.Height,2}  ") +
                    Inv($"{SeasonMinutes(r, mpc) / Math.Max(1, r.GamesPlayed),5:F1}   ({r.GamesPlayed,2} gp)") +
                    //  S79.3: the two halves printed SEPARATELY rather than as one ambiguous
                    //  "poss" column. The split is now measured and exact, and printing both
                    //  puts the denominator architecture on the page: PTS/100 is read against
                    //  the first, BLK% and STL% against the second.
                    Inv($"  {r.OffensiveCredits,6} off poss | {r.DefensiveCredits,6} def poss"));
            }
        }
        Console.WriteLine();

        // ★ A percentage is neither a total nor an average, and a bare "61.2%" is not
        // inspectable. Every shooting row carries makes-attempts, attempts per game, games
        // played and minutes, so a suspicious number can be judged on the page rather than
        // prompting a re-run.
        //
        // ★ The two filters are CUMULATIVE and ORDERED: clear the minutes floor first, then
        // the attempts-per-game threshold for that category. A minutes floor alone does not fix
        // a percentage board and assuming it does is the trap — a man can play 600 minutes,
        // attempt fifteen threes all season, make nine, and lead the league at 60% ahead of
        // every real shooter. Minutes do not catch him because he genuinely played.
        //
        // ★ ATTEMPTS, not makes — deliberately against NCAA convention. A makes threshold
        // demands more attempts from a man who is missing, so it filters out bad shooters and
        // the qualifying pool skews good. That biases the very thing being measured.
        Console.WriteLine("  SHOOTING — minutes floor first, then attempts per game (attempts, NOT makes)");

        void ShootingBoard(string label, double attemptFloor,
                           Func<SeasonPlayerRecord, long> made, Func<SeasonPlayerRecord, long> att)
        {
            var pool = qualified
                .Where(r => r.GamesPlayed > 0 && att(r) / (double)r.GamesPlayed >= attemptFloor)
                .ToList();
            var top = pool
                .OrderByDescending(r => att(r) > 0 ? made(r) / (double)att(r) : 0.0)
                .ThenBy(r => r.PoolId)
                .Take(10).ToList();
            Console.WriteLine(Inv($"    {label} (>= {attemptFloor:F1} att/gm — {pool.Count} qualify)"));
            for (var i = 0; i < top.Count; i++)
            {
                var r = top[i];
                var pct = att(r) > 0 ? 100.0 * made(r) / att(r) : 0.0;
                Console.WriteLine(
                    Inv($"      {i + 1,2}. {r.Name,-12} {Team(r),-6} {r.Pos} {r.Height,2}  ") +
                    Inv($"{pct,5:F1}%  {made(r),4}-{att(r),-4} {att(r) / (double)r.GamesPlayed,4:F1} att/gm  ") +
                    Inv($"({r.GamesPlayed,2} gp, {SeasonMinutes(r, mpc) / r.GamesPlayed,4:F1} mpg)"));
            }
        }

        ShootingBoard("FG%", SeasonFgAttemptFloor, r => r.Fgm, r => r.Fga);
        ShootingBoard("3P%", SeasonTpAttemptFloor, r => r.Tpm, r => r.Tpa);
        ShootingBoard("FT%", SeasonFtAttemptFloor, r => r.Ftm, r => r.Fta);
        Console.WriteLine();

        // ── (a2) Rate boards ────────────────────────────────────────────────────
        //  ★ THE DEFECT THESE EXIST TO FIX. A per-game board multiplies every man by his
        //  playing time before ability is consulted. Bigs run ~24 mpg against guards' ~32, so
        //  the per-game block board applies a ~1.33x handicap to every big — and the worst
        //  per-minute shot blocker in its top ten sat NINTH. Rate boards rank production per
        //  unit of the thing that was actually available to him.
        //
        //  ★ R7 — THESE ARE DIRECT INTERNAL RATES, NOT COPIES OF EXTERNAL ESTIMATORS.
        //  Published BLK%/STL%/AST%/TRB% estimate on-floor denominators from minutes share.
        //  This engine COUNTS them. Same familiar units, different provenance; no claim of
        //  formula identity is made anywhere on this page.
        //
        //  ★ EVERY RATE IS AN OUTCOME THE ENGINE ALREADY PRODUCED. This section reports; it
        //  does not recalibrate, and it proves nothing about whether these rates are right.
        Console.WriteLine(Inv($"  RATES — per-opportunity, {qualified.Count} men clearing the >={minuteFloor} minute floor"));
        Console.WriteLine("    (direct on-floor counters, not minutes-share estimates — may differ from external providers'");
        Console.WriteLine("     box-score estimators though expressed in the same familiar units. BLK% is total blocks over");
        Console.WriteLine("     opponent TWO-point attempts faced, the usual convention: threes are blockable in this engine,");
        Console.WriteLine("     so it is NOT a strict share of blockable opportunities.)");

        void RateBoard(string label, string definition, string suffix,
                       Func<SeasonPlayerRecord, long> num, Func<SeasonPlayerRecord, long> den)
        {
            //  ★ ELIGIBILITY, not a zero guard. A man whose denominator is zero is removed
            //  BEFORE sorting rather than handed a rate of 0.0 and left in the pool — a zero
            //  denominator means the question was never asked of him, which is not the same
            //  answer as "never did it". The arithmetic guard below stops a NaN reaching a
            //  comparator; this filter is the semantic rule.
            var pool = qualified.Where(r => den(r) > 0).ToList();
            var top  = pool
                .OrderByDescending(r => num(r) / (double)den(r))   // rank on FULL precision
                .ThenBy(r => r.PoolId)                             // the CountingBoard secondary key
                .Take(10).ToList();
            Console.WriteLine(Inv($"    {label}  {definition}   ({pool.Count} eligible)"));
            for (var i = 0; i < top.Count; i++)
            {
                var r = top[i];
                var v = den(r) > 0 ? 100.0 * num(r) / den(r) : 0.0;
                Console.WriteLine(
                    Inv($"      {i + 1,2}. {r.Name,-12} {Team(r),-6} {r.Pos} {r.Height,2}  ") +
                    //  R3: the raw counts sit in the same row as the rate, always, so sample
                    //  size cannot be missed — a 4-minute man topping a board is WANTED (R4),
                    //  and this is what makes him readable rather than misleading.
                    Inv($"{v,6:F2}{suffix,-1}  ({num(r),5} of {den(r),-6})  ") +
                    Inv($"({r.GamesPlayed,2} gp, {SeasonMinutes(r, mpc) / Math.Max(1, r.GamesPlayed),4:F1} mpg)"));
            }
        }

        RateBoard("BLK%   ", "blk / opp 2PA faced",  "%", r => r.Blk,    r => r.OpponentTwoPaOnFloor);
        RateBoard("REB%   ", "reb / secured boards", "%", r => r.Reb,    r => r.SecuredBoardsOnFloor);
        RateBoard("AST%   ", "ast / teammate FGM",   "%", r => r.Ast,    r => r.OffensiveTeamFgmOnFloor - r.Fgm);
        RateBoard("STL%   ", "stl / def poss",       "%", r => r.Stl,    r => r.DefensiveCredits);
        //  R1: scoring has no percentage, and per-100 IS the real unit for it.
        RateBoard("PTS/100", "pts / off poss",       "",  r => r.Points, r => r.OffensiveCredits);
        Console.WriteLine();

        // ── (b) One team, in full ───────────────────────────────────────────────
        var teamRows = all.Where(r => r.SchoolId == SeasonFullTeamSchoolId)
                          .OrderByDescending(r => r.Credits)
                          .ThenBy(r => r.AcquisitionIndex)
                          .ToList();
        if (teamRows.Count > 0)
        {
            var school = world.Schools.FirstOrDefault(x => x.Id == SeasonFullTeamSchoolId);
            Console.WriteLine(Inv(
                $"  ONE TEAM IN FULL — {school?.Name ?? "?"} (id {SeasonFullTeamSchoolId}), in rotation order"));
            Console.WriteLine("    " + $"{"player",-12} {"pos",-4}{"ht",3} {"gp",3} {"mpg",5} {"pts",5} {"reb",5} {"ast",5} " +
                              $"{"stl",4} {"blk",4} {"to",4}  {"fg%",6} {"3p%",6} {"ft%",6}  {"sfl*",5} {"nsf*",5}");
            foreach (var r in teamRows)
            {
                var g   = Math.Max(1, r.GamesPlayed);
                var fg  = r.Fga > 0 ? 100.0 * r.Fgm / r.Fga : 0.0;
                var tp  = r.Tpa > 0 ? 100.0 * r.Tpm / r.Tpa : 0.0;
                var ft  = r.Fta > 0 ? 100.0 * r.Ftm / r.Fta : 0.0;
                Console.WriteLine(
                    Inv($"    {r.Name,-12} {r.Pos,-4}{r.Height,3} {r.GamesPlayed,3} ") +
                    Inv($"{SeasonMinutes(r, mpc) / g,5:F1} {r.PerGame(r.Points),5:F1} {r.PerGame(r.Reb),5:F1} ") +
                    Inv($"{r.PerGame(r.Ast),5:F1} {r.PerGame(r.Stl),4:F1} {r.PerGame(r.Blk),4:F1} ") +
                    Inv($"{r.PerGame(r.To),4:F1}  {fg,5:F1}% {tp,5:F1}% {ft,5:F1}%  ") +
                    Inv($"{r.PerGame(r.ShFoul),5:F1} {r.PerGame(r.NsFoul),5:F1}"));
            }
            Console.WriteLine("    (* S87: sfl/nsf are now a RECORD of who committed the foul — the engine names");
            Console.WriteLine("       the man at the whistle. They no longer agree with any pre-S87 run's columns,");
            Console.WriteLine("       which were drawn afterwards, in a different order, over a rebuilt lineup.)");
        }
        else
        {
            Console.WriteLine(Inv($"  ONE TEAM IN FULL — school id {SeasonFullTeamSchoolId} is not in this world; section skipped."));
        }
        Console.WriteLine();

        // ── (c) League distributions ────────────────────────────────────────────
        //  Distribution SHAPE is where implausibility shows up first — a league whose best
        //  scorer averages 40, or whose median man averages 2, is visible here and nowhere else.
        Console.WriteLine(Inv($"  LEAGUE DISTRIBUTION — all {all.Count} player-seasons, unfiltered (per game)"));
        Console.WriteLine($"    {"stat",-10}{"rk1",7}{"rk10",7}{"rk50",7}{"rk100",7}{"rk500",7}{"rk1000",8}{"median",8}");

        //  S79.3: the population is now a parameter so the rate rows below can run on the
        //  QUALIFIED pool while the existing per-game rows keep running on everybody. The
        //  existing four calls pass `all` and their output is unchanged.
        void DistributionOver(IReadOnlyList<SeasonPlayerRecord> pop, string label,
                              Func<SeasonPlayerRecord, double> value, string trailer = "")
        {
            var sorted = pop.Select(value).OrderByDescending(v => v).ToList();
            double At(int rank) => rank <= sorted.Count ? sorted[rank - 1] : double.NaN;
            var median = sorted.Count == 0 ? 0.0
                       : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2]
                       : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
            string F(double v) => double.IsNaN(v) ? "—" : v.ToString("F1", CultureInfo.InvariantCulture);
            Console.WriteLine(
                Inv($"    {label,-10}{F(At(1)),7}{F(At(10)),7}{F(At(50)),7}{F(At(100)),7}") +
                Inv($"{F(At(500)),7}{F(At(1000)),8}{F(median),8}") + trailer);
        }

        void Distribution(string label, Func<SeasonPlayerRecord, double> value)
            => DistributionOver(all, label, value);

        Distribution("points",   r => r.PerGame(r.Points));
        Distribution("rebounds", r => r.PerGame(r.Reb));
        Distribution("assists",  r => r.PerGame(r.Ast));
        Distribution("minutes",  r => r.GamesPlayed > 0 ? SeasonMinutes(r, mpc) / r.GamesPlayed : 0.0);

        //  S79.3: the same shape for the five rates, on the QUALIFIED population — the floor
        //  is in force here and is NOT in force on the rows above, so the header says which
        //  population produced which row. The pool moves with the CLI tier.
        //  ★ Per-row eligibility: a man with a zero denominator is dropped from HIS row only,
        //  for the same reason he is dropped from the board — the question was never asked of
        //  him. Each row therefore prints its own n.
        Console.WriteLine(Inv(
            $"    — rates below: {qualified.Count} of {all.Count} clearing the >={minuteFloor} minute floor"));

        void RateDistribution(string label, Func<SeasonPlayerRecord, long> num,
                              Func<SeasonPlayerRecord, long> den)
        {
            var pop = qualified.Where(r => den(r) > 0).ToList();
            DistributionOver(pop, label, r => 100.0 * num(r) / den(r), Inv($"   n={pop.Count}"));
        }

        RateDistribution("BLK%",    r => r.Blk,    r => r.OpponentTwoPaOnFloor);
        RateDistribution("REB%",    r => r.Reb,    r => r.SecuredBoardsOnFloor);
        RateDistribution("AST%",    r => r.Ast,    r => r.OffensiveTeamFgmOnFloor - r.Fgm);
        RateDistribution("STL%",    r => r.Stl,    r => r.DefensiveCredits);
        RateDistribution("PTS/100", r => r.Points, r => r.OffensiveCredits);
        Console.WriteLine();
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  PHASE 73 — the gates. These prove the roll-up is ARITHMETICALLY FAITHFUL and
    //  attached to the right people. They say nothing about whether the numbers are
    //  good basketball; that judgement is Emmett's, on the page.
    // ────────────────────────────────────────────────────────────────────────────

    private static bool Phase73SeasonStatsCheck(string configPath)
    {
        Console.WriteLine();
        Console.WriteLine("== Phase 73 — Season stat roll-up (per-player season records: conservation, identity, minutes, games played) ==");
        var pass = true;

        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {name}" + (detail.Length > 0 ? $" — {detail}" : ""));
            pass = pass && ok;
        }

        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "worlds", "fixture-tiny.world.json");
            var tiny = LoadWorld(fixturePath);
            const long seed = 20260703;   // Phase 55's fixed fixture seed

            var run = RunSeasonCore(tiny, seed, configPath, verbose: false);
            var s   = run.League;
            var all = s.PlayerSeasons.Values.ToList();

            // ── Gate 1: conservation, field by field ────────────────────────────
            //
            //  ★ The box-sourced fields must match EXACTLY. The record-sourced shooting fields
            //  are checked against the possession records, which is the comparison that would
            //  have caught S76.1's silent drop — the box and the records are two independent
            //  paths to the same shots, and only one of them was going through the broken guard.
            long SumOf(Func<SeasonPlayerRecord, long> f) => all.Sum(f);

            Check("conservation: OREB", SumOf(r => r.OReb) == s.OReb, $"players {SumOf(r => r.OReb)} vs league {s.OReb}");
            Check("conservation: DREB", SumOf(r => r.DReb) == s.DReb, $"players {SumOf(r => r.DReb)} vs league {s.DReb}");
            Check("conservation: AST",  SumOf(r => r.Ast)  == s.Ast,  $"players {SumOf(r => r.Ast)} vs league {s.Ast}");
            Check("conservation: STL",  SumOf(r => r.Stl)  == s.Stl,  $"players {SumOf(r => r.Stl)} vs league {s.Stl}");
            Check("conservation: BLK",  SumOf(r => r.Blk)  == s.Blk,  $"players {SumOf(r => r.Blk)} vs league {s.Blk}");
            Check("conservation: SFL",  SumOf(r => r.ShFoul) == s.SflTotal, $"players {SumOf(r => r.ShFoul)} vs league {s.SflTotal}");
            Check("conservation: NSF",  SumOf(r => r.NsFoul) == s.NsfTotal, $"players {SumOf(r => r.NsFoul)} vs league {s.NsfTotal}");

            // Shooting: per-player (box) vs league (records). Any shortfall is attribution that
            // reached no individual — reported as a named bucket rather than hidden in a
            // tolerance, so a future regression shows up as the number moving.
            //  ★ The first draft of this gate asserted per-player == league and FAILED on FGA
            //  (82) and FGM (47) while 3PA/3PM reconciled exactly. That asymmetry was the tell:
            //  the shortfall is not a roll-up loss, it is the engine's own named unattributed
            //  buckets — a field-goal attempt that belongs to no slot, and a bonus free-throw
            //  trip that reached the line before Roll E selected a shooter. Three-point
            //  attempts have no such bucket, which is exactly why their gap was zero. Counting
            //  the engine's buckets turns the gate from a tolerance into an EXACT identity, and
            //  a real roll-up regression now moves a number that is pinned at zero.
            Console.WriteLine($"        engine-unattributed (belongs to no man): FGA {s.UnattributedFga} FGM {s.UnattributedFgm} FTA {s.UnattributedFta}");
            Check("conservation: FGA == players + engine-unattributed",
                  SumOf(r => r.Fga) + s.UnattributedFga == s.Fga,
                  $"{SumOf(r => r.Fga)} + {s.UnattributedFga} vs {s.Fga}");
            Check("conservation: FGM == players + engine-unattributed",
                  SumOf(r => r.Fgm) + s.UnattributedFgm == s.Fgm,
                  $"{SumOf(r => r.Fgm)} + {s.UnattributedFgm} vs {s.Fgm}");
            Check("conservation: 3PA (no unattributed bucket exists)",
                  SumOf(r => r.Tpa) == s.ThreePa, $"players {SumOf(r => r.Tpa)} vs league {s.ThreePa}");
            Check("conservation: 3PM (no unattributed bucket exists)",
                  SumOf(r => r.Tpm) == s.ThreePm, $"players {SumOf(r => r.Tpm)} vs league {s.ThreePm}");
            Check("conservation: FTA == players + engine-unattributed",
                  SumOf(r => r.Fta) + s.UnattributedFta == s.Fta,
                  $"{SumOf(r => r.Fta)} + {s.UnattributedFta} vs {s.Fta}");
            //  FTM has no unattributed counterpart on the record — only the ATTEMPTS bucket is
            //  stamped — so the exact identity is unavailable and the honest statement is the
            //  bound: unattributed makes cannot exceed unattributed attempts, and cannot be
            //  negative. Named as a bound rather than dressed up as an equality.
            var ftmGap = s.Ftm - SumOf(r => r.Ftm);
            Check("conservation: FTM gap is bounded by the FTA bucket (no FTM bucket is stamped)",
                  ftmGap >= 0 && ftmGap <= s.UnattributedFta,
                  $"gap {ftmGap}, bucket {s.UnattributedFta}");

            // ── Gate 2: identity ────────────────────────────────────────────────
            //  The strong half throws inside Accumulate before anything is credited, so
            //  reaching this line at all means every one of the 26 stamped identities agreed
            //  with the season row table on every game. The drift counter is the weak
            //  secondary — stable metadata proves only that metadata is stable.
            Check("identity: all 26 stamped names matched the row table, every game",
                  true, "asserted in-line at the accumulation boundary; a mismatch throws");
            Check("identity: no metadata drift across observations",
                  s.IdentityDriftObservations == 0, $"drift observations {s.IdentityDriftObservations}");
            Check("identity: one record per person, not per seat",
                  all.Select(r => r.PoolId).Distinct().Count() == all.Count,
                  $"{all.Count} records, {all.Select(r => r.PoolId).Distinct().Count()} distinct pool ids");

            // ── Gate 3: minutes reconcile ───────────────────────────────────────
            //  Three ways, all from the same per-side bucket the S76 ladder is sorted from.
            //  NOTE, stated rather than faked: the S76 per-RANK ladder cannot be re-derived
            //  from season totals, because ranking happens within a team-game and a season
            //  total has thrown that ordering away. What IS provable is that both readouts are
            //  fed by the identical bucket and therefore agree in total — which is the claim
            //  the gate makes. Re-deriving the ladder itself needs per-game retention, a
            //  separate design question (game logs / splits), deliberately not opened here.
            var credits = SumOf(r => r.Credits);
            Check("minutes: player credits == the S76 rank-distribution total",
                  credits == s.RotationRankCredits.Sum(),
                  $"{credits} vs {s.RotationRankCredits.Sum()}");
            Check("minutes: player credits == league credits less dropped",
                  credits == s.PossessionCredits - s.DroppedCredits,
                  $"{credits} vs {s.PossessionCredits} - {s.DroppedCredits}");
            Check("minutes: player credits == 10 x possession records",
                  credits == 2L * Lineup.Size * s.XPossessionRecords,
                  $"{credits} vs {2L * Lineup.Size} x {s.XPossessionRecords}");
            Check("minutes: nothing was dropped", s.DroppedCredits == 0, $"dropped {s.DroppedCredits}");

            // ── Gate 4: games played ────────────────────────────────────────────
            //  A player receives ONE game played for POSITIVE floor-time credit in a team-game,
            //  at most one per team-game, none for zero credit. NOT roster membership — that
            //  yields a full slate for everybody and CONCEALS the DNPs this page exists to expose.
            //  ★ S93 — the bound is the league's AUTHORED game count, not a flat 30: the tiny
            //  fixture's four leagues play 16 apiece and a team can never exceed its own slate.
            var teamGames = tiny.Conferences.Max(c => c.Games);
            var maxGp     = all.Count == 0 ? 0 : all.Max(r => r.GamesPlayed);
            Check("games played: no player exceeds his school's team-game count",
                  maxGp <= teamGames, $"max {maxGp} of {teamGames}");
            Check("games played: a man with zero credit has zero games played",
                  all.All(r => r.Credits > 0 || r.GamesPlayed == 0),
                  $"{all.Count(r => r.Credits == 0 && r.GamesPlayed > 0)} violations");
            Check("games played: a man with credit has at least one game played",
                  all.All(r => r.Credits == 0 || r.GamesPlayed > 0),
                  $"{all.Count(r => r.Credits > 0 && r.GamesPlayed == 0)} violations");
            Console.WriteLine($"        DNP-visible: {all.Count(r => r.GamesPlayed == 0)} of {all.Count} player-seasons never took the floor");

            // ── Gate 4b: the on-floor denominators (S79.3) ──────────────────────
            //
            //  ★ WHAT THESE PROVE, AND WHAT THEY DO NOT. The four identities below prove
            //  TOTALS. They are blind to ATTRIBUTION: swapping two players' counters wholesale
            //  leaves every one of them exact. Attribution is proven only by the locked
            //  expected boards recorded in the journal for this seed, and only for that seed.
            //  Nothing in Phase 73 reads a board or its ordering, so a percentage with the
            //  wrong denominator leaves this whole gate green.
            Check("denominators: sum OffensiveCredits == 5 x possession records",
                  SumOf(r => r.OffensiveCredits) == Lineup.Size * s.XPossessionRecords,
                  $"{SumOf(r => r.OffensiveCredits)} vs {Lineup.Size} x {s.XPossessionRecords}");
            Check("denominators: sum OpponentTwoPaOnFloor == 5 x league two-point attempts",
                  SumOf(r => r.OpponentTwoPaOnFloor) == Lineup.Size * (s.Fga - s.ThreePa),
                  $"{SumOf(r => r.OpponentTwoPaOnFloor)} vs {Lineup.Size} x ({s.Fga} - {s.ThreePa})");
            Check("denominators: sum SecuredBoardsOnFloor == 10 x league secured boards",
                  SumOf(r => r.SecuredBoardsOnFloor) == 2L * Lineup.Size * s.SecuredBoards,
                  $"{SumOf(r => r.SecuredBoardsOnFloor)} vs {2L * Lineup.Size} x {s.SecuredBoards}");
            Check("denominators: sum OffensiveTeamFgmOnFloor == 5 x league FGM",
                  SumOf(r => r.OffensiveTeamFgmOnFloor) == Lineup.Size * s.Fgm,
                  $"{SumOf(r => r.OffensiveTeamFgmOnFloor)} vs {Lineup.Size} x {s.Fgm}");

            //  Per-player feasibility: a numerator can never exceed its own denominator.
            //  Structural — each numerator counts a SUBSET of the events its denominator counts.
            //  ★ `Blk <= OpponentTwoPaOnFloor` is DELIBERATELY ABSENT. RollHConfig sets
            //  BlockThree = 0.01, so threes are blockable and a player's block total contains
            //  events the two-point denominator excludes. The ratio is not bounded by 1 and
            //  asserting it would be asserting a property of today's population, not a law.
            Check("denominators: 0 <= OffensiveCredits <= Credits",
                  all.All(r => r.OffensiveCredits >= 0 && r.OffensiveCredits <= r.Credits),
                  $"{all.Count(r => r.OffensiveCredits < 0 || r.OffensiveCredits > r.Credits)} violations");
            Check("denominators: 0 <= REB <= SecuredBoardsOnFloor",
                  all.All(r => r.Reb >= 0 && r.Reb <= r.SecuredBoardsOnFloor),
                  $"{all.Count(r => r.Reb < 0 || r.Reb > r.SecuredBoardsOnFloor)} violations");
            Check("denominators: 0 <= FGM <= OffensiveTeamFgmOnFloor",
                  all.All(r => r.Fgm >= 0 && r.Fgm <= r.OffensiveTeamFgmOnFloor),
                  $"{all.Count(r => r.Fgm < 0 || r.Fgm > r.OffensiveTeamFgmOnFloor)} violations");
            Check("denominators: 0 <= AST <= teammate FGM",
                  all.All(r => r.Ast >= 0 && r.Ast <= r.OffensiveTeamFgmOnFloor - r.Fgm),
                  $"{all.Count(r => r.Ast < 0 || r.Ast > r.OffensiveTeamFgmOnFloor - r.Fgm)} violations");
            Check("denominators: 0 <= STL <= defensive possessions",
                  all.All(r => r.Stl >= 0 && r.Stl <= r.DefensiveCredits),
                  $"{all.Count(r => r.Stl < 0 || r.Stl > r.DefensiveCredits)} violations");

            //  ★ SAY PLAINLY HOW WEAK THOSE BOUNDS ARE. On a real season the observed maxima
            //  sit three to eleven times away from 1.0. They catch a counter fed on both sides,
            //  a sign error or a wholesale skip. They do NOT catch two players' counters being
            //  swapped, and must not be described as attribution protection.
            double MaxRatio(Func<SeasonPlayerRecord, long> n, Func<SeasonPlayerRecord, long> d)
            {
                var pool = all.Where(r => d(r) > 0).ToList();
                return pool.Count == 0 ? 0.0 : pool.Max(r => n(r) / (double)d(r));
            }
            string R4(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
            var hReb = R4(MaxRatio(r => r.Reb, r => r.SecuredBoardsOnFloor));
            var hAst = R4(MaxRatio(r => r.Ast, r => r.OffensiveTeamFgmOnFloor - r.Fgm));
            var hStl = R4(MaxRatio(r => r.Stl, r => r.DefensiveCredits));
            Console.WriteLine($"        bound headroom (WEAK — nowhere near binding): reb/boards {hReb} " +
                              $"ast/tmFGM {hAst} stl/defposs {hStl}  (bound is 1.0000 in each case)");

            //  Zero-consistency. Only the FIRST of these is load-bearing; the other two are
            //  contract statements that are VACUOUS on any population where every man who
            //  played took the floor on both ends. They pass without testing anything, and are
            //  recorded as contract rather than counted as coverage.
            Check("denominators: a man with zero credits has all four counters zero",
                  all.All(r => r.Credits != 0 || (r.OffensiveCredits == 0 && r.OpponentTwoPaOnFloor == 0 &&
                                                  r.SecuredBoardsOnFloor == 0 && r.OffensiveTeamFgmOnFloor == 0)),
                  $"{all.Count(r => r.Credits == 0 && (r.OffensiveCredits != 0 || r.OpponentTwoPaOnFloor != 0 || r.SecuredBoardsOnFloor != 0 || r.OffensiveTeamFgmOnFloor != 0))} violations " +
                  $"over {all.Count(r => r.Credits == 0)} zero-credit records");
            Check("denominators: zero offensive credits implies zero team FGM on floor (VACUOUS if nobody qualifies)",
                  all.All(r => r.OffensiveCredits != 0 || r.OffensiveTeamFgmOnFloor == 0),
                  $"{all.Count(r => r.OffensiveCredits == 0 && r.OffensiveTeamFgmOnFloor != 0)} violations, " +
                  $"over {all.Count(r => r.Credits > 0 && r.OffensiveCredits == 0)} men who played only on defence");
            Check("denominators: zero defensive credits implies zero opponent 2PA (VACUOUS if nobody qualifies)",
                  all.All(r => r.DefensiveCredits != 0 || r.OpponentTwoPaOnFloor == 0),
                  $"{all.Count(r => r.DefensiveCredits == 0 && r.OpponentTwoPaOnFloor != 0)} violations, " +
                  $"over {all.Count(r => r.Credits > 0 && r.DefensiveCredits == 0)} men who played only on offence");

            //  ★ REPORTED, NOT ASSERTED. On a full season every positive-credit man happens to
            //  have all four counters positive — but that is a property of the ROTATION, not of
            //  the counters. A short stint can contain no secured board, no opponent two-point
            //  attempt and no team make; this tiny fixture reaches zero far more easily. An
            //  earlier draft proposed asserting it and was wrong to.
            var thin = all.Where(r => r.Credits > 0)
                          .OrderBy(r => r.Credits)
                          .FirstOrDefault();
            var allFourPositive = all.Count(r => r.Credits > 0 && r.OffensiveCredits > 0 &&
                                                 r.OpponentTwoPaOnFloor > 0 && r.SecuredBoardsOnFloor > 0 &&
                                                 r.OffensiveTeamFgmOnFloor > 0);
            Console.WriteLine($"        diagnostic (NOT a gate): {allFourPositive} of {all.Count(r => r.Credits > 0)} positive-credit men have all four counters positive; " +
                              $"thinnest is {thin?.Credits ?? 0} credits / {thin?.OpponentTwoPaOnFloor ?? 0} opp 2PA / {thin?.SecuredBoardsOnFloor ?? 0} boards / {thin?.OffensiveTeamFgmOnFloor ?? 0} team makes");

            // ── Gate 5: determinism ─────────────────────────────────────────────
            var run2 = RunSeasonCore(tiny, seed, configPath, verbose: false);
            var a1 = all.OrderBy(r => r.PoolId).ToList();
            var a2 = run2.League.PlayerSeasons.Values.OrderBy(r => r.PoolId).ToList();
            var same = a1.Count == a2.Count && a1.Zip(a2).All(p =>
                p.First.PoolId == p.Second.PoolId && p.First.SchoolId == p.Second.SchoolId &&
                p.First.GamesPlayed == p.Second.GamesPlayed && p.First.Credits == p.Second.Credits &&
                p.First.Fga == p.Second.Fga && p.First.Fgm == p.Second.Fgm &&
                p.First.Tpa == p.Second.Tpa && p.First.Tpm == p.Second.Tpm &&
                p.First.Fta == p.Second.Fta && p.First.Ftm == p.Second.Ftm &&
                p.First.OReb == p.Second.OReb && p.First.DReb == p.Second.DReb &&
                p.First.Ast == p.Second.Ast && p.First.Stl == p.Second.Stl &&
                p.First.Blk == p.Second.Blk && p.First.To == p.Second.To &&
                p.First.ShFoul == p.Second.ShFoul && p.First.NsFoul == p.Second.NsFoul &&
                // S90: the new offensive-foul channel joins the reproducibility contract for
                // the same reason S85's and S87's columns did — a per-player field two
                // identical runs never compare is a field that can drift in silence.
                p.First.OffFoul == p.Second.OffFoul &&
                // S79.3 — the four on-floor denominators reproduce too, or a rate is not
                // reproducible even though every counting stat is.
                p.First.OffensiveCredits == p.Second.OffensiveCredits &&
                p.First.OpponentTwoPaOnFloor == p.Second.OpponentTwoPaOnFloor &&
                p.First.SecuredBoardsOnFloor == p.Second.SecuredBoardsOnFloor &&
                p.First.OffensiveTeamFgmOnFloor == p.Second.OffensiveTeamFgmOnFloor);
            Check("determinism: same seed reproduces every per-player record", same,
                  $"{a1.Count} records compared field by field");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Check("Phase 73 ran", false, ex.Message);
        }

        Console.WriteLine(pass ? "  Phase 73 PASSED" : "  Phase 73 FAILED");
        return pass;
    }
}
