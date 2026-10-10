using Charm.Engine;
using System.Globalization;

namespace Charm.Harness;

// ============================================================================
//  Session 121 — THE OFFSEASON CAMP.
//
//  Every player now carries a hidden potential (a tier per attribute — none, low,
//  medium, high, very high — a RATE, not a ceiling) and a hidden work ethic, both
//  rolled once on arrival and fixed for his career. Between seasons each returner goes
//  through a camp: fifty points spent on the attributes with the most promise, a camp
//  that goes bad / normal / good / breakout on weighted odds tilted by work ethic and
//  minutes, and growth that depends on the tier, the points and how the camp went.
//  IQ and Discipline grow on their own by age and minutes, capped near where he arrived.
//
//  Emmett's rulings (2026-10-10) are in the S121 prompt §0 and design.md "Development".
//  Every number lives in config (DevelopmentConfig); group membership (K1) is code.
//
//  ★ WHERE THIS RUNS. Development is its OWN step, after the turnover (and on a career
//    after the people are numbered): row i of the turned-over pool is row i developed.
//    The turnover's own contracts (the same Player object for a returner) run on the
//    undeveloped result, where they are still true (S121 gate, G4).
//
//  ★ RANDOMNESS (K5). Two per-person SplitMix64 streams. The POTENTIAL stream is seeded
//    from the arrival season's seed and the man's index in the pool he arrived in; its
//    first draw is his DEVELOPMENT SEED, kept with him for his career. Each summer's CAMP
//    stream is seeded from that development seed and the coming season's seed. After
//    arrival nobody's draws depend on anybody else's, so adding, removing or reordering
//    people moves nobody else's camp. Every man draws a FIXED number of values from each
//    stream whatever his branches, so no rule's outcome shifts a later draw.
// ============================================================================

internal static partial class Program
{
    // ── K5 — the streams' own salts and mix primes. Never the class draw's or the board's. ──
    private const ulong DevPotentialSalt = 0x907E5C0D9071E7A1UL;
    private const ulong DevPotentialMix  = 0xD1B54A32D192ED03UL;
    private const ulong DevCampSalt      = 0xCA4B5EA5C0FFEE21UL;
    private const ulong DevCampMix       = 0xAEF17502108EF2D9UL;

    /// <summary>The 27 funded attributes, canonical order (skills, body, athleticism) — the
    /// scouting file's order. Processing order is the config's AttributeOrder.</summary>
    private static readonly string[] DevFunded = DevelopmentConfig.FundedAttributes;
    private const int DevSkillCount = 20;
    private const int DevBodyEnd = 23;          // 20..22 body, 23..26 athleticism
    private const int DevFundedCount = 27;
    private const int DevIqSlot = 27;
    private const int DevDisciplineSlot = 28;
    private const int DevProgressCount = 29;

