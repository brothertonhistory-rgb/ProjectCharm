using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Session 29 — Roster Genesis Pass 1.5: the national pool & the divvy.
//
// One national talent pool (10 x school count, unclassified) whose SHAPE is the
// only authored distribution — the leg-count mix, the third-leg gradient inside
// the two-leg population, exact positional quotas — divided among a world file's
// schools by prestige-weighted selection. Prestige stops generating quality and
// becomes what it is: ACCESS. The Session 26 machinery (GenRatings, floors,
// leg-health enforcement, GenMapToPlayer, Player.Validate) is reused verbatim;
// the pool never calls GenLegCountFor — the old prestige curve survives untouched
// in the `gen` lab mode.
//
// Every number below marked "placeholder" is exactly that (brief §3b): proposed,
// judged by Emmett in basketball terms at the check-in gate (32% two-leg is the
// thickened-middle-class revision of the prompt's 24%), oracle-proven, and tuned
// later against the burn-in. The odds exponent is the CONSTITUTIONAL DIAL: too
// steep and the pool collapses back into an authored prestige curve; the payoff
// is a blue blood getting more cracks while occasionally losing one (the Gonzaga
// mechanism — see DivvyOddsK).
//
// RNG contract (mirrored bit-for-bit by the Python oracle, S28 pattern):
//   Sequential stream: one WorldRng(divvySeed), consumed in fixed phase order —
//     Phase A: leg-tier Fisher-Yates shuffle (i = P-1 .. 1, one draw each)
//     Phase B: one role draw per player, in player-id order
//     Phase C: ratings per player in id order (GenLegPriority, GenRatings,
//              the third-leg redraw, the FT recompute where SIZE was redrawn)
//     Phase D: one winner draw per pick
//   Per-pair stream: board noise is drawn from a FRESH SplitMix64 seeded by a
//     mix of (divvySeed, schoolId, poolPlayerId) — random-access, order-free
//     (assumption 6: a sequential stream would make each perturbation depend on
//     draft order, exactly the reroll incoherence the brief forbids).
// ============================================================================

internal static partial class Program
{
    // ── The pool mix (ALL placeholders; Emmett-approved at the S29 check-in) ────
    private const double DivvyThreeLegFrac = 0.008;   // ~28 at n=347 — the All-American tier
    private const double DivvyTwoLegFrac   = 0.32;    // thickened middle class (was 24% in the draft prompt)
    private const double DivvyOneLegFrac   = 0.672;   // the mass
    // Third-leg gradient inside the two-leg population (disjoint inclusive integer
    // bands on the generic 0-99 scale — one definition shared with the oracle):
    private const double DivvyBorderlineFrac = 0.15;  // "2.5-leg" players
    private const double DivvyUsefulFrac     = 0.35;
    private const double DivvyScarceFrac     = 0.50;
    private const int DivvyBorderlineLo = 56, DivvyBorderlineHi = 70;
    private const int DivvyUsefulLo     = 46, DivvyUsefulHi     = 55;
    private const int DivvyScarceLo     = 34, DivvyScarceHi     = 45;

    // Positions: exact quotas per school (4G/3W/3B), so global coverage is feasible
    // by construction; the preflight validator still asserts it loudly.
    // Role quotas guarantee coverage supply with 20% headroom over one-per-team.
    private const double DivvyRoleHeadroom = 1.2;

    // The pool path's leg-health floor (Emmett's call at the S29 check-in: 20, so
    // the scarce gradient band ships as drawn — a leg can be bad, never broken).
    // The `gen` lab mode keeps its original 40 via the default parameter.
    private const int DivvyLegHealthFloor = 20;

    // ── The odds curve — THE constitutional dial (placeholder until burn-in) ────
    // Each pick is won by weight (currentPrestige + 10)^k among schools with a
    // legal candidate. The +10 keeps prestige-0 schools strictly positive.
    private const double DivvyOddsBase = 10.0;
    private const double DivvyOddsK    = 2.0;

    // ── Board noise: sd = 8% of the pool's rank range (placeholder) ─────────────
    // Triangular (sum of two uniforms), like the world seeder's jitter — no
    // transcendentals, so the oracle mirrors it bit-for-bit.
    private const double DivvyNoiseSigmaFrac = 0.08;
    private const ulong DivvyMixP1 = 0xC2B2AE3D27D4EB4FUL;   // school mix prime
    private const ulong DivvyMixP2 = 0x165667B19E3779F9UL;   // player mix prime

    // ── Scout rank (quarantined to the divvy; never on a Player, never a sort) ──
    // rank = L1 + 0.9*L2 + 0.4*L3 + 0.045*max(0, L3-30)^2 over the three leg means
    // (holes excluded, SIZE normalized to the generic scale; since 29.1 the SKILL
    // mean also excludes FreeThrow and a big's ATH mean is graded back onto the
    // position's scale — see DivvyScoutRank), sorted descending. Monotone in every
    // feeding rating; convex in the third leg (the quadratic); ordered on average
    // three > borderline > useful (scarce and one-leg sit even by ruling).
    private const double DivvyRankW1 = 1.0, DivvyRankW2 = 0.9, DivvyRankW3 = 0.4;
    private const double DivvyRankConvCoef = 0.045, DivvyRankConvAnchor = 30.0;

    // SIZE <-> generic normalization: one affine map per position anchored at
    // (ordinary-lo -> 44) and (plus-hi -> 88); the slope is 44/24 for every
    // position by construction of the size bands. Used FORWARD to place a SIZE
    // third leg's gradient band on the position's scale, and INVERSE to feed the
    // rank a position-comparable SIZE leg mean.
    private const double DivvyNormSlope = 44.0 / 24.0;

