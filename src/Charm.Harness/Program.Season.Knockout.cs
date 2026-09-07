using System.Globalization;

namespace Charm.Harness;

// ============================================================================
//  S109 — THE SINGLE-ELIMINATION BRACKET PRIMITIVE. Lose and go home.
//
//  ★ THIS IS DORMANT. Nothing in the season plays it. The existing MTE tables
//  (BracketRoutes8 / BracketRoutes4) are CONSOLATION brackets — every team plays
//  every round and every place is decided on the floor — and they stay exactly as
//  they are. This file builds the shape the engine has never played: N teams,
//  N − 1 games, one champion, no consolation, no reseeding, no byes.
//
//  It is exercised only by Phase 98. Wiring it into any production path would
//  delete five games from every eight-team event and one from every four-team
//  event, and move the event-games fingerprint this session exists to hold still.
//  A coder who feels it "ought to be used somewhere" is experiencing the failure
//  mode, not solving it. Session B (conference tournaments) is its first consumer.
//
//  ── The pipeline, decomposed on purpose ────────────────────────────────────
//  participants → entry round → initial bracket position → advancement → reseeding
//
//  Here the entry round is a CONSTANT (everyone enters round one), the initial
//  position is the canonical seed line, advancement is the fold of that line, and
//  reseeding is "hold the line". Each step is its own small method rather than one
//  formula, because pods, protected seeds, divisional seeding, byes and stepladders
//  each vary exactly one of these steps and nothing else. None of those variations
//  exist here and none may be built here.
//
//  ── Seeds are positional ───────────────────────────────────────────────────
//  The caller hands in a seed order: index 0 is seed 1, index N−1 is seed N.
//  Seeds are never carried as (seed, participant) pairs. The canonical line
//  operates on seed NUMBERS and the participant identities ride along at their
//  positions.
// ============================================================================

internal static partial class Program
{
    /// <summary>The closed supported set. Powers of two from 2 to 64, and nothing else,
    /// because with no byes and no entry-round variation only a power of two is
    /// constructible, and 64 is the national bracket's size — the same primitive, the same
    /// recursion. Every member of this set is proven by Phase 98; nothing is accepted that
    /// is not proven.</summary>
    private static readonly int[] KnockoutSupportedFields = { 2, 4, 8, 16, 32, 64 };

    /// <summary>One side of a knockout game. This is a CLOSED union of exactly two shapes —
    /// a seed, or the winner of an earlier game. There is deliberately no loser variant and
    /// no list of sides: a loser edge and a three-input node are unconstructible rather than
    /// invalid, and Phase 98 demonstrates that closure rather than trusting this comment.
    /// The private constructor is what closes it: only the two nested records can derive.</summary>
    private abstract record KnockoutSide
    {
        private KnockoutSide() { }

        /// <summary>A participant entering from the seed line. 1-based seed number.</summary>
        public sealed record Seed(int Number) : KnockoutSide
        {
            public override string ToString() => $"seed {Number.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>The winner of a strictly earlier game.</summary>
        public sealed record WinnerOf(int GameIndex) : KnockoutSide
        {
            public override string ToString() => $"W{GameIndex.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    /// <summary>One game of the bracket. Exactly two sides, by shape. <c>WinnerTo</c> is the
    /// index of the game the winner feeds and <c>WinnerToSlot</c> which of its two sides
    /// (0 = A, 1 = B); both are null for the final and only for the final. The loser has no
    /// destination field at all — there is nowhere for an eliminated team to go.</summary>
    private sealed record KnockoutGame(
        int GameIndex, int Round, KnockoutSide A, KnockoutSide B,
        int? WinnerTo, int? WinnerToSlot);

    /// <summary>A built bracket: the field, the participants by seed (index 0 = seed 1),
    /// and the ordered games. Structural identity: two builds from identical inputs are
    /// equal element-for-element (Phase 98 invariant 1).</summary>
    private sealed record KnockoutBracket(
        int FieldSize, int Rounds,
        IReadOnlyList<int> ParticipantBySeed,
        IReadOnlyList<KnockoutGame> Games)
    {
        public int ParticipantOfSeed(int seed) => ParticipantBySeed[seed - 1];
        public KnockoutGame Final => Games[^1];
    }

