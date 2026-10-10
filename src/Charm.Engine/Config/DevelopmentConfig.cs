using System.Text.Json;

namespace Charm.Engine;

/// <summary>
/// S121 — THE OFFSEASON CAMP'S DIALS. Loaded from the "Development" section of config.json.
///
/// <para><b>Emmett's standing instruction for this arc:</b> "The prerogative is to make things
/// editable and modifiable." Every number the potential roll and the camp use lives here; none
/// lives in code. What stays in code is the attribute GROUPS (which attribute is a skill, a body
/// attribute or athleticism — K1) and the generator's free-throw constants.</para>
///
/// <para><b>Validation is total and aggregated.</b> An edit that breaks an equation (odds that do
/// not sum to one, a divisor of zero, a split over the budget, an attribute order missing a name)
/// is refused when the file loads, never mid-season, and every failure in the section is listed in
/// ONE refusal — the loader never stops at the first bad key. A missing section is quiet at
/// runtime (the compiled first-draft values apply) and loud at test time (Phase 71), the same
/// ruling every other section follows.</para>
///
/// <para>The first-draft values below are the S121 prompt's §4 numbers. "We are more building the
/// systems and we can go back and tweak the numbers" (Emmett, 2026-10-10).</para>
/// </summary>
public sealed class DevelopmentConfig
{
    // ── The potential roll (4b) ──────────────────────────────────────────────
    /// <summary>The player-wide potential tier: none / low / medium / high / very high.</summary>
    public double[] PlayerTierOdds { get; set; } = { 0.08, 0.28, 0.37, 0.19, 0.08 };
    /// <summary>Each skill's offset from the player-wide tier, −2..+2.</summary>
    public double[] SkillOffsetOdds { get; set; } = { 0.08, 0.25, 0.40, 0.20, 0.07 };
    /// <summary>Each body and athletic attribute's tier, independent of the player-wide tier.</summary>
    public double[] BodyTierOdds { get; set; } = { 0.15, 0.30, 0.30, 0.18, 0.07 };
    public double BodyFirstNotchDown { get; set; } = 1.0;
    public double BodyFirstFullGap { get; set; } = 1.25;
    public double ReadyNotchDown { get; set; } = 1.0;
    public double ReadyFullZ { get; set; } = 1.25;
    public double TalentNotchUp { get; set; } = 0.15;
    public double TalentTopFraction { get; set; } = 1.0 / 3.0;
    public int WorkEthicBetaShape { get; set; } = 3;
    public string[] GuardDownSkills { get; set; } = { "PostMoves", "PostDefense", "RimProtection" };
    public string[] BigDownSkills { get; set; } = { "Outside", "BallHandling", "SelfCreation", "Playmaking" };

    // ── The camp (4c) ────────────────────────────────────────────────────────
    public int Budget { get; set; } = 50;
    public int MaxPerAttribute { get; set; } = 20;
    /// <summary>The computer's split, one list per camp: camp 1, camp 2, camp 3 (beyond the list,
    /// its last entry). The widening (Emmett's R3 ruling).</summary>
    public int[][] PrioritySplits { get; set; } =
    {
        new[] { 20, 15, 15 },
        new[] { 15, 15, 10, 10 },
        new[] { 15, 10, 10, 10, 5 },
    };
    /// <summary>The rotation: an attribute funded this many summers running ranks at
    /// <see cref="RepeatFactor"/> of its promise.</summary>
    public int RepeatAfter { get; set; } = 2;
    public double RepeatFactor { get; set; } = 0.6;
    /// <summary>Growth per point at each tier.</summary>
    public double[] TierRate { get; set; } = { 0.0, 0.10, 0.22, 0.35, 0.50 };
    /// <summary>Per group: exactly Skill, Body, Athleticism.</summary>
    public Dictionary<string, double> GroupFactor { get; set; } = new(StringComparer.Ordinal)
    {
        ["Skill"] = 1.0, ["Body"] = 1.0, ["Athleticism"] = 0.35,
    };
    /// <summary>Athleticism's slow natural climb per summer, by tier, funded or not.</summary>
    public double[] NaturalClimb { get; set; } = { 0.0, 0.3, 0.6, 1.0, 1.5 };
    public double NaturalJitterLo { get; set; } = 0.5;
    public double NaturalJitterHi { get; set; } = 1.5;
    public double GrowthJitterLo { get; set; } = 0.8;
    public double GrowthJitterHi { get; set; } = 1.2;
    /// <summary>An unfunded skill or body attribute slips one point with this chance.</summary>
    public double AtrophyChance { get; set; } = 0.25;
    /// <summary>bad / normal / good / breakout.</summary>
    public double[] CampOdds { get; set; } = { 0.20, 0.50, 0.22, 0.08 };
    public double[] CampMultiplier { get; set; } = { 0.45, 1.0, 1.45, 2.1 };
    /// <summary>On a breakout every funded attribute grows at least at this tier's rate.</summary>
    public int BreakoutFloorTier { get; set; } = 3;
    public double WorkEthicTilt { get; set; } = 0.10;
    public double MinutesTilt { get; set; } = 0.06;
    public double MinutesPivot { get; set; } = 0.40;
    public double MinutesSpan { get; set; } = 0.60;