    private static double DivvySizeToGeneric(double v, string pos)
    {
        var (_, _, oLo, _) = GenSizeBand(pos);
        return 44.0 + (v - oLo) * DivvyNormSlope;
    }

    private static int DivvyGenericToSize(double g, string pos)
    {
        var (_, _, oLo, _) = GenSizeBand(pos);
        return (int)Math.Round(oLo + (g - 44.0) / DivvyNormSlope);
    }

    // ── System.Random adapter over WorldRng, so GenRatings is reused VERBATIM ───
    // (assumption 1: the rating draw takes a Random; reproducibility on any runtime
    // requires SplitMix64 — this adapter is the seam, flagged not slipped).
    // Contract: every Next overload consumes exactly ONE double, derived as
    // min + (int)(u * (max - min)) — mirrored by the oracle's next_int.
    private sealed class SplitMixRandom : Random
    {
        private readonly WorldRng _rng;
        public SplitMixRandom(WorldRng rng) => _rng = rng;
        public override double NextDouble() => _rng.NextDouble();
        protected override double Sample() => _rng.NextDouble();
        public override int Next() => (int)(_rng.NextDouble() * int.MaxValue);
        public override int Next(int maxValue) => (int)(_rng.NextDouble() * maxValue);
        public override int Next(int minValue, int maxValue)
            => minValue + (int)(_rng.NextDouble() * ((long)maxValue - minValue));
        public override void NextBytes(byte[] buffer)
        {
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = (byte)(_rng.NextDouble() * 256);
        }
    }

    // ── Largest-remainder apportionment (canonical-order ties — the WorldApportion
    //    pattern with rng: null; fractions are the caller's, so it is its own fn) ─
    private static int[] DivvyApportion(int total, double[] fractions)
    {
        var quotas = fractions.Select(f => total * f).ToArray();
        var counts = quotas.Select(q => (int)q).ToArray();
        var leftover = total - counts.Sum();
        var order = Enumerable.Range(0, fractions.Length)
            .OrderByDescending(i => quotas[i] - counts[i])
            .ThenBy(i => i)
            .ToArray();
        for (var k = 0; k < leftover; k++)
            counts[order[k]] += 1;
        return counts;
    }

    // ── One pool player ──────────────────────────────────────────────────────────
    // PoolId is the pool index 0..P-1 (never a PlayerId — stamping happens only at
    // a sim seam, per A0.7). ScoutRank lives HERE, on the pool row: it is consumed
    // by the draft and the readout's tables only, never written to the Player,
    // never printed on a roster sheet, never sorts anything outside the board.
    private sealed record PoolPlayer(
        int PoolId, string Pos, string Role, int LegCount, string? GradientTier,
        HashSet<string> PlusLegs, Dictionary<string, int> Ratings, Player Player,
        double ScoutRank);

    // ── Pool generation ──────────────────────────────────────────────────────────
    private static List<PoolPlayer> BuildDivvyPool(int schoolCount, WorldRng rng)
    {
        var n = schoolCount;
        var P = 10 * n;

        // Positions fixed by pool id: 4n guards, then 3n wings, then 3n bigs.
        var pos = new string[P];
        for (var i = 0; i < 4 * n; i++) pos[i] = "G";
        for (var i = 4 * n; i < 7 * n; i++) pos[i] = "W";
        for (var i = 7 * n; i < P; i++) pos[i] = "B";

        // Leg tiers: hierarchical apportionment — top-level mix over the pool first,
        // gradient tiers over the REALIZED two-leg count (so the two roundings cannot
        // disagree at small n).
        var top = DivvyApportion(P, new[] { DivvyThreeLegFrac, DivvyTwoLegFrac, DivvyOneLegFrac });
        var grad = DivvyApportion(top[1], new[] { DivvyBorderlineFrac, DivvyUsefulFrac, DivvyScarceFrac });
        var labels = new List<(int LegCount, string? Tier)>(P);
        for (var i = 0; i < top[0]; i++) labels.Add((3, null));
        for (var i = 0; i < grad[0]; i++) labels.Add((2, "borderline"));
        for (var i = 0; i < grad[1]; i++) labels.Add((2, "useful"));
        for (var i = 0; i < grad[2]; i++) labels.Add((2, "scarce"));
        for (var i = 0; i < top[2]; i++) labels.Add((1, null));

        // Phase A: Fisher-Yates over the label list (one draw per swap, i = P-1..1).
        for (var i = P - 1; i > 0; i--)
        {
            var j = (int)(rng.NextDouble() * (i + 1));
            (labels[i], labels[j]) = (labels[j], labels[i]);
        }

        // Phase B: roles — exactly one draw per player in id order. The first
        // ceil(1.2n) guards are forced into lead-handler roles and the first
        // ceil(1.2n) wings are forced ThreeAndDWing (the draw picks within the
        // forced set, or is consumed-and-ignored where the set has one member),
        // guaranteeing coverage supply with headroom; the rest draw uniformly.
        var leadQuota = (int)Math.Ceiling(DivvyRoleHeadroom * n);
        var tdwQuota = (int)Math.Ceiling(DivvyRoleHeadroom * n);
        var roles = new string[P];
        int gSeen = 0, wSeen = 0;
        for (var pid = 0; pid < P; pid++)
        {
            var u = rng.NextDouble();
            switch (pos[pid])
            {
                case "G":
                    roles[pid] = gSeen < leadQuota ? GenLeadRoles[(int)(u * 2)] : GenGuardRoles[(int)(u * 4)];
                    gSeen++;
                    break;
                case "W":
                    roles[pid] = wSeen < tdwQuota ? GenWingDefenderRole : GenWingRoles[(int)(u * 2)];
                    wSeen++;
                    break;
                default:
                    roles[pid] = GenBigRoles[(int)(u * 3)];
                    break;
            }
        }

        // Phase C: ratings, in player-id order. Session 26 machinery verbatim,
        // then the third-leg gradient redraw for two-leg players, then the floors
        // and the (pool-floor) leg-health guarantee.
        var r = new SplitMixRandom(rng);
        var pool = new List<PoolPlayer>(P);
        for (var pid = 0; pid < P; pid++)
        {
            var (lc, tier) = labels[pid];
            var (v, plusLegs) = GenRatings(roles[pid], pos[pid], lc, r);

            if (lc == 2)
            {
                var (bandLo, bandHi) = tier switch
                {
                    "borderline" => (DivvyBorderlineLo, DivvyBorderlineHi),
                    "useful"     => (DivvyUsefulLo, DivvyUsefulHi),
                    _            => (DivvyScarceLo, DivvyScarceHi),
                };
                // The third leg is the leg NOT in the plus set — derived from the
                // plus set directly, never via a second GenLegPriority call (which
                // would consume an extra wing draw and diverge from the oracle's
                // consumption contract). SKILL is never third: every position's
                // priority puts SKILL in the top two, so the third is SIZE or ATH.
                var third = plusLegs.Contains("SIZE") ? "ATH" : "SIZE";
                DivvyRedrawThirdLeg(v, pos[pid], third, bandLo, bandHi, r);
            }

            GenEnforceFloors(v, pos[pid]);
            GenEnforceLegHealth(v, pos[pid], DivvyLegHealthFloor);
            DeriveAndStampTendencies(v);   // AFTER all rating mutation (incl. third-leg redraw), BEFORE mapping

            var player = GenMapToPlayer(v, $"Pool_{pid}");
            var errs = player.Validate();
            if (errs.Count > 0)
                throw new InvalidOperationException(
                    $"pool generation bug — pool player {pid} ({roles[pid]}) failed Player.Validate():\n  " +
                    string.Join("\n  ", errs));

            pool.Add(new PoolPlayer(pid, pos[pid], roles[pid], lc, tier, plusLegs, v, player,
                DivvyScoutRank(v, pos[pid])));
        }

        return pool;
    }

