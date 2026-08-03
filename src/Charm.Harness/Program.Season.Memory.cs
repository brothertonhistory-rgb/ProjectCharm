using System.Globalization;
using Charm.History;

namespace Charm.Harness;

// ============================================================================
//  S96 — A SEASON REMEMBERS WHO HOSTED.
//
//  The basketball: a conference pair that meets an ODD number of times cannot
//  split those meetings evenly, so one of the two schools gets the extra home
//  game. Until this session that residual was decided the same way every single
//  year — the slate takes no randomness and had no past — so in a career, the
//  same school hosted the extra game forever. This layer reads last season's
//  retained log, sees who got it, and fixes the residual to the OTHER school.
//
//  ★ MEMORY MEANS SEASON N-1, AND IT IS FOUND BY ARITHMETIC, NEVER BY LOOKING
//  AROUND. The season about to be scheduled is the one the history's counter is
//  about to hand out; the previous CAREER season is that minus one. The
//  candidate file path is computed from that number and either exists or does
//  not. There is deliberately no directory listing, no filename parsing and no
//  "highest season found" search — that would be the LATEST RETAINED season,
//  which is a different thing. A career that logged season 1, ran season 2
//  without retention, and then reached season 3 would flip season 1's hosts and
//  call it year-over-year alternation. It would have passed every check.
//
//  ★ NO FALLBACK, EVER. Once the candidate is season N-1's log, any failure to
//  find, read or validate it disables host memory for this run. No other log is
//  opened. A valid season-1 log sitting beside a corrupt season-2 log yields NO
//  memory — never season-1 memory.
//
//  ★ SCHEDULE FACTS ONLY, and the boundary is enforced by what this code can
//  see. Each log block is projected IMMEDIATELY into three fields — home school,
//  away school, conference-or-not — and nothing wider is carried past that line.
//  The log also holds scores, possession counts, rosters, ratings and every
//  man's stat line; none of it reaches the scheduler, and the projection is why,
//  rather than a promise that nobody will read them.
//
//  ★ EVERY WAY THIS CAN COME UP EMPTY IS A VALUE, NOT AN EXCEPTION. A missing
//  history, a first season, an unretained season, a damaged file — each is a
//  named status carrying what was attempted, and the season proceeds exactly as
//  it did before this session existed. A career must not fail to schedule
//  because a file from last year is unreadable.
// ============================================================================

internal static partial class Program
{
    /// <summary>Why this season does or does not have last season's hosts.</summary>
    private enum HostMemoryStatus
    {
        /// <summary>Legacy mode — no career is attached, so there is no previous season.</summary>
        NoHistory,
        /// <summary>This is season 1 of the career. Season 0 was never a candidate.</summary>
        FirstSeason,
        /// <summary>Season N-1 exists as a number but published no log — it either ran
        /// without retention or failed before finalizing.</summary>
        NoPublishedLog,
        /// <summary>The candidate log is present and could not be trusted.</summary>
        Unreadable,
        /// <summary>A complete, valid season N-1 log was read.</summary>
        Loaded,
    }

    /// <summary>The classified reason a candidate log was refused. Normalized categories,
    /// never a raw exception and never parsed message text.
    ///
    /// <para>★ THE CATEGORIES FOLLOW THE READER'S OWN TYPED FAILURE SURFACE. The rule is
    /// to preserve the finest STABLE distinction the reader actually exposes and to invent
    /// none. `GameLogError` names the three binding failures separately, so they stay
    /// separate here: a log from another career, another world and another season are three
    /// genuinely different things to see on a page. It does NOT distinguish the many kinds
    /// of internal damage — a bad block checksum, a bad payload digest, a footer whose
    /// counts disagree, bytes after the footer — so those collapse into one honest
    /// <see cref="Corrupt"/>.</para></summary>
    private enum HostMemoryProblem
    {
        None,
        /// <summary>The log belongs to a different career lineage.</summary>
        WrongCareer,
        /// <summary>The log was written against a different world.</summary>
        WrongWorld,
        /// <summary>The log is not the season it was asked for.</summary>
        WrongSeason,
        /// <summary>The file ends partway through, or has no season footer.</summary>
        Truncated,
        /// <summary>Not a retention log at all, or a format this build cannot read.</summary>
        UnsupportedVersion,
        /// <summary>The file is structurally complete and does not match itself —
        /// checksums, digests, footer counts, ordering, domains.</summary>
        Corrupt,
        /// <summary>The filesystem refused the read.</summary>
        IoFailure,
        /// <summary>The log read cleanly and its own schedule facts cannot be true: a pair
        /// whose home counts cannot represent an even split plus at most one residual, or a
        /// game whose two sides are the same school.</summary>
        InconsistentPairFacts,
    }

