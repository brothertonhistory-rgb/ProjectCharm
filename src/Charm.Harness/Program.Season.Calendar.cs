using System.Globalization;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
//  S122 — THE SEASON ON THE CALENDAR.
//
//  Until this session the season was played in fixture-ordinal order: the whole
//  league slate first, then the November tournaments, the showcases, the
//  conference tournaments and the November buy games — every one of them after
//  March, each carrying its real date but not played on it. That was harmless
//  while nothing carried from one game to the next, and it stops being harmless
//  the moment anything happens week to week (practice, injuries, recruiting).
//
//  Now the season is played ONE DAY AT A TIME, from November 1 to the championship
//  Monday, each day playing exactly that day's slate (Emmett, C-67).
//
//  ★ NO SEED MOVES. A game's fixture ordinal is still its engine seed (base + 2g,
//  base + 2g + 1), its log ordinal and its index in every per-game list. Only the
//  order in which games are PLAYED changes, and that cannot move a result because
//  no game reads another game's result except in two structural cases, both
//  handled here by construction:
//    - a later bracket or conference-tournament round reads its feeders' winners,
//      and every later round is dated STRICTLY after its feeders (asserted on the
//      slot table before the first tip, never trusted);
//    - a conference tournament is seeded from its league's records, which it now
//      reads the morning it opens, from its own league's games only, and it refuses
//      by name if any of them is unplayed.
//
//  ★ THE LOOP OWNS PLAY. A source never plays a game. A source states, for each of
//  its games, the ordinal, the date and the feeders; when the loop reaches a game
//  the source releases it (building the fixture once both sides are known) and is
//  handed the result. Static sources — the league slate, the showcases, the buy
//  games — know every fixture before November 1. Dynamic sources — each bracket
//  event and each conference tournament — fill later rounds from results.
//
//  ★ WITHIN ONE DAY, GAMES PLAY IN ORDINAL ORDER (K5). No game reads a same-day
//  result: no school plays twice in a day (asserted as each game is released) and
//  no later round shares its feeder's day (asserted on the slot table), so the
//  order within a day cannot change a result; ordinal order makes it deterministic.
//
//  ★ FixtureOrdinalForTest replays the OLD order — ordinals ascending — through the
//  very same sources. It exists for exactly two things in Phase 113: the negative
//  control (the old order puts November after March) and the same-machine proof that
//  both orders write every file byte for byte. ★ S123 — Phase 114 also uses it, to prove a
//  chunked advance in that order is refused (K4). No production caller passes it.
// ============================================================================

internal static partial class Program
{
    /// <summary>★ S122 — which order the season's games are played in. Calendar is the only
    /// production value.</summary>
    private enum SeasonPlayOrder
    {
        Calendar,
        FixtureOrdinalForTest,
    }

    /// <summary>★ S122 — one kind of game a season plays: the league slate, one bracket event,
    /// the showcases, one conference tournament, the buy games. The loop asks it for each
    /// game's ordinal, date and feeders before the first tip, then releases each game in turn
    /// and hands it the result. A source never plays anything itself.</summary>
    private abstract class SeasonGameSource
    {
        /// <summary>A name for refusals: "the league slate", "Maui Invitational", "conference 7".</summary>
        public abstract string Name { get; }

        /// <summary>How many games this source owes. Local indices are 0..Count-1.</summary>
        public abstract int Count { get; }

        public abstract int Ordinal(int local);

        /// <summary>The game's date, known before the first tip for every kind (a later round's
        /// date depends on its round, never on who wins).</summary>
        public abstract DateOnly? Date(int local);

        /// <summary>The local indices of the games whose results decide this game's sides.
        /// Empty for every game whose sides are known before the season.</summary>
        public virtual IEnumerable<int> Feeders(int local) => Array.Empty<int>();

        /// <summary>Build the game now that its turn has come. Refuses by name if a side is
        /// still unknown or (a conference tournament) its seeding cannot yet be read.</summary>
        public abstract PlayedSeasonGame Release(int local);