    // Two-leg players only: overwrite the third leg's ratings from the gradient
    // band. Consumption is the leg's rating-array order; a SIZE redraw changes
    // Height, so FreeThrow is recomputed (3 draws) to keep its shape coherent.
    // A big's ATH third applies the downshift AFTER the band draw (assumption 3);
    // the SIZE path maps the band through the position scaling and — as found in
    // GenRatings — never consults the permitted-hole set.
    private static void DivvyRedrawThirdLeg(
        Dictionary<string, int> v, string pos, string third, int bandLo, int bandHi, Random r)
    {
        if (third == "SIZE")
        {
            var lo = DivvyGenericToSize(bandLo, pos);
            var hi = DivvyGenericToSize(bandHi, pos);
            foreach (var rt in GenSizeRatings)
                v[rt] = r.Next(lo, hi + 1);
            v["FreeThrow"] = DrawFreeThrowGen(v["Outside"], v["Height"], r);
        }
        else   // ATH — SKILL is never the third leg
        {
            foreach (var rt in GenAthRatings)
            {
                var val = r.Next(bandLo, bandHi + 1);
                if (pos == "B") val = Math.Max(0, val - GenBigAthDownshift);
                v[rt] = val;
            }
        }
    }

    // ── Scout rank ───────────────────────────────────────────────────────────────
    private static double DivvyRankFromLegs(double gSize, double gAth, double gSkill)
    {
        Span<double> l = stackalloc double[] { gSize, gAth, gSkill };
        l.Sort();   // ascending: l[2] = L1, l[0] = L3
        var conv = Math.Max(0.0, l[0] - DivvyRankConvAnchor);
        return DivvyRankW1 * l[2] + DivvyRankW2 * l[1] + DivvyRankW3 * l[0]
             + DivvyRankConvCoef * conv * conv;
    }

    // Session 29.1 fair scouting (Emmett's rulings, 2026-07-02):
    //  - the rank's SKILL input excludes FreeThrow (shooting must not buy prestige
    //    access; the FT draw itself is untouched, and the sheet's FT column stands);
    //  - a big's ATH is graded back onto the position's own scale by re-adding
    //    GenBigAthDownshift (the exact inverse of the generation-side -8 on the
    //    pool path — proven post-enforcement; standing condition: re-run that
    //    proof if the scarce band's floor or the pool leg-health floor ever moves).
    private static double DivvyScoutRank(Dictionary<string, int> v, string pos)
    {
        var holes = GenPermittedHoles[pos];
        var skillEx = new HashSet<string>(holes) { "FreeThrow" };
        var gs = DivvySizeToGeneric(GenLegMeanExHoles(v, "SIZE", holes), pos);
        var ga = GenLegMeanExHoles(v, "ATH", holes) + (pos == "B" ? GenBigAthDownshift : 0);
        var gk = GenLegMeanExHoles(v, "SKILL", skillEx);
        return DivvyRankFromLegs(gs, ga, gk);
    }