    // ── The IQ rule (4c-6) ───────────────────────────────────────────────────
    public double IqAgeGrowth { get; set; } = 1.0;
    public double IqMinutesGrowth { get; set; } = 2.0;
    public int IqCapAboveArrival { get; set; } = 8;
    public double DisciplineAgeGrowth { get; set; } = 1.0;
    public double DisciplineMinutesGrowth { get; set; } = 2.0;
    public int DisciplineCapAboveArrival { get; set; } = 8;

    // ── Height (4c-7) ────────────────────────────────────────────────────────
    public double HeightSpurtChance { get; set; } = 0.04;
    public int HeightSpurtRating { get; set; } = 3;
    public int HeightSpurtWingspan { get; set; } = 2;

    /// <summary>The 27 funded attributes in the order that sets every tie-break and every
    /// per-attribute draw. Any permutation of the 27 (K1 fixes WHICH 27).</summary>
    public string[] AttributeOrder { get; set; } = (string[])FundedAttributes.Clone();

    // ── K1 — group membership, fixed in code ─────────────────────────────────
    /// <summary>The 20 camp-funded skills: the generator's 19 spend skills plus HelpDefense.</summary>
    public static readonly string[] Skills =
    {
        "Outside", "Mid", "OffBallMovement", "Close", "Finishing", "PostMoves", "Screening",
        "BallHandling", "Passing", "Playmaking", "SelfCreation", "FoulDrawing",
        "PerimeterDefense", "Steals", "OffBallDefense", "PostDefense", "RimProtection",
        "OffensiveRebounding", "DefensiveRebounding", "HelpDefense",
    };
    public static readonly string[] BodyAttributes = { "Strength", "Weight", "Endurance" };
    public static readonly string[] AthleticAttributes = { "Speed", "Quickness", "FirstStep", "Vertical" };
    /// <summary>The 27 funded attributes, canonical order (the scouting file's order).</summary>
    public static readonly string[] FundedAttributes = Skills.Concat(BodyAttributes).Concat(AthleticAttributes).ToArray();
    public static readonly string[] GroupNames = { "Skill", "Body", "Athleticism" };

    // ── Loading ──────────────────────────────────────────────────────────────