    /// <summary>Last season's residual hosts, and the honest story of where they came from.
    ///
    /// <para>★ THE STATE TABLE, asserted by Phase 87 C2 rather than trusted here:
    /// <c>NoHistory</c> carries no source, no attempt, no problem. <c>FirstSeason</c> carries
    /// no attempt either — season 0 was never a candidate, so naming it would invent one.
    /// <c>NoPublishedLog</c> carries the attempt and no problem: nothing went wrong, there
    /// is simply no file. <c>Unreadable</c> carries the attempt and a problem. <c>Loaded</c>
    /// carries source == attempt and no problem.</para></summary>
    /// <param name="PreviousResidualHost">For an odd-meeting pair (Lo, Hi), the school that
    /// hosted the extra game last season. Empty for every status but Loaded — and legitimately
    /// empty for Loaded too, when last season's league had no odd pairs at all.</param>
    /// <param name="ConferenceGamesRead">Conference blocks actually projected out of the
    /// candidate. This SURVIVES a rejection at the consistency stage, as evidence of how far
    /// the read got. A failure inside the reader itself leaves it at zero, because the reader
    /// is all-or-nothing and hands back no blocks at all.</param>
    /// <param name="ResidualPairsRemembered">Odd pairs found. Zero unless the ENTIRE source
    /// validated — a partially-trusted memory is not a thing this layer produces.</param>
    private sealed record HostMemory(
        IReadOnlyDictionary<(int Lo, int Hi), int> PreviousResidualHost,
        HostMemoryStatus Status,
        long? SourceSeasonId,
        long? AttemptedSeasonId,
        HostMemoryProblem Problem,
        int ConferenceGamesRead,
        int ResidualPairsRemembered)
    {
        private static readonly Dictionary<(int Lo, int Hi), int> NoPairs = new();

        internal static HostMemory Empty(HostMemoryStatus status, long? attempted,
                                         HostMemoryProblem problem = HostMemoryProblem.None,
                                         int conferenceGamesRead = 0)
            => new(NoPairs, status, null, attempted, problem, conferenceGamesRead, 0);
    }

    /// <summary>What the season did with its memory, carried out of the schedule builder so
    /// the page reports the run that happened rather than re-deriving it.</summary>
    /// <param name="ResidualsFlipped">Memory-derived fixed hosts actually supplied to
    /// conference slates that built — applied, not merely remembered.</param>
    /// <param name="LeaguesWithResidualsFlipped">Conference slates that received at least one.
    /// The page's "across N leagues" has a runtime source and can never be a stock constant.</param>
    private sealed record SeasonMemoryOutcome(
        HostMemoryStatus Status,
        long? SourceSeasonId,
        long? AttemptedSeasonId,
        HostMemoryProblem Problem,
        int ResidualsFlipped,
        int LeaguesWithResidualsFlipped)
    {
        internal static readonly SeasonMemoryOutcome None =
            new(HostMemoryStatus.NoHistory, null, null, HostMemoryProblem.None, 0, 0);
    }

    // ── Reading last season ──────────────────────────────────────────────────────

    /// <summary>Season N-1's residual hosts, or a named reason there are none. Never throws
    /// for a bad candidate; a career must not fail to schedule because last year's file is
    /// damaged.</summary>
    private static HostMemory ReadHostMemory(HistoryStore? history)
    {
        if (history is null)
            return HostMemory.Empty(HostMemoryStatus.NoHistory, null);

        // ★ The peek, not a reservation. The schedule is built before any season number is
        //   spent — deliberately, so a slate that fails to build burns nothing — so the
        //   memory layer cannot wait for the reservation to tell it which season this is.
        var pending = history.PeekNextSeasonId;
        var previous = pending - 1;
        if (previous <= 0)
            return HostMemory.Empty(HostMemoryStatus.FirstSeason, null);

        var path = GameLogWriter.FinalPathFor(history.Path, previous);
        if (!File.Exists(path))
            return HostMemory.Empty(HostMemoryStatus.NoPublishedLog, previous);

        // ★ THE PROJECTION. Three fields per game and nothing else crosses this line.
        List<(int Home, int Away, bool Conference)> facts;
        try
        {
            var bindings = new GameLogBindings(
                history.HistoryId, history.WorldFingerprint, previous,
                // No schedule fingerprint: this season cannot know last season's, and the
                // binding is optional by design (GameLogReader, DecodeFileHeader).
                ScheduleFingerprint: null);
            var log = GameLogReader.ReadFinalized(path, bindings);
            facts = new List<(int, int, bool)>(log.Blocks.Count);
            foreach (var block in log.Blocks)
                facts.Add((block.Facts.HomeSchoolId, block.Facts.AwaySchoolId,
                           block.Facts.IsConferenceGame));
        }
        catch (GameLogException gx)
        {
            return HostMemory.Empty(HostMemoryStatus.Unreadable, previous, Classify(gx.Error));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ★ BOUNDED, never `catch (Exception)`. A bad or inaccessible candidate cannot
            //   stop the season; a programming failure is not reclassified as bad memory.
            return HostMemory.Empty(HostMemoryStatus.Unreadable, previous, HostMemoryProblem.IoFailure);
        }

        return Aggregate(facts, previous);
    }

