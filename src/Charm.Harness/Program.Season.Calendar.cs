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
//  both orders write every file byte for byte. No production caller passes it.
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
    /// slate, the buy games. The optional callback sees each result (the buy games keep a row).</summary>
    private sealed class SeasonStaticSource : SeasonGameSource
    {
        private readonly string _name;
        private readonly IReadOnlyList<PlayedSeasonGame> _games;
        private readonly Action<int, PlayedSeasonGame, int, int>? _onResult;

        public SeasonStaticSource(string name, IReadOnlyList<PlayedSeasonGame> games,
                                  Action<int, PlayedSeasonGame, int, int>? onResult = null)
        {
            _name = name;
            _games = games;
            _onResult = onResult;
        }

        public override string Name => _name;
        public override int Count => _games.Count;
        public override int Ordinal(int local) => _games[local].FixtureOrdinal;
        public override DateOnly? Date(int local) => _games[local].Game.Date;
        public override PlayedSeasonGame Release(int local) => _games[local];
        public override void Finish(int local, PlayedSeasonGame played, int homeScore, int awayScore)
            => _onResult?.Invoke(local, played, homeScore, awayScore);
    }

    /// <summary>★ S122 — what the loop did: the ordinals in the order they were played, and the
    /// season's span.</summary>
    private sealed record SeasonCalendarPlay(
        IReadOnlyList<int> PlayOrder,
        DateOnly FirstLegalDay,
        DateOnly LastLegalDay);

    /// <summary>★ S122 — THE DAY LOOP. Every game of every source, played on its date from the
    /// season's first legal day to its last, each day's games in ordinal order; or, for Phase
    /// 113 only, every game in ascending ordinal order (the pre-S122 order).
    ///
    /// <para>Refused by name, all before the first tip unless noted: two games claiming one
    /// ordinal, a gap in the ordinals, an undated game, a game dated outside the season, a later
    /// round dated on or before one of its feeders. During play: a released game that is not
    /// the fixture it claimed to be, a school playing twice on one day. After the loop: a game
    /// never played, a source left incomplete.</para></summary>
    private static SeasonCalendarPlay SeasonPlayCalendar(
        IReadOnlyList<SeasonGameSource> sources,
        int seasonYear,
        SeasonPlayOrder order,
        Func<PlayedSeasonGame, (int HomeScore, int AwayScore)> play,
        Action<string, int>? monthDone = null)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        var calendar = new BasketballSeasonCalendar(seasonYear);

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

        // ── The walk. ─────────────────────────────────────────────────────────────
        IEnumerable<int> Walk()
        {
            if (order == SeasonPlayOrder.FixtureOrdinalForTest)
            {
                for (var g = 0; g < total; g++) yield return g;
                yield break;
            }
            var byDay = new Dictionary<DateOnly, List<int>>();
            for (var g = 0; g < total; g++)   // ascending, so each day's list is in ordinal order
            {
                var d = slots[g]!.Value.Date;
                if (!byDay.TryGetValue(d, out var list)) byDay[d] = list = new List<int>();
                list.Add(g);
            }
            for (var day = calendar.FirstLegalDay; day <= calendar.LastLegalDay; day = day.AddDays(1))
                if (byDay.TryGetValue(day, out var todays))
                    foreach (var g in todays) yield return g;
        }

        var playOrder = new List<int>(total);
        var played = new bool[total];
        var schoolsToday = new HashSet<int>();
        DateOnly? currentDay = null;
        string? currentMonth = null;
        var monthGames = 0;
        foreach (var g in Walk())
        {
            var (source, local, date) = slots[g]!.Value;
            if (currentDay != date)
            {
                currentDay = date;
                schoolsToday.Clear();
            }
            var month = date.ToString("MMMM", CultureInfo.InvariantCulture);
            if (currentMonth != month)
            {
                if (currentMonth is not null) monthDone?.Invoke(currentMonth, monthGames);
                currentMonth = month;
                monthGames = 0;
            }

            var pg = source.Release(local);
            if (pg.FixtureOrdinal != g || pg.Game.Date != date)
                throw new InvalidOperationException(Inv(
                    $"SEASON CALENDAR: {source.Name} game {local} was released as ordinal {pg.FixtureOrdinal} on ") +
                    Inv($"{pg.Game.Date:yyyy-MM-dd} but its slot is ordinal {g} on {date:yyyy-MM-dd}."));
            if (!schoolsToday.Add(pg.Game.HomeId) || !schoolsToday.Add(pg.Game.AwayId))
                throw new InvalidOperationException(Inv(
                    $"SEASON CALENDAR: a school plays twice on {date:yyyy-MM-dd} (ordinal {g}, {source.Name}: ") +
                    Inv($"{pg.Game.HomeId} v {pg.Game.AwayId}); a game cannot read a same-day result, so this is refused."));

            var (home, away) = play(pg);
            source.Finish(local, pg, home, away);
            played[g] = true;
            playOrder.Add(g);
            monthGames++;
        }
        if (currentMonth is not null) monthDone?.Invoke(currentMonth, monthGames);

        for (var g = 0; g < total; g++)
            if (!played[g])
                throw new InvalidOperationException(Inv(
                    $"SEASON CALENDAR: ordinal {g} ({slots[g]!.Value.Source.Name}, {slots[g]!.Value.Date:yyyy-MM-dd}) was never played."));
        foreach (var source in sources) source.AssertComplete();

        return new SeasonCalendarPlay(playOrder, calendar.FirstLegalDay, calendar.LastLegalDay);
    }
}