        /// <summary>The result of a game this source released.</summary>
        public virtual void Finish(int local, PlayedSeasonGame played, int homeScore, int awayScore) { }

        /// <summary>After the loop: refuses by name if any game is unplayed or the source's own
        /// bookkeeping did not close (a bracket with a place unassigned, a tournament with no
        /// champion).</summary>
        public virtual void AssertComplete() { }
    }

    /// <summary>★ S122 — a source whose every fixture is known before November 1: the league
    /// slate. ★ S123 — its result callback is gone: no caller ever set it (the buy games keep their
    /// rows in their own source), and the state inventory (Phase 114 C3) carries no unexplained
    /// delegate.</summary>
    private sealed class SeasonStaticSource : SeasonGameSource
    {
        private readonly string _name;
        private readonly IReadOnlyList<PlayedSeasonGame> _games;

        public SeasonStaticSource(string name, IReadOnlyList<PlayedSeasonGame> games)
        {
            _name = name;
            _games = games;
        }

        public override string Name => _name;
        public override int Count => _games.Count;
        public override int Ordinal(int local) => _games[local].FixtureOrdinal;
        public override DateOnly? Date(int local) => _games[local].Game.Date;
        public override PlayedSeasonGame Release(int local) => _games[local];
    }

    /// <summary>★ S122 — what the loop did: the ordinals in the order they were played, and the
    /// season's span.</summary>
    private sealed record SeasonCalendarPlay(
        IReadOnlyList<int> PlayOrder,
        DateOnly FirstLegalDay,
        DateOnly LastLegalDay);

    /// <summary>★ S122 — THE DAY LOOP, in one call: every game of every source, played on its date
    /// from the season's first legal day to its last, each day's games in ordinal order; or, for
    /// Phase 113 only, every game in ascending ordinal order (the pre-S122 order).
    ///
    /// <para>★ S123 — now a wrapper over <see cref="SeasonCalendarRun"/>: build (the slot table and
    /// its four refusals, before anything plays), advance through the last legal day, finish. Same
    /// signature, same refusals at the same moments; Phase 113 calls it directly.</para></summary>
    private static SeasonCalendarPlay SeasonPlayCalendar(
        IReadOnlyList<SeasonGameSource> sources,
        int seasonYear,
        SeasonPlayOrder order,
        Func<PlayedSeasonGame, (int HomeScore, int AwayScore)> play,
        Action<string, int>? monthDone = null)
    {
        using var run = new SeasonCalendarRun(sources, seasonYear, order, play, monthDone);
        run.AdvanceThrough(run.LastLegalDay);
        return run.Finish();
    }

    // ============================================================================
    //  ★ S123 — THE DAY LOOP AS AN OBJECT THAT CAN STOP (save design, Phase A).
    //
    //  The constructor is S122's slot table, built and proven before anything plays,
    //  unchanged. The walk's six locals (played, playOrder, schoolsToday, currentDay,
    //  currentMonth, monthGames) are fields, so a season advanced in chunks walks
    //  exactly the games, in exactly the order, that one call walks:
    //
    //    - A chunk boundary always falls between two days. `schoolsToday` resets
    //      only when the date changes, as before.
    //    - The month line fires when the next PLAYED game is in a new month, as
    //      before; a chunk ending mid-month does not fire it. The last month's line
    //      fires in Finish, where the loop's end fired it.
    //
    //  THE CONTRACT (K3): NextDay is the first day not yet played. AdvanceThrough(day)
    //  plays [NextDay, day] and sets NextDay = day + 1; day == NextDay - 1 is a no-op;
    //  earlier is refused (backwards); past LastLegalDay is refused. Finish is legal
    //  only once, only when complete.
    //
    //  FIXTURE ORDER (K4, Phase 113 only): the only day accepted is LastLegalDay. The
    //  first such call plays every game in ordinal order; any later one is the no-op;
    //  any other day is refused — a partial advance means nothing in fixture order.
    //
    //  LIFECYCLE (K9): a precondition refusal is raised before anything plays and
    //  leaves the run usable. Any exception while playing or finishing FAULTS the run:
    //  the original exception propagates unchanged, and every later call is refused
    //  by name. A faulted run is never retried in memory. Dispose is idempotent;
    //  after it every call is refused.
    // ============================================================================
    private sealed class SeasonCalendarRun : IDisposable
    {
        private readonly IReadOnlyList<SeasonGameSource> _sources;
        private readonly SeasonPlayOrder _order;
        private readonly Func<PlayedSeasonGame, (int HomeScore, int AwayScore)> _play;
        private readonly Action<string, int>? _monthDone;
        private readonly DateOnly _firstLegalDay;
        private readonly DateOnly _lastLegalDay;
        private readonly int _total;
        private readonly (SeasonGameSource Source, int Local, DateOnly Date)?[] _slots;
        //  ★ A2 — S122's Walk() built this inside the iterator; it is a pure function of the slot
        //    table, so it is built once here and held. Each day's list is in ordinal order.
        private readonly Dictionary<DateOnly, List<int>> _byDay;