    // ── Step 1: the field size, refused loudly outside the closed set ──────────

    private static void KnockoutRefuseField(int fieldSize)
    {
        if (Array.IndexOf(KnockoutSupportedFields, fieldSize) >= 0) return;
        throw new InvalidOperationException(
            $"KNOCKOUT: a field of {fieldSize.ToString(CultureInfo.InvariantCulture)} is not " +
            "supported; only 2, 4, 8, 16, 32 and 64 are constructible (powers of two, no byes). " +
            "Arbitrary sizes arrive with staggered entry, later and deliberately.");
    }

    /// <summary>Number of rounds for a supported field: log2(N).</summary>
    private static int KnockoutRounds(int fieldSize)
    {
        var r = 0;
        for (var n = fieldSize; n > 1; n >>= 1) r++;
        return r;
    }

    // ── Step 2: the seed order, refused loudly before any topology exists ──────

    private static void KnockoutRefuseSeedOrder(int fieldSize, IReadOnlyList<int>? seedOrder)
    {
        if (seedOrder is null)
            throw new InvalidOperationException(
                "KNOCKOUT: the seed order is null; a bracket needs its participants before it has a shape.");
        if (seedOrder.Count == 0)
            throw new InvalidOperationException(
                "KNOCKOUT: the seed order is empty; a bracket needs its participants before it has a shape.");
        if (seedOrder.Count != fieldSize)
            throw new InvalidOperationException(
                $"KNOCKOUT: a field of {fieldSize.ToString(CultureInfo.InvariantCulture)} was handed " +
                $"{seedOrder.Count.ToString(CultureInfo.InvariantCulture)} participants; the seed order " +
                "must carry exactly one participant per seed.");
        var seen = new HashSet<int>();
        for (var i = 0; i < seedOrder.Count; i++)
            if (!seen.Add(seedOrder[i]))
                throw new InvalidOperationException(
                    $"KNOCKOUT: participant {seedOrder[i].ToString(CultureInfo.InvariantCulture)} appears " +
                    $"twice in the seed order (again at seed {(i + 1).ToString(CultureInfo.InvariantCulture)}); " +
                    "a team cannot occupy two branches.");
    }

    // ── Step 3: entry round — a DEFAULT, not a solver ──────────────────────────

    /// <summary>★ SCOPE WALL. The round a seed enters. Today the answer is a constant —
    /// every seed enters round one — and the method exists only so the question has a
    /// place to be asked later (byes, double byes, stepladders). A build that can construct
    /// a field where different seeds enter at different rounds has LEFT SCOPE.</summary>
    private static int KnockoutEntryRound(int fieldSize, int seed) => 0;

    // ── Step 4: initial bracket position — the canonical seed line ─────────────

    /// <summary>★ THE CANONICAL SEEDED LINE, a topology and not a set of properties.
    /// <code>
    ///   line(2)  = [1, 2]
    ///   line(2k) = for each seed s in line(k), emit s then (2k + 1 − s)
    /// </code>
    /// So line(4) = [1,4,2,3] and line(8) = [1,8,4,5,2,7,3,6]. Opening games are consecutive
    /// pairs down the list; the tree folds it. This is what puts 1 and 2 on opposite sides,
    /// 1 and 4 in different semifinal regions, and every seed on its own half of the draw.
    /// Two outside reviews each defeated a weaker, property-based contract; the line is the
    /// contract.</summary>
    private static IReadOnlyList<int> KnockoutSeedLine(int fieldSize)
    {
        KnockoutRefuseField(fieldSize);
        var line = new List<int> { 1, 2 };
        while (line.Count < fieldSize)
        {
            var next = new List<int>(line.Count * 2);
            var size = line.Count * 2;
            foreach (var s in line)
            {
                next.Add(s);
                next.Add(size + 1 - s);
            }
            line = next;
        }
        return line;
    }