    private static readonly Dictionary<string, int> DevIndexOf =
        DevFunded.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => x.i, StringComparer.Ordinal);

    /// <summary>The body index for cohort descriptors (4b-1).</summary>
    private static readonly string[] DevBodyIndex = { "Height", "Strength", "Speed", "Quickness", "FirstStep", "Vertical" };

    private enum DevGroup { Skill = 0, Body = 1, Athleticism = 2 }

    private static DevGroup DevGroupOf(int canon)
        => canon < DevSkillCount ? DevGroup.Skill : canon < DevBodyEnd ? DevGroup.Body : DevGroup.Athleticism;

    private static double DevGroupFactor(DevelopmentConfig cfg, DevGroup g)
        => cfg.GroupFactor[DevelopmentConfig.GroupNames[(int)g]];

    /// <summary>One man's hidden development state — everything the scouting file holds about him.</summary>
    private sealed class DevState
    {
        public required ulong DevSeed { get; init; }
        public required int WorkEthic { get; init; }
        public required int ArrivalIq { get; init; }
        public required int ArrivalDiscipline { get; init; }
        /// <summary>Tier 0..4 per funded attribute, canonical order. Fixed at arrival.</summary>
        public required int[] Tiers { get; init; }
        /// <summary>Summers in a row each funded attribute was funded, canonical order.</summary>
        public required int[] Streaks { get; init; }
        /// <summary>Unspent fraction per developing attribute, in [0, 1): 27 funded, then IQ, Discipline.</summary>
        public required double[] Progress { get; init; }

        public DevState Clone() => new()
        {
            DevSeed = DevSeed, WorkEthic = WorkEthic, ArrivalIq = ArrivalIq, ArrivalDiscipline = ArrivalDiscipline,
            Tiers = (int[])Tiers.Clone(), Streaks = (int[])Streaks.Clone(), Progress = (double[])Progress.Clone(),
        };

        public bool SameAs(DevState o)
            => DevSeed == o.DevSeed && WorkEthic == o.WorkEthic && ArrivalIq == o.ArrivalIq
               && ArrivalDiscipline == o.ArrivalDiscipline && Tiers.SequenceEqual(o.Tiers)
               && Streaks.SequenceEqual(o.Streaks)
               && Progress.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(o.Progress.Select(BitConverter.DoubleToInt64Bits));
    }

    // ── The streams (K5) ─────────────────────────────────────────────────────

    private static WorldRng DevPotentialStream(long arrivalSeasonSeed, int arrivalPoolIndex)
    {
        var seed = unchecked((ulong)arrivalSeasonSeed) ^ DevPotentialSalt;
        seed ^= unchecked((ulong)(arrivalPoolIndex + 1) * DevPotentialMix);
        return new WorldRng(unchecked((long)seed));
    }

    private static WorldRng DevCampStream(ulong devSeed, long nextSeasonSeed)
    {
        var seed = devSeed ^ DevCampSalt;
        seed ^= unchecked((ulong)(nextSeasonSeed + 1) * DevCampMix);
        return new WorldRng(unchecked((long)seed));
    }

    // ── Shared arithmetic ────────────────────────────────────────────────────

    /// <summary>The first index whose left-to-right accumulated odds exceed u; else the last.</summary>
    private static int DevPick(IReadOnlyList<double> odds, double u)
    {
        var c = 0.0;
        for (var i = 0; i < odds.Count; i++)
        {
            c += odds[i];
            if (u < c) return i;
        }
        return odds.Count - 1;
    }

    private static double DevClamp(double x, double lo, double hi) => Math.Max(lo, Math.Min(hi, x));
    private static int DevClampInt(int x, int lo, int hi) => Math.Max(lo, Math.Min(hi, x));

    /// <summary>Population mean and SD by plain left-to-right loops (4b-1's one accumulation rule);
    /// an SD of zero becomes 1.</summary>
    private static (double Mean, double Sd) DevMeanSd(IReadOnlyList<double> xs)
    {
        var s = 0.0;
        foreach (var x in xs) s += x;
        var mu = s / xs.Count;
        var v = 0.0;
        foreach (var x in xs) v += (x - mu) * (x - mu);
        var sd = Math.Sqrt(v / xs.Count);
        return (mu, sd > 0 ? sd : 1.0);
    }

    // ── 4b-1: cohort descriptors ─────────────────────────────────────────────

    /// <summary>One man of a cohort being rolled: his ARRIVAL POOL INDEX (never a person number),
    /// his position, his card.</summary>
    private sealed record DevCohortMan(int ArrivalIndex, string Pos, IReadOnlyDictionary<string, int> Card);

    /// <summary>Generation-time only — used once to roll potential, never stored, never on a Player.</summary>
    private sealed record DevDescriptor(double Body, double Skill, double Bz, double Sz, double Gap, double Talent, bool TalentTop);

    /// <summary>Descriptors for a cohort, aligned with the input list. Within each position the men
    /// are taken in arrival-index order; talent ties go to the lower arrival index.</summary>
    private static DevDescriptor[] DevCohortDescriptors(IReadOnlyList<DevCohortMan> men, DevelopmentConfig cfg)
    {
        var outp = new DevDescriptor[men.Count];
        foreach (var pos in men.Select(m => m.Pos).Distinct().OrderBy(p => p, StringComparer.Ordinal))
        {
            var idx = Enumerable.Range(0, men.Count).Where(i => men[i].Pos == pos)
                                .OrderBy(i => men[i].ArrivalIndex).ToArray();
            var b = new double[idx.Length];
            var s = new double[idx.Length];
            for (var j = 0; j < idx.Length; j++)
            {
                var card = men[idx[j]].Card;
                var acc = 0;
                foreach (var k in DevBodyIndex) acc += card[k];
                b[j] = acc / (double)DevBodyIndex.Length;
                var top = PlayerGenPass3.SPEND_SKILLS.Select(k => card[k]).OrderByDescending(x => x).Take(5).Sum();
                s[j] = top / 5.0;
            }
            var (bm, bs) = DevMeanSd(b);
            var (sm, ss) = DevMeanSd(s);
            var bz = new double[idx.Length];
            var sz = new double[idx.Length];
            var talent = new double[idx.Length];
            for (var j = 0; j < idx.Length; j++)
            {
                bz[j] = (b[j] - bm) / bs;
                sz[j] = (s[j] - sm) / ss;
                talent[j] = (bz[j] + sz[j]) / 2;
            }
            var ranked = Enumerable.Range(0, idx.Length)
                                   .OrderByDescending(j => talent[j]).ThenBy(j => men[idx[j]].ArrivalIndex).ToArray();
            var cut = (int)Math.Round(idx.Length * cfg.TalentTopFraction, MidpointRounding.ToEven);
            var top3 = new bool[idx.Length];
            for (var r = 0; r < cut && r < ranked.Length; r++) top3[ranked[r]] = true;
            for (var j = 0; j < idx.Length; j++)
                outp[idx[j]] = new DevDescriptor(b[j], s[j], bz[j], sz[j], bz[j] - sz[j], talent[j], top3[j]);
        }
        return outp;
    }

    // ── 4b-2..5: the potential roll ──────────────────────────────────────────

    /// <summary>What the roll produced, with the decision states the oracle parity compares.</summary>
    private sealed record DevPotentialRoll(int RawShift, int PlayerTier, int BestSkill, int[] Tiers, int WorkEthic);

    /// <summary>4b-2..5 for one man, given his descriptor and the next-uniform source (his potential
    /// stream AFTER the development seed). Draws exactly 4 + 20 + 7 + (2k − 1) values.</summary>
    private static DevPotentialRoll DevRollPotential(
        DevDescriptor d, string pos, IReadOnlyDictionary<string, int> card, Func<double> u, DevelopmentConfig cfg)
    {
        var shift = 0;
        var u1 = u(); var u2 = u(); var u3 = u(); var u4 = u();          // always four
        if (u1 < cfg.BodyFirstNotchDown * DevClamp(d.Gap / cfg.BodyFirstFullGap, 0, 1)) shift -= 1;
        if (u2 < cfg.ReadyNotchDown * DevClamp(d.Sz / cfg.ReadyFullZ, 0, 1)) shift -= 1;
        if (d.TalentTop && u3 < cfg.TalentNotchUp) shift += 1;
        var P = DevClampInt(DevPick(cfg.PlayerTierOdds, u4) + DevClampInt(shift, -1, 1), 0, 4);

        var order = cfg.AttributeOrder;
        var orderIndex = order.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => x.i, StringComparer.Ordinal);
        // His best current skill: highest of the 19 spend skills, ties to the earlier in AttributeOrder.
        var best = PlayerGenPass3.SPEND_SKILLS
            .OrderByDescending(k => card[k]).ThenBy(k => orderIndex[k]).First();

        var tiers = new int[DevFundedCount];
        foreach (var k in order)
        {
            var c = DevIndexOf[k];
            if (DevGroupOf(c) != DevGroup.Skill) continue;
            var off = DevPick(cfg.SkillOffsetOdds, u()) - 2;
            var sh = 0;
            if (pos == "G" && cfg.GuardDownSkills.Contains(k, StringComparer.Ordinal)) sh -= 1;
            if (pos == "B" && cfg.BigDownSkills.Contains(k, StringComparer.Ordinal)) sh -= 1;
            if (k == best) sh -= 1;
            tiers[c] = DevClampInt(P + off + DevClampInt(sh, -1, 1), 0, 4);
        }
        foreach (var k in order)
        {
            var c = DevIndexOf[k];
            if (DevGroupOf(c) == DevGroup.Skill) continue;
            tiers[c] = DevPick(cfg.BodyTierOdds, u());
        }
        var a = cfg.WorkEthicBetaShape;
        var us = new double[2 * a - 1];
        for (var i = 0; i < us.Length; i++) us[i] = u();
        return new DevPotentialRoll(shift, P, DevIndexOf[best], tiers, DevWorkEthic(us, a));
    }

    /// <summary>4b-5 — Beta(a, a) as the a-th smallest of 2a − 1 uniforms, mapped to 1..99.</summary>
    private static int DevWorkEthic(double[] us, int a)
    {
        var sorted = (double[])us.Clone();
        Array.Sort(sorted);
        return 1 + (int)Math.Floor(98 * sorted[a - 1] + 0.5);
    }

    /// <summary>A newly arrived man's whole state: the development seed (the potential stream's
    /// first draw), the roll, and zero progress and streaks.</summary>
    private static (DevState State, DevPotentialRoll Roll) DevArrive(
        DevDescriptor d, string pos, IReadOnlyDictionary<string, int> card,
        long arrivalSeasonSeed, int arrivalPoolIndex, DevelopmentConfig cfg)
    {
        var rng = DevPotentialStream(arrivalSeasonSeed, arrivalPoolIndex);
        var devSeed = rng.NextU64();
        var roll = DevRollPotential(d, pos, card, rng.NextDouble, cfg);
        return (new DevState
        {
            DevSeed = devSeed, WorkEthic = roll.WorkEthic,
            ArrivalIq = card["BasketballIQ"], ArrivalDiscipline = card["Discipline"],
            Tiers = roll.Tiers, Streaks = new int[DevFundedCount], Progress = new double[DevProgressCount],
        }, roll);
    }

    /// <summary>Potential for a whole arriving cohort (the bootstrap pool, or a freshman class).</summary>
    private static (DevState[] States, DevPotentialRoll[] Rolls, DevDescriptor[] Descriptors) DevArriveCohort(
        IReadOnlyList<DevCohortMan> men, long arrivalSeasonSeed, DevelopmentConfig cfg)
    {
        var desc = DevCohortDescriptors(men, cfg);
        var states = new DevState[men.Count];
        var rolls = new DevPotentialRoll[men.Count];
        for (var i = 0; i < men.Count; i++)
            (states[i], rolls[i]) = DevArrive(desc[i], men[i].Pos, men[i].Card, arrivalSeasonSeed, men[i].ArrivalIndex, cfg);
        return (states, rolls, desc);
    }

    /// <summary>The bootstrap pool's potential: arrival index = bootstrap pool index, arrival seed =
    /// season one's seed. A pure function of the pool and the seed — the career and the stacked
    /// command compute it identically.</summary>
    private static (DevState[] States, DevPotentialRoll[] Rolls, DevDescriptor[] Descriptors) DevBootstrap(
        DivvyResult divvy, long seasonSeed, DevelopmentConfig cfg)
        => DevArriveCohort(divvy.Pool.Select(p => new DevCohortMan(p.PoolId, p.Pos, p.Ratings)).ToList(), seasonSeed, cfg);

    // ── 4c-1: minutes share ──────────────────────────────────────────────────

    /// <summary>His integer seconds over his team's games × 2,400 (regulation), clamped to [0, 1].</summary>
    private static double DevMinutesShare(long seconds, int teamGames)
        => teamGames == 0 ? 0.0 : DevClamp(seconds / (teamGames * 2400.0), 0.0, 1.0);

    /// <summary>The S117 integer seconds rule for one game, from credits, possessions and overtimes.</summary>
    private static long DevGameSeconds(long credits, int possessions, int overtimes)
    {
        var num = credits * (40 + 5 * overtimes) * 60L;
        var q = num / possessions;
        return 2 * (num % possessions) >= possessions ? q + 1 : q;
    }

    // ── 4c-2: the allocation ─────────────────────────────────────────────────

    /// <summary>The computer's camp: the split for this camp dealt out by rank (promise, cut by the
    /// rotation), then current rating (K9), then config order. Canonical-order points.</summary>
    /// <para><paramref name="lowestFirstForTest"/> is Phase 112 C1's negative control and nothing else
    /// sets it: it funds the LOWEST promise first, which the oracle parity must reject.</para>
    private static int[] DevAllocate(int[] tiers, IReadOnlyDictionary<string, int> card, int campNumber,
                                     int[] streaks, DevelopmentConfig cfg, bool lowestFirstForTest = false)
    {
        var split = cfg.PrioritySplits[Math.Min(campNumber, cfg.PrioritySplits.Length) - 1];
        double Promise(int c) => cfg.TierRate[tiers[c]] * DevGroupFactor(cfg, DevGroupOf(c));
        double Rank(int c) => Promise(c) * (streaks[c] >= cfg.RepeatAfter ? cfg.RepeatFactor : 1.0);
        var order = cfg.AttributeOrder.Select(k => DevIndexOf[k]).ToArray();
        var pos = new int[DevFundedCount];
        for (var i = 0; i < order.Length; i++) pos[order[i]] = i;
        var elig = order.Where(c => Promise(c) > 0).ToList();
        elig.Sort((x, y) =>
        {
            var r = lowestFirstForTest ? Rank(x).CompareTo(Rank(y)) : Rank(y).CompareTo(Rank(x));
            if (r != 0) return r;
            var v = card[DevFunded[y]].CompareTo(card[DevFunded[x]]);
            return v != 0 ? v : pos[x].CompareTo(pos[y]);
        });
        var points = new int[DevFundedCount];
        for (var i = 0; i < elig.Count && i < split.Length; i++) points[elig[i]] = split[i];
        return points;
    }

    /// <summary>Every points map — the computer's or, later, a hand-set one — is checked: integers
    /// ≥ 0, each ≤ MaxPerAttribute, total ≤ Budget. Refused by name.</summary>
    private static void DevValidatePoints(int[] points, DevelopmentConfig cfg)
    {
        if (points.Length != DevFundedCount)
            throw new InvalidOperationException($"camp points refused: {points.Length} entries for {DevFundedCount} funded attributes.");
        var bad = Enumerable.Range(0, DevFundedCount).Where(c => points[c] < 0 || points[c] > cfg.MaxPerAttribute)
                            .Select(c => DevFunded[c]).ToList();
        if (bad.Count > 0)
            throw new InvalidOperationException($"camp points refused: [{string.Join(", ", bad)}] outside 0..{cfg.MaxPerAttribute}.");
        if (points.Sum() > cfg.Budget)
            throw new InvalidOperationException($"camp points refused: {points.Sum()} over the {cfg.Budget}-point budget.");
    }

    /// <summary>A hand-set camp by attribute name (the training screen's future door). Unknown names refused.</summary>
    private static int[] DevPointsByName(IReadOnlyDictionary<string, int> byName)
    {
        var unknown = byName.Keys.Where(k => !DevIndexOf.ContainsKey(k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"camp points refused: [{string.Join(", ", unknown)}] are not funded attributes.");
        var p = new int[DevFundedCount];
        foreach (var (k, v) in byName) p[DevIndexOf[k]] = v;
        return p;
    }

    // ── 4c-3: the camp roll ──────────────────────────────────────────────────

    /// <summary>The tilted odds: bad / normal / good / breakout. Normal never changes; the tilt moves
    /// mass between bad and good+breakout in the configured good:breakout ratio, clamped.</summary>
    private static double[] DevCampOdds(int workEthic, double share, DevelopmentConfig cfg)
    {
        var T = cfg.WorkEthicTilt * (workEthic - 50) / 49
              + cfg.MinutesTilt * DevClamp((share - cfg.MinutesPivot) / cfg.MinutesSpan, -1, 1);
        double bad = cfg.CampOdds[0], nor = cfg.CampOdds[1], good = cfg.CampOdds[2], bo = cfg.CampOdds[3];
        double g0 = good, b0 = bo;
        if (T >= 0)
        {
            var d = Math.Min(T, bad);
            bad -= d; good += d * g0 / (g0 + b0); bo += d * b0 / (g0 + b0);
        }
        else
        {
            var d = Math.Min(-T, good + bo);
            good -= d * g0 / (g0 + b0); bo -= d * b0 / (g0 + b0); bad += d;
        }
        return new[] { bad, nor, good, bo };
    }

    // ── 4c-4: growth, one attribute ──────────────────────────────────────────

    /// <summary>The per-attribute rule as one pure function, given its draws (ua: the natural climb,
    /// athleticism only; ub: jitter or slip). Funded athleticism = natural climb + a bonus, never
    /// less than unfunded (Phase 112 C1b tests exactly this function).</summary>
    private static (double Gain, bool Slipped) DevGrowOne(
        int canon, int tier, int points, int outcome, double ua, double ub, DevelopmentConfig cfg)
    {
        var g = DevGroupOf(canon);
        var gain = 0.0;
        var slipped = false;
        if (g == DevGroup.Athleticism)
            gain += cfg.NaturalClimb[tier] * (cfg.NaturalJitterLo + (cfg.NaturalJitterHi - cfg.NaturalJitterLo) * ua);
        if (points > 0)
        {
            var rate = cfg.TierRate[tier];
            if (outcome == 3) rate = Math.Max(rate, cfg.TierRate[cfg.BreakoutFloorTier]);
            gain += points * rate * DevGroupFactor(cfg, g) * cfg.CampMultiplier[outcome]
                    * (cfg.GrowthJitterLo + (cfg.GrowthJitterHi - cfg.GrowthJitterLo) * ub);
        }
        else if (g != DevGroup.Athleticism)
        {
            slipped = ub < cfg.AtrophyChance;
        }
        return (gain, slipped);
    }

    // ── 4c-8: free throws follow the shooting and the height ─────────────────

    private static double DevFtCore(int outside, int height)
        => PlayerGenPass3.FT_CENTER
           + PlayerGenPass3.FT_OUT_SPAN * Math.Tanh((outside - PlayerGenPass3.FT_OUT_ANCHOR) / PlayerGenPass3.FT_OUT_SCALE)
           - PlayerGenPass3.FT_HEIGHT_COEF * ((height - 55.0) / 40.0);

    /// <summary>The change in DeriveFt's own terms; the per-man idiosyncrasy (not on the card) cancels.
    /// Returns the rounded delta and the unrounded one (C1's documented tanh exception reads it).</summary>
    private static (int Delta, double Raw) DevFtDelta(int oldOut, int oldH, int newOut, int newH)
    {
        var raw = DevFtCore(newOut, newH) - DevFtCore(oldOut, oldH);
        return ((int)Math.Round(raw, MidpointRounding.ToEven), raw);
    }

    // ── 4c: one camp ─────────────────────────────────────────────────────────

    /// <summary>Everything one camp did, for the checks and the page.</summary>
    private sealed record DevCampRecord(
        int CampNumber, double Share, double[] Odds, int Outcome, int[] Points,
        double[] Gains, bool[] Slipped, bool Spurt, int FtDelta, double FtRaw);

    /// <summary>One camp, in place: <paramref name="card"/> (the 38) and <paramref name="st"/>'s
    /// progress and streaks are updated. Draws exactly 1 + 2·4 + 23 + 1 = 33 values from
    /// <paramref name="u"/>. Tendencies are NOT re-derived here (the caller rebuilds the man).</summary>
    private static DevCampRecord DevRunCamp(
        DevState st, Dictionary<string, int> card, int campNumber, double share, Func<double> u,
        DevelopmentConfig cfg, int[]? handPoints = null, bool keepPastNinetyNineForTest = false)
    {
        var points = handPoints ?? DevAllocate(st.Tiers, card, campNumber, st.Streaks, cfg);
        DevValidatePoints(points, cfg);
        for (var c = 0; c < DevFundedCount; c++) st.Streaks[c] = points[c] > 0 ? st.Streaks[c] + 1 : 0;

        var odds = DevCampOdds(st.WorkEthic, share, cfg);
        var outcome = DevPick(odds, u());
        int oldOut = card["Outside"], oldH = card["Height"];
        var gains = new double[DevFundedCount];
        var slipped = new bool[DevFundedCount];
        foreach (var k in cfg.AttributeOrder)
        {
            var c = DevIndexOf[k];
            var ua = DevGroupOf(c) == DevGroup.Athleticism ? u() : double.NaN;
            var ub = u();
            var (gain, slip) = DevGrowOne(c, st.Tiers[c], points[c], outcome, ua, ub, cfg);
            gains[c] = gain; slipped[c] = slip;
            if (slip) card[k] = Math.Max(0, card[k] - 1);                  // progress kept through a slip
            st.Progress[c] += gain;
            var w = (int)Math.Floor(st.Progress[c]);
            card[k] += w; st.Progress[c] -= w;
            if (card[k] >= 99 && !keepPastNinetyNineForTest) { card[k] = 99; st.Progress[c] = 0.0; }   // excess discarded
        }
        DevIqRule(card, st, "BasketballIQ", DevIqSlot, st.ArrivalIq, cfg.IqAgeGrowth, cfg.IqMinutesGrowth, cfg.IqCapAboveArrival, share);
        DevIqRule(card, st, "Discipline", DevDisciplineSlot, st.ArrivalDiscipline, cfg.DisciplineAgeGrowth,
                  cfg.DisciplineMinutesGrowth, cfg.DisciplineCapAboveArrival, share);
        var spurt = u() < cfg.HeightSpurtChance;
        if (spurt)
        {
            card["Height"] = Math.Min(99, card["Height"] + cfg.HeightSpurtRating);
            card["Wingspan"] = Math.Min(99, card["Wingspan"] + cfg.HeightSpurtWingspan);
        }
        var (ftDelta, ftRaw) = DevFtDelta(oldOut, oldH, card["Outside"], card["Height"]);
        card["FreeThrow"] = (int)DevClamp(card["FreeThrow"] + ftDelta, PlayerGenPass3.FT_MIN, PlayerGenPass3.FT_MAX);
        return new DevCampRecord(campNumber, share, odds, outcome, (int[])points.Clone(), gains, slipped, spurt, ftDelta, ftRaw);
    }

    /// <summary>4c-6 — age plus minutes, whole points move the rating, capped at arrival + cap.
    /// No draws; never slips.</summary>
    private static void DevIqRule(Dictionary<string, int> card, DevState st, string key, int slot, int arrival,
                                  double ageGrowth, double minutesGrowth, int capAbove, double share)
    {
        var cap = Math.Min(99, arrival + capAbove);
        st.Progress[slot] += ageGrowth + minutesGrowth * share;
        var w = (int)Math.Floor(st.Progress[slot]);
        card[key] += w; st.Progress[slot] -= w;
        if (card[key] >= cap) { card[key] = cap; st.Progress[slot] = 0.0; }
    }

    /// <summary>The camp number for a returner: the class of the season he just played — 1 after his
    /// freshman year, 2 after sophomore, 3 after junior. His row's class is already advanced.</summary>
    private static int DevCampNumber(ClassYear advancedClass) => (int)advancedClass;

    // ── The development step: the turned-over pool, developed index for index ──

    /// <summary>The whole step's output. Index for index with the season's pool.</summary>
    private sealed class DevelopmentStep
    {
        public required DivvyResult Divvy { get; init; }
        /// <summary>Every man of the new season's pool: returners after camp, freshmen as rolled.</summary>
        public required DevState[] States { get; init; }
        /// <summary>The camp each returner had; null for a freshman.</summary>
        public required DevCampRecord?[] Camps { get; init; }
        /// <summary>The freshman class's rolls (index − ReturnerCount).</summary>
        public required DevPotentialRoll[] FreshmanRolls { get; init; }
        public required DevDescriptor[] FreshmanDescriptors { get; init; }
        public required int ReturnerCount { get; init; }
    }

    /// <summary>★ The development step (G4). Row i of <paramref name="turnedOver"/>'s pool comes back
    /// as row i developed: same PoolId, position, roles, plane, scout rank, class, name and hierarchy
    /// rank; a new card and a Player rebuilt from it. Freshmen pass through as the SAME objects and
    /// get their potential rolled. The contract is checked here, by name.</summary>
    private static DevelopmentStep DevelopSeason(
        DivvyResult turnedOver, int returnerCount, IReadOnlyList<DevState> returnerStates,
        IReadOnlyList<double> returnerShares, long seasonSeed, DevelopmentConfig cfg)
    {
        var pool = turnedOver.Pool;
        if (returnerStates.Count != returnerCount || returnerShares.Count != returnerCount)
            throw new InvalidOperationException(
                $"DEVELOPMENT: {returnerStates.Count} states and {returnerShares.Count} shares for {returnerCount} returners.");

        var newPool = new List<PoolPlayer>(pool.Count);
        var states = new DevState[pool.Count];
        var camps = new DevCampRecord?[pool.Count];
        for (var i = 0; i < returnerCount; i++)
        {
            var row = pool[i];
            var st = returnerStates[i].Clone();
            var card = new Dictionary<string, int>(row.Ratings, StringComparer.Ordinal);
            var rng = DevCampStream(st.DevSeed, seasonSeed);
            camps[i] = DevRunCamp(st, card, DevCampNumber(row.Class), returnerShares[i], rng.NextDouble, cfg);
            DeriveAndStampTendencies(card);
            var player = GenMapToPlayer(card, row.Player.Name, row.Player.HierarchyRank);
            var errs = player.Validate();
            if (errs.Count > 0)
                throw new InvalidOperationException(
                    $"DEVELOPMENT: returner #{i} failed Player.Validate() after camp:\n  " + string.Join("\n  ", errs));
            newPool.Add(row with { Ratings = card, Player = player });
            states[i] = st;
        }

        var freshMen = Enumerable.Range(returnerCount, pool.Count - returnerCount)
                                 .Select(i => new DevCohortMan(i, pool[i].Pos, pool[i].Ratings)).ToList();
        var (fStates, fRolls, fDesc) = DevArriveCohort(freshMen, seasonSeed, cfg);
        for (var i = returnerCount; i < pool.Count; i++)
        {
            newPool.Add(pool[i]);
            states[i] = fStates[i - returnerCount];
        }

        var developed = new DivvyResult
        {
            Pool = newPool, Rosters = turnedOver.Rosters, Picks = turnedOver.Picks, NoiseScale = turnedOver.NoiseScale,
            PersonIds = turnedOver.PersonIds, MinSlackLead = turnedOver.MinSlackLead, MinSlackTdw = turnedOver.MinSlackTdw,
        };
        ValidateDevelopment(turnedOver, developed, returnerCount);
        return new DevelopmentStep
        {
            Divvy = developed, States = states, Camps = camps, FreshmanRolls = fRolls,
            FreshmanDescriptors = fDesc, ReturnerCount = returnerCount,
        };
    }

    /// <summary>The development step's own contract, refused by name.</summary>
    private static void ValidateDevelopment(DivvyResult before, DivvyResult after, int returnerCount)
    {
        if (after.Pool.Count != before.Pool.Count)
            throw new InvalidOperationException($"DEVELOPMENT: pool size moved {before.Pool.Count} -> {after.Pool.Count}.");
        for (var i = 0; i < after.Pool.Count; i++)
        {
            var a = before.Pool[i]; var b = after.Pool[i];
            if (b.PoolId != i || a.PoolId != i)
                throw new InvalidOperationException($"DEVELOPMENT: row {i} carries pool id {b.PoolId}; development is index for index.");
            if (a.Pos != b.Pos || a.Role != b.Role || a.DefensivePlane != b.DefensivePlane || a.OffensiveRole != b.OffensiveRole
                || a.ScoutRank != b.ScoutRank || a.Class != b.Class)
                throw new InvalidOperationException($"DEVELOPMENT: row {i}'s labels moved; labels stay for the career (K2).");
            if (a.Player.Name != b.Player.Name || a.Player.HierarchyRank != b.Player.HierarchyRank)
                throw new InvalidOperationException($"DEVELOPMENT: row {i} is not the same man (name or hierarchy rank moved).");
            if (i >= returnerCount && !ReferenceEquals(a, b))
                throw new InvalidOperationException($"DEVELOPMENT: freshman row {i} was touched; freshmen arrive as drafted.");
        }
        if (!ReferenceEquals(before.Rosters, after.Rosters))
            throw new InvalidOperationException("DEVELOPMENT: the rosters moved; development changes men, not rosters.");
    }

    // ── The stacked path (no career): turn over, then camp — the ONE function both the
    //    `seasons` command and Phase 104 call, so neither can play season two undeveloped. ──

    private sealed class StackedStep
    {
        public required TurnoverResult Turnover { get; init; }
        public required DevelopmentStep Development { get; init; }
        public required DevState[] SeasonOneStates { get; init; }
        /// <summary>Season one's pool index for each returner of season two (index &lt; ReturnerCount).</summary>
        public required int[] PreviousIndex { get; init; }
        public DivvyResult SeasonTwo => Development.Divvy;
    }

    private static StackedStep StackedTurnoverAndCamp(
        WorldFile world, SeasonRunOutcome one, long seedOne, long seedTwo, DevelopmentConfig cfg)
    {
        var (states1, _, _) = DevBootstrap(one.Divvy, seedOne, cfg);
        var t = RunTurnover(world, one.Divvy, seedTwo);
        var byPlayer = new Dictionary<Player, int>(ReferenceEqualityComparer.Instance);
        foreach (var row in one.Divvy.Pool) byPlayer[row.Player] = row.PoolId;
        var schoolOf = DevSchoolOf(one.Divvy);
        var prevIndex = new int[t.ReturnerCount];
        var rs = new DevState[t.ReturnerCount];
        var shares = new double[t.ReturnerCount];
        for (var i = 0; i < t.ReturnerCount; i++)
        {
            if (!byPlayer.TryGetValue(t.SeasonTwo.Pool[i].Player, out var k))
                throw new InvalidOperationException($"DEVELOPMENT: season-two returner #{i} is not a man from season one.");
            prevIndex[i] = k;
            rs[i] = states1[k];
            one.SecondsByPool.TryGetValue(k, out var secs);
            one.TeamGames.TryGetValue(schoolOf[k], out var games);
            shares[i] = DevMinutesShare(secs, games);
        }
        var step = DevelopSeason(t.SeasonTwo, t.ReturnerCount, rs, shares, seedTwo, cfg);
        return new StackedStep { Turnover = t, Development = step, SeasonOneStates = states1, PreviousIndex = prevIndex };
    }

    /// <summary>Pool id -> school id, from the rosters.</summary>
    private static Dictionary<int, int> DevSchoolOf(DivvyResult d)
    {
        var m = new Dictionary<int, int>(d.Pool.Count);
        foreach (var (school, ids) in d.Rosters) foreach (var pid in ids) m[pid] = school;
        return m;
    }

    // ── The page line ────────────────────────────────────────────────────────

    private static void PrintDevelopmentReport(DevelopmentStep step)
    {
        var camps = step.Camps.Where(c => c is not null).Select(c => c!).ToList();
        if (camps.Count == 0) return;
        var ci = CultureInfo.InvariantCulture;
        var names = new[] { "bad", "normal", "good", "breakout" };
        Console.WriteLine("=== THE OFFSEASON CAMP (every returner; potential and work ethic stay hidden) ===");
        Console.WriteLine("  camps: " + string.Join("  ", Enumerable.Range(0, 4).Select(o =>
            $"{names[o]} {camps.Count(c => c.Outcome == o)}")) + $"  (of {camps.Count})");
        Console.WriteLine(string.Format(ci, "  height spurts: {0};  freshmen rolled: {1}",
            camps.Count(c => c.Spurt), step.Divvy.Pool.Count - step.ReturnerCount));
        Console.WriteLine();
    }
}
