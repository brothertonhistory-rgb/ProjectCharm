using System.Globalization;
using Charm.Engine;

namespace Charm.Harness;

// ============================================================================
// Season calibration instrument — Session 31.
//
// The measuring stick, not the dials: the season loop stops discarding its
// per-game numbers, aggregates them league-wide, and prints one page section —
// sim vs. the D1 decade blend, line by line — plus a possession-length split
// by how possessions end.
//
// Every target below is PAGE-ONLY, never a suite assertion. Rates move when
// dials are tuned in later sessions; hard-asserting them here would make every
// future calibration session break the suite by design. Phase 55 §3.8 asserts
// only MACHINERY: conservation identities proving this accumulator counts what
// the engine produced (points reconcile three ways, the ending buckets
// partition the records, per-game elapsed matches TotalSeconds).
//
// Turnover counting is by the committed 15-label classifier
// IsTurnoverPossession (Program.Harness.Shared.cs) — the label list is NEVER
// re-listed here. Turnover METADATA (TurnoverOffSlot / TurnoverWasLiveBall) is
// used only by the drift guard, which turns a future unclassified TO label red
// instead of letting it leak into the OTHER bucket.
// ============================================================================

internal static partial class Program
{
    /// <summary>League-wide accumulator for the calibration readout. Fed once per
    /// game inside RunSeasonCore from the triple the season loop already produces
    /// — (game, result, attributed) — and carried on SeasonRunOutcome so Phase 55
    /// reads it from the §3.5 fixture run it already makes (zero extra games).
    /// Public mutable fields, matching the PlayerBoxTotals sibling.</summary>
    private sealed class SeasonLeagueStats
    {
        public long Games;

        // Points, three independent ways (Phase 55 §3.8(a) reconciles all three):
        // from the game's score fields, and from the per-possession records. The
        // third leg — the recorded SeasonGameResult scores — lives on the outcome.
        public long PointsFromScores;    // sum of game.HomeScore + game.AwayScore
        public long PointsFromRecords;   // sum of PossessionRecord.Points

        // From records (result.Possessions):
        public long Fga, Fgm, ThreePa, ThreePm, Fta, Ftm;

        // Session 32: per-zone attempt/make totals (Three rides the existing
        // ThreePa/ThreePm pair above — the Three zone IS the three-point line).
        // Sourced from the same PossessionRecord fields the per-seed observation
        // mode already asserts bin-conservation on; §3.8 proves the ACCUMULATOR
        // preserved that identity at league scale.
        public long RimFga, RimFgm, ShortFga, ShortFgm, MidFga, MidFgm, LongFga, LongFgm;
        public long PossessionRecords;       // every record — the pace numerator
        public long TurnoverPossessions;     // via IsTurnoverPossession, all records
        public long MetadataDriftRecords;    // TO metadata present but classifier says no

        // From the box (attributed, all 20 indices — side-symmetric by construction):
        public long OReb, DReb, Ast, Stl, Blk;

        // From the game/result:
        public long OtGames, OtPeriods;
        public double TotalSeconds;

        // The per-game elapsed guard: |sum of record Elapsed − result.TotalSeconds|.
        // Both sides sum the identical `applied` doubles in the identical order, so
        // exact equality is expected; the 1e-6 tolerance is defensive. Per-game, not
        // a season aggregate — offsetting errors cannot satisfy it.
        public long ElapsedMismatchGames;
        public double MaxElapsedMismatch;

        // The length-split buckets: (count, sum of Elapsed) per ending. NoShot and
        // HoldShootLast possessions are EXCLUDED from the buckets — identified via
        // EndOfHalfIntent, never via EndLabel, because HoldShootLast still runs the
        // resolver and can end Made/DREB/turnover — but INCLUDED in pace and every
        // other line (they are real possessions whose Elapsed is a clock-fill charge,
        // not a drawn possession length).
        public long MadeN;      public double MadeS;
        public long FtTripN;    public double FtTripS;
        public long MissDrebN;  public double MissDrebS;
        public long MissOobN;   public double MissOobS;
        public long TurnoverN;  public double TurnoverS;
        public long FixedTimeN; public double FixedTimeS;   // sub-line of TURNOVER
        public long OtherN;     public double OtherS;
        public long ExcludedN;                              // the NoShot/HoldShootLast count