    // ── Stable per-(school, player) board noise — order-free by construction ────
    // A fresh SplitMix64 seeded from a mix of (divvySeed, schoolId, poolPlayerId);
    // two draws, triangular on (-1, 1) (sd 1/sqrt(6)). Reading it twice, in any
    // order, yields the identical value (Phase 54 §6.2).
    private static double DivvyNoiseU(long divvySeed, int schoolId, int poolPlayerId)
    {
        var seed = unchecked((ulong)divvySeed);
        seed ^= unchecked((ulong)(schoolId + 1) * DivvyMixP1);
        seed ^= unchecked((ulong)(poolPlayerId + 1) * DivvyMixP2);
        var r = new WorldRng(unchecked((long)seed));
        return r.NextDouble() + r.NextDouble() - 1.0;
    }

    // ── Pool preflight — global positional/role feasibility, validated loudly
    //    BEFORE pick one (it proves the draft STARTS feasible; the protected-supply
    //    rule is what keeps it feasible pick by pick) ─────────────────────────────
    private static void ValidateDivvyPool(IReadOnlyList<PoolPlayer> pool, int schoolCount)
    {
        var n = schoolCount;
        int g = pool.Count(p => p.Pos == "G"), w = pool.Count(p => p.Pos == "W"), b = pool.Count(p => p.Pos == "B");
        if (g != 4 * n || w != 3 * n || b != 3 * n)
            throw new InvalidOperationException(
                $"DIVVY INFEASIBLE: positional quotas broken — need {4 * n}G/{3 * n}W/{3 * n}B, " +
                $"pool has {g}G/{w}W/{b}B (shortfall: " +
                $"G {4 * n - g:+0;-0;0}, W {3 * n - w:+0;-0;0}, B {3 * n - b:+0;-0;0}).");
        var lead = pool.Count(p => GenLeadRoles.Contains(p.Role));
        var tdw = pool.Count(p => p.Role == GenWingDefenderRole);
        var quota = (int)Math.Ceiling(DivvyRoleHeadroom * n);
        if (lead < quota)
            throw new InvalidOperationException(
                $"DIVVY INFEASIBLE: lead-handler supply {lead} below quota {quota} (need >= 1.2 per school).");
        if (tdw < quota)
            throw new InvalidOperationException(
                $"DIVVY INFEASIBLE: {GenWingDefenderRole} supply {tdw} below quota {quota} (need >= 1.2 per school).");
    }

    // ── The draft ────────────────────────────────────────────────────────────────
    private sealed record DivvyPick(int PickNumber, int SchoolId, int PoolId, double PerceivedRank);

    private sealed class DivvyResult
    {
        public required List<PoolPlayer> Pool { get; init; }
        public required Dictionary<int, List<int>> Rosters { get; init; }   // schoolId -> pool ids, ACQUISITION ORDER (immutable)
        public required List<DivvyPick> Picks { get; init; }
        public required double NoiseScale { get; init; }
        public int MinSlackLead { get; set; }
        public int MinSlackTdw { get; set; }
    }