    public static DevelopmentConfig Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("Development", out var section))
            return new DevelopmentConfig();
        return FromSection(section);
    }

    /// <summary>Reads every key the section carries, collecting every unreadable value, then
    /// validates the whole; one refusal lists every failure.</summary>
    public static DevelopmentConfig FromSection(JsonElement section)
    {
        var errs = new List<string>();
        var c = new DevelopmentConfig();
        if (section.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Development config refused: the section is not an object.");

        foreach (var p in section.EnumerateObject())
        {
            var v = p.Value;
            try
            {
                switch (p.Name)
                {
                    case nameof(PlayerTierOdds): c.PlayerTierOdds = Doubles(v); break;
                    case nameof(SkillOffsetOdds): c.SkillOffsetOdds = Doubles(v); break;
                    case nameof(BodyTierOdds): c.BodyTierOdds = Doubles(v); break;
                    case nameof(BodyFirstNotchDown): c.BodyFirstNotchDown = Dbl(v); break;
                    case nameof(BodyFirstFullGap): c.BodyFirstFullGap = Dbl(v); break;
                    case nameof(ReadyNotchDown): c.ReadyNotchDown = Dbl(v); break;
                    case nameof(ReadyFullZ): c.ReadyFullZ = Dbl(v); break;
                    case nameof(TalentNotchUp): c.TalentNotchUp = Dbl(v); break;
                    case nameof(TalentTopFraction): c.TalentTopFraction = Dbl(v); break;
                    case nameof(WorkEthicBetaShape): c.WorkEthicBetaShape = Int(v); break;
                    case nameof(GuardDownSkills): c.GuardDownSkills = Strings(v); break;
                    case nameof(BigDownSkills): c.BigDownSkills = Strings(v); break;
                    case nameof(Budget): c.Budget = Int(v); break;
                    case nameof(MaxPerAttribute): c.MaxPerAttribute = Int(v); break;
                    case nameof(PrioritySplits):
                        if (v.ValueKind != JsonValueKind.Array) throw new FormatException("expected a list of lists");
                        c.PrioritySplits = v.EnumerateArray().Select(Ints).ToArray();
                        break;
                    case nameof(RepeatAfter): c.RepeatAfter = Int(v); break;
                    case nameof(RepeatFactor): c.RepeatFactor = Dbl(v); break;
                    case nameof(TierRate): c.TierRate = Doubles(v); break;
                    case nameof(GroupFactor):
                        if (v.ValueKind != JsonValueKind.Object) throw new FormatException("expected an object");
                        c.GroupFactor = v.EnumerateObject().ToDictionary(x => x.Name, x => Dbl(x.Value), StringComparer.Ordinal);
                        break;
                    case nameof(NaturalClimb): c.NaturalClimb = Doubles(v); break;
                    case nameof(NaturalJitterLo): c.NaturalJitterLo = Dbl(v); break;
                    case nameof(NaturalJitterHi): c.NaturalJitterHi = Dbl(v); break;
                    case nameof(GrowthJitterLo): c.GrowthJitterLo = Dbl(v); break;
                    case nameof(GrowthJitterHi): c.GrowthJitterHi = Dbl(v); break;
                    case nameof(AtrophyChance): c.AtrophyChance = Dbl(v); break;
                    case nameof(CampOdds): c.CampOdds = Doubles(v); break;
                    case nameof(CampMultiplier): c.CampMultiplier = Doubles(v); break;
                    case nameof(BreakoutFloorTier): c.BreakoutFloorTier = Int(v); break;
                    case nameof(WorkEthicTilt): c.WorkEthicTilt = Dbl(v); break;
                    case nameof(MinutesTilt): c.MinutesTilt = Dbl(v); break;
                    case nameof(MinutesPivot): c.MinutesPivot = Dbl(v); break;
                    case nameof(MinutesSpan): c.MinutesSpan = Dbl(v); break;
                    case nameof(IqAgeGrowth): c.IqAgeGrowth = Dbl(v); break;
                    case nameof(IqMinutesGrowth): c.IqMinutesGrowth = Dbl(v); break;
                    case nameof(IqCapAboveArrival): c.IqCapAboveArrival = Int(v); break;
                    case nameof(DisciplineAgeGrowth): c.DisciplineAgeGrowth = Dbl(v); break;
                    case nameof(DisciplineMinutesGrowth): c.DisciplineMinutesGrowth = Dbl(v); break;
                    case nameof(DisciplineCapAboveArrival): c.DisciplineCapAboveArrival = Int(v); break;
                    case nameof(HeightSpurtChance): c.HeightSpurtChance = Dbl(v); break;
                    case nameof(HeightSpurtRating): c.HeightSpurtRating = Int(v); break;
                    case nameof(HeightSpurtWingspan): c.HeightSpurtWingspan = Int(v); break;
                    case nameof(AttributeOrder): c.AttributeOrder = Strings(v); break;
                    // An unknown key is not refused here: Phase 71 reports it as an orphan,
                    // which is the project-wide ruling for every section.
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                errs.Add($"{p.Name}: {ex.Message}");
            }
        }
        errs.AddRange(c.Validate());
        if (errs.Count > 0)
            throw new InvalidOperationException("Development config refused: " + string.Join("; ", errs));
        return c;
    }

    private static double Dbl(JsonElement v)
        => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : throw new FormatException("expected a number");
    private static int Int(JsonElement v)
        => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : throw new FormatException("expected an integer");
    private static double[] Doubles(JsonElement v)
        => v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(Dbl).ToArray() : throw new FormatException("expected a list of numbers");
    private static int[] Ints(JsonElement v)
        => v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(Int).ToArray() : throw new FormatException("expected a list of integers");
    private static string[] Strings(JsonElement v)
        => v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new FormatException("expected a list of names")).ToArray()
            : throw new FormatException("expected a list of names");

    // ── Validation (4a) ──────────────────────────────────────────────────────

    /// <summary>Every failure, by name. Empty means the section is usable. Public so a check can
    /// build a config in memory (including values JSON cannot carry, like NaN) and see it refused.</summary>
    public List<string> Validate()
    {
        var errs = new List<string>();
        void Need(bool ok, string msg) { if (!ok) errs.Add(msg); }

        // Finite first: a NaN would slip past every comparison below.
        foreach (var (name, values) in AllNumbers())
            Need(values.All(double.IsFinite), $"{name}: every number must be finite (no NaN or infinity)");
        if (errs.Count > 0) return errs;

        Need(Budget >= 1, "Budget: integer >= 1");
        Need(MaxPerAttribute >= 1 && MaxPerAttribute <= Budget, "MaxPerAttribute: integer in 1..Budget");
        Need(GroupFactor is not null && GroupFactor.Count == 3 && GroupNames.All(GroupFactor.ContainsKey),
             "GroupFactor: exactly Skill, Body, Athleticism");
        Need(AttributeOrder is not null && AttributeOrder.Length == FundedAttributes.Length
             && AttributeOrder.Distinct(StringComparer.Ordinal).Count() == FundedAttributes.Length
             && AttributeOrder.All(a => FundedAttributes.Contains(a, StringComparer.Ordinal)),
             "AttributeOrder: exactly the 27 funded attributes, each once");
        foreach (var (name, list) in new[] { (nameof(GuardDownSkills), GuardDownSkills), (nameof(BigDownSkills), BigDownSkills) })
            Need(list is not null && list.Distinct(StringComparer.Ordinal).Count() == list.Length
                 && list.All(s => Skills.Contains(s, StringComparer.Ordinal)), $"{name}: distinct skills only");

        foreach (var (name, odds, n) in new[]
                 {
                     (nameof(PlayerTierOdds), PlayerTierOdds, 5), (nameof(SkillOffsetOdds), SkillOffsetOdds, 5),
                     (nameof(BodyTierOdds), BodyTierOdds, 5), (nameof(CampOdds), CampOdds, 4),
                 })
            Need(odds is not null && odds.Length == n && odds.All(p => p >= 0) && Math.Abs(SumLeftToRight(odds) - 1.0) <= 1e-9,
                 $"{name}: {n} non-negative odds summing to 1");
        Need(CampMultiplier is not null && CampMultiplier.Length == 4 && CampMultiplier.All(m => m >= 0), "CampMultiplier: 4 non-negative");
        Need(CampOdds is not { Length: 4 } || CampOdds[2] + CampOdds[3] > 0,
             "CampOdds: good + breakout must be > 0 (the tilt splits by their ratio)");
        foreach (var (name, p) in new[]
                 {
                     (nameof(BodyFirstNotchDown), BodyFirstNotchDown), (nameof(ReadyNotchDown), ReadyNotchDown),
                     (nameof(TalentNotchUp), TalentNotchUp), (nameof(AtrophyChance), AtrophyChance),
                     (nameof(HeightSpurtChance), HeightSpurtChance),
                 })
            Need(p >= 0.0 && p <= 1.0, $"{name}: must be in [0,1]");
        Need(TalentTopFraction > 0 && TalentTopFraction <= 1, "TalentTopFraction: must be in (0,1]");
        foreach (var (name, d) in new[]
                 {
                     (nameof(BodyFirstFullGap), BodyFirstFullGap), (nameof(ReadyFullZ), ReadyFullZ),
                     (nameof(MinutesSpan), MinutesSpan),
                 })
            Need(d > 0, $"{name}: must be > 0 (a divisor)");
        Need(WorkEthicBetaShape >= 1, "WorkEthicBetaShape: integer >= 1");
        Need(TierRate is not null && TierRate.Length == 5 && TierRate.All(r => r >= 0), "TierRate: 5 non-negative");
        Need(NaturalClimb is not null && NaturalClimb.Length == 5 && NaturalClimb.All(r => r >= 0), "NaturalClimb: 5 non-negative");
        Need(GroupFactor is null || GroupFactor.Values.All(v => v >= 0), "GroupFactor: every factor non-negative");
        Need(NaturalJitterLo >= 0 && NaturalJitterLo <= NaturalJitterHi, "NaturalJitterLo: must be >= 0 and <= NaturalJitterHi");
        Need(GrowthJitterLo >= 0 && GrowthJitterLo <= GrowthJitterHi, "GrowthJitterLo: must be >= 0 and <= GrowthJitterHi");
        Need(BreakoutFloorTier >= 0 && BreakoutFloorTier <= 4, "BreakoutFloorTier: must be in 0..4");
        Need(PrioritySplits is not null && PrioritySplits.Length >= 1
             && PrioritySplits.All(sp => sp is not null && sp.Length >= 1 && sp.All(p => p >= 1 && p <= MaxPerAttribute)
                                         && sp.Sum() <= Budget),
             "PrioritySplits: one non-empty list per camp, positive integers, each <= MaxPerAttribute, sum <= Budget");
        Need(RepeatAfter >= 1, "RepeatAfter: integer >= 1");
        Need(RepeatFactor >= 0 && RepeatFactor <= 1, "RepeatFactor: must be in [0,1]");
        foreach (var (name, v) in new[]
                 {
                     (nameof(WorkEthicTilt), WorkEthicTilt), (nameof(MinutesTilt), MinutesTilt),
                     (nameof(IqAgeGrowth), IqAgeGrowth), (nameof(IqMinutesGrowth), IqMinutesGrowth),
                     (nameof(IqCapAboveArrival), (double)IqCapAboveArrival),
                     (nameof(DisciplineAgeGrowth), DisciplineAgeGrowth), (nameof(DisciplineMinutesGrowth), DisciplineMinutesGrowth),
                     (nameof(DisciplineCapAboveArrival), (double)DisciplineCapAboveArrival),
                     (nameof(HeightSpurtRating), (double)HeightSpurtRating), (nameof(HeightSpurtWingspan), (double)HeightSpurtWingspan),
                 })
            Need(v >= 0, $"{name}: must be >= 0 (IQ and Discipline never decrease, and tilts and spurts are never negative)");
        return errs;
    }

    /// <summary>Every number in the section, by key — the finite check's universe.</summary>
    private IEnumerable<(string Name, IEnumerable<double> Values)> AllNumbers()
    {
        yield return (nameof(PlayerTierOdds), PlayerTierOdds ?? Array.Empty<double>());
        yield return (nameof(SkillOffsetOdds), SkillOffsetOdds ?? Array.Empty<double>());
        yield return (nameof(BodyTierOdds), BodyTierOdds ?? Array.Empty<double>());
        yield return (nameof(TierRate), TierRate ?? Array.Empty<double>());
        yield return (nameof(NaturalClimb), NaturalClimb ?? Array.Empty<double>());
        yield return (nameof(CampOdds), CampOdds ?? Array.Empty<double>());
        yield return (nameof(CampMultiplier), CampMultiplier ?? Array.Empty<double>());
        yield return (nameof(GroupFactor), GroupFactor?.Values ?? (IEnumerable<double>)Array.Empty<double>());
        foreach (var (n, v) in new[]
                 {
                     (nameof(BodyFirstNotchDown), BodyFirstNotchDown), (nameof(BodyFirstFullGap), BodyFirstFullGap),
                     (nameof(ReadyNotchDown), ReadyNotchDown), (nameof(ReadyFullZ), ReadyFullZ),
                     (nameof(TalentNotchUp), TalentNotchUp), (nameof(TalentTopFraction), TalentTopFraction),
                     (nameof(RepeatFactor), RepeatFactor), (nameof(NaturalJitterLo), NaturalJitterLo),
                     (nameof(NaturalJitterHi), NaturalJitterHi), (nameof(GrowthJitterLo), GrowthJitterLo),
                     (nameof(GrowthJitterHi), GrowthJitterHi), (nameof(AtrophyChance), AtrophyChance),
                     (nameof(WorkEthicTilt), WorkEthicTilt), (nameof(MinutesTilt), MinutesTilt),
                     (nameof(MinutesPivot), MinutesPivot), (nameof(MinutesSpan), MinutesSpan),
                     (nameof(IqAgeGrowth), IqAgeGrowth), (nameof(IqMinutesGrowth), IqMinutesGrowth),
                     (nameof(DisciplineAgeGrowth), DisciplineAgeGrowth),
                     (nameof(DisciplineMinutesGrowth), DisciplineMinutesGrowth),
                     (nameof(HeightSpurtChance), HeightSpurtChance),
                 })
            yield return (n, new[] { v });
    }

    /// <summary>Plain left-to-right sum — the same accumulation the oracle uses.</summary>
    private static double SumLeftToRight(double[] xs)
    {
        var s = 0.0;
        foreach (var x in xs) s += x;
        return s;
    }
}