        public void Accumulate(GameState game, GovernorRunResult result, PlayerBoxTotals box)
        {
            Games++;
            PointsFromScores += game.HomeScore + game.AwayScore;
            TotalSeconds += result.TotalSeconds;
            if (result.OvertimePeriods > 0) OtGames++;
            OtPeriods += result.OvertimePeriods;

            for (var i = 0; i < 20; i++)
            {
                OReb += box.OReb[i]; DReb += box.DReb[i];
                Ast  += box.Ast[i];  Stl  += box.Stl[i];  Blk += box.Blk[i];
            }

            var elapsedSum = 0.0;
            foreach (var r in result.Possessions)
            {
                PossessionRecords++;
                elapsedSum        += r.Elapsed;
                PointsFromRecords += r.Points;
                Fga += r.Fga; Fgm += r.Fgm;
                ThreePa += r.ThreePa; ThreePm += r.ThreePm;
                Fta += r.Fta; Ftm += r.Ftm;
                RimFga   += r.RimFga;   RimFgm   += r.RimFgm;
                ShortFga += r.ShortFga; ShortFgm += r.ShortFgm;
                MidFga   += r.MidFga;   MidFgm   += r.MidFgm;
                LongFga  += r.LongFga;  LongFgm  += r.LongFgm;

                var isTo = IsTurnoverPossession(r);
                if (isTo) TurnoverPossessions++;
                if (!isTo && (r.TurnoverOffSlot != null || r.TurnoverWasLiveBall))
                    MetadataDriftRecords++;

                if (r.EndOfHalfIntent != null) { ExcludedN++; continue; }

                if (r.EndLabel == "Made")                     { MadeN++;     MadeS     += r.Elapsed; }
                else if (r.EndLabel == "FreeThrowsMade")      { FtTripN++;   FtTripS   += r.Elapsed; }
                else if (r.EndLabel == "DefensiveRebound")    { MissDrebN++; MissDrebS += r.Elapsed; }
                else if (r.EndLabel == "MissOutOfBoundsLost") { MissOobN++;  MissOobS  += r.Elapsed; }
                else if (isTo)
                {
                    TurnoverN++; TurnoverS += r.Elapsed;
                    // The fixed-time subset (invariant ElapsedSeconds: 30.0 / 0.0 /
                    // 10.0 s, RollCConfig). Not a second copy of the classifier —
                    // COUNTING is by IsTurnoverPossession above; this only splits the
                    // already-counted turnover bucket for the length readout.
                    if (r.EndLabel is "ShotClockViolation" or "FiveSecondInbound"
                                    or "TenSecondBackcourt")
                    { FixedTimeN++; FixedTimeS += r.Elapsed; }
                }
                else { OtherN++; OtherS += r.Elapsed; }   // parked:*, LooseBallFoulOnOffense, future labels
            }

            var delta = Math.Abs(elapsedSum - result.TotalSeconds);
            if (delta > MaxElapsedMismatch) MaxElapsedMismatch = delta;
            if (delta > 1e-6) ElapsedMismatchGames++;
        }
    }

    // ── The reference card ────────────────────────────────────────────────────
    //
    // PROVISIONAL Session 31 reference card. Center points compiled from published
    // national per-season averages — the blend is the mean of the ten annual D1
    // per-team-per-game national averages, 2015-16..2024-25 — not yet pinned to one
    // saved source extract. The bands (±1.0 point on percentages, ±5% relative on
    // volumes) are 5–10× wider than any methodology-of-averaging difference, so a
    // verdict cannot flip on provenance. When a tuning session needs a bullseye on a
    // specific line rather than a band, that session pins that line's source first
    // (recorded as a Session 31 deferral).
    //
    // Rebound caveat, stated once: public reference rebound totals include TEAM
    // rebounds (dead-ball boards no player is credited with); the sim total is all
    // credited individual rebounds — the engine's closest analog of the uncredited
    // board is the MissOutOfBoundsLost ending. The instrument makes that definition
    // gap visible; it does not paper over it. (Personal fouls ~17.5 is on the
    // real-world card but is NOT printable in v1 — the engine keeps no cumulative
    // PF counter, and an estimate must not wear a measurement's clothes; deferred.)

    private const double CalRelBand = 0.05;   // ±5% relative on volume lines

    // ── Per-zone observed-FG% anchor targets (the Session 50 ruling: what an
    //    average shooter in an even matchup should shoot per zone, box-score
    //    definition). This static table is the SOLE committed home of these five
    //    constants — the make-midpoint oracle receives them as explicit inputs
    //    copied from the printed readout, never as a second hard-coded copy. ──
    private const double ZoneTargetRim   = 61.0;
    private const double ZoneTargetShort = 43.0;
    private const double ZoneTargetMid   = 39.0;
    private const double ZoneTargetLong  = 36.0;
    private const double ZoneTargetThree = 34.0;

    private static void PrintCalibrationReadout(SeasonLeagueStats s)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        static double Pct(long num, long den) => den > 0 ? 100.0 * num / den : 0.0;
        static double Avg(double sum, long n) => n > 0 ? sum / n : 0.0;

        Console.WriteLine("--- CALIBRATION READOUT (sim vs D1 decade blend 2015-16..2024-25, " +
                          "provisional; page-only, never asserted) ---");
        if (s.Games == 0)
        {
            Console.WriteLine("  no games accumulated.");
            return;
        }

        Console.WriteLine(Inv($"  {"line",-28}{"sim",-9}{"target",-10}{"band",-12}verdict"));

        var g2 = 2.0 * s.Games;   // every per-team-per-game line divides by 2·G —
                                  // the league totals pool BOTH teams of every game.