    private static DivvyResult RunDivvyDraft(WorldFile world, long divvySeed)
    {
        var n = world.Schools.Count;
        var rng = new WorldRng(divvySeed);
        var pool = BuildDivvyPool(n, rng);
        ValidateDivvyPool(pool, n);

        var P = pool.Count;
        var ranks = pool.Select(p => p.ScoutRank).ToArray();
        var sigma = DivvyNoiseSigmaFrac * (ranks.Max() - ranks.Min());
        var scale = sigma * Math.Sqrt(6.0);   // triangular sd = scale / sqrt(6)

        var isLead = pool.Select(p => GenLeadRoles.Contains(p.Role)).ToArray();
        var isTdw = pool.Select(p => p.Role == GenWingDefenderRole).ToArray();
        var remaining = new bool[P];
        Array.Fill(remaining, true);

        // Perceived boards: global rank + the school's stable perturbation. Drawn
        // once per (school, player), never rerolled — the coherent alternate board.
        var perceived = new Dictionary<int, double[]>(n);
        foreach (var s in world.Schools)
        {
            var board = new double[P];
            for (var pid = 0; pid < P; pid++)
                board[pid] = ranks[pid] + DivvyNoiseU(divvySeed, s.Id, pid) * scale;
            perceived[s.Id] = board;
        }

        var caps = world.Schools.ToDictionary(s => s.Id, _ => new Dictionary<string, int> { ["G"] = 4, ["W"] = 3, ["B"] = 3 });
        var needLead = world.Schools.ToDictionary(s => s.Id, _ => true);
        var needTdw = world.Schools.ToDictionary(s => s.Id, _ => true);
        var rosters = world.Schools.ToDictionary(s => s.Id, _ => new List<int>());
        var weights = world.Schools.ToDictionary(s => s.Id, s => Math.Pow(s.CurrentPrestige + DivvyOddsBase, DivvyOddsK));
        var rem = new Dictionary<string, int>
        {
            ["G"] = pool.Count(p => p.Pos == "G"),
            ["W"] = pool.Count(p => p.Pos == "W"),
            ["B"] = pool.Count(p => p.Pos == "B"),
        };
        var supplyLead = isLead.Count(x => x); var obligLead = n;
        var supplyTdw = isTdw.Count(x => x); var obligTdw = n;
        var result = new DivvyResult
        {
            Pool = pool, Rosters = rosters, Picks = new List<DivvyPick>(P), NoiseScale = scale,
            MinSlackLead = supplyLead - obligLead, MinSlackTdw = supplyTdw - obligTdw,
        };

        // Legality, two layers (brief §3d — coverage is a hard constraint, never a
        // preference score):
        //  (i)  per-school last-slot rule: an unmet coverage role must keep a slot
        //       of its position free — a school never burns its final guard slot on
        //       a non-lead while it still lacks a lead (same for wings/3&D);
        //  (ii) global protected supply: taking a protected-role player a school
        //       does NOT need is illegal whenever remaining supply of that role
        //       equals remaining unmet obligations (a needed take reduces supply
        //       and obligation together, and is always safe).
        bool HasLegal(int sid)
        {
            var c = caps[sid];
            if (c["G"] > 0 && rem["G"] > 0)
            {
                if (needLead[sid] && c["G"] == 1) { if (supplyLead > 0) return true; }
                else if (!needLead[sid] && supplyLead == obligLead) { if (rem["G"] - supplyLead > 0) return true; }
                else return true;
            }
            if (c["W"] > 0 && rem["W"] > 0)
            {
                if (needTdw[sid] && c["W"] == 1) { if (supplyTdw > 0) return true; }
                else if (!needTdw[sid] && supplyTdw == obligTdw) { if (rem["W"] - supplyTdw > 0) return true; }
                else return true;
            }
            if (c["B"] > 0 && rem["B"] > 0) return true;   // the interior body is any B
            return false;
        }

        bool IsLegal(int sid, int pid)
        {
            if (!remaining[pid]) return false;
            var c = caps[sid];
            var p = pool[pid].Pos;
            if (c[p] == 0) return false;
            if (needLead[sid] && c["G"] == 1 && p == "G" && !isLead[pid]) return false;
            if (needTdw[sid] && c["W"] == 1 && p == "W" && !isTdw[pid]) return false;
            if (!needLead[sid] && supplyLead == obligLead && isLead[pid]) return false;
            if (!needTdw[sid] && supplyTdw == obligTdw && isTdw[pid]) return false;
            return true;
        }

        var eligIds = new List<int>(n);
        var eligW = new List<double>(n);
        for (var pick = 0; pick < P; pick++)
        {
            // Who picks: prestige-weighted draw among schools with a legal candidate
            // this pick (a school with none is skipped; some school always has one —
            // the school holding the unmet obligation can always take the protected
            // player — so the draft always progresses; oracle-proven no-stall).
            eligIds.Clear(); eligW.Clear();
            foreach (var s in world.Schools)
            {
                var c = caps[s.Id];
                if (c["G"] + c["W"] + c["B"] == 0) continue;
                if (HasLegal(s.Id)) { eligIds.Add(s.Id); eligW.Add(weights[s.Id]); }
            }
            if (eligIds.Count == 0)
                throw new InvalidOperationException($"DIVVY STALL at pick {pick} — no school has a legal candidate (should be impossible; see oracle no-stall).");

            // Phase D: one draw per pick. Winner = first cumulative weight strictly
            // above u * total (the oracle's searchsorted-right, exactly).
            var u = rng.NextDouble();
            var total = 0.0;
            foreach (var wgt in eligW) total += wgt;
            var target = u * total;
            var winner = eligIds[^1];
            var acc = 0.0;
            for (var i = 0; i < eligIds.Count; i++)
            {
                acc += eligW[i];
                if (target < acc) { winner = eligIds[i]; break; }
            }

            // What they take: highest PERCEIVED rank in the legal set; ties break
            // to the lowest pool id (strictly-greater comparison, ascending scan).
            var board = perceived[winner];
            var best = -1; var bestVal = double.NegativeInfinity;
            for (var pid = 0; pid < P; pid++)
            {
                if (!IsLegal(winner, pid)) continue;
                if (board[pid] > bestVal) { bestVal = board[pid]; best = pid; }
            }
            if (best < 0)
                throw new InvalidOperationException($"DIVVY STALL: winner {winner} had no legal candidate at pick {pick}.");

            remaining[best] = false;
            rosters[winner].Add(best);
            caps[winner][pool[best].Pos] -= 1;
            rem[pool[best].Pos] -= 1;
            if (isLead[best])
            {
                supplyLead--;
                if (needLead[winner]) { needLead[winner] = false; obligLead--; }
            }
            if (isTdw[best])
            {
                supplyTdw--;
                if (needTdw[winner]) { needTdw[winner] = false; obligTdw--; }
            }
            result.MinSlackLead = Math.Min(result.MinSlackLead, supplyLead - obligLead);
            result.MinSlackTdw = Math.Min(result.MinSlackTdw, supplyTdw - obligTdw);
            result.Picks.Add(new DivvyPick(pick, winner, best, bestVal));
        }

        return result;
    }