        //  The walk state (K5).
        private readonly List<int> _playOrder;
        private readonly bool[] _played;
        private readonly HashSet<int> _schoolsToday;
        private DateOnly? _currentDay;
        private string? _currentMonth;
        private int _monthGames;
        private DateOnly _nextDay;

        //  The lifecycle (K9).
        private bool _finished;
        private bool _disposed;
        private string? _faultedOn;

        private static string Inv(FormattableString f) => FormattableString.Invariant(f);

        public SeasonCalendarRun(
            IReadOnlyList<SeasonGameSource> sources,
            int seasonYear,
            SeasonPlayOrder order,
            Func<PlayedSeasonGame, (int HomeScore, int AwayScore)> play,
            Action<string, int>? monthDone = null)
        {
            _sources = sources;
            _order = order;
            _play = play;
            _monthDone = monthDone;
            var calendar = new BasketballSeasonCalendar(seasonYear);
            _firstLegalDay = calendar.FirstLegalDay;
            _lastLegalDay = calendar.LastLegalDay;

            // ── The slot table, built and proven before anything plays. ─────────────────
            var total = sources.Sum(s => s.Count);
            var slots = new (SeasonGameSource Source, int Local, DateOnly Date)?[total];
            foreach (var source in sources)
                for (var local = 0; local < source.Count; local++)
                {
                    var ordinal = source.Ordinal(local);
                    if (ordinal < 0 || ordinal >= total)
                        throw new InvalidOperationException(Inv(
                            $"SEASON CALENDAR: {source.Name} game {local} claims ordinal {ordinal}; this season's {total} games hold 0..{total - 1}."));
                    if (slots[ordinal] is { } taken)
                        throw new InvalidOperationException(Inv(
                            $"SEASON CALENDAR: ordinal {ordinal} is claimed by both {taken.Source.Name} and {source.Name}."));
                    if (source.Date(local) is not { } date)
                        throw new InvalidOperationException(Inv(
                            $"SEASON CALENDAR: {source.Name} game {local} (ordinal {ordinal}) has no date; every game the season plays is played on a day."));
                    if (!calendar.Contains(date))
                        throw new InvalidOperationException(Inv(
                            $"SEASON CALENDAR: {source.Name} game {local} (ordinal {ordinal}) is dated {date:yyyy-MM-dd}, outside the season ") +
                            Inv($"{calendar.FirstLegalDay:yyyy-MM-dd}..{calendar.LastLegalDay:yyyy-MM-dd}."));
                    slots[ordinal] = (source, local, date);
                }
            for (var g = 0; g < total; g++)
                if (slots[g] is null)
                    throw new InvalidOperationException(Inv($"SEASON CALENDAR: no game holds ordinal {g}; the ordinals are contiguous from zero."));

            //  ★ A later round waits for its feeders BY DATE. A feeder on the same day would play
            //    first only because its ordinal is lower — exactly the order-dependence this
            //    session removes — so it is refused here, before anything tips.
            foreach (var source in sources)
                for (var local = 0; local < source.Count; local++)
                {
                    var date = source.Date(local)!.Value;
                    foreach (var feeder in source.Feeders(local))
                    {
                        var fd = source.Date(feeder)!.Value;
                        if (fd >= date)
                            throw new InvalidOperationException(Inv(
                                $"SEASON CALENDAR: {source.Name} game {local} ({date:yyyy-MM-dd}) is fed by game {feeder} ({fd:yyyy-MM-dd}); ") +
                                "a later round must be dated strictly after every game that feeds it.");
                    }
                }

            _total = total;
            _slots = slots;
            _byDay = new Dictionary<DateOnly, List<int>>();
            for (var g = 0; g < total; g++)   // ascending, so each day's list is in ordinal order
            {
                var d = slots[g]!.Value.Date;
                if (!_byDay.TryGetValue(d, out var list)) _byDay[d] = list = new List<int>();
                list.Add(g);
            }

            _playOrder = new List<int>(total);
            _played = new bool[total];
            _schoolsToday = new HashSet<int>();
            _nextDay = _firstLegalDay;
        }