        void Row(string line, double sim, double target, double halfBand, string band, string fmt)
        {
            var verdict = sim < target - halfBand ? "LOW"
                        : sim > target + halfBand ? "HIGH" : "OK";
            Console.WriteLine(Inv($"  {line,-28}") +
                sim.ToString(fmt, CultureInfo.InvariantCulture).PadRight(9) +
                target.ToString(fmt, CultureInfo.InvariantCulture).PadRight(10) +
                band.PadRight(12) + verdict);
        }
        void RowAbs(string line, double sim, double target, double halfBand, string fmt = "F1") =>
            Row(line, sim, target, halfBand,
                "+/-" + halfBand.ToString("0.0", CultureInfo.InvariantCulture), fmt);
        void RowRel(string line, double sim, double target, string fmt = "F1") =>
            Row(line, sim, target, target * CalRelBand, "+/-5% rel", fmt);

        RowAbs("points",                    s.PointsFromScores / g2, 72.0, 2.0);
        RowRel("FGA",                       s.Fga / g2,              57.5);
        RowAbs("FG%",                       Pct(s.Fgm, s.Fga),       44.0, 1.0);
        RowRel("3PA",                       s.ThreePa / g2,          22.5);
        RowAbs("3P%",                       Pct(s.ThreePm, s.ThreePa), 34.0, 1.0);
        RowRel("FTA",                       s.Fta / g2,              19.5);
        RowAbs("FT%",                       Pct(s.Ftm, s.Fta),       71.0, 1.0);
        RowRel("rebounds (credited)",       (s.OReb + s.DReb) / g2,  34.5);
        RowRel("- offensive",               s.OReb / g2,              9.5);
        RowAbs("ORB%",                      Pct(s.OReb, s.OReb + s.DReb), 28.5, 1.5);
        RowRel("assists",                   s.Ast / g2,              13.5);
        RowRel("turnovers",                 s.TurnoverPossessions / g2, 12.5);
        RowAbs("TO% of possessions",        Pct(s.TurnoverPossessions, s.PossessionRecords), 18.5, 1.5);
        RowRel("steals",                    s.Stl / g2,               6.2);
        RowRel("blocks",                    s.Blk / g2,               3.5);
        RowRel("pace (poss/team, OT incl.)", s.PossessionRecords / g2, 69.0);
        RowRel("3PA rate (3PA/FGA)",        s.Fga > 0 ? (double)s.ThreePa / s.Fga : 0.0, 0.39, "F2");
        RowRel("FT rate (FTA/FGA)",         s.Fga > 0 ? (double)s.Fta / s.Fga : 0.0,     0.34, "F2");
        Row("OT games",                     Pct(s.OtGames, s.Games), 6.0, 2.0, "4-8%", "F1");

        // Session 32: per-zone shooting block — the make dial's instrument.
        // Same Row machinery, same page-only discipline: verdicts are never asserted.
        Console.WriteLine("  per-zone FG% (sim vs Session 50 anchors; page-only, never asserted):");
        RowAbs("  rim FG%",                 Pct(s.RimFgm,   s.RimFga),   ZoneTargetRim,   1.0);
        RowAbs("  short FG%",               Pct(s.ShortFgm, s.ShortFga), ZoneTargetShort, 1.0);
        RowAbs("  mid FG%",                 Pct(s.MidFgm,   s.MidFga),   ZoneTargetMid,   1.0);
        RowAbs("  long FG%",                Pct(s.LongFgm,  s.LongFga),  ZoneTargetLong,  1.0);
        RowAbs("  three FG%",               Pct(s.ThreePm,  s.ThreePa),  ZoneTargetThree, 1.0);
        Console.WriteLine(Inv($"    zone FGA mix: rim {s.RimFga} / short {s.ShortFga} / mid {s.MidFga}") +
                          Inv($" / long {s.LongFga} / three {s.ThreePa}  (sum {s.RimFga + s.ShortFga + s.MidFga + s.LongFga + s.ThreePa} vs FGA {s.Fga})"));

        Console.WriteLine("  seconds per possession by ending (NoShot/HoldShootLast excluded):");
        Console.WriteLine(
            Inv($"    made {Avg(s.MadeS, s.MadeN):F1}s (n={s.MadeN})") +
            Inv($" | FT trip {Avg(s.FtTripS, s.FtTripN):F1}s") +
            Inv($" | miss->DREB {Avg(s.MissDrebS, s.MissDrebN):F1}s") +
            Inv($" | miss OOB {Avg(s.MissOobS, s.MissOobN):F1}s"));
        Console.WriteLine(
            Inv($"    turnover {Avg(s.TurnoverS, s.TurnoverN):F1}s (n={s.TurnoverN})  ") +
            Inv($"[fixed-time violations {Avg(s.FixedTimeS, s.FixedTimeN):F1}s (n={s.FixedTimeN})]  ") +
            Inv($"| other n={s.OtherN}"));
    }
}