    // ── Step 5: reseeding — hold the line ──────────────────────────────────────

    /// <summary>★ SCOPE WALL. Whether the surviving field is re-paired between rounds.
    /// The answer is "hold the line": the fold of the seed line decides every later pairing
    /// and nothing re-ranks anyone after play begins. The method exists so a later session
    /// asks the question here rather than reinventing it; it has no second branch.</summary>
    private static bool KnockoutReseedBetweenRounds() => false;

    // ── The build ──────────────────────────────────────────────────────────────

    /// <summary>Build the single-elimination bracket for a supported field and a seed order.
    ///
    /// <para>Games are numbered in play order: round one is games 0..N/2−1 in seed-line
    /// order, each later round follows, and the final is the last game. Every input is
    /// either a seed or the winner of a strictly lower-indexed game. Every non-final winner
    /// feeds exactly one later slot; losers are eliminated and have nowhere to go.</para>
    ///
    /// <para>The nominal A side of a game is the better original seed on the seed line
    /// (its first slot), which is cosmetic and deterministic — the same rule the MTE
    /// factory uses for its nominal home side. Nothing here reads it as a venue.</para></summary>
    private static KnockoutBracket BuildKnockoutBracket(int fieldSize, IReadOnlyList<int> seedOrder)
    {
        KnockoutRefuseField(fieldSize);
        KnockoutRefuseSeedOrder(fieldSize, seedOrder);

        var rounds = KnockoutRounds(fieldSize);
        var line = KnockoutSeedLine(fieldSize);
        var games = new List<KnockoutGame>(fieldSize - 1);

        // Round one: consecutive pairs down the seed line. The entry-round seam is consulted
        // for every seed and, this session, always answers zero.
        for (var s = 1; s <= fieldSize; s++)
            if (KnockoutEntryRound(fieldSize, s) != 0)
                throw new InvalidOperationException(
                    "KNOCKOUT: the entry-round seam answered something other than round one; " +
                    "staggered entry is not built and may not be constructed here.");

        var previousRound = new List<int>();   // game indices of the round just laid down
        for (var i = 0; i + 1 < line.Count; i += 2)
        {
            games.Add(new KnockoutGame(games.Count, 0,
                                       new KnockoutSide.Seed(line[i]),
                                       new KnockoutSide.Seed(line[i + 1]),
                                       WinnerTo: null, WinnerToSlot: null));
            previousRound.Add(games.Count - 1);
        }

        // Later rounds: the fold. Game j of round r takes the winners of games 2j and 2j+1
        // of round r−1. Because reseeding holds the line, this is the whole advancement rule.
        if (KnockoutReseedBetweenRounds())
            throw new InvalidOperationException(
                "KNOCKOUT: reseeding between rounds is not built and may not be constructed here.");

        for (var round = 1; round < rounds; round++)
        {
            var thisRound = new List<int>();
            for (var j = 0; j + 1 < previousRound.Count; j += 2)
            {
                var index = games.Count;
                games.Add(new KnockoutGame(index, round,
                                           new KnockoutSide.WinnerOf(previousRound[j]),
                                           new KnockoutSide.WinnerOf(previousRound[j + 1]),
                                           WinnerTo: null, WinnerToSlot: null));
                games[previousRound[j]] = games[previousRound[j]] with { WinnerTo = index, WinnerToSlot = 0 };
                games[previousRound[j + 1]] = games[previousRound[j + 1]] with { WinnerTo = index, WinnerToSlot = 1 };
                thisRound.Add(index);
            }
            previousRound = thisRound;
        }

        if (games.Count != fieldSize - 1)
            throw new InvalidOperationException(
                $"KNOCKOUT: a field of {fieldSize.ToString(CultureInfo.InvariantCulture)} built " +
                $"{games.Count.ToString(CultureInfo.InvariantCulture)} games; single elimination is exactly N − 1.");

        return new KnockoutBracket(fieldSize, rounds, seedOrder.ToList(), games);
    }
}