        public DateOnly FirstLegalDay => _firstLegalDay;
        public DateOnly LastLegalDay => _lastLegalDay;

        /// <summary>The first day not yet played.</summary>
        public DateOnly NextDay { get { RefuseIfDeadOrFaulted(nameof(NextDay)); return _nextDay; } }

        /// <summary>True exactly when every day of the season has been walked.</summary>
        public bool IsComplete { get { RefuseIfDeadOrFaulted(nameof(IsComplete)); return _nextDay > _lastLegalDay; } }

        /// <summary>How many games have been played so far (Phase 114's no-op check).</summary>
        public int GamesPlayed { get { RefuseIfDeadOrFaulted(nameof(GamesPlayed)); return _playOrder.Count; } }

        /// <summary>Null unless the run faulted; then the day it failed on. Never refused — the
        /// season run reads it to fault itself with the same day.</summary>
        public string? FaultedOn => _faultedOn;

        private void RefuseIfDeadOrFaulted(string call)
        {
            if (_disposed)
                throw new InvalidOperationException(
                    $"SEASON RUN: {call} was called on a season run that has been disposed.");
            if (_faultedOn is not null)
                throw new InvalidOperationException(
                    $"SEASON RUN: {call} refused — this season run failed on {_faultedOn} and cannot continue in memory.");
        }

        public void AdvanceThrough(DateOnly day)
        {
            RefuseIfDeadOrFaulted(nameof(AdvanceThrough));
            if (_finished)
                throw new InvalidOperationException(Inv(
                    $"SEASON RUN: AdvanceThrough({day:yyyy-MM-dd}) after Finish; a finished season plays nothing more."));
            if (day > _lastLegalDay)
                throw new InvalidOperationException(Inv(
                    $"SEASON RUN: AdvanceThrough({day:yyyy-MM-dd}) is past the last day of the season ({_lastLegalDay:yyyy-MM-dd})."));
            var lastPlayed = _nextDay.AddDays(-1);
            if (day == lastPlayed) return;   // the no-op
            if (day < lastPlayed)
                throw new InvalidOperationException(Inv(
                    $"SEASON RUN: AdvanceThrough({day:yyyy-MM-dd}) goes backwards; the season has already played through {lastPlayed:yyyy-MM-dd}."));
            if (_order == SeasonPlayOrder.FixtureOrdinalForTest && day != _lastLegalDay)
                throw new InvalidOperationException(Inv(
                    $"SEASON RUN: AdvanceThrough({day:yyyy-MM-dd}) in fixture order; a partial advance has no meaning there — ") +
                    Inv($"the only day accepted is the last, {_lastLegalDay:yyyy-MM-dd}."));

            //  Past this line anything thrown faults the run.
            DateOnly playing = _nextDay;
            try
            {
                if (_order == SeasonPlayOrder.FixtureOrdinalForTest)
                {
                    for (var g = 0; g < _total; g++)
                    {
                        playing = _slots[g]!.Value.Date;
                        PlayOne(g);
                    }
                    _nextDay = _lastLegalDay.AddDays(1);
                    return;
                }
                for (var d = _nextDay; d <= day; d = d.AddDays(1))
                {
                    playing = d;
                    if (_byDay.TryGetValue(d, out var todays))
                        foreach (var g in todays) PlayOne(g);
                    _nextDay = d.AddDays(1);
                }
            }
            catch
            {
                _faultedOn = playing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                throw;
            }
        }