    /// <summary>Turn one season's projected schedule facts into residual hosts, or refuse the
    /// whole source.
    ///
    /// <para>★ REJECT THE WHOLE SOURCE, never part of it. A memory that is right about some
    /// pairs and wrong about others is worse than no memory, because the schedule it produces
    /// looks reasonable.</para>
    ///
    /// <para>★ WHAT THESE INVARIANTS DO AND DO NOT ESTABLISH, honestly. They prove that each
    /// pair's home counts CAN represent an even split plus at most one residual. They do not
    /// establish block-level integrity — a symmetrically duplicated pair (1-1 becoming 2-2)
    /// satisfies every one of them. Block uniqueness, contiguous fixture ordering, footer
    /// counts and the payload digest are the reader's job and it does all four; this layer
    /// relies on that, and the reliance is recorded rather than assumed.</para></summary>
    private static HostMemory Aggregate(
        List<(int Home, int Away, bool Conference)> facts, long seasonId)
    {
        var loHome = new Dictionary<(int Lo, int Hi), int>();
        var hiHome = new Dictionary<(int Lo, int Hi), int>();
        var read = 0;

        foreach (var (home, away, conference) in facts)
        {
            if (!conference) continue;      // only a conference pair owns a residual
            read++;
            // ★ Caught BEFORE normalization, which would otherwise fold a self-game into a
            //   well-formed-looking pair (5,5). The reader validates that both ids are
            //   non-negative and nothing more — it deliberately knows no league.
            if (home == away)
                return HostMemory.Empty(HostMemoryStatus.Unreadable, seasonId,
                                        HostMemoryProblem.InconsistentPairFacts, read);
            var key = (Lo: Math.Min(home, away), Hi: Math.Max(home, away));
            loHome.TryAdd(key, 0);
            hiHome.TryAdd(key, 0);
            if (home == key.Lo) loHome[key]++; else hiHome[key]++;
        }

        var residuals = new Dictionary<(int Lo, int Hi), int>();
        foreach (var key in loHome.Keys)
        {
            var a = loHome[key];
            var b = hiHome[key];
            var total = a + b;
            var gap = Math.Abs(a - b);
            if (total <= 0 || gap > 1 || (gap == 1) != (total % 2 == 1))
                return HostMemory.Empty(HostMemoryStatus.Unreadable, seasonId,
                                        HostMemoryProblem.InconsistentPairFacts, read);
            if (gap == 1) residuals[key] = a > b ? key.Lo : key.Hi;
        }

        return new HostMemory(residuals, HostMemoryStatus.Loaded, seasonId, seasonId,
                              HostMemoryProblem.None, read, residuals.Count);
    }

    /// <summary>The reader's typed error, normalized. Every code is named deliberately;
    /// the default is the honest catch-all for internal damage.</summary>
    private static HostMemoryProblem Classify(GameLogError error) => error switch
    {
        GameLogError.HistoryIdMismatch      => HostMemoryProblem.WrongCareer,
        GameLogError.WorldDigestMismatch    => HostMemoryProblem.WrongWorld,
        GameLogError.SeasonIdMismatch       => HostMemoryProblem.WrongSeason,
        // Unreachable today — this caller supplies no schedule fingerprint, so the reader
        // never compares one. Classified with the other binding failures rather than left
        // to the catch-all, so a later caller that DOES bind a schedule gets the right word.
        GameLogError.ScheduleDigestMismatch => HostMemoryProblem.WrongSeason,
        GameLogError.IncompleteTail         => HostMemoryProblem.Truncated,
        GameLogError.MissingFooter          => HostMemoryProblem.Truncated,
        GameLogError.WrongMagic             => HostMemoryProblem.UnsupportedVersion,
        GameLogError.UnsupportedLogVersion  => HostMemoryProblem.UnsupportedVersion,
        GameLogError.LogReadFailed          => HostMemoryProblem.IoFailure,
        _                                   => HostMemoryProblem.Corrupt,
    };

