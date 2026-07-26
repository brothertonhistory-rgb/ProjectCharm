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
        // Session 38: fast-break shot-diet totals (page-only, never asserted on the page).
        public long FastBreakFga, FastBreakThreePa, FastBreakThreePm;
        public long PossessionRecords;       // every record — the pace numerator
        public long TurnoverPossessions;     // via IsTurnoverPossession, all records
        public long MetadataDriftRecords;    // TO metadata present but classifier says no

        // From the box (attributed, all 20 indices — side-symmetric by construction):
        public long OReb, DReb, Ast, Stl, Blk;

        // Session 63: the baseline-read lines (page-only, never asserted). Full-game
        // foul totals from the attribution arrays (SFL = shooting fouls, NSF =
        // non-shooting — the S62 split), and a per-(school, depth-slot) usage
        // accumulator so the league usage SPREAD (max/p90/median/min) is readable on
        // the page. Usage = (FGA + 0.44·FTA + TO) / the same team total — the
        // standard box-score possession-share proxy.
        public long SflTotal, NsfTotal;
        public readonly Dictionary<(int SchoolId, int Slot), (long Fga, long Fta, long To)> PlayerUsage = new();
        public readonly Dictionary<int, (long Fga, long Fta, long To)> TeamUsage = new();

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
        // Session 37: court-aware turnover-length split. The official classifier filters
        // FIRST (fixed-time violations, which carry no TimeProfile, stay on their own
        // sub-line above); only the profile-stamped drawn turnovers split by court.
        // Raw = the pre-clamp band draw (the oracle's prediction target); applied = the
        // clamped record Elapsed (raw min period-remaining).
        public long BackcourtToN;  public double BackcourtToAppliedS, BackcourtToRawS;
        public long FrontcourtToN; public double FrontcourtToAppliedS, FrontcourtToRawS;
        // Session 37 structural observations — read + ASSERTED by Phase 57, never printed
        // as a calibration target. Raw band ranges (frontcourt max over SINGLE-period
        // draws only: a multi-period frontcourt total can legitimately exceed 30s), the
        // multi-period frontcourt count, and a LEAK counter: any drawn (non-fixed-time)
        // turnover that reached the Governor with NO TimeProfile — an emitter that forgot
        // to stamp, which would silently draw the shared clock. Phase 57 asserts it is 0.
        public double BackcourtRawMin = double.PositiveInfinity, BackcourtRawMax = double.NegativeInfinity;
        public double FrontcourtRawMin1P = double.PositiveInfinity, FrontcourtRawMax1P = double.NegativeInfinity;
        public long FrontcourtMultiPeriodN;
        public long DrawnTurnoverNoProfileN;
        public long OtherN;     public double OtherS;
        public long ExcludedN;                              // the NoShot/HoldShootLast count

        // Session 33 Phase A: itemize the OTHER bucket — per-label (count, elapsed
        // sum), fed ONLY from records landing in the else-branch below. Classifies
        // nothing; this is the measurement that informs rulings R1/R2.
        public readonly Dictionary<string, (long N, double S)> OtherByLabel = new();

        // Session 33 Phase A: jump-ball award relative to the possession's OFFENSE,
        // parsed from the label suffix — never assumed to flip, because a held ball
        // can retain offense (the arrow/tip sets the awarded team). Split tip vs
        // arrow, retained vs awarded-to-defense, each with its elapsed sum.
        public long JbTipRetainedN,   JbTipAwardedN,   JbArrowRetainedN,   JbArrowAwardedN;
        public double JbTipRetainedS, JbTipAwardedS,   JbArrowRetainedS,   JbArrowAwardedS;

        // ── S75 measurement: cross-position occupancy ────────────────────────────
        //  Primary measure is TIME (possession credits) outside the occupant's stored
        //  position, not substitution counts — many short cross-position stints and few
        //  long ones look identical in a substitution tally and nothing alike on a page.
        //  Height is carried because it is the signal that motivated the ladder; it is
        //  explicitly NOT sufficient on its own to judge whether the ladder is priced.
        public readonly Dictionary<string, long> XCredits = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> XHeightSum = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> XSeatHeightSum = new(StringComparer.Ordinal);
        public long PossessionCredits, XPossessionRecords, DroppedCredits;

        public void NoteOccupancy(
            IReadOnlyList<PossessionRecord> records, GameState game,
            IReadOnlyDictionary<int, string> storedPos,
            IReadOnlyDictionary<(TeamSide, int), string> seatPos,
            IReadOnlyDictionary<(TeamSide, int), int> seatStarterHeight)
        {
            long credited = 0;
            foreach (var r in records)
                for (var slot = 1; slot <= Lineup.Size; slot++)
                    foreach (var side in new[] { r.Offense, r.Defense })
                    {
                        var p = game.RosterFor(side).PlayerAt(new Slot(side, slot), r.Number);
                        if (p is null) continue;
                        credited++;
                        if (!RosterShape.IsLegalPlayerId(p.PlayerId)) { DroppedCredits++; continue; }
                        if (!storedPos.TryGetValue(p.PlayerId, out var stored)) continue;
                        if (!seatPos.TryGetValue((side, slot), out var seat)) continue;
                        var key = PositionalEligibility.TransitionLabel(stored, seat);
                        XCredits[key]   = XCredits.TryGetValue(key, out var c) ? c + 1 : 1;
                        XHeightSum[key] = (XHeightSum.TryGetValue(key, out var h) ? h : 0) + p.Height;
                        XSeatHeightSum[key] = (XSeatHeightSum.TryGetValue(key, out var sh) ? sh : 0)
                                            + (seatStarterHeight.TryGetValue((side, slot), out var v) ? v : p.Height);
                    }
            PossessionCredits += credited;
            XPossessionRecords += records.Count;
        }

        public void Accumulate(GameState game, GovernorRunResult result, PlayerBoxTotals box,
                               int homeSchoolId, int awaySchoolId)
        {
            Games++;
            PointsFromScores += game.HomeScore + game.AwayScore;
            TotalSeconds += result.TotalSeconds;
            if (result.OvertimePeriods > 0) OtGames++;
            OtPeriods += result.OvertimePeriods;

            for (var i = 0; i < RosterShape.PlayerArrayWidth; i++)
            {
                OReb += box.OReb[i]; DReb += box.DReb[i];
                Ast  += box.Ast[i];  Stl  += box.Stl[i];  Blk += box.Blk[i];

                // Session 63: fouls + usage. Box index i maps to (school, acquisition-order
                // index) exactly as the season stamps PlayerIds: home rows are ids
                // 1..RosterShape.Size (indices 0..Size-1), away rows the next Size — see
                // BuildSeasonSide.
                //
                // ★ S75: these two lines were hardcoded to 10 and did NOT throw or drop when
                // the roster grew — home players 11-13 landed at indices 10-12, were credited
                // to the AWAY school, and their slot keys wrapped into collisions. A silent
                // cross-team MISATTRIBUTION, invisible to every conservation check because the
                // totals still balanced. The tell was `n=3470` player-seasons on a 4,511-player
                // league: (i % 10) can only ever produce ten distinct slots.
                SflTotal += box.ShFoul[i]; NsfTotal += box.NsFoul[i];
                var school = i < RosterShape.Size ? homeSchoolId : awaySchoolId;
                var slot = (i % RosterShape.Size) + 1;
                var pk = (school, slot);
                var pv = PlayerUsage.TryGetValue(pk, out var p0) ? p0 : (0L, 0L, 0L);
                PlayerUsage[pk] = (pv.Item1 + box.Fga[i], pv.Item2 + box.Fta[i], pv.Item3 + box.To[i]);
                var tv = TeamUsage.TryGetValue(school, out var t0) ? t0 : (0L, 0L, 0L);
                TeamUsage[school] = (tv.Item1 + box.Fga[i], tv.Item2 + box.Fta[i], tv.Item3 + box.To[i]);
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
                FastBreakFga += r.FastBreakFga; FastBreakThreePa += r.FastBreakThreePa; FastBreakThreePm += r.FastBreakThreePm;

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
                    // Session 37: split the drawn (non-fixed-time) turnovers by court.
                    // TimeProfile is non-null exactly for the profile-stamped drawn
                    // turnovers (and offensive fouls, which ARE on the 17-label turnover
                    // line); the three fixed-time violations carry no profile and fall
                    // through to their sub-line above, never here.
                    else if (r.TimeProfile is { } prof)
                    {
                        if (prof == PossessionTimeProfile.BackcourtTurnover)
                        {
                            BackcourtToN++; BackcourtToAppliedS += r.Elapsed;
                            if (r.TurnoverRawElapsed is { } raw)
                            {
                                BackcourtToRawS += raw;
                                if (raw < BackcourtRawMin) BackcourtRawMin = raw;
                                if (raw > BackcourtRawMax) BackcourtRawMax = raw;
                            }
                        }
                        else
                        {
                            FrontcourtToN++; FrontcourtToAppliedS += r.Elapsed;
                            if (r.ShotClockPeriods > 1) FrontcourtMultiPeriodN++;
                            if (r.TurnoverRawElapsed is { } raw)
                            {
                                FrontcourtToRawS += raw;
                                // Range asserted only on single-period draws (a multi-period
                                // total can exceed 30 legitimately — see §3).
                                if (r.ShotClockPeriods == 1)
                                {
                                    if (raw < FrontcourtRawMin1P) FrontcourtRawMin1P = raw;
                                    if (raw > FrontcourtRawMax1P) FrontcourtRawMax1P = raw;
                                }
                            }
                        }
                    }
                    else
                    {
                        // A drawn (non-fixed-time) turnover that carries no TimeProfile — an
                        // emitter forgot to stamp and this possession drew the shared clock.
                        DrawnTurnoverNoProfileN++;
                    }
                }
                else
                {
                    OtherN++; OtherS += r.Elapsed;   // parked:*, LooseBallFoulOnOffense, future labels
                    // Session 33 Phase A: per-label tally of everything landing here.
                    OtherByLabel[r.EndLabel] = OtherByLabel.TryGetValue(r.EndLabel, out var t)
                        ? (t.N + 1, t.S + r.Elapsed)
                        : (1L, r.Elapsed);
                    // Jump-ball award vs the record's Offense — a record-level read,
                    // never a pure label match (a held ball can retain offense).
                    if (r.EndLabel.StartsWith("JumpBallTip:", StringComparison.Ordinal)
                        || r.EndLabel.StartsWith("JumpBallArrow:", StringComparison.Ordinal))
                    {
                        var isTip    = r.EndLabel.StartsWith("JumpBallTip:", StringComparison.Ordinal);
                        var suffix   = r.EndLabel[(r.EndLabel.IndexOf(':') + 1)..];
                        var retained = suffix == r.Offense.ToString();
                        if (isTip)
                        {
                            if (retained) { JbTipRetainedN++;   JbTipRetainedS   += r.Elapsed; }
                            else          { JbTipAwardedN++;    JbTipAwardedS    += r.Elapsed; }
                        }
                        else
                        {
                            if (retained) { JbArrowRetainedN++; JbArrowRetainedS += r.Elapsed; }
                            else          { JbArrowAwardedN++;  JbArrowAwardedS  += r.Elapsed; }
                        }
                    }
                }
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
        // Session 41 (Phase C): the credited-rebound LOW is a DEFINITION gap, not a bug.
        // 'rebounds (credited)' counts only player-credited boards; the public 34.5 includes
        // uncredited TEAM rebounds. This instrument cannot yet reconcile them — rebound-origin
        // provenance is lost in the final possession labels (jump-ball arrows arise from Rolls
        // A/B/F/I/J/K/M, not only Roll I/M rebound scrambles), so completeness cannot be proven
        // page-only. The candidates below are therefore a page-only DIAGNOSTIC: never summed
        // into the credited line, never judged against 34.5. (A reconciled team-rebound line
        // needs rebound-provenance instrumentation — a future item, out of scope here.)
        {
            long CN(string k) => s.OtherByLabel.TryGetValue(k, out var v) ? v.N : 0L;
            var oob = CN("OutOfBoundsOffOffense");
            var jb  = CN("JumpBallArrow:Home") + CN("JumpBallArrow:Away");
            var lbf = CN("LooseBallFoulOnOffense");
            var candSum = oob + jb + lbf + s.MissOobN;
            Console.WriteLine("    note: 'rebounds (credited)' excludes uncredited team rebounds the public 34.5 includes;");
            Console.WriteLine(Inv($"          this instrument does not yet fully measure them (credited gap ~{34.5 - (s.OReb + s.DReb) / g2:F1}/team/game)."));
            Console.WriteLine("    candidate dead-ball possession endings (NOT reconciled to rebounds; page-only diagnostic):");
            Console.WriteLine(Inv($"      OutOfBoundsOffOffense {oob / g2:F2} | jump-ball arrows {jb / g2:F2} | ") +
                              Inv($"LooseBallFoulOnOffense {lbf / g2:F2} | MissOutOfBoundsLost {s.MissOobN / g2:F2} | sum {candSum / g2:F2}"));
        }
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

        // Session 38: fast-break shot-diet readout (page-only, no target asserted). Excludes
        // Roll K putbacks; the 3PA-rate is the transition three share realized in play.
        Console.WriteLine(Inv(
            $"    fast-break shot diet: FGA {s.FastBreakFga} ({Pct(s.FastBreakFga, s.Fga):F1}% of all FGA)") +
            Inv($"  3PA-rate {(s.FastBreakFga > 0 ? 100.0 * s.FastBreakThreePa / s.FastBreakFga : 0.0):F1}%") +
            Inv($"  3P% {Pct(s.FastBreakThreePm, s.FastBreakThreePa):F1}%"));

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
        // Session 37: the drawn turnovers split by court (offensive fouls included — they
        // are on the 17-label turnover line). Raw = pre-clamp band draw (compare to the
        // oracle: ~5s backcourt / ~15s frontcourt); applied = clamped record length.
        Console.WriteLine(
            Inv($"    turnover by court — backcourt applied {Avg(s.BackcourtToAppliedS, s.BackcourtToN):F1}s ") +
            Inv($"raw {Avg(s.BackcourtToRawS, s.BackcourtToN):F1}s (n={s.BackcourtToN})  |  ") +
            Inv($"frontcourt applied {Avg(s.FrontcourtToAppliedS, s.FrontcourtToN):F1}s ") +
            Inv($"raw {Avg(s.FrontcourtToRawS, s.FrontcourtToN):F1}s (n={s.FrontcourtToN})"));

        // Session 33 Phase A: the OTHER bucket itemized — each label, count, share
        // of OTHER, mean seconds. Page-only; classifies nothing. Exists so rulings
        // R1/R2 are taken with the counts on the page.
        if (s.OtherN > 0)
        {
            Console.WriteLine("  OTHER itemized (per label; page-only, classifies nothing):");
            foreach (var kv in s.OtherByLabel.OrderByDescending(kv => kv.Value.N))
                Console.WriteLine(Inv(
                    $"    {kv.Key,-28} n={kv.Value.N,8}  {Pct(kv.Value.N, s.OtherN),5:F1}% of OTHER  mean {Avg(kv.Value.S, kv.Value.N):F1}s"));

            var jbN = s.JbTipRetainedN + s.JbTipAwardedN + s.JbArrowRetainedN + s.JbArrowAwardedN;
            if (jbN > 0)
            {
                Console.WriteLine("  jump-ball award vs the possession's offense (held ball can retain offense):");
                Console.WriteLine(Inv(
                    $"    tip:   offense retained n={s.JbTipRetainedN} (mean {Avg(s.JbTipRetainedS, s.JbTipRetainedN):F1}s)") + Inv(
                    $" | defense awarded n={s.JbTipAwardedN} (mean {Avg(s.JbTipAwardedS, s.JbTipAwardedN):F1}s)"));
                Console.WriteLine(Inv(
                    $"    arrow: offense retained n={s.JbArrowRetainedN} (mean {Avg(s.JbArrowRetainedS, s.JbArrowRetainedN):F1}s)") + Inv(
                    $" | defense awarded n={s.JbArrowAwardedN} (mean {Avg(s.JbArrowAwardedS, s.JbArrowAwardedN):F1}s)"));
            }
        }
    }

    // ── Session 63: the baseline-read lines (the calibration arc's starting picture).
    //    Page-only, never asserted. PPP and the foul split come straight from the
    //    accumulator; the usage spread is computed over every (school, slot) player
    //    from the season-long box sums. ──────────────────────────────────────────────
    private static void PrintBaselineReadout(SeasonLeagueStats s)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        // ── S75: cross-position occupancy (page-only; the S76 design input) ────────
        {
            static string Inv2(FormattableString f) => FormattableString.Invariant(f);
            Console.WriteLine("--- CROSS-POSITION OCCUPANCY (Session 75; page-only, never asserted) ---");
            var total = s.XCredits.Values.Sum();
            if (total == 0)
            {
                Console.WriteLine("  no occupancy recorded (instrument not wired) — treat every minutes number as unproven.");
            }
            else
            {
                var cross = s.XCredits.Where(kv => kv.Key[0] != kv.Key[^1]).Sum(kv => kv.Value);
                Console.WriteLine(Inv2($"  floor time outside stored position: {100.0 * cross / total,5:F2} % of {total} player-possession credits"));
                Console.WriteLine(Inv2($"  credit identity: {s.PossessionCredits} credits / {s.XPossessionRecords} records = {(s.XPossessionRecords == 0 ? 0 : (double)s.PossessionCredits / s.XPossessionRecords),4:F1} (expect {2 * Lineup.Size}.0); dropped {s.DroppedCredits}"));
                Console.WriteLine("  by transition (share of all floor time; mean occupant height vs the seat's own starter):");
                foreach (var key in PositionalEligibility.LegalTransitions)
                {
                    if (!s.XCredits.TryGetValue(key, out var c) || c == 0) { Console.WriteLine(Inv2($"    {key}   —")); continue; }
                    var occH = (double)s.XHeightSum[key] / c;
                    var seatH = (double)s.XSeatHeightSum[key] / c;
                    var tag = key[0] == key[^1] ? " " : "*";
                    Console.WriteLine(Inv2($"    {key}{tag} {100.0 * c / total,6:F2} %   occupant {occH,5:F1}  seat-starter {seatH,5:F1}  gap {occH - seatH,+6:F1}"));
                }
                Console.WriteLine("    (* = cross-position. Height is the signal that motivated the ladder, NOT a");
                Console.WriteLine("     sufficient test of whether out-of-position play is priced — see A11.)");
            }
            Console.WriteLine();
        }

        Console.WriteLine("--- BASELINE LINES (Session 63; page-only, never asserted) ---");
        if (s.Games == 0) { Console.WriteLine("  no games accumulated."); return; }
        var g2 = 2.0 * s.Games;

        var ppp = s.PossessionRecords > 0 ? (double)s.PointsFromScores / s.PossessionRecords : 0.0;
        Console.WriteLine(Inv($"  PPP (points / possession records)      {ppp:F4}"));
        Console.WriteLine(Inv($"  fouls/team/game (full game)            {(s.SflTotal + s.NsfTotal) / g2:F2}") +
                          Inv($"   [shooting {s.SflTotal / g2:F2} | non-shooting {s.NsfTotal / g2:F2}]"));

        // Usage spread: (FGA + 0.44·FTA + TO) / same team total, per player-season.
        var usages = new List<double>();
        foreach (var kv in s.PlayerUsage)
        {
            var (fga, fta, to) = kv.Value;
            var (tf, tt, tv) = s.TeamUsage[kv.Key.SchoolId];
            var teamPoss = tf + 0.44 * tt + tv;
            if (teamPoss > 0) usages.Add(100.0 * (fga + 0.44 * fta + to) / teamPoss);
        }
        usages.Sort();
        if (usages.Count > 0)
        {
            double At(double p) => usages[Math.Min(usages.Count - 1, (int)(p * usages.Count))];
            Console.WriteLine(Inv($"  usage spread (n={usages.Count} player-seasons): ") +
                Inv($"max {usages[^1]:F1}%  p90 {At(0.90):F1}%  median {At(0.50):F1}%  min {usages[0]:F1}%"));
        }
    }

    // ── Session 63: the roster census — proves roster SUPPLY was not silently
    //    distorted while standings conservation stayed green. Page-only. ─────────────
    private static void PrintRosterCensus(DivvyResult res, WorldFile world)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        Console.WriteLine("--- ROSTER CENSUS (Session 63; page-only) ---");
        var pool = res.Pool;
        var all = res.Rosters.Values.SelectMany(r => r).ToList();
        var drafted = all.Count;
        var distinct = all.Distinct().Count();
        var posOk = res.Rosters.Values.Count(r =>
            r.Count(pid => pool[pid].Pos == PositionalEligibility.Guard) == RosterShape.Guards &&
            r.Count(pid => pool[pid].Pos == PositionalEligibility.Wing)  == RosterShape.Wings &&
            r.Count(pid => pool[pid].Pos == PositionalEligibility.Big)   == RosterShape.Bigs);
        var covOk = res.Rosters.Values.Count(r =>
            r.Any(pid => GenLeadRoles.Contains(pool[pid].Role)) &&
            r.Any(pid => pool[pid].Role == GenWingDefenderRole));
        Console.WriteLine(Inv($"  players drafted {drafted} of pool {pool.Count} (distinct {distinct}; undrafted {pool.Count - distinct})"));
        Console.WriteLine(Inv($"  rosters exactly {RosterShape.Guards}G/{RosterShape.Wings}W/{RosterShape.Bigs}B: {posOk}/{res.Rosters.Count}   protected roles covered: {covOk}/{res.Rosters.Count}"));

        // ── S75 measurement product: the numbers S76 designs its allocator against ──
        //  Page-only, never asserted. A9 ruling: the opening-five histogram is EVIDENCE,
        //  not a target — no distribution is expected and lineup selection is untouched.
        var shapes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in res.Rosters)
        {
            var five = BuildOpeningFive(kv.Value, pid => pool[pid].Pos);
            int fg = 0, fw = 0, fb = 0;
            foreach (var pid in five)
            {
                var q = pool[pid].Pos;
                if (q == PositionalEligibility.Guard) fg++;
                else if (q == PositionalEligibility.Wing) fw++;
                else fb++;
            }
            var key = Inv($"{fg}G/{fw}W/{fb}B");
            shapes[key] = shapes.TryGetValue(key, out var c) ? c + 1 : 1;
        }
        Console.WriteLine("  opening-five shapes (S75 evidence; lineup selection is UNCHANGED — see A9):");
        foreach (var kv in shapes.OrderByDescending(x => x.Value))
            Console.WriteLine(Inv($"    {kv.Key}  {kv.Value,4} schools"));

        Console.WriteLine("  drafted height by position (pool means survive the divvy?):");
        foreach (var q in new[] { PositionalEligibility.Guard, PositionalEligibility.Wing, PositionalEligibility.Big })
        {
            var hs = all.Where(pid => pool[pid].Pos == q).Select(pid => (double)pool[pid].Player.Height).ToList();
            if (hs.Count == 0) continue;
            Console.WriteLine(Inv($"    {q}  mean {hs.Average(),5:F1}  [{hs.Min(),3:F0}..{hs.Max(),3:F0}]  (n={hs.Count})"));
        }

        Console.WriteLine("  scout rank by acquisition-order index (NOT a depth chart — see A10):");
        for (var idx = 0; idx < RosterShape.Size; idx++)
        {
            var rs = res.Rosters.Values.Where(r => r.Count > idx).Select(r => pool[r[idx]].ScoutRank).ToList();
            if (rs.Count == 0) continue;
            var posMix = res.Rosters.Values.Where(r => r.Count > idx)
                .GroupBy(r => pool[r[idx]].Pos)
                .OrderByDescending(gp => gp.Count())
                .Select(gp => Inv($"{gp.Key}{gp.Count()}"));
            Console.WriteLine(Inv($"    idx {idx + 1,2}  rank {rs.Average(),6:F1}  [{rs.Min(),6:F1} .. {rs.Max(),6:F1}]  pos {string.Join("/", posMix)}"));
        }

        var prestige = world.Schools.ToDictionary(x => x.Id, x => x.CurrentPrestige);
        Console.WriteLine("  drafted scout-rank spread by prestige band (mean [min..max]):");
        foreach (var (lo, hi) in SeasonBands)
        {
            var ranks = res.Rosters.Where(kv => prestige[kv.Key] >= lo && prestige[kv.Key] <= hi)
                                   .SelectMany(kv => kv.Value).Select(pid => pool[pid].ScoutRank).ToList();
            if (ranks.Count == 0) continue;
            Console.WriteLine(Inv($"    prestige {lo,2}-{hi,-2}  {ranks.Average(),6:F1}  [{ranks.Min(),6:F1} .. {ranks.Max(),6:F1}]  (n={ranks.Count})"));
        }
    }
}
