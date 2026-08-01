using System.Globalization;
using Charm.Engine;
using Charm.History;

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
        //  Session 85, PAGE-ONLY — the fast-break readout.
        //
        //  TransitionEntries: possessions that arrived off a rebound, a free-throw rebound or a
        //  steal. NOT the same thing as a fast break: Roll J's Settle arm leaves the state
        //  untouched, so a settled transition entry is indistinguishable from an ordinary
        //  halfcourt possession downstream. Both numbers are printed and they must differ.
        //
        //  TransitionPush/Settle/Turnover/DefFoul/JumpBall: the five sibling arms of the
        //  run-or-not pie, on transition entries only. They are SIBLINGS — a turnover, a foul
        //  or a tie-up happens INSTEAD of a push, not after a failed one.
        //
        //  PossWithFastBreakFga: possessions carrying at least one break shot. Distinct from
        //  the attempt total, and distinct again from the push count — a push can die before a
        //  shot ever goes up, and the gap between the two is itself a finding.
        //  TransitionEntriesNoShot: transition entries the resolver NEVER WALKED. An
        //  end-of-half possession that runs the clock out without a shot is recorded but never
        //  resolved at all — the Governor short-circuits before calling the resolver, so Roll J
        //  does not run and there is no arm to report. These are a handful per thousand
        //  possessions and they are the reason the five arms sum to slightly FEWER than the
        //  transition entries. Counted and printed rather than swept up, because an unexplained
        //  residual on a conservation line is indistinguishable from a wiring bug.
        public long TransitionEntries, TransitionEntriesNoShot, PossWithFastBreakFga;
        public long TransitionPush, TransitionSettle, TransitionTurnover,
                    TransitionDefFoul, TransitionJumpBall;
        //  The three-way shot partition and its blocks. Break-side counts are ALL-SOURCE (push-
        //  born plus press-born); the *PressBorn fields hold the press half so the page can
        //  print the split beside every total. A single possession cannot be both: the press is
        //  rolled only on a dead-ball start, which a rebound/steal start never takes, so a
        //  possession's own entry decides which side it belongs to with no extra counter.
        public long FastBreakFgm, FastBreakBlk;
        public long BreakPutbackFga, BreakPutbackFgm, BreakPutbackBlk;
        public long NonBreakFga, NonBreakFgm, NonBreakBlk;
        public long PressBornFga, PressBornFgm, PressBornThreePm, PressBornBlk, PressBornPossessions;
        //  Break blocks credited to the DEFENSIVE team, per team — the denominator of the
        //  concentration board. The per-MAN numerator lives on SeasonPlayerRecord.FbBlk,
        //  because a lineup seat is not a person: with a real rotation several men share seat 3
        //  across a season, so a seat-level share would understate how concentrated the credit
        //  actually is.
        public readonly Dictionary<int, long> TeamFastBreakBlk = new();
        //  Transition entries CONCEDED, per team, with the possessions each team defended as
        //  the denominator. Defensive-side because this is a defence instrument: the question
        //  is how often an opponent gets out and runs against this team, not how often this
        //  team runs.
        public readonly Dictionary<int, long> TeamDefTransitionEntries = new();
        public readonly Dictionary<int, long> TeamDefPossessions = new();
        //  S86: the per-team push band — the instrument that grades the opportunity/bar wire.
        //  OFFENSIVE-side, because this asks how often THIS team gets out and runs, the mirror
        //  of the defensive pair above. Denominator is every Roll J entry the resolver actually
        //  RESOLVED for this team (all five arms), matching the oracle's absolute push
        //  probability — NOT Push+Settle, which would be a conditional rate the oracle never
        //  computes. The S85 block prints the split league-wide only, and a league mean cannot
        //  show whether teams spread apart: both halves are needed and neither detects the
        //  other's failure.
        public readonly Dictionary<int, long> TeamOffTransitionResolved = new();
        public readonly Dictionary<int, long> TeamOffTransitionPush = new();
        public long PossessionRecords;       // every record — the pace numerator
        //  S79.3: league secured boards — the sum of PossessionRecord.OrbChances, and the
        //  league-side leg of the SecuredBoardsOnFloor identity. It exists for no other reason.
        //  ★ SECURED, not available: a defensive-rebound terminal or an offensive-rebound
        //  continue each count one; fouls, out-of-bounds and jump balls are excluded by the
        //  resolver (Resolver.cs:800-810), matching the box-score rebounding convention.
        public long SecuredBoards;
        public long TurnoverPossessions;     // via IsTurnoverPossession, all records
        public long MetadataDriftRecords;    // TO metadata present but classifier says no

        // From the box (attributed, all 20 indices — side-symmetric by construction):
        public long OReb, DReb, Ast, Stl, Blk;

        // Session 79, PAGE-ONLY. Block credit split by whether the credited defender was the
        // man guarding the shooter, near the rim (Rim/Short) vs out (Mid/Long/Three).
        // Never asserted — it separates credit REDISTRIBUTION from a real RATE change, which
        // a leaderboard alone cannot do.
        public long BlkMatchedNear, BlkHelperNear, BlkMatchedOut, BlkHelperOut;

        // Session 84, PAGE-ONLY. The lineup passing factor the assist door actually applied,
        // event-weighted (one observation per assist-eligible made field goal, assisted or
        // not — the denominator is chances, not conversions). Never asserted.
        //
        // Why the page carries this at all: S84 found AssistPassMidpoint sitting at 71.31
        // against a real population mean of 30.73, a 40-point drift that had been in the tree
        // since S41 and was invisible on the page. It jammed every team onto the flat tail of
        // the tanh — best and worst passing teams within 5% of each other — and no line on the
        // season page would have shown it. A dial nobody can see is a dial that drifts.
        //
        // The league pair reports the LEVEL (a healthy midpoint puts the mean at ~1.000); the
        // per-team dictionary reports the SEPARATION, which is the half the league mean cannot
        // show — a correctly centred midpoint with a dead swing would still read 1.000.
        public double AssistPassFactorSum;
        public long   AssistPassFactorEvents;
        public readonly Dictionary<int, (double Sum, long Events)> TeamAssistPassFactor = new();

        // Session 63: the baseline-read lines (page-only, never asserted). Full-game
        // foul totals from the attribution arrays (SFL = shooting fouls, NSF =
        // non-shooting — the S62 split), and a per-(school, depth-slot) usage
        // accumulator so the league usage SPREAD (max/p90/median/min) is readable on
        // the page. Usage = (FGA + 0.44·FTA + TO) / the same team total — the
        // standard box-score possession-share proxy.
        public long SflTotal, NsfTotal;

        // ── S87, PAGE-ONLY: the foul-out layer ───────────────────────────────────
        //  FoulOuts            — men who reached the disqualification threshold, per game.
        //  OffFoulTotal        — offensive fouls charged to a man. NEW: before S87 these
        //                        reached no foul count at all. Reported SEPARATELY from
        //                        the shooting/non-shooting line, because they never touch
        //                        the team-foul stream and so are not part of the
        //                        fouls/team/game figure that line reports.
        //  PfBucket[0..5]      — the personal-foul spread, 0/1/2/3/4/5+, over player-GAMES
        //                        in which the man occupied a floor seat for at least one
        //                        possession. Same floor-time convention the stat page
        //                        already uses, so the 1,025 men who never played a minute
        //                        cannot flood the zero bucket.
        //  R4Occurrences       — men the escape hatch stranded: disqualified, no eligible
        //                        reserve, left on the floor. Counted once per man.
        //  PossPlayedWhileDq   — trips played by a man already disqualified. Non-zero from
        //                        the escape hatch and from the one trip a man finishes
        //                        after his fifth foul.
        //  FoulOutReplacements — forced replacements the rotation actually made.
        public long FoulOuts, OffFoulTotal, R4Occurrences, PossPlayedWhileDq, FoulOutReplacements;
        public readonly long[] PfBucket = new long[6];

        //  S77: the engine's OWN unattributed buckets, carried so Gate 1 can be an EXACT
        //  identity rather than a tolerance. A possession can produce a field-goal attempt that
        //  belongs to no slot (`SlotUnattributedFga/Fgm`, stamped by the Resolver) and a bonus
        //  free-throw trip that reached the line before Roll E selected a shooter
        //  (`FtaBonusUnattributed` — the named loose end the bench readout has always printed as
        //  `Unattr`). These are not roll-up losses; they are shots the engine never assigned to a
        //  man. Summing them is what turns "per-player totals ≈ league totals" into "==".
        public long UnattributedFga, UnattributedFgm, UnattributedFta;
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

        //  Session 76 — THE ROTATION DEPTH DISTRIBUTION.
        //
        //  The single number this session exists to move. Before S76 five men took ~88% of
        //  every team's possessions, so every calibration figure on this page described a
        //  league where the average man on the floor plays 35 minutes.
        //
        //  Deliberately keyed by REALIZED rank, not by acquisition index and not by target:
        //  it asks "how much did this team's most-used man play, its second most-used, and
        //  so on", which needs no knowledge of the plan and therefore cannot be flattered by
        //  it. A per-game sort is the honest instrument — a man who is 4th one night and 8th
        //  the next contributes to both buckets, exactly as a rotation actually behaves.
        public readonly long[] RotationRankCredits = new long[RosterShape.Size];
        public long RotationTeamGames, RotationRecords;

        //  Session 77 — THE PER-PLAYER SEASON RECORD.
        //
        //  ★ Keyed by POOL ID — the person — never by (school, acquisition index).
        //  A (school, seat) key is correct for exactly one season: next season school 200's
        //  seventh pick is a different human being, and a transferring player's record would
        //  stay behind with the seat rather than moving with him. Nothing persists between
        //  seasons today (the world is rebuilt from the seed every run), so this buys no career
        //  totals yet — it buys not having to rewrite the stat layer when persistence lands.
        //  The school rides ON the record as data, which is the shape a career row wants anyway.
        //
        //  Fed from BOTH accumulators and from neither exclusively: `Accumulate` supplies the
        //  box (shooting, boards, playmaking, fouls) and `NoteOccupancy` supplies floor time and
        //  games played, because minutes are not in the box at all (A3).
        public readonly Dictionary<int, SeasonPlayerRecord> PlayerSeasons = new();

        /// <summary>★ S89 — the frozen pool-slot -> person map, set once before the game loop,
        /// null in legacy mode. The roll-up's LOGIC is untouched by it (A-5): the record is
        /// still keyed by pool slot, still fetched the same way, still filed under the same
        /// man. The identity is stamped beside the key, not in place of it.</summary>
        public PersonIdentityMap? PersonIds { get; set; }

        /// <summary>Fetch-or-create the record for a person, stamping the identity fields on
        /// first sight. Metadata is written ONCE and thereafter only re-verified — see the
        /// drift counter, which is how a scrambled mapping shows up as something other than a
        /// silently-overwritten field.</summary>
        private SeasonPlayerRecord RecordFor(int schoolId, GenPlayerRow row)
        {
            if (PlayerSeasons.TryGetValue(row.PoolId, out var rec))
            {
                if (rec.SchoolId != schoolId || rec.Pos != row.Pos
                    || rec.Height != row.Player.Height || rec.ScoutRank != row.ScoutRank)
                    IdentityDriftObservations++;
                return rec;
            }
            rec = new SeasonPlayerRecord
            {
                PoolId           = row.PoolId,
                // ★ S89 — the permanent number, stamped at the same moment as the rest of the
                // identity metadata. A lookup miss THROWS (PersonIdentityMap's indexer): a
                // silently skipped man would leave a whole season of statistics filed nowhere
                // while every conservation total stayed green, because the totals would only
                // ever have counted the men who were found.
                PersonId         = PersonIds?[row.PoolId],
                SchoolId         = schoolId,
                AcquisitionIndex = row.Slot,
                Name             = row.Player.Name,
                Pos              = row.Pos,
                Height           = row.Player.Height,
                ScoutRank        = row.ScoutRank,
            };
            PlayerSeasons[row.PoolId] = rec;
            return rec;
        }

        /// <summary>Secondary Gate 2 check: across every observation of an independently
        /// resolved identity, stored position, height, school and scout rank stay put.
        /// Asserted 0 by Phase 73. This is the WEAK half of the gate on purpose — stable
        /// metadata proves the metadata is stable and nothing about whose statistics landed
        /// under it, which is why the name comparison below exists as well.</summary>
        public long IdentityDriftObservations;

        /// <summary>Gate 2, the strong half — run once per game BEFORE a single box field,
        /// floor-time credit or game played is written, over ALL 26 stamped identities.
        ///
        /// <para>Two INDEPENDENT paths to the same person. Path A is the season row table read
        /// positionally, `rows[index - 1].Player.Name`. Path B is the `Player` object the engine
        /// was actually handed, carrying stamped id `k`, reporting his own name. They are
        /// different objects — `StampPlayerId` returns `new Player(p.Name)` and never mutates
        /// the generated player — so agreement is evidence rather than tautology.</para>
        ///
        /// <para>★ Deliberately NOT "re-derive the index from the id and check the key matches":
        /// that checks the code against itself and passes by construction. And deliberately
        /// unconditional over all 26 rather than over men with a nonzero box line — a man can
        /// play four minutes and record a completely blank line, which is ordinary basketball,
        /// and under an events-only check his identity would go unverified while his minutes and
        /// his game played were credited to whoever a broken mapping named.</para></summary>
        private void AssertIdentity(SeasonGameIdentity id)
        {
            foreach (var seated in id.StampedPlayers())
            {
                var (_, row) = id.Resolve(seated.PlayerId);
                if (!string.Equals(row.Player.Name, seated.Name, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"S77 Gate 2: stamped id {seated.PlayerId} is '{seated.Name}' on the floor " +
                        $"but '{row.Player.Name}' in the season row table. The index->person mapping " +
                        "is scrambled; every per-player total is landing on the wrong man. " +
                        "(Conservation cannot see this — the league sums are right either way.)");
            }
        }

        /// <summary>S79.3 — one team-game's worth of on-floor DENOMINATORS for one man, staged
        /// during the record walk and drained into his season record inside the same method.
        ///
        /// <para>★ Why staged rather than written straight onto the record: NoteOccupancy is two
        /// loops, not one. Only the record walk can see a <see cref="PossessionRecord"/>, and
        /// only the roll-up below calls <c>RecordFor</c>. Writing through directly would mean
        /// resolving a person ten times per possession instead of once per team-game.</para>
        ///
        /// <para>The four travel together in one object rather than in four parallel dictionaries
        /// because they are written at one site and read at one site — four dictionaries would
        /// only create four chances to keep one and drop another.</para></summary>
        private sealed class OnFloorTally
        {
            public long OffensiveCredits;
            public long OpponentTwoPa;
            public long SecuredBoards;
            public long OffensiveTeamFgm;
        }

        public void NoteOccupancy(
            IReadOnlyList<PossessionRecord> records, GameState game,
            IReadOnlyDictionary<int, string> storedPos,
            IReadOnlyDictionary<(TeamSide, int), string> seatPos,
            IReadOnlyDictionary<(TeamSide, int), int> seatStarterHeight,
            SeasonGameIdentity identity)
        {
            long credited = 0;
            // S76: per-side, per-player floor time for THIS game, so the rotation depth
            // distribution can be ranked within the game that produced it.
            var perSide = new Dictionary<TeamSide, Dictionary<int, long>>
            {
                [TeamSide.Home] = new(),
                [TeamSide.Away] = new(),
            };
            //  S79.3: the four on-floor denominators, staged EXACTLY like the credit bucket
            //  above and drained in the same roll-up below, keyed by the same stamped id.
            var perSideDen = new Dictionary<TeamSide, Dictionary<int, OnFloorTally>>
            {
                [TeamSide.Home] = new(),
                [TeamSide.Away] = new(),
            };

            foreach (var r in records)
                for (var slot = 1; slot <= Lineup.Size; slot++)
                    foreach (var side in new[] { r.Offense, r.Defense })
                    {
                        var p = game.RosterFor(side).PlayerAt(new Slot(side, slot), r.Number);
                        if (p is null) continue;
                        credited++;
                        if (!RosterShape.IsLegalPlayerId(p.PlayerId)) { DroppedCredits++; continue; }

                        var bucket = perSide[side];
                        bucket[p.PlayerId] = bucket.TryGetValue(p.PlayerId, out var pc) ? pc + 1 : 1;

                        //  S79.3 — the four on-floor denominators, in the SAME pass over the
                        //  records and BELOW the SAME dropped-credit guard as the credit above.
                        //  ★ That placement is load-bearing: a credit can never be dropped
                        //  without its denominators, or the reverse, so the four league
                        //  identities stay exact under any future drop.
                        //
                        //  ★ Which end: `side` is either r.Offense or r.Defense, and those are
                        //  never the same team, so `side == r.Offense` is an exact one-side test.
                        //  Three counters land on one side each; secured boards land on BOTH,
                        //  because a board is contested with all ten men on the floor.
                        var denBucket = perSideDen[side];
                        if (!denBucket.TryGetValue(p.PlayerId, out var den))
                            denBucket[p.PlayerId] = den = new OnFloorTally();
                        if (side == r.Offense)
                        {
                            den.OffensiveCredits++;
                            den.OffensiveTeamFgm += r.Fgm;   // all five offensive men's makes, his own included
                        }
                        else
                        {
                            den.OpponentTwoPa += r.Fga - r.ThreePa;
                        }
                        den.SecuredBoards += r.OrbChances;

                        if (!storedPos.TryGetValue(p.PlayerId, out var stored)) continue;
                        if (!seatPos.TryGetValue((side, slot), out var seat)) continue;
                        var key = PositionalEligibility.TransitionLabel(stored, seat);
                        XCredits[key]   = XCredits.TryGetValue(key, out var c) ? c + 1 : 1;
                        XHeightSum[key] = (XHeightSum.TryGetValue(key, out var h) ? h : 0) + p.Height;
                        XSeatHeightSum[key] = (XSeatHeightSum.TryGetValue(key, out var sh) ? sh : 0)
                                            + (seatStarterHeight.TryGetValue((side, slot), out var v) ? v : p.Height);
                    }

            foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
            {
                var ranked = perSide[side].Values.OrderByDescending(v => v).ToList();
                for (var i = 0; i < ranked.Count && i < RosterShape.Size; i++)
                    RotationRankCredits[i] += ranked[i];
                RotationTeamGames++;
                RotationRecords += records.Count;

                //  S77: the SAME per-side, per-player bucket the S76 rank distribution is
                //  sorted from — not a second walk. Extending this one rather than inventing
                //  another is what guarantees the two readouts can never disagree: re-ranking
                //  these numbers within each team-game reproduces the S76 ladder by identity.
                //
                //  ★ Games played is defined here, and the definition is load-bearing: a man
                //  receives ONE game played for POSITIVE floor-time credit in this team-game,
                //  and a man with zero credit receives none. Roster membership would yield 30
                //  for everybody, which is not merely useless — it would CONCEAL the DNPs this
                //  page exists to expose. S76's zero-target men should surface as near-zero
                //  games played; that is the instrument working, not a defect to repair.
                foreach (var (stampedId, credits) in perSide[side])
                {
                    if (credits <= 0) continue;
                    var (schoolId, row) = identity.Resolve(stampedId);
                    var rec = RecordFor(schoolId, row);
                    rec.Credits += credits;
                    rec.GamesPlayed++;        // at most one per team-game, by the loop shape

                    //  S79.3: the denominators ride THIS roll-up — same stamped id, same
                    //  Resolve, same RecordFor — so they cannot land on a different man than his
                    //  credits did. Every id in perSide has a tally by construction (both are
                    //  written at the same site under the same guard); the TryGetValue is a
                    //  guard against a future divergence, not an expected miss.
                    if (perSideDen[side].TryGetValue(stampedId, out var den))
                    {
                        rec.OffensiveCredits        += den.OffensiveCredits;
                        rec.OpponentTwoPaOnFloor    += den.OpponentTwoPa;
                        rec.SecuredBoardsOnFloor    += den.SecuredBoards;
                        rec.OffensiveTeamFgmOnFloor += den.OffensiveTeamFgm;
                    }
                }
            }

            PossessionCredits += credited;
            XPossessionRecords += records.Count;
        }

        /// <summary>
        /// S87, PAGE-ONLY: the foul-out layer for one game. Reads the tracker that actually
        /// ran and the policy that actually enforced the rule — nothing here re-derives a
        /// decision from the records, because a second implementation of the rule is a
        /// second chance to disagree with it.
        ///
        /// <para>The personal-foul spread is over men who OCCUPIED A FLOOR SEAT for at
        /// least one possession, which the roster's substitution log answers exactly: every
        /// man who ever held a seat has an entry, and no one else does. A man who dressed
        /// and never played is not a zero — he is not in the distribution at all.</para>
        /// </summary>
        public void AccumulateFouling(GameState game, GovernorRunResult result, MinutesAllocatorPolicy policy)
        {
            var pf = game.PersonalFouls;

            foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
            {
                var seen = new HashSet<int>();
                foreach (var entry in game.RosterFor(side).Log)
                {
                    var id = entry.Player.PlayerId;
                    if (!seen.Add(id)) continue;                 // one player-game per man

                    var n = pf.CountFor(id);
                    PfBucket[Math.Min(n, 5)]++;
                    if (pf.IsDisqualified(id)) FoulOuts++;
                }

                var st = policy.StateFor(side);
                R4Occurrences       += st.R4Occurrences;
                PossPlayedWhileDq   += st.PossessionsPlayedWhileDisqualified;
                FoulOutReplacements += st.FoulOutReplacements;
            }
        }

        public void Accumulate(GameState game, GovernorRunResult result, PlayerBoxTotals box,
                               SeasonGameIdentity identity)
        {
            // Gate 2 runs FIRST — before any box field, any floor-time credit, any game played.
            // `Accumulate` is called before `NoteOccupancy` at the one production call site, so
            // "first here" is "first at all" for the whole per-player layer.
            AssertIdentity(identity);

            var homeSchoolId = identity.HomeSchoolId;
            var awaySchoolId = identity.AwaySchoolId;

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
                OffFoulTotal += box.OffFoul[i];
                var school = i < RosterShape.Size ? homeSchoolId : awaySchoolId;
                var slot = (i % RosterShape.Size) + 1;
                var pk = (school, slot);
                var pv = PlayerUsage.TryGetValue(pk, out var p0) ? p0 : (0L, 0L, 0L);
                PlayerUsage[pk] = (pv.Item1 + box.Fga[i], pv.Item2 + box.Fta[i], pv.Item3 + box.To[i]);
                var tv = TeamUsage.TryGetValue(school, out var t0) ? t0 : (0L, 0L, 0L);
                TeamUsage[school] = (tv.Item1 + box.Fga[i], tv.Item2 + box.Fta[i], tv.Item3 + box.To[i]);

                // S77: the same box index, filed under the PERSON. Box index i is stamped id
                // i+1 (PlayerBoxTotals is indexed by PlayerId - 1), and `Resolve` owns the one
                // copy of the offset arithmetic — this loop never restates it.
                var (recSchool, recRow) = identity.Resolve(i + 1);
                var rec = RecordFor(recSchool, recRow);
                rec.Fga    += box.Fga[i];    rec.Fgm    += box.Fgm[i];
                rec.Tpa    += box.Tpa[i];    rec.Tpm    += box.Tpm[i];
                rec.Fta    += box.Fta[i];    rec.Ftm    += box.Ftm[i];
                rec.OReb   += box.OReb[i];   rec.DReb   += box.DReb[i];
                rec.Ast    += box.Ast[i];    rec.Stl    += box.Stl[i];
                rec.Blk    += box.Blk[i];    rec.To     += box.To[i];
                rec.ShFoul += box.ShFoul[i]; rec.NsFoul += box.NsFoul[i];
                rec.FbBlk  += box.FbBlk[i];   // Session 85: the break subset of the line above
            }

            var elapsedSum = 0.0;
            foreach (var r in result.Possessions)
            {
                PossessionRecords++;
                BlkMatchedNear += r.BlkMatchedNear; BlkHelperNear += r.BlkHelperNear;
                BlkMatchedOut  += r.BlkMatchedOut;  BlkHelperOut  += r.BlkHelperOut;
                // Session 84, PAGE-ONLY. Credited to the OFFENSIVE team — the passing lineup
                // is the one with the ball. Possessions with no eligible make contribute
                // nothing to either side of the ratio, which is why the guard is on the count
                // and not on the sum (a genuine factor of exactly 0.0 cannot occur: the tanh
                // range is (0.75, 1.25), so a zero sum with a positive count would be a bug,
                // not an empty possession).
                AssistPassFactorSum    += r.AssistPassFactorSum;
                AssistPassFactorEvents += r.AssistPassFactorEvents;
                if (r.AssistPassFactorEvents > 0)
                {
                    var offSchool = r.Offense == TeamSide.Home ? homeSchoolId : awaySchoolId;
                    var av = TeamAssistPassFactor.TryGetValue(offSchool, out var a0)
                           ? a0 : (Sum: 0.0, Events: 0L);
                    TeamAssistPassFactor[offSchool] =
                        (av.Sum + r.AssistPassFactorSum, av.Events + r.AssistPassFactorEvents);
                }
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
                // ── Session 85, PAGE-ONLY: the fast-break readout ────────────────────────
                // Entry rate, and the run-or-not split on transition entries only. The split
                // fires off the carried LABEL, which the engine stamps only inside Roll J, so
                // it cannot fire on a possession Roll J never touched.
                if (r.Entry == EntryType.Transition)
                {
                    TransitionEntries++;
                    if (r.EndOfHalfIntent == Charm.Engine.EndOfHalfIntent.NoShot) TransitionEntriesNoShot++;
                }
                {
                    var defSchool0 = r.Defense == TeamSide.Home ? homeSchoolId : awaySchoolId;
                    TeamDefPossessions[defSchool0] =
                        (TeamDefPossessions.TryGetValue(defSchool0, out var p0) ? p0 : 0L) + 1L;
                    if (r.Entry == EntryType.Transition)
                        TeamDefTransitionEntries[defSchool0] =
                            (TeamDefTransitionEntries.TryGetValue(defSchool0, out var e0) ? e0 : 0L) + 1L;
                }
                switch (r.TransitionArm)
                {
                    case TransitionOutcome.Push:          TransitionPush++;     break;
                    case TransitionOutcome.Settle:        TransitionSettle++;   break;
                    case TransitionOutcome.Turnover:      TransitionTurnover++; break;
                    case TransitionOutcome.DefensiveFoul: TransitionDefFoul++;  break;
                    case TransitionOutcome.JumpBall:      TransitionJumpBall++; break;
                    case null:                                                  break;
                }
                // S86: the same arm, split by the team that had the ball. A non-null arm IS the
                // "resolver walked this Roll J entry" test — that is precisely what the S85
                // nullable label was built to represent, so the denominator needs no new field.
                if (r.TransitionArm is { } s86Arm)
                {
                    var offSchool = r.Offense == TeamSide.Home ? homeSchoolId : awaySchoolId;
                    TeamOffTransitionResolved[offSchool] =
                        (TeamOffTransitionResolved.TryGetValue(offSchool, out var tr0) ? tr0 : 0L) + 1L;
                    if (s86Arm == TransitionOutcome.Push)
                        TeamOffTransitionPush[offSchool] =
                            (TeamOffTransitionPush.TryGetValue(offSchool, out var tp0) ? tp0 : 0L) + 1L;
                }
                if (r.FastBreakFga > 0) PossWithFastBreakFga++;
                FastBreakFgm    += r.FastBreakFgm;    FastBreakBlk    += r.FastBreakBlk;
                BreakPutbackFga += r.BreakPutbackFga; BreakPutbackFgm += r.BreakPutbackFgm;
                BreakPutbackBlk += r.BreakPutbackBlk;
                NonBreakFga     += r.NonBreakFga;     NonBreakFgm     += r.NonBreakFgm;
                NonBreakBlk     += r.NonBreakBlk;
                // The press half of the break totals. A break on a possession whose entry is
                // NOT Transition can only have come from a beaten press (Roll J is the only
                // other stamper of the flag and it runs only on transition entries), so the
                // entry field alone is the provenance test — no engine-side field needed.
                if (r.Entry != EntryType.Transition &&
                    (r.FastBreakFga > 0 || r.BreakPutbackFga > 0))
                {
                    PressBornPossessions++;
                    PressBornFga     += r.FastBreakFga + r.BreakPutbackFga;
                    PressBornFgm     += r.FastBreakFgm + r.BreakPutbackFgm;
                    PressBornThreePm += r.FastBreakThreePm;
                    PressBornBlk     += r.FastBreakBlk + r.BreakPutbackBlk;
                }
                // Break blocks by DEFENSIVE team — the concentration board's denominator.
                if (r.FastBreakBlk > 0)
                {
                    var defSchool = r.Defense == TeamSide.Home ? homeSchoolId : awaySchoolId;
                    TeamFastBreakBlk[defSchool] =
                        (TeamFastBreakBlk.TryGetValue(defSchool, out var d0) ? d0 : 0L) + r.FastBreakBlk;
                }
                SecuredBoards += r.OrbChances;   // S79.3 — the league leg of the REB% identity

                UnattributedFga += r.SlotUnattributedFga;
                UnattributedFgm += r.SlotUnattributedFgm;
                //  Phase 51 decomposes every FTA into exactly five buckets which reconcile to
                //  Fta: FtaBonusPicker + FtaBonusSelected + FtaBonusUnattributed +
                //  FtaShootingSelected + FtaShootingNoSlot. TWO of the five have no owning
                //  slot — the bonus trip that reached the line before Roll E selected a
                //  shooter, AND a shooting foul carrying no slot. Counting only the first left
                //  a 30-attempt hole in the fixture season; both are the unattributed side.
                UnattributedFta += r.FtaBonusUnattributed + r.FtaShootingNoSlot;

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
        {
            // Session 84, PAGE-ONLY, never asserted. The assist door multiplies a per-zone base
            // rate by this factor, so the factor is the whole of what team passing quality does
            // to a team's assist total. Two things to read, and they fail independently:
            //
            //   LEVEL      — the league mean should sit at ~1.000. It is 1.000 by construction
            //                only while AssistPassMidpoint matches the population mean lineup
            //                passing quality. Drift in the generator moves the population and
            //                this line follows it; at S84 the midpoint was 40 points stale and
            //                this mean would have read 0.760.
            //   SEPARATION — the p10..p90 band across teams. A centred midpoint with a dead
            //                swing still reads 1.000 at the league line, so the level alone
            //                cannot tell a working dial from an inert one. At S84's settings
            //                the band is roughly 0.93..1.07.
            //
            // Both are page-only reads. Nothing here is a target and nothing is chased.
            if (s.AssistPassFactorEvents > 0)
            {
                var lg = s.AssistPassFactorSum / s.AssistPassFactorEvents;
                var teamFactors = s.TeamAssistPassFactor.Values
                                   .Where(v => v.Events > 0)
                                   .Select(v => v.Sum / v.Events)
                                   .OrderBy(x => x).ToList();
                Console.WriteLine(
                    Inv($"    lineup passing factor applied (page-only): league mean {lg:F4}") +
                    Inv($" over {s.AssistPassFactorEvents} assist-eligible makes"));
                if (teamFactors.Count > 0)
                {
                    double At(double p) => teamFactors[Math.Min(teamFactors.Count - 1, (int)(p * teamFactors.Count))];
                    Console.WriteLine(
                        Inv($"      by team (n={teamFactors.Count}): min {teamFactors[0]:F4}  p10 {At(0.10):F4}") +
                        Inv($"  median {At(0.50):F4}  p90 {At(0.90):F4}  max {teamFactors[^1]:F4}"));
                }
            }
        }
        RowRel("turnovers",                 s.TurnoverPossessions / g2, 12.5);
        RowAbs("TO% of possessions",        Pct(s.TurnoverPossessions, s.PossessionRecords), 18.5, 1.5);
        RowRel("steals",                    s.Stl / g2,               6.2);
        RowRel("blocks",                    s.Blk / g2,               3.5);
        {
            // Session 79, PAGE-ONLY: WHERE blocks happen and WHO is credited. Emmett's ruling
            // (2026-07-26): blocks should be rare at mid, long and three; the vast majority
            // happen near the rim. The matched/helper split is the S79 structural claim — an
            // elite rim protector guarding nobody should still be blocking shots.
            var near = s.BlkMatchedNear + s.BlkHelperNear;
            var outq = s.BlkMatchedOut  + s.BlkHelperOut;
            var all  = near + outq;
            Console.WriteLine(Inv($"    block location: near the rim {Pct(near, all):F1}% / out {Pct(outq, all):F1}%   (Rim+Short vs Mid+Long+Three; {all} credited blocks)"));
            Console.WriteLine(Inv($"    credited to a HELPER — near the rim {Pct(s.BlkHelperNear, near):F1}% / out {Pct(s.BlkHelperOut, outq):F1}%   (pre-S79 ~80% at every zone: the picker ignored who was matched)"));
        }
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

        // ── Session 85: THE FAST-BREAK READOUT (page-only, nothing asserted, no targets) ──
        //
        // A defence instrument, credited to the DEFENDING team throughout. It exists because
        // Emmett ruled a set of transition-defence effects and none of them could be designed:
        // every transition number on this page before today was offensive (break FGA, its
        // three-rate, its three-point percentage), so there was no line that a change to
        // transition defence would move. S84's lesson is the reason this is its own session —
        // the assist dial was 40 points wrong for 43 sessions because no line printed it.
        //
        // Vocabulary is fixed and load-bearing. A TRANSITION ENTRY (arrived off a rebound, a
        // free-throw rebound or a steal) is NOT a fast break: Roll J's Settle arm leaves the
        // state untouched, so a settled entry is indistinguishable downstream from an ordinary
        // halfcourt possession. "Transition FG%" and "halfcourt FGA" are therefore not labels
        // used here — each of them spans two of the three shot buckets below.
        {
            Console.WriteLine("  --- transition / fast-break readout (Session 85; page-only, never asserted) ---");

            // 1. Entry rate. Printed beside the break-shot rate on purpose: they measure
            //    different things and must differ, which is the one thing a completeness check
            //    cannot tell you.
            Console.WriteLine(
                Inv($"    transition entries {s.TransitionEntries} of {s.PossessionRecords} possessions = {Pct(s.TransitionEntries, s.PossessionRecords):F2}%") +
                Inv($"   |  possessions carrying a break shot {s.PossWithFastBreakFga} = {Pct(s.PossWithFastBreakFga, s.PossessionRecords):F2}%"));
            {
                var teamRates = s.TeamDefTransitionEntries.Keys
                                 .Where(k => s.TeamDefPossessions.TryGetValue(k, out var d) && d > 0)
                                 .Select(k => 100.0 * s.TeamDefTransitionEntries[k] / s.TeamDefPossessions[k])
                                 .OrderBy(x => x).ToList();
                if (teamRates.Count > 0)
                {
                    double At(double p) => teamRates[Math.Min(teamRates.Count - 1, (int)(p * teamRates.Count))];
                    Console.WriteLine(
                        Inv($"      entries CONCEDED by team (n={teamRates.Count}): min {teamRates[0]:F2}%") +
                        Inv($"  p10 {At(0.10):F2}%  median {At(0.50):F2}%  p90 {At(0.90):F2}%  max {teamRates[^1]:F2}%"));
                }
            }

            // 2. The run-or-not split, on transition entries only. FIVE SIBLING outcomes: a
            //    turnover, a foul or a tie-up happens INSTEAD of a push, not after a failed
            //    one. Do not read the non-push arms as failed pushes.
            var armSum = s.TransitionPush + s.TransitionSettle + s.TransitionTurnover +
                         s.TransitionDefFoul + s.TransitionJumpBall;
            Console.WriteLine(
                Inv($"    on transition entries — push {Pct(s.TransitionPush, armSum):F2}% ({s.TransitionPush})") +
                Inv($"  settle {Pct(s.TransitionSettle, armSum):F2}% ({s.TransitionSettle})") +
                Inv($"  turnover {Pct(s.TransitionTurnover, armSum):F2}%") +
                Inv($"  def foul {Pct(s.TransitionDefFoul, armSum):F2}%") +
                Inv($"  jump ball {Pct(s.TransitionJumpBall, armSum):F2}%"));
            // The denominator is the transition entries the resolver actually WALKED. An
            // end-of-half possession that kills the clock without shooting is recorded but never
            // resolved — Roll J does not run on it, so it has no arm. Printed rather than
            // absorbed: an unexplained residual on a conservation line cannot be told apart
            // from a wiring bug.
            var armDen = s.TransitionEntries - s.TransitionEntriesNoShot;
            Console.WriteLine(
                Inv($"      five sibling arms sum {armSum} vs resolved transition entries {armDen} (residual {armSum - armDen})") +
                Inv($"   |  {s.TransitionEntriesNoShot} entries never resolved (end-of-half, no shot)"));

            // 2b. S86 — the SAME push share, per offensive team. The league mean above cannot
            //     show whether teams spread apart, and the spread is the whole point of the
            //     opportunity/bar wire: a fast roster should run and a plodding one should not.
            //     A flat band here means the wire has gone inert even if the league mean moved.
            //     Denominator is every RESOLVED Roll J entry for that team (all five arms), so
            //     this is directly comparable to the oracle's absolute push probability.
            //     Page-only, never asserted.
            {
                var pushRates = s.TeamOffTransitionResolved.Keys
                                 .Where(k => s.TeamOffTransitionResolved[k] > 0)
                                 .Select(k => 100.0 * s.TeamOffTransitionPush.GetValueOrDefault(k)
                                                    / s.TeamOffTransitionResolved[k])
                                 .OrderBy(x => x)
                                 .ToList();
                var pushZeroTeams = s.TeamOffTransitionResolved.Count(kv => kv.Value == 0);
                if (pushRates.Count > 0)
                {
                    double AtPush(double p) =>
                        pushRates[Math.Min(pushRates.Count - 1, (int)(p * pushRates.Count))];
                    var pushMean = pushRates.Sum() / pushRates.Count;
                    Console.WriteLine(
                        Inv($"      push% by OFFENSIVE team (n={pushRates.Count}): mean {pushMean:F2}%") +
                        Inv($"  min {pushRates[0]:F2}%  p10 {AtPush(0.10):F2}%  median {AtPush(0.50):F2}%") +
                        Inv($"  p90 {AtPush(0.90):F2}%  max {pushRates[^1]:F2}%") +
                        Inv($"  |  spread {pushRates[^1] - pushRates[0]:F2}pp") +
                        Inv($"  |  {pushZeroTeams} teams excluded (zero resolved entries)"));
                }
                else
                {
                    Console.WriteLine(
                        Inv($"      push% by OFFENSIVE team: no team had a resolved transition entry") +
                        Inv($"  ({pushZeroTeams} teams excluded)"));
                }
            }

            // 3. Push selected vs break shot produced. A push that dies before a shot goes up
            //    is a real thing and the gap is itself a finding. The break-shot total is
            //    ALL-SOURCE, so it is not bounded by the push count — whether it exceeds it
            //    depends on whether press-born attempts outnumber dead pushes. Measured, not
            //    predicted.
            Console.WriteLine(
                Inv($"    pushes selected {s.TransitionPush}  ->  break shots produced (all sources) {s.FastBreakFga}") +
                Inv($"   [push-born break+putback shots {s.FastBreakFga + s.BreakPutbackFga - s.PressBornFga}") +
                Inv($" | press-born {s.PressBornFga} over {s.PressBornPossessions} possessions]"));

            // 4. The three-way shot partition with FG% for each. Three buckets, not two: a
            //    putback taken while the break was still live is break-stamped but excluded
            //    from the break-shot count, so it is neither of the other two. The break-vs-
            //    non-break gap is the number a later session moves.
            var partSum = s.FastBreakFga + s.BreakPutbackFga + s.NonBreakFga;
            var fbPct   = Pct(s.FastBreakFgm, s.FastBreakFga);
            var nbPct   = Pct(s.NonBreakFgm,  s.NonBreakFga);
            Console.WriteLine(
                Inv($"    shot partition — fast break {s.FastBreakFga} FG% {fbPct:F1}%") +
                Inv($"  |  break putback {s.BreakPutbackFga} FG% {Pct(s.BreakPutbackFgm, s.BreakPutbackFga):F1}%") +
                Inv($"  |  non-break {s.NonBreakFga} FG% {nbPct:F1}%"));
            Console.WriteLine(
                Inv($"      three buckets sum {partSum} vs FGA {s.Fga} (residual {partSum - s.Fga})") +
                Inv($"   |  break-vs-non-break FG% gap {fbPct - nbPct:+0.0;-0.0} pts"));

            // 5. Fast-break FIELD-GOAL points allowed, per team per game. Field-goal points
            //    only: free throws and putbacks sit outside the break-shot bucket, so folding
            //    them in would mean the label no longer matched what was counted. Total points
            //    generated by a break opportunity is a wider instrument and is not this session.
            var fbPoints = 2.0 * (s.FastBreakFgm - s.FastBreakThreePm) + 3.0 * s.FastBreakThreePm;
            var pressPts = 2.0 * (s.PressBornFgm - s.PressBornThreePm) + 3.0 * s.PressBornThreePm;
            Console.WriteLine(
                Inv($"    fast-break FIELD-GOAL points allowed: {Avg(fbPoints, s.Games * 2):F2} per team per game") +
                Inv($"   (FT and putbacks excluded by definition; press-born share {(fbPoints > 0.0 ? 100.0 * pressPts / fbPoints : 0.0):F1}%)"));

            // 6. Block rate per bucket, all three. A blocked shot IS an attempt, so these are
            //    shares of each bucket's attempts and the three counts sum to league blocks.
            //    All three are printed because a two-way line beside a three-way partition
            //    invites the false reading that two rates exhaust every blocked attempt.
            var blkSum = s.FastBreakBlk + s.BreakPutbackBlk + s.NonBreakBlk;
            Console.WriteLine(
                Inv($"    block rate by bucket — fast break {Pct(s.FastBreakBlk, s.FastBreakFga):F2}% ({s.FastBreakBlk})") +
                Inv($"  |  break putback {Pct(s.BreakPutbackBlk, s.BreakPutbackFga):F2}% ({s.BreakPutbackBlk})") +
                Inv($"  |  non-break {Pct(s.NonBreakBlk, s.NonBreakFga):F2}% ({s.NonBreakBlk})"));
            Console.WriteLine(
                Inv($"      three bucket blocks sum {blkSum} vs league blocks {s.Blk} (residual {blkSum - s.Blk})") +
                Inv($"   |  press-born break blocks {s.PressBornBlk}"));

            // 7. WHO gets fast-break blocks — the baseline for "the spread should widen with a
            //    fast lineup". The top-credited defender's share of his own team's break
            //    blocks, as a league distribution.
            //
            //    ★ Read this next to O-48. On a break the engine deliberately assigns NOBODY:
            //    BlockerPicker exempts transition entirely, so every gate is 1.0 and all five
            //    defenders are equally eligible. The concentration below is therefore "whoever
            //    the shot-blocking numbers favour, with the matchup filter switched off" — it
            //    is the honest baseline, and any change that widens it runs into O-48 first.
            //
            //    Teams with zero break blocks are EXCLUDED and counted, because their share is
            //    undefined, not zero — including them as 0% would drag every percentile down
            //    with a number that means "no sample".
            //
            //    All-source only, by design. Press-born break blocks are a sliver of a sliver;
            //    per-team percentiles on that sample would be noise wearing a table's clothes.
            {
                var topByTeam = s.PlayerSeasons.Values
                                 .Where(p => p.FbBlk > 0)
                                 .GroupBy(p => p.SchoolId)
                                 .ToDictionary(gp => gp.Key, gp => gp.Max(p => p.FbBlk));
                var shares = s.TeamFastBreakBlk
                              .Where(kv => kv.Value > 0 && topByTeam.ContainsKey(kv.Key))
                              .Select(kv => 100.0 * topByTeam[kv.Key] / kv.Value)
                              .OrderBy(x => x).ToList();
                var zeroTeams = s.TeamDefPossessions.Keys
                                 .Count(k => !s.TeamFastBreakBlk.TryGetValue(k, out var v) || v == 0);
                if (shares.Count > 0)
                {
                    double At(double p) => shares[Math.Min(shares.Count - 1, (int)(p * shares.Count))];
                    Console.WriteLine(
                        Inv($"    top defender's share of his team's break blocks (n={shares.Count} teams; {zeroTeams} excluded with zero break blocks):"));
                    Console.WriteLine(
                        Inv($"      min {shares[0]:F1}%  p10 {At(0.10):F1}%  median {At(0.50):F1}%") +
                        Inv($"  p90 {At(0.90):F1}%  max {shares[^1]:F1}%"));
                }
                else
                {
                    Console.WriteLine(
                        Inv($"    top defender's share of his team's break blocks: no team recorded one ({zeroTeams} teams with zero)"));
                }
            }
        }

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

        // ── S76: rotation depth — the distribution this session exists to move ──────
        {
            static string Inv3(FormattableString f) => FormattableString.Invariant(f);
            Console.WriteLine("--- ROTATION DEPTH (Session 76; page-only, never asserted) ---");
            var totalCredits = s.RotationRankCredits.Sum();
            if (s.RotationTeamGames == 0 || totalCredits == 0)
            {
                Console.WriteLine("  no rotation data recorded (instrument not wired) — treat every minutes number as unproven.");
            }
            else
            {
                // Nominal minutes per team-game for the Nth most-used man. Records per
                // team-game x 40 minutes / records = the 200-minute pie, so a credit is
                // worth 40 / (records per team-game) minutes.
                var recordsPerTeamGame = s.RotationRecords / (double)s.RotationTeamGames;
                var minutesPerCredit = recordsPerTeamGame > 0 ? 40.0 / recordsPerTeamGame : 0.0;

                long topFive = 0;
                for (var i = 0; i < Lineup.Size && i < s.RotationRankCredits.Length; i++) topFive += s.RotationRankCredits[i];

                Console.WriteLine(Inv3(
                    $"  minutes by realized rotation rank (mean over {s.RotationTeamGames} team-games, {recordsPerTeamGame:F1} records/team-game):"));
                var parts = new List<string>();
                for (var i = 0; i < s.RotationRankCredits.Length; i++)
                {
                    var mins = s.RotationRankCredits[i] / (double)s.RotationTeamGames * minutesPerCredit;
                    parts.Add(FormattableString.Invariant($"{i + 1,2}:{mins,5:F1}"));
                }
                for (var i = 0; i < parts.Count; i += 7)
                    Console.WriteLine("    " + string.Join("  ", parts.Skip(i).Take(7)));

                Console.WriteLine(Inv3(
                    $"  top-5 share of floor time: {100.0 * topFive / totalCredits:F1} %   (S75 measured ~88 % — the concentration this session set out to break)"));
                var played = s.RotationRankCredits.Count(c => c > 0);
                Console.WriteLine(Inv3(
                    $"  men used per team-game: {played} of {RosterShape.Size} roster slots see floor time at some rank"));
            }
            Console.WriteLine();
        }

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

        // ── S87 (page-only, never asserted) — the foul-out layer ────────────────────
        // The line above is TEAM fouls: shooting + non-shooting, the two that feed the
        // bonus. Offensive fouls are reported separately and deliberately are NOT added
        // into it — they are charged to the man and never to the team, so folding them in
        // would misstate the bonus-relevant number.
        Console.WriteLine(Inv($"  offensive fouls/team/game              {s.OffFoulTotal / g2:F2}") +
                          "   (charged to the MAN only — no team foul, no bonus)");
        Console.WriteLine(Inv($"  foul-outs per team-game                {s.FoulOuts / g2:F3}"));
        {
            var seated = 0L;
            foreach (var b in s.PfBucket) seated += b;
            if (seated > 0)
            {
                Console.WriteLine(Inv($"  personal fouls per player-game (n={seated:N0} men who played a possession):"));
                var labels = new[] { "0", "1", "2", "3", "4", "5+" };
                var row = "    ";
                for (var i = 0; i < 6; i++)
                    row += Inv($"{labels[i]} {100.0 * s.PfBucket[i] / seated,5:F1}%  ");
                Console.WriteLine(row);
            }
        }
        Console.WriteLine(Inv($"  escape hatch — men left on floor        {s.R4Occurrences}") +
                          Inv($"  | trips played while disqualified {s.PossPlayedWhileDq}") +
                          Inv($"  | forced replacements {s.FoulOutReplacements}"));

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

    // ── S78: STAGE 2 of the diagnostic ladder — the ROSTERED population by body band.
    //    Page-only, never asserted, void by design.
    //
    //    Why this exists: a season page alone cannot validate the generator. O-6's
    //    guard-leaning scout rank could drop every big-skilled player S78 creates, and
    //    the page would look unchanged while the generator worked perfectly. Stage 1
    //    (the oracle's generated table) says what was MADE; this says what got DRAFTED.
    //    A gap between the two is a DRAFT problem, not a generator problem.
    //
    //    NOTE ON WHAT IS MEASURED: the roster bridge carries only the 33-key CURRENT
    //    card (PoolPlayer.Ratings) — latent does not survive the crossing. So these are
    //    CURRENT ratings, and the oracle's stage-1 table prints current alongside latent
    //    so the two are comparable. Comparing generated-LATENT against rostered-CURRENT
    //    would confound arrival expression with draft selection, which is exactly the
    //    thing this ladder exists to separate.
    private static void PrintS78BodyBandCensus(DivvyResult res)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);
        var bands = new (string Name, int Lo, int Hi)[]
        {
            ("5'8-5'9", 40, 44), ("5'10-5'11", 45, 50), ("6'0-6'1", 51, 56),
            ("6'2-6'5", 57, 65), ("6'6-6'7", 66, 70), ("6'8-6'9", 71, 79),
            ("6'10-7'0", 80, 86), ("7'1+", 87, 99),
        };
        var watched = new[]
        {
            "RimProtection", "PostDefense", "OffensiveRebounding", "DefensiveRebounding",
            "BallHandling", "Outside", "BasketballIQ", "Discipline", "HelpDefense",
        };
        var pool = res.Pool;
        var drafted = new HashSet<int>(res.Rosters.Values.SelectMany(r => r));

        Console.WriteLine("--- S78 BODY-BAND CENSUS, ROSTERED (stage 2 of the ladder; CURRENT ratings; page-only) ---");
        Console.WriteLine($"  {"band",-11}{"n",5}   " + string.Join("  ", watched.Select(w => $"{w[..Math.Min(6, w.Length)],6}")));
        foreach (var (name, lo, hi) in bands)
        {
            var sub = pool.Where(p => drafted.Contains(p.PoolId)
                                      && p.Ratings["Height"] >= lo && p.Ratings["Height"] <= hi).ToList();
            if (sub.Count == 0) continue;
            var cells = watched.Select(w => Inv($"{sub.Average(p => p.Ratings[w]),6:F1}"));
            Console.WriteLine($"  {name,-11}{sub.Count,5}   " + string.Join("  ", cells));
        }

        // S78 MEASURED FINDING, recorded here so nobody re-derives it: the accepted pool
        // is drafted ONE-FOR-ONE (BuildRecruitedCohort returns exactly PoolSize players
        // and every one lands on a roster), so a pool->rostered transition count is
        // 100% BY CONSTRUCTION and can never detect anything. Draft-level masking is
        // structurally impossible.
        //
        // That relocates the O-6 masking risk rather than clearing it: the scout rank
        // decides WHICH SCHOOL and WHAT DEPTH-CHART SLOT, so it can still bury this
        // session's players in minutes. The live test is stage 3 — minutes by man on the
        // S77 page — not this seam.
        Console.WriteLine($"  accepted pool {pool.Count} -> rostered {drafted.Count} " +
                          "(1:1 by construction — draft-level masking is impossible; " +
                          "the O-6 risk is MINUTES, read it on the stage-3 page)");
        foreach (var w in watched.Take(4))
        {
            var hi = pool.Count(p => p.Ratings[w] >= 80);
            Console.WriteLine(Inv($"    {w,-22} rostered with current >= 80: {hi,5} ({100.0 * hi / Math.Max(1, pool.Count),5:F2}%)"));
        }
    }

    // ── S80 instrument: the POSITION census. The readout the interior-defence bid
    //    change is ruled on and validated against. Page-only, never asserted.
    //
    //    Why this exists ALONGSIDE the band census above rather than replacing it: the
    //    height bands are S78's recorded baseline and stay untouched so S78's numbers
    //    remain comparable. What they cannot show is a change that lands on GUARDS as a
    //    class — banding by height splits guards across three rows, and a band MEAN
    //    hides both the tail and the direction. S80 moves the LOW END of a distribution,
    //    so the instrument has to print the low end: median, p10, p90, and the share
    //    sitting at or under 10 and 20.
    //
    //    CLAUDE'S CALL, flagged for Emmett: rows are grouped by WHAT SHARES A BUDGET,
    //    not alphabetically, and interior defence is printed directly above perimeter
    //    defence. That ordering encodes Emmett's S80 ruling — the two bids are ONE dial
    //    and move together, so a big losing perimeter defence is INTENDED behaviour, not
    //    collateral damage. The page has to make the mirror visible side by side or the
    //    ruling cannot be checked. Reversible: reorder the groups array.
    //
    //    The offence and rebounding groups are here because the family budget is
    //    COMPETITIVE (PlayerGenPass3 normalizes pulls into shares of a fixed total), so
    //    budget a guard stops spending on interior defence necessarily lands somewhere
    //    else. Those rows are the "somewhere else".
    private static void PrintS80PositionCensus(DivvyResult res)
    {
        static string Inv(FormattableString f) => FormattableString.Invariant(f);

        // Nearest-rank percentile over an ASCENDING list; p=50 is the median. Kept local
        // rather than promoted to a shared utility — the harness has no percentile helper
        // today and one page is not enough reason to add one.
        static int Pctile(List<int> ascending, double p)
        {
            if (ascending.Count == 0) return 0;
            var idx = (int)Math.Ceiling(p / 100.0 * ascending.Count) - 1;
            return ascending[Math.Clamp(idx, 0, ascending.Count - 1)];
        }

        var groups = new (string Label, string[] Skills)[]
        {
            ("interior defence  (S80 primary)",
                new[] { "RimProtection", "PostDefense" }),
            ("perimeter defence (the ruled mirror — same dial, opposite plane)",
                new[] { "PerimeterDefense", "Steals", "OffBallDefense" }),
            ("interior offence  (competes for the same budget)",
                new[] { "PostMoves", "Close", "Finishing", "Screening" }),
            ("perimeter offence (competes for the same budget)",
                new[] { "Outside", "Mid", "BallHandling", "Passing", "Playmaking" }),
            ("rebounding        (competes for the same budget)",
                new[] { "OffensiveRebounding", "DefensiveRebounding" }),
        };

        var pool = res.Pool;
        var drafted = new HashSet<int>(res.Rosters.Values.SelectMany(r => r));

        Console.WriteLine("--- S80 POSITION CENSUS, ROSTERED (stage 2; CURRENT ratings; page-only, never asserted) ---");
        foreach (var pos in new[]
                 { PositionalEligibility.Guard, PositionalEligibility.Wing, PositionalEligibility.Big })
        {
            var sub = pool.Where(p => drafted.Contains(p.PoolId) && p.Pos == pos).ToList();
            if (sub.Count == 0) continue;

            Console.WriteLine(Inv($"  {pos}  (n={sub.Count})"));
            Console.WriteLine($"      {"skill",-22}{"med",5}{"p10",6}{"p90",6}{"<=10",8}{"<=20",8}");
            foreach (var (label, skills) in groups)
            {
                Console.WriteLine($"    {label}");
                foreach (var w in skills)
                {
                    var v = sub.Select(p => p.Ratings[w]).OrderBy(x => x).ToList();
                    var le10 = v.Count(x => x <= 10);
                    var le20 = v.Count(x => x <= 20);
                    Console.WriteLine(Inv(
                        $"      {w,-22}{Pctile(v, 50),5}{Pctile(v, 10),6}{Pctile(v, 90),6}{100.0 * le10 / v.Count,7:F1}%{100.0 * le20 / v.Count,7:F1}%"));
                }
            }
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

        PrintS78BodyBandCensus(res);
        PrintS80PositionCensus(res);

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