    // ── The opening five — the binding contract (S29 prompt §4, amended 29.1, 30.1) ─
    // Acquisition order is IMMUTABLE and is the printed depth order. Session 29.1
    // (Emmett's ruling, 2026-07-02): the opening five gets a PLAYABLE FLOOR — the
    // earliest five acquired that satisfies the quotas. Session 30.1 (Emmett's
    // ruling, 2026-07-03, after the first stock season showed 44 schools whose
    // three wings never played a possession): the floor extends to a wing —
    // at least 1 big, at least 2 guards, at least 1 wing.
    // Greedy walk of the acquisition order with the same feasibility logic the
    // draft's last-slot rule uses: take the earliest player, skipping one only
    // when taking him would leave too few remaining slots to cover the unmet
    // quota. When the raw first five already satisfies the quotas, the opening
    // five IS the raw first five. Every roster is exactly 4G/3W/3B, so the quota
    // is always satisfiable and the walk cannot strand — proven exhaustively over
    // all 4,200 distinct orderings of a 4G/3W/3B roster (Session 30.1 pre-check;
    // Phase 54 asserts on live drafts).
    // Deterministic; still rank-blind by signature — the inputs are the
    // acquisition order and positions, never rank or ratings (Phase 54 asserts).
    // The 29.1 no-wing residual is RETIRED: every position now seats at least one
    // starter, so every benched player has a same-position door under the fence.
    // The absent-position warnings downstream stay as never-fire sentinels.
    private static int[] BuildOpeningFive(IReadOnlyList<int> acquisitionOrder, Func<int, string> positionOf)
    {
        if (acquisitionOrder.Count != 10)
            throw new InvalidOperationException($"BuildOpeningFive needs a full ten-man roster (got {acquisitionOrder.Count}).");
        var five = new List<int>(5);
        int needB = 1, needG = 2, needW = 1;
        foreach (var pid in acquisitionOrder)
        {
            if (five.Count == 5) break;
            var pos = positionOf(pid);
            var nb = pos == "B" ? Math.Max(0, needB - 1) : needB;
            var ng = pos == "G" ? Math.Max(0, needG - 1) : needG;
            var nw = pos == "W" ? Math.Max(0, needW - 1) : needW;
            if (nb + ng + nw > 5 - five.Count - 1) continue;   // taking him would strand a quota
            five.Add(pid);
            needB = nb; needG = ng; needW = nw;
        }
        if (five.Count != 5)
            throw new InvalidOperationException("BuildOpeningFive could not seat a legal five (roster-shape bug — every roster must be 4G/3W/3B).");
        return five.ToArray();
    }

    // ── The readout ──────────────────────────────────────────────────────────────
    private static void RunDivvy(string engineConfigPath, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage: divvy <world.json> <seed> [schoolIdA schoolIdB]   (the two ids run a smoke sim)");
            return;
        }
        WorldFile world;
        if (!long.TryParse(args[2], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var seed))
        {
            Console.WriteLine($"DIVVY ERROR: seed '{args[2]}' is not a valid integer.");
            return;
        }
        try
        {
            world = LoadWorld(args[1]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.WriteLine($"DIVVY ERROR: {ex.Message}");
            return;
        }

        Console.WriteLine($"=== DIVVY: {world.Schools.Count} schools, seed {seed} (pool + draft reproducible from world + seed) ===");
        Console.WriteLine();

        DivvyResult res;
        try { res = RunDivvyDraft(world, seed); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"DIVVY ERROR: {ex.Message}");
            return;
        }

        PrintPoolSheet(res, world.Schools.Count);
        PrintDraftStory(res, world);
        PrintSampleRosterSheets(res, world, seed);
        PrintVarianceAndOverlap(res, world);

