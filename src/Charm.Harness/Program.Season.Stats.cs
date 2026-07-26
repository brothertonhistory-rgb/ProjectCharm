using System.Globalization;
using Charm.Engine;

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

        public long Points => 2 * Fgm + Tpm + Ftm;
        public long Reb    => OReb + DReb;

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
                    Inv($"{SeasonMinutes(r, mpc) / Math.Max(1, r.GamesPlayed),5:F1}   ({r.GamesPlayed,2} gp)"));
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
            Console.WriteLine("    (* sfl/nsf are POST-HOC HARNESS DRAWS, not a record of who committed a foul —");
            Console.WriteLine("       the committer is allocated by weight in AttributeGame, not chosen by the engine.)");
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

        void Distribution(string label, Func<SeasonPlayerRecord, double> value)
        {
            var sorted = all.Select(value).OrderByDescending(v => v).ToList();
            double At(int rank) => rank <= sorted.Count ? sorted[rank - 1] : double.NaN;
            var median = sorted.Count == 0 ? 0.0
                       : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2]
                       : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;
            string F(double v) => double.IsNaN(v) ? "—" : v.ToString("F1", CultureInfo.InvariantCulture);
            Console.WriteLine(
                Inv($"    {label,-10}{F(At(1)),7}{F(At(10)),7}{F(At(50)),7}{F(At(100)),7}") +
                Inv($"{F(At(500)),7}{F(At(1000)),8}{F(median),8}"));
        }

        Distribution("points",   r => r.PerGame(r.Points));
        Distribution("rebounds", r => r.PerGame(r.Reb));
        Distribution("assists",  r => r.PerGame(r.Ast));
        Distribution("minutes",  r => r.GamesPlayed > 0 ? SeasonMinutes(r, mpc) / r.GamesPlayed : 0.0);
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
            //  yields 30 for everybody and CONCEALS the DNPs this page exists to expose.
            var teamGames = tiny.Schools.Count * 30 / 2 * 2 / tiny.Schools.Count;   // == 30
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
                p.First.ShFoul == p.Second.ShFoul && p.First.NsFoul == p.Second.NsFoul);
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