        //  One game: the S122 loop body, moved whole.
        private void PlayOne(int g)
        {
            var (source, local, date) = _slots[g]!.Value;
            if (_currentDay != date)
            {
                _currentDay = date;
                _schoolsToday.Clear();
            }
            var month = date.ToString("MMMM", CultureInfo.InvariantCulture);
            if (_currentMonth != month)
            {
                if (_currentMonth is not null) _monthDone?.Invoke(_currentMonth, _monthGames);
                _currentMonth = month;
                _monthGames = 0;
            }

            var pg = source.Release(local);
            if (pg.FixtureOrdinal != g || pg.Game.Date != date)
                throw new InvalidOperationException(Inv(
                    $"SEASON CALENDAR: {source.Name} game {local} was released as ordinal {pg.FixtureOrdinal} on ") +
                    Inv($"{pg.Game.Date:yyyy-MM-dd} but its slot is ordinal {g} on {date:yyyy-MM-dd}."));
            if (!_schoolsToday.Add(pg.Game.HomeId) || !_schoolsToday.Add(pg.Game.AwayId))
                throw new InvalidOperationException(Inv(
                    $"SEASON CALENDAR: a school plays twice on {date:yyyy-MM-dd} (ordinal {g}, {source.Name}: ") +
                    Inv($"{pg.Game.HomeId} v {pg.Game.AwayId}); a game cannot read a same-day result, so this is refused."));

            var (home, away) = _play(pg);
            source.Finish(local, pg, home, away);
            _played[g] = true;
            _playOrder.Add(g);
            _monthGames++;
        }

        /// <summary>The loop's end: the last month's line, then every game played and every source
        /// complete, or the season refuses by name.</summary>
        public SeasonCalendarPlay Finish()
        {
            RefuseIfDeadOrFaulted(nameof(Finish));
            if (_finished)
                throw new InvalidOperationException("SEASON RUN: Finish was called twice; a season closes once.");
            if (_nextDay <= _lastLegalDay)
                throw new InvalidOperationException(Inv(
                    $"SEASON RUN: Finish before the season is complete; it has played through {_nextDay.AddDays(-1):yyyy-MM-dd} ") +
                    Inv($"and its last day is {_lastLegalDay:yyyy-MM-dd}."));
            try
            {
                if (_currentMonth is not null) _monthDone?.Invoke(_currentMonth, _monthGames);

                for (var g = 0; g < _total; g++)
                    if (!_played[g])
                        throw new InvalidOperationException(Inv(
                            $"SEASON CALENDAR: ordinal {g} ({_slots[g]!.Value.Source.Name}, {_slots[g]!.Value.Date:yyyy-MM-dd}) was never played."));
                foreach (var source in _sources) source.AssertComplete();
            }
            catch
            {
                _faultedOn = "its close, after " + _lastLegalDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                throw;
            }
            _finished = true;
            return new SeasonCalendarPlay(_playOrder, _firstLegalDay, _lastLegalDay);
        }

        /// <summary>Owns nothing to release; marks the run dead. Idempotent.</summary>
        public void Dispose() => _disposed = true;
    }
}