    // ── Turning memory into venues ───────────────────────────────────────────────

    /// <summary>Last season's residual hosts, inverted, for the pairs where that is still a
    /// legal thing to say. Pure — no disk, no history, no clock.
    ///
    /// <para>★ THE INVERSION LIVES HERE. The memory records who hosted; what comes out is a
    /// fixed venue naming the OTHER school. That is the whole basketball of this session.</para>
    ///
    /// <para>★ MEMORY FOLLOWS THE SCHOOL PAIR, NOT THE CONFERENCE. Hosting fairness is a debt
    /// between two schools: if both move to a new league together and still meet, the
    /// alternation should survive the move. So membership and parity are validated against the
    /// CURRENT conference and this never asks which conference the memory came from.</para>
    ///
    /// <para>★ FIVE CONDITIONS, and anything failing one is SILENTLY SKIPPED rather than
    /// refused. A pair that met an odd number of times last year and an even number this year
    /// has no residual to decide, and that is an ordinary consequence of a league changing
    /// size — not an error. Dropping them here is also what keeps them away from
    /// <c>OrientConferenceSlate</c>, which would correctly reject them as an invalid
    /// configuration and take the whole season down with it.</para>
    ///
    /// <para><paramref name="members"/> is the membership validation, not redundancy: it is
    /// the only thing standing between a hand-built or foreign memory entry and a lookup on a
    /// school this league has never heard of.</para></summary>
    private static List<FixedResidualHost> ResidualsToFlip(
        HostMemory memory, Dictionary<(int Lo, int Hi), int> meetings, List<int> members)
    {
        var flips = new List<FixedResidualHost>();
        if (memory.Status != HostMemoryStatus.Loaded || memory.PreviousResidualHost.Count == 0)
            return flips;

        var inLeague = new HashSet<int>(members);
        foreach (var entry in memory.PreviousResidualHost
                                    .OrderBy(kv => kv.Key.Lo).ThenBy(kv => kv.Key.Hi))
        {
            var (lo, hi) = entry.Key;
            var hosted = entry.Value;
            if (lo >= hi) continue;                                 // not a normalized pair
            if (!inLeague.Contains(lo) || !inLeague.Contains(hi)) continue;   // foreign school
            if (!meetings.TryGetValue((lo, hi), out var m)) continue;         // not a pair here
            if (m % 2 == 0) continue;                               // no residual to decide
            if (hosted != lo && hosted != hi) continue;             // host not in the pair
            flips.Add(new FixedResidualHost(lo, hi, hosted == lo ? hi : lo));
        }
        return flips;
    }

    /// <summary>The page's one line, built entirely from the run that produced it. No stock
    /// constant appears here and no raw exception text ever does.</summary>
    private static string? HostMemoryPageLine(SeasonMemoryOutcome m) => m.Status switch
    {
        // Legacy mode says nothing at all — there is no career for a line to be about.
        HostMemoryStatus.NoHistory => null,
        HostMemoryStatus.FirstSeason => "Host memory: none (first season of this career)",
        HostMemoryStatus.NoPublishedLog => "Host memory: none (no log for season " +
                                           $"{m.AttemptedSeasonId?.ToString(CultureInfo.InvariantCulture) ?? "?"})",
        HostMemoryStatus.Unreadable =>
            $"Host memory: none (season {m.AttemptedSeasonId?.ToString(CultureInfo.InvariantCulture) ?? "?"} " +
            $"log unreadable: {ProblemWord(m.Problem)})",
        _ => $"Host memory: season {m.SourceSeasonId} — {m.ResidualsFlipped} residual" +
             $"{(m.ResidualsFlipped == 1 ? "" : "s")} flipped across " +
             $"{m.LeaguesWithResidualsFlipped} league{(m.LeaguesWithResidualsFlipped == 1 ? "" : "s")}",
    };

    private static string ProblemWord(HostMemoryProblem p) => p switch
    {
        HostMemoryProblem.WrongCareer => "another career",
        HostMemoryProblem.WrongWorld => "another world",
        HostMemoryProblem.WrongSeason => "another season",
        HostMemoryProblem.Truncated => "truncated",
        HostMemoryProblem.UnsupportedVersion => "unsupported format",
        HostMemoryProblem.Corrupt => "corrupt",
        HostMemoryProblem.IoFailure => "unreadable file",
        HostMemoryProblem.InconsistentPairFacts => "inconsistent schedule facts",
        _ => "unknown",
    };
}