        if (args.Length >= 5 &&
            int.TryParse(args[3], out var idA) && int.TryParse(args[4], out var idB))
            RunDivvySmokeSim(res, world, idA, idB, seed, engineConfigPath);
    }

    private static string DivvyGroupOf(PoolPlayer p)
        => p.LegCount == 3 ? "three-leg" : p.LegCount == 2 ? $"two ({p.GradientTier})" : "one-leg";

    private static void PrintPoolSheet(DivvyResult res, int n)
    {
        var pool = res.Pool;
        var P = pool.Count;
        var top = DivvyApportion(P, new[] { DivvyThreeLegFrac, DivvyTwoLegFrac, DivvyOneLegFrac });
        var grad = DivvyApportion(top[1], new[] { DivvyBorderlineFrac, DivvyUsefulFrac, DivvyScarceFrac });

        Console.WriteLine($"--- THE POOL ({P} players; the one authored distribution) ---");
        Console.WriteLine($"  leg-count mix    target {top[0]}/{top[1]}/{top[2]} (3/2/1-leg)  " +
                          $"generated {pool.Count(p => p.LegCount == 3)}/{pool.Count(p => p.LegCount == 2)}/{pool.Count(p => p.LegCount == 1)}");
        Console.WriteLine($"  gradient tiers   target {grad[0]}/{grad[1]}/{grad[2]} (borderline/useful/scarce)  " +
                          $"generated {pool.Count(p => p.GradientTier == "borderline")}/{pool.Count(p => p.GradientTier == "useful")}/{pool.Count(p => p.GradientTier == "scarce")}");
        Console.WriteLine($"  positions        {pool.Count(p => p.Pos == "G")}G / {pool.Count(p => p.Pos == "W")}W / {pool.Count(p => p.Pos == "B")}B  (quotas {4 * n}/{3 * n}/{3 * n})");
        Console.WriteLine($"  coverage supply  lead-handlers {pool.Count(p => GenLeadRoles.Contains(p.Role))}, " +
                          $"{GenWingDefenderRole} {pool.Count(p => p.Role == GenWingDefenderRole)}  (quota >= {(int)Math.Ceiling(DivvyRoleHeadroom * n)} each)");
        Console.WriteLine("  scout rank by group (mean [min..max]) — the divvy's board, never a game input:");
        foreach (var g in new[] { "three-leg", "two (borderline)", "two (useful)", "two (scarce)", "one-leg" })
        {
            var rs = pool.Where(p => DivvyGroupOf(p) == g).Select(p => p.ScoutRank).ToList();
            Console.WriteLine($"    {g,-18} {rs.Average(),6:F1}  [{rs.Min(),5:F1} .. {rs.Max(),5:F1}]   n={rs.Count}");
        }
        Console.WriteLine();
    }

    private static void PrintDraftStory(DivvyResult res, WorldFile world)
    {
        var prestige = world.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
        var names = world.Schools.ToDictionary(s => s.Id, s => s.Name);
        Console.WriteLine("--- THE DRAFT (did access work) ---");
        Console.WriteLine("  picks by prestige band (mean / median pick number):");
        foreach (var band in new[] { (80, 99), (60, 79), (40, 59), (20, 39), (0, 19) })
        {
            var picks = res.Picks.Where(p => prestige[p.SchoolId] >= band.Item1 && prestige[p.SchoolId] <= band.Item2)
                                 .Select(p => p.PickNumber).OrderBy(x => x).ToList();
            if (picks.Count == 0) continue;
            Console.WriteLine($"    {band.Item1,2}-{band.Item2,-2}  mean {picks.Average(),7:F1}   median {picks[picks.Count / 2],5}   (picks: {picks.Count})");
        }

        // The surprises table: no true sim-value metric exists this pass, so nothing
        // claims a player was ACTUALLY undervalued — the two variance sources are
        // reported as separate columns. "Actual college value" joins when seasons exist.
        var ranks = res.Pool.Select(p => p.ScoutRank).ToArray();
        var rankIndex = Enumerable.Range(0, ranks.Length).OrderByDescending(i => ranks[i]).ToArray();
        var expectedPick = new int[ranks.Length];
        for (var i = 0; i < rankIndex.Length; i++) expectedPick[rankIndex[i]] = i;
        var topDecile = new HashSet<int>(rankIndex.Take(ranks.Length / 10));
        var pickOf = res.Picks.ToDictionary(p => p.PoolId);

        Console.WriteLine("  draft surprises (largest access deviation among top-decile-rank players):");
        Console.WriteLine($"    {"pool#",-6}{"grp",-17}{"global",-8}{"perceived",-10}{"pick",-6}{"selector (prestige)",-30}{"boardDev",-9}accessDev");
        var rows = topDecile.Select(pid => (pid, dev: pickOf[pid].PickNumber - expectedPick[pid]))
                            .OrderByDescending(t => Math.Abs(t.dev)).Take(10);
        foreach (var (pid, dev) in rows)
        {
            var pk = pickOf[pid];
            var p = res.Pool[pid];
            Console.WriteLine($"    {pid,-6}{DivvyGroupOf(p),-17}{p.ScoutRank,-8:F1}{pk.PerceivedRank,-10:F1}{pk.PickNumber,-6}" +
                              $"{names[pk.SchoolId] + " (" + prestige[pk.SchoolId] + ")",-30}{pk.PerceivedRank - p.ScoutRank,-9:F1}{dev:+0;-0;0}");
        }

        var med = world.Schools.Select(s => s.CurrentPrestige).OrderBy(x => x).ElementAt(world.Schools.Count / 2);
        var leaks = res.Rosters.Where(kv => prestige[kv.Key] < med)
                               .Sum(kv => kv.Value.Count(pid => topDecile.Contains(pid)));
        Console.WriteLine($"  leaks: {leaks} top-decile-rank players hold roster spots below median prestige (the Gonzaga mechanism, alive)");
        Console.WriteLine();
    }

    private static void PrintSampleRosterSheets(DivvyResult res, WorldFile world, long seed)
    {
        var byPrestige = world.Schools.OrderByDescending(s => s.CurrentPrestige).ToList();
        var picksSample = new List<WorldSchool>
        {
            byPrestige[0],                          // highest prestige
            byPrestige[byPrestige.Count / 2],       // median
            byPrestige[^1],                         // lowest
        };
        // two seeded-random others — a SEPARATE mixed stream so the sample choice
        // never perturbs the pool/draft consumption contract
        var sampleRng = new WorldRng(unchecked(seed ^ 0x5EED5A17));
        while (picksSample.Count < Math.Min(5, world.Schools.Count))
        {
            var s = world.Schools[(int)(sampleRng.NextDouble() * world.Schools.Count)];
            if (!picksSample.Contains(s)) picksSample.Add(s);
        }

        Console.WriteLine("--- SAMPLE ROSTER SHEETS (acquisition order = depth order; * = opening five) ---");
        Console.WriteLine("  (the Session 26 depth-gap headline must now EMERGE: high prestige two-leg deep");
        Console.WriteLine("   into the rotation, low prestige cratering after the top man — check it did)");
        foreach (var s in picksSample)
            PrintDivvyRosterSheet(res, s);
        Console.WriteLine();
    }

    private static void PrintDivvyRosterSheet(DivvyResult res, WorldSchool school)
    {
        var roster = res.Rosters[school.Id];
        var five = new HashSet<int>(BuildOpeningFive(roster, pid => res.Pool[pid].Pos));
        Console.WriteLine($"  === {school.Name} ({school.Abbr})  prestige {school.CurrentPrestige} ===");
        Console.WriteLine($"    {"Acq",-5}{"Pos",-4}{"Role",-17}{"Legs",-22}{"Size",5}{"Ath",5}{"Skl",5}{"FT",5}  Depth");
        for (var i = 0; i < roster.Count; i++)
        {
            var p = res.Pool[roster[i]];
            var holes = GenPermittedHoles[p.Pos];
            var legsStr = string.Join(" ", new[] { "SIZE", "ATH", "SKILL" }
                .Select(l => (p.PlusLegs.Contains(l) ? "+" : "~") + l[0]));
            var star = five.Contains(roster[i]) ? "*" : " ";
            Console.WriteLine($"    {star}{i + 1,-4}{p.Pos,-4}{p.Role,-17}{legsStr,-22}" +
                              $"{GenLegMeanExHoles(p.Ratings, "SIZE", holes),5:F0}" +
                              $"{GenLegMeanExHoles(p.Ratings, "ATH", holes),5:F0}" +
                              $"{GenLegMeanExHoles(p.Ratings, "SKILL", holes),5:F0}" +
                              $"{p.Ratings["FreeThrow"],5}  " +
                              (p.LegCount == 3 ? "three-leg" : p.LegCount == 2 ? $"two-leg ({p.GradientTier})" : "one-leg"));
        }
    }

    private static void PrintVarianceAndOverlap(DivvyResult res, WorldFile world)
    {
        var prestige = world.Schools.ToDictionary(s => s.Id, s => s.CurrentPrestige);
        Console.WriteLine("--- VARIANCE & OVERLAP (the Pass 3 assumption, observed early) ---");
        var bands = new[] { (0, 19), (20, 39), (40, 59), (60, 79), (80, 99) };
        var stats = new List<(string Label, double Mean, int Min, int Max)>();
        foreach (var (lo, hi) in bands)
        {
            var counts = res.Rosters.Where(kv => prestige[kv.Key] >= lo && prestige[kv.Key] <= hi)
                                    .Select(kv => kv.Value.Count(pid => res.Pool[pid].LegCount >= 2)).ToList();
            if (counts.Count == 0) continue;
            stats.Add(($"{lo}-{hi}", counts.Average(), counts.Min(), counts.Max()));
            Console.WriteLine($"  prestige {lo,2}-{hi,-2}  multi-leg per roster: mean {counts.Average():F2}  spread [{counts.Min()}..{counts.Max()}]  (n={counts.Count})");
        }
        for (var i = 0; i + 1 < stats.Count; i++)
        {
            var overlap = stats[i].Max >= stats[i + 1].Min;
            Console.WriteLine($"  bands {stats[i].Label} and {stats[i + 1].Label} overlap: {(overlap ? "YES" : "no")} " +
                              $"(lower band's best roster {stats[i].Max} vs upper band's worst {stats[i + 1].Min})");
        }
        Console.WriteLine();
    }

    // ── Optional smoke sim: two divvied rosters through the existing ten-man gen
    //    runner (a sanity check ONLY, never the proof) ─────────────────────────────
    private static void RunDivvySmokeSim(
        DivvyResult res, WorldFile world, int idA, int idB, long seed, string engineConfigPath)
    {
        var schoolA = world.Schools.FirstOrDefault(s => s.Id == idA);
        var schoolB = world.Schools.FirstOrDefault(s => s.Id == idB);
        if (schoolA is null || schoolB is null)
        {
            Console.WriteLine($"DIVVY SMOKE SIM: unknown school id ({(schoolA is null ? idA : idB)}).");
            return;
        }

        // Build GenPlayerRow lists in ACQUISITION order (slot = acquisition index,
        // starter = opening five), then reuse the gen path's assembly verbatim:
        // stamp A -> 1..10, B -> 11..20, seat the five, fence the reserves.
        List<GenPlayerRow> Rows(WorldSchool s)
        {
            var roster = res.Rosters[s.Id];
            var five = new HashSet<int>(BuildOpeningFive(roster, pid => res.Pool[pid].Pos));
            return roster.Select((pid, i) =>
            {
                var p = res.Pool[pid];
                return new GenPlayerRow(i + 1, p.Pos, p.Role, five.Contains(pid), p.LegCount,
                                        p.PlusLegs, p.Ratings, p.Player);
            }).ToList();
        }

        // Session 30.1: with the seating floor at 1B/2G/1W, no position can be
        // absent — this warning is now a never-fire SENTINEL, kept (per the 29.1
        // once-per-side contract) so any future floor change stays visible on the
        // page, never silent. A line printing here is a seating bug.
        void WarnAbsentPositions(WorldSchool s, List<GenPlayerRow> rows)
        {
            var onFloor = new HashSet<string>(rows.Where(r => r.Starter).Select(r => r.Pos));
            var absent = new[] { "G", "W", "B" }.Where(p => !onFloor.Contains(p)).ToList();
            if (absent.Count > 0)
                Console.WriteLine($"  NOTE {s.Name}: no {string.Join("/", absent)} on the floor — " +
                                  $"benched {string.Join("/", absent)} cannot enter under the same-position fence.");
        }

        var rowsA = Rows(schoolA);
        var rowsB = Rows(schoolB);
        WarnAbsentPositions(schoolA, rowsA);
        WarnAbsentPositions(schoolB, rowsB);
        var stampedA = new Player[10];
        var stampedB = new Player[10];
        for (var i = 0; i < 10; i++) stampedA[i] = StampPlayerId(rowsA[i].Player, rowsA[i].Slot);
        for (var i = 0; i < 10; i++) stampedB[i] = StampPlayerId(rowsB[i].Player, rowsB[i].Slot + 10);
        var sideA = BuildGenSideData(rowsA, stampedA);
        var sideB = BuildGenSideData(rowsB, stampedB);
        var identity = BuildGenIdentity(rowsA, rowsB);

        var smokeConfig = new GenConfig
        {
            GameCount = 200,
            BaseSeed = unchecked((int)seed),
            GenSeed = unchecked((int)seed),
            ProgramA = new GenProgram(Math.Max(1, schoolA.CurrentPrestige), "none"),
            ProgramB = new GenProgram(Math.Max(1, schoolB.CurrentPrestige), "none"),
        };

        Console.WriteLine($"--- SMOKE SIM (sanity only): {schoolA.Name} (prestige {schoolA.CurrentPrestige}) vs " +
                          $"{schoolB.Name} (prestige {schoolB.CurrentPrestige}), {smokeConfig.GameCount} games ---");
        var stats = RunGenMatchup(smokeConfig, stampedA, stampedB, sideA, sideB, engineConfigPath);
        PrintGenChannels(stats);
        PrintGenBoxScore(stats, identity);
    }
}
