namespace Charm.Engine;

/// <summary>
/// C# port, S69: the DETERMINISTIC MATH of the locked Pass-3 TWO-PLANE BUDGET
/// player-generation oracle (<c>tools/gen_pass3_budget_oracle.py</c>, LOCKED 2026-07-24,
/// S68). Pure functions only — every transform takes its drawn value(s) as EXPLICIT
/// inputs and draws nothing. The parity gate (Phase 69) replays the committed fixture
/// <c>tools/gen_pass3_replay_fixture_s69.json</c> through <see cref="BuildFromDraws"/>;
/// the live generator (<see cref="PlayerGenPass3Live"/>) draws from <see cref="IRng"/>
/// and calls THESE SAME functions, so the math proven by replay is the math that ships.
///
/// <para><b>The oracle wins.</b> Every constant and expression is a verbatim
/// transcription (oracle names preserved, oracle line cited per site as of the locked
/// file post-recorder-seam). The fixture's constants echo is asserted against
/// <see cref="ConstantsEcho"/> BEFORE any replay runs.</para>
///
/// <para><b>LIVE SINCE S70 (the bridge swap):</b> the divvy's <c>BuildRecruitedCohort</c>
/// draws this generator; position follows the DEFENSIVE PLANE by exact-count rank
/// (Emmett's 2026-07-24 ruling). <b>The A5 card shift, executed:</b>
/// OffensiveRebounding/DefensiveRebounding LEFT the size card (the old
/// <c>post_bonus = 8*o</c> + height stamp is RETIRED) and became SPENDABLE, expressed
/// skills carried on latent/current/runway. The 33-key card keeps the SAME key names, so
/// downstream readers (tendency derivation, rebounder pickers, scout rank) keep
/// compiling — but the VALUES became current-expressed skill, which is the S70 page's
/// baseline story. Also gone relative to Pass 2: orientation <c>o</c>, weapon/argmax,
/// age/class placeholders; new: the defensive plane, the offensive role, the budget,
/// concentration, family allocation, body caps, and concave pricing.</para>
///
/// <para><b>Port traps handled (the S43 discipline):</b> every float→int rounds
/// HALF-TO-EVEN via <see cref="RoundHalfEven"/>; every integer site in this oracle is
/// round-THEN-clamp (uniform — no Pass-2-style height exception; Height comes off the
/// inverse CDF as an integer directly); every float SUM whose order matters (the CDF
/// accumulation, sharpen's normalizer, role-odds' normalizer, the Rscore family masses)
/// iterates in the oracle's insertion order, carried here by explicit ordered key
/// arrays.</para>
/// </summary>
public static class PlayerGenPass3
{
    // ========================================================================
    // FROZEN CONSTANTS — transcribed verbatim from the locked oracle.
    // ========================================================================

    public const int    SEED        = 20260724;   // canonical cohort seed (oracle :85)
    public const int    N_CANDIDATE = 46000;
    public const double R_LINE      = 17.0;       // the standing recruiting line (S66)

    // --- D1: the preserved height marginal (oracle :94-105) — heights 40..99 ---
    public static readonly int[] HEIGHT_KEYS = BuildHeightKeys();
    public static readonly Dictionary<string, double> HEIGHT_MARGINAL = new(StringComparer.Ordinal)
    {
        ["40"] = 0.0468696, ["41"] = 0.0067391, ["42"] = 0.0084565, ["43"] = 0.0099130,
        ["44"] = 0.0106304, ["45"] = 0.0120435, ["46"] = 0.0145435, ["47"] = 0.0178478,
        ["48"] = 0.0199783, ["49"] = 0.0240000, ["50"] = 0.0246739, ["51"] = 0.0310652,
        ["52"] = 0.0348478, ["53"] = 0.0383478, ["54"] = 0.0360870, ["55"] = 0.0365435,
        ["56"] = 0.0365217, ["57"] = 0.0360870, ["58"] = 0.0366087, ["59"] = 0.0376304,
        ["60"] = 0.0343478, ["61"] = 0.0351522, ["62"] = 0.0331087, ["63"] = 0.0319565,
        ["64"] = 0.0296957, ["65"] = 0.0289348, ["66"] = 0.0290435, ["67"] = 0.0268696,
        ["68"] = 0.0267826, ["69"] = 0.0263913, ["70"] = 0.0237391, ["71"] = 0.0233043,
        ["72"] = 0.0211304, ["73"] = 0.0190435, ["74"] = 0.0176522, ["75"] = 0.0155435,
        ["76"] = 0.0135217, ["77"] = 0.0111304, ["78"] = 0.0090000, ["79"] = 0.0061739,
        ["80"] = 0.0050870, ["81"] = 0.0038478, ["82"] = 0.0029130, ["83"] = 0.0017826,
        ["84"] = 0.0014130, ["85"] = 0.0012391, ["86"] = 0.0007174, ["87"] = 0.0004565,
        ["88"] = 0.0001739, ["89"] = 0.0001087, ["90"] = 0.0001522, ["91"] = 0.0000870,
        ["92"] = 0.0000217, ["93"] = 0.0000217, ["94"] = 0.0, ["95"] = 0.0000217,
        ["96"] = 0.0, ["97"] = 0.0, ["98"] = 0.0, ["99"] = 0.0,
    };
    private static int[] BuildHeightKeys()
    {
        var ks = new int[60];
        for (var h = 40; h < 100; h++) ks[h - 40] = h;
        return ks;
    }
    // HEIGHT_CDF (oracle :106-112): sequential accumulation in 40..99 order — the SAME
    // summation order as the oracle, so cumulative boundaries land bit-identical; the
    // last entry is forced to exactly 1.0 the way the oracle forces it.
    private static readonly (double Cum, int H)[] HeightCdf = BuildHeightCdf();
    private static (double, int)[] BuildHeightCdf()
    {
        var vals = new double[60];
        for (var i = 0; i < 60; i++) vals[i] = HEIGHT_MARGINAL[HEIGHT_KEYS[i].ToString()];
        var total = NeumaierSum(vals);
        var cdf = new (double, int)[60];
        var acc = 0.0;
        for (var i = 0; i < 60; i++)
        {
            acc += HEIGHT_MARGINAL[HEIGHT_KEYS[i].ToString()] / total;
            cdf[i] = (acc, HEIGHT_KEYS[i]);
        }
        cdf[59] = (1.0, cdf[59].Item2);
        return cdf;
    }

    /// <summary>The oracle's <c>height_from_u</c>: inverse-CDF lookup, boundary rule
    /// <c>u &lt;= cum</c> (a boundary uniform lands in the LOWER bin — the committed
    /// edge table probes every boundary at ±1e-12 so a <c>&lt;</c> port dies loudly).</summary>
    public static int HeightFromU(double u)
    {
        foreach (var (cum, h) in HeightCdf)
            if (u <= cum)
                return h;
        return 99;
    }

    // --- Body machinery (oracle :124-131) — unchanged from the old model ---
    public const double ATH_HEIGHT_CENTER = 60.0;
    public static readonly Dictionary<string, double> SIZE_COEF = new(StringComparer.Ordinal)
    {
        ["Strength"] = +0.42, ["Speed"] = -0.22, ["Quickness"] = -0.22, ["FirstStep"] = -0.22,
        ["Vertical"] = -0.02, ["Endurance"] = -0.06, ["Hustle"] = 0.00,
    };
    public static readonly Dictionary<string, double> ATH_SIGMA = new(StringComparer.Ordinal)
    {
        ["Strength"] = 6.0, ["Speed"] = 7.0, ["Quickness"] = 7.0, ["FirstStep"] = 7.0,
        ["Vertical"] = 8.0, ["Endurance"] = 6.0, ["Hustle"] = 8.0,
    };
    public const double ATH_BASE_LO = 25.0;
    public const double ATH_BASE_HI = 85.0;
    public const double ATHQ_A = 2.2;
    public const double ATHQ_B = 2.2;
    public static readonly string[] ATH_KEYS =
        { "Strength", "Speed", "Quickness", "FirstStep", "Vertical", "Endurance", "Hustle" };

    // --- Arrival / expression (oracle :134-140; D2: the mean's source is the BODY) ---
    public const double ARR_READY = 0.72;
    public const double ARR_RAW   = 0.42;
    public const double ARRB_LO   = 48.0;
    public const double ARRB_HI   = 78.0;
    public const double ARR_SIGMA = 0.18;
    public const double E_MIN     = 0.15;
    public const double EXPR_BASELINE = 14.0;

    // --- FreeThrow derivation (oracle :153-155; S71 ruling 2026-07-24: mirror real-life
    //     FT shooting — median ~70, low-90s tail real, hack-target floor 40). Re-anchored
    //     at the population's own Outside center: the old inline anchor (Outside 50) sat
    //     far above the league's actual Outside median (~32), so any span strong enough
    //     to separate shooters dragged the middle down. FT_OUT_ANCHOR is a NAMED constant
    //     (the old inline 50.0 is retired) and FT_CENTER is legible: it IS what the
    //     median player shoots. FT_OUT_SCALE reaffirmed at 25.0. ---
    public const double FT_CENTER = 71.5;
    public const double FT_OUT_ANCHOR = 36.0;
    public const double FT_OUT_SPAN = 9.0;
    public const double FT_OUT_SCALE = 25.0;
    public const double FT_HEIGHT_COEF = 9.0;
    public const double FT_MIN = 40.0;
    public const double FT_MAX = 96.0;
    public const double FT_SIGMA = 6.0;

    // --- The seven families (oracle :150-163; Rebounding SPENDABLE per S67 ruling 2) ---
    public static readonly string[] FAMILY_ORDER =
        { "Shooting", "InteriorOffense", "Creation", "PerimDefense", "InteriorDefense", "Rebounding", "Glue" };
    public static readonly Dictionary<string, string[]> FAMILIES = new(StringComparer.Ordinal)
    {
        ["Shooting"]        = new[] { "Outside", "Mid", "OffBallMovement" },
        ["InteriorOffense"] = new[] { "Close", "Finishing", "PostMoves", "Screening" },
        ["Creation"]        = new[] { "BallHandling", "Passing", "Playmaking", "SelfCreation", "FoulDrawing" },
        ["PerimDefense"]    = new[] { "PerimeterDefense", "Steals", "OffBallDefense" },
        ["InteriorDefense"] = new[] { "PostDefense", "RimProtection" },
        ["Rebounding"]      = new[] { "OffensiveRebounding", "DefensiveRebounding" },
        ["Glue"]            = new[] { "BasketballIQ", "Discipline", "HelpDefense" },
    };
    public static readonly string[] SPEND_SKILLS = BuildSpendSkills();   // 22, family order
    private static string[] BuildSpendSkills()
    {
        var ks = new List<string>(22);
        foreach (var f in FAMILY_ORDER) ks.AddRange(FAMILIES[f]);
        return ks.ToArray();
    }

    // --- The 33-key card contract (S70) — the exact key set GenMapToPlayer consumes.
    // FROZEN VERBATIM, deliberately not derived from the families above: BuildCard
    // assembles the card FROM the live families and asserts it against THIS list, so
    // a FAMILIES/ATH_KEYS edit that drifts the card off the consumer's contract throws
    // at generation time instead of stamping meaning nowhere. Matches Pass 2's ALL_KEYS
    // key-for-key (set-verified S70); frozen here so retiring Pass 2 breaks nothing.
    public static readonly string[] CARD_KEYS =
    {
        "Height", "Wingspan", "Weight",
        "Strength", "Speed", "Quickness", "FirstStep", "Vertical", "Endurance", "Hustle",
        "Close", "Mid", "Outside", "Finishing", "FreeThrow", "FoulDrawing",
        "BallHandling", "Passing", "Playmaking", "SelfCreation", "PostMoves",
        "OffBallMovement", "Screening", "OffensiveRebounding", "DefensiveRebounding",
        "PerimeterDefense", "PostDefense", "RimProtection", "Steals",
        "HelpDefense", "OffBallDefense", "BasketballIQ", "Discipline",
    };
    public static readonly string[] PERIM_FAMS = { "Shooting", "Creation", "PerimDefense" };
    public static readonly string[] POST_FAMS  = { "InteriorOffense", "InteriorDefense", "Rebounding" };

    // --- Plane 1: defensive position (oracle :173-181) ---
    public const double DEF_MID = 62.0;
    public const double DEF_STEEP = 9.0;
    public const double DEF_NOISE = 0.10;

    // --- Plane 2: offensive role, size-sliding asymmetric odds (oracle :185-216) ---
    public static readonly string[] ROLES = { "Creator", "Shooter", "Slasher", "PostScorer", "Connector" };
    public static readonly Dictionary<string, double> ROLE_PRIOR = new(StringComparer.Ordinal)
    {
        ["Creator"] = 0.24, ["Shooter"] = 0.24, ["Slasher"] = 0.20, ["PostScorer"] = 0.18, ["Connector"] = 0.14,
    };
    public const double HFRAC_LO = 44.0;
    public const double HFRAC_HI = 84.0;
    public const double PERIM_DECAY = 4.6;   // steep: the oversized perimeter identity is rare
    public const double POST_DECAY  = 2.1;   // gentle: the undersized post identity is uncommon, not rare

    // --- The budget (oracle :220-230) ---
    public const double TALENT_A = 2.3;
    public const double TALENT_B = 2.7;
    public const double BUDGET_LO = 260.0;
    public const double BUDGET_SPAN = 620.0;
    public const double BUDGET_POW = 1.35;
    public const double CONC_A = 2.0;
    public const double CONC_B = 2.0;

    // --- Pulls (oracle :234-261) ---
    public const double PULL_EPS = 0.010;
    public const double PULL_DICE_SIGMA = 0.50;
    public static readonly Dictionary<string, Dictionary<string, double>> ROLE_FAM_PREF = new(StringComparer.Ordinal)
    {
        ["Creator"]    = new(StringComparer.Ordinal) { ["Shooting"] = 0.85, ["InteriorOffense"] = 0.30, ["Creation"] = 1.60 },
        ["Shooter"]    = new(StringComparer.Ordinal) { ["Shooting"] = 1.75, ["InteriorOffense"] = 0.30, ["Creation"] = 0.55 },
        ["Slasher"]    = new(StringComparer.Ordinal) { ["Shooting"] = 0.72, ["InteriorOffense"] = 1.30, ["Creation"] = 0.85 },
        ["PostScorer"] = new(StringComparer.Ordinal) { ["Shooting"] = 0.42, ["InteriorOffense"] = 1.90, ["Creation"] = 0.30 },
        ["Connector"]  = new(StringComparer.Ordinal) { ["Shooting"] = 0.80, ["InteriorOffense"] = 0.80, ["Creation"] = 0.80 },
    };
    public const double GLUE_PREF = 0.32;

    // --- Within-family member preferences (oracle :269-280) ---
    public static readonly Dictionary<string, Dictionary<string, double>> WITHIN_PREF = new(StringComparer.Ordinal)
    {
        ["Creator"]    = new(StringComparer.Ordinal) { ["BallHandling"] = 1.5, ["Playmaking"] = 1.5, ["Passing"] = 1.3,
                                                       ["SelfCreation"] = 1.2, ["Outside"] = 1.3, ["PostMoves"] = 0.5, ["Screening"] = 0.5 },
        ["Shooter"]    = new(StringComparer.Ordinal) { ["Outside"] = 1.55, ["OffBallMovement"] = 1.4, ["Mid"] = 0.9,
                                                       ["SelfCreation"] = 0.7, ["PostMoves"] = 0.4, ["Screening"] = 0.5 },
        ["Slasher"]    = new(StringComparer.Ordinal) { ["Finishing"] = 1.7, ["Close"] = 1.3, ["FoulDrawing"] = 1.4,
                                                       ["SelfCreation"] = 1.3, ["BallHandling"] = 1.1, ["Outside"] = 0.7, ["PostMoves"] = 0.5 },
        ["PostScorer"] = new(StringComparer.Ordinal) { ["PostMoves"] = 1.8, ["Close"] = 1.5, ["Finishing"] = 1.4,
                                                       ["Screening"] = 1.1, ["BallHandling"] = 0.5, ["SelfCreation"] = 0.6, ["Outside"] = 0.6 },
        ["Connector"]  = new(StringComparer.Ordinal) { },
    };
    public const double WITHIN_DICE_SIGMA = 0.50;

    // --- Concentration -> sharpening (oracle :285-291) ---
    public const double GAMMA_LO = 0.75;
    public const double GAMMA_HI = 3.2;

    // --- Body caps + pricing (oracle :295-317) ---
    public const double FLAT_BASE = 8.0;
    public const double BASE_JITTER = 2.2;
    public const double IDCAP_LO_H = 46.0;
    public const double IDCAP_HI_H = 74.0;
    public const double IDCAP_MIN = 34.0;
    public const double REBCAP_MIN = 52.0;
    public const double PRICE_TAU = 52.0;

    // --- Rscore (oracle :409-413; D3 realized-card weights) ---
    public const double HF_LO = 0.20;
    public const double HF_HI = 1.45;
    public const double HF_RANGE = 1.25;
    public const double HF_STEEP = 0.13;
    public const double HF_MID = 59.0;
    public const double LOW_TAPER_FLOOR = 0.10;
    public const double LOW_TAPER_TOP = 51.0;
    public const double PATHWAY_W_FLOOR = 0.55;

    // --- Live draw shapes hardcoded at the oracle's draw sites, named here ---
    public const double WS_NOISE_MEAN = 4.0;      // mean INCLUDED in the drawn value
    public const double WS_NOISE_SIGMA = 3.0;
    public const double WEIGHT_NOISE_SIGMA = 6.0;

    // ========================================================================
    // Primitive helpers
    // ========================================================================

    /// <summary>The oracle's <c>clamp</c> (oracle :319-320), mirrored verbatim.</summary>
    public static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);

    /// <summary><b>S69 PORT FINDING:</b> CPython 3.12 changed the builtin <c>sum()</c>
    /// to NEUMAIER COMPENSATED SUMMATION (gh-100425) — a naive left fold in C# lands one
    /// ulp off the oracle on some inputs, which is invisible at 1e-9 everywhere EXCEPT
    /// where a sum feeds an EXACT boundary (the height-CDF total made <c>cum(95)</c>
    /// straddle 1.0 and flipped <c>HeightFromU(1.0)</c> from 99 to 95 against the edge
    /// table). Every Python <c>sum()</c>-over-floats site in the oracle therefore maps
    /// to THIS function, never to <c>+=</c>: the CDF total, the role-odds normalizer,
    /// sharpen's normalizer, and the Rscore family masses. Explicit `+=` loops in the
    /// oracle (the CDF accumulation, draw_role's cumulative scan) stay naive folds.</summary>
    public static double NeumaierSum(IReadOnlyList<double> xs)
    {
        var total = 0.0;
        var comp = 0.0;
        for (var i = 0; i < xs.Count; i++)
        {
            var x = xs[i];
            var t = total + x;
            if (Math.Abs(total) >= Math.Abs(x)) comp += (total - t) + x;
            else comp += (x - t) + total;
            total = t;
        }
        return total + comp;
    }

    /// <summary>Python's <c>round()</c>: HALF-TO-EVEN. The ONLY legal float→int rounding
    /// in this port. Every integer site in this oracle is round-THEN-clamp.</summary>
    public static double RoundHalfEven(double x) => Math.Round(x, MidpointRounding.ToEven);

    /// <summary>The oracle's <c>sharpen</c> (oracle :288-291): power-transform then
    /// normalize. Iterates and SUMS in the caller-supplied key order — Python 3.7+ dict
    /// insertion order — so the float normalizer accumulates in the oracle's order.</summary>
    public static Dictionary<string, double> Sharpen(
        IReadOnlyList<string> keys, Dictionary<string, double> weights, double gamma)
    {
        var powed = new Dictionary<string, double>(StringComparer.Ordinal);
        var vals = new double[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            powed[keys[i]] = Math.Pow(weights[keys[i]], gamma);
            vals[i] = powed[keys[i]];
        }
        var tot = NeumaierSum(vals);   // Python builtin sum() — see the NeumaierSum note
        var share = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var k in keys) share[k] = powed[k] / tot;
        return share;
    }

    /// <summary>The oracle's <c>body_cap</c> (oracle :304-311): interior-defense skills
    /// hard-capped low on small bodies; rebounding craft milder; everything else open
    /// (the 7'0" pure shooter is legal).</summary>
    public static double BodyCap(string skill, int height)
    {
        if (skill == "PostDefense" || skill == "RimProtection")
        {
            var t = Clamp((height - IDCAP_LO_H) / (IDCAP_HI_H - IDCAP_LO_H), 0.0, 1.0);
            return IDCAP_MIN + (99.0 - IDCAP_MIN) * t;
        }
        if (skill == "OffensiveRebounding" || skill == "DefensiveRebounding")
        {
            var t = Clamp((height - IDCAP_LO_H) / (IDCAP_HI_H - IDCAP_LO_H), 0.0, 1.0);
            return REBCAP_MIN + (99.0 - REBCAP_MIN) * t;
        }
        return 99.0;
    }

    /// <summary>The oracle's <c>price</c> (oracle :315-317): the ONE concave curve, same
    /// for every skill and player — ratings SATURATE toward the cap and never seek it.</summary>
    public static double Price(double spend, double baseVal, double cap)
        => baseVal + (cap - baseVal) * (1.0 - Math.Exp(-spend / PRICE_TAU));

    /// <summary>FreeThrow derivation (oracle <c>derive_ft</c>, :335-338): tanh on Outside
    /// about the NAMED population anchor (S71), height penalty, plus the player's ONE
    /// persistent idiosyncrasy. Round-then-clamp.</summary>
    public static int DeriveFt(int outside, double ftIdio, int height)
    {
        var val = FT_CENTER + FT_OUT_SPAN * Math.Tanh((outside - FT_OUT_ANCHOR) / FT_OUT_SCALE)
                - FT_HEIGHT_COEF * ((height - 55.0) / 40.0) + ftIdio;
        return (int)Clamp(RoundHalfEven(val), FT_MIN, FT_MAX);
    }

    /// <summary>The role-odds transform (oracle <c>role_odds</c>, :196-208): size-sliding
    /// ASYMMETRIC multipliers on the prior, normalized in ROLES order.</summary>
    public static Dictionary<string, double> RoleOdds(int height)
    {
        var hf = Clamp((height - HFRAC_LO) / (HFRAC_HI - HFRAC_LO), 0.0, 1.0);
        var mult = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Creator"]    = Math.Exp(-PERIM_DECAY * hf),
            ["Shooter"]    = Math.Exp(-0.55 * PERIM_DECAY * hf),
            ["Slasher"]    = Math.Exp(-1.4 * Math.Abs(hf - 0.42)),
            ["PostScorer"] = Math.Exp(-POST_DECAY * (1.0 - hf)),
            ["Connector"]  = 1.0,
        };
        var w = new Dictionary<string, double>(StringComparer.Ordinal);
        var vals = new double[ROLES.Length];
        for (var i = 0; i < ROLES.Length; i++)
        {
            w[ROLES[i]] = ROLE_PRIOR[ROLES[i]] * mult[ROLES[i]];
            vals[i] = w[ROLES[i]];
        }
        var tot = NeumaierSum(vals);   // Python builtin sum() — see the NeumaierSum note
        var odds = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var ro in ROLES) odds[ro] = w[ro] / tot;
        return odds;
    }

    /// <summary>The deterministic tail of <c>draw_role</c> (oracle :209-216): cumulative
    /// scan of the odds in ROLES order, boundary rule <c>u &lt;= acc</c>, fallback last.</summary>
    public static string RoleFromU(double u, int height)
    {
        var odds = RoleOdds(height);
        var acc = 0.0;
        foreach (var ro in ROLES)
        {
            acc += odds[ro];
            if (u <= acc)
                return ro;
        }
        return ROLES[^1];
    }

    /// <summary>The deterministic tail of <c>draw_def_plane</c> (oracle :177-183):
    /// logistic in Height plus the recorded modest noise, clamped; category thresholds
    /// at 0.35 / 0.65.</summary>
    public static (double DPlane, string DCat) DefPlaneFromNoise(int height, double defNoise)
    {
        var baseV = 1.0 / (1.0 + Math.Exp(-(height - DEF_MID) / DEF_STEEP));
        var d = Clamp(baseV + defNoise, 0.0, 1.0);
        var cat = d < 0.35 ? "PerimD" : (d < 0.65 ? "WingD" : "PostD");
        return (d, cat);
    }

    /// <summary>The deterministic tail of <c>draw_budget</c> (oracle :227-231): the
    /// talent draw maps to nominal ceiling points, top-heavier than linear.</summary>
    public static double BudgetFromQ(double q) => BUDGET_LO + Math.Pow(q, BUDGET_POW) * BUDGET_SPAN;

    /// <summary>The pull-preference table of <c>family_pulls</c> (oracle :249-262):
    /// role preference x body factor, per family, BEFORE the dice — the defensive PLANE
    /// carries the defense split, the body leans rebounding, and small bodies lean
    /// handle/shoot (the S68 "no 5'9\" elite defender who can't dribble" ruling).</summary>
    public static Dictionary<string, double> FamilyPullPrefs(string role, int height, double dplane)
    {
        var hf = Clamp((height - HFRAC_LO) / (HFRAC_HI - HFRAC_LO), 0.0, 1.0);
        var pref = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var kv in ROLE_FAM_PREF[role]) pref[kv.Key] = kv.Value;
        pref["PerimDefense"]    = 0.30 + 0.85 * (1.0 - dplane);
        pref["InteriorDefense"] = 0.30 + 0.85 * dplane;
        pref["Rebounding"]      = 0.22 + 0.80 * hf;
        // small-body handle/shoot pull lean — the .get(k, 0.8) default mirrors the oracle
        // but is unreachable (every role names Creation and Shooting in ROLE_FAM_PREF).
        pref["Creation"] = (pref.TryGetValue("Creation", out var cr) ? cr : 0.8) * (1.0 + 0.45 * (1.0 - hf));
        pref["Shooting"] = (pref.TryGetValue("Shooting", out var sh) ? sh : 0.8) * (1.0 + 0.25 * (1.0 - hf));
        pref["Glue"] = GLUE_PREF;
        return pref;
    }

    /// <summary>Arrival mean from the BODY (oracle :377-379, the D2 ruling): small
    /// arrives ready, big arrives raw. Shared home so the live drawer (which needs the
    /// mean BEFORE drawing) and any reader use the exact same expressions.</summary>
    public static double ComputeArrivalMean(int height)
    {
        var hb = Clamp((height - ARRB_LO) / (ARRB_HI - ARRB_LO), 0.0, 1.0);
        return ARR_READY - hb * (ARR_READY - ARR_RAW);
    }

    // ========================================================================
    // THE TRANSFORMS — one player from recorded draws (oracle generate_player,
    // restructured only in that every draw arrives as a parameter)
    // ========================================================================

    /// <summary>Rebuild one player's full deterministic state from raw draws — the exact
    /// factoring of the oracle's <c>generate_player(r)</c> with the RNG removed, and the
    /// executable twin of <c>tools/gen_pass3_replay_check.py</c>'s replay.</summary>
    public static Pass3Result BuildFromDraws(Pass3Draws d)
    {
        // ---- 1. BODY FIRST (D1): height from the preserved marginal ----
        var height = HeightFromU(d.HeightU);
        var wingspan = (int)Clamp(RoundHalfEven(height + d.WsNoise), 40.0, 99.0);
        var athCenter = ATH_BASE_LO + d.A * (ATH_BASE_HI - ATH_BASE_LO);
        var ath = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in ATH_KEYS)
        {
            var val = athCenter + SIZE_COEF[k] * (height - ATH_HEIGHT_CENTER) + d.AthNoise[k];
            ath[k] = (int)Clamp(RoundHalfEven(val), 8.0, 99.0);
        }
        var weight = (int)Clamp(RoundHalfEven(30 + 0.40 * height + 0.30 * ath["Strength"] + d.WeightNoise), 20.0, 99.0);

        // ---- 2. THE TWO PLANES (role drawn FROM the body; height biases, never dictates) ----
        var (dplane, dcat) = DefPlaneFromNoise(height, d.DefNoise);
        var role = RoleFromU(d.RoleU, height);

        // ---- 3. THE BUDGET (talent) + concentration (independent dice) ----
        var budget = BudgetFromQ(d.Q);
        var gamma = GAMMA_LO + d.C * (GAMMA_HI - GAMMA_LO);

        // ---- 4. PULLS -> family-first allocation -> within-family second stage ----
        var pref = FamilyPullPrefs(role, height, dplane);
        var pulls = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var fam in FAMILY_ORDER)
        {
            var dice = Math.Exp(d.PullGauss[fam]);
            pulls[fam] = Math.Max(PULL_EPS, pref[fam] * dice);
        }
        var famShare = Sharpen(FAMILY_ORDER, pulls, gamma);
        var famBudget = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in FAMILY_ORDER) famBudget[f] = budget * famShare[f];

        var spend = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var fam in FAMILY_ORDER)
        {
            var members = FAMILIES[fam];
            var wp = WITHIN_PREF[role];
            var mw = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var k in members)
            {
                var dice = Math.Exp(d.WithinGauss[k]);
                mw[k] = Math.Max(PULL_EPS, (wp.TryGetValue(k, out var w) ? w : 1.0) * dice);
            }
            var mshare = Sharpen(members, mw, Math.Max(0.75, 0.55 * gamma + 0.35));
            foreach (var k in members)
                spend[k] = famBudget[fam] * mshare[k];
        }

        // ---- 5. PRICING -> the LATENT card (ceilings; concave, cap-saturating) ----
        var latent = new Dictionary<string, int>(StringComparer.Ordinal);
        var caps = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var k in SPEND_SKILLS)
        {
            var baseVal = Clamp(FLAT_BASE + Math.Abs(d.BaseJitterGauss[k]), 8.0, 16.0);
            var cap = BodyCap(k, height);
            caps[k] = cap;
            latent[k] = (int)Clamp(RoundHalfEven(Price(spend[k], baseVal, cap)), 8.0, 99.0);
        }

        // ---- 6. ARRIVAL (D2: mean follows the BODY; dice on top) + expression ----
        var arrival = Clamp(d.ArrivalRaw, 0.0, 1.0);
        var e = E_MIN + arrival * (1.0 - E_MIN);
        var current = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in SPEND_SKILLS)
        {
            var latentK = latent[k];
            current[k] = latentK <= EXPR_BASELINE
                ? latentK
                : (int)RoundHalfEven(EXPR_BASELINE + e * (latentK - EXPR_BASELINE));
        }

        // ---- 7. FreeThrow (derived; the ONE persistent idiosyncrasy feeds both) ----
        var latentFt = DeriveFt(latent["Outside"], d.FtIdio, height);
        var currentFt = DeriveFt(current["Outside"], d.FtIdio, height);

        var runway = new Dictionary<string, int>(StringComparer.Ordinal);
        var runwayTotal = 0;
        foreach (var k in SPEND_SKILLS)
        {
            runway[k] = latent[k] - current[k];
            runwayTotal += runway[k];
        }
        runway["FreeThrow"] = latentFt - currentFt;
        runwayTotal += runway["FreeThrow"];

        // ---- Rscore, from the realized card (D3: label-free by construction) ----
        var (rscore, which, parts) = ComputeRscoreParts(current, ath, height);

        var result = new Pass3Result
        {
            Height = height, Wingspan = wingspan, Weight = weight, Ath = ath,
            DPlane = dplane, DCat = dcat, Role = role,
            Q = d.Q, Budget = budget, Conc = d.C, Gamma = gamma,
            Pulls = pulls, FamShare = famShare, Spend = spend, Caps = caps,
            Latent = latent, Current = current,
            LatentFt = latentFt, CurrentFt = currentFt,
            Arrival = arrival, E = e,
            Runway = runway, RunwayTotal = runwayTotal,
            Rscore = rscore, RscoreWhich = which, RscoreParts = parts,
        };
        result.Card = BuildCard(result);   // S70: the ONE assembly site (see BuildCard)
        return result;
    }

    /// <summary>S70 — the canonical 33-key CURRENT card, the shape the bridge feeds
    /// <c>GenMapToPlayer</c>: 3 size + the 7 <see cref="ATH_KEYS"/> + the 22
    /// <see cref="SPEND_SKILLS"/> + <c>CurrentFt</c> under <c>FreeThrow</c>. The ONLY
    /// assembly site — any fixture or diagnostic that needs the card calls this, so a
    /// second drifting assembly cannot exist. Asserts the exact key SET against the
    /// frozen <see cref="CARD_KEYS"/> contract (33 wrong keys is still 33 keys).
    /// Pure — consumes no RNG; prefix stability (A4) untouched.</summary>
    public static Dictionary<string, int> BuildCard(Pass3Result r)
    {
        var card = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Height"] = r.Height, ["Wingspan"] = r.Wingspan, ["Weight"] = r.Weight,
        };
        foreach (var k in ATH_KEYS) card[k] = r.Ath[k];
        foreach (var k in SPEND_SKILLS) card[k] = r.Current[k];
        card["FreeThrow"] = r.CurrentFt;

        // Exact key-SET assertion against the frozen contract, both directions:
        // count equality + every contract key present == set equality (extras included).
        if (card.Count != CARD_KEYS.Length)
            throw new InvalidOperationException(
                $"Pass-3 card contract broken: {card.Count} keys, expected {CARD_KEYS.Length}.");
        foreach (var k in CARD_KEYS)
            if (!card.ContainsKey(k))
                throw new InvalidOperationException(
                    $"Pass-3 card contract broken: expected key '{k}' missing.");
        return card;
    }

    /// <summary>The realized-card family mass (oracle <c>family_mass</c>, :415-416):
    /// current skill over 20, summed family order then member order.</summary>
    public static double FamilyMass(Dictionary<string, int> cur, string[] fams)
    {
        var vals = new List<double>(10);
        foreach (var f in fams)
            foreach (var k in FAMILIES[f])
                vals.Add(Math.Max(0.0, cur[k] - 20.0));
        return NeumaierSum(vals);   // Python builtin sum() — see the NeumaierSum note
    }

    /// <summary>The recruiting line (oracle <c>rscore_parts</c>, :418-453, D3 re-derived):
    /// pathway WEIGHTS from the REALIZED card's family allocation — never a stored label —
    /// so flipping role/plane labels moves Rscore by EXACTLY ZERO (label-freedom holds BY
    /// CONSTRUCTION here: role and plane are not even parameters). Three pathways: the
    /// old perimeter and post pathways, plus the S68 DEFENSE pathway — elite defense is
    /// a ticket in; interior rides the post pathway, the perimeter stopper is gated by
    /// size + athleticism + a barely-viable handle/shot. Transcribed operand-for-operand.</summary>
    public static (double Rscore, string Which, Dictionary<string, double> Parts) ComputeRscoreParts(
        Dictionary<string, int> c, Dictionary<string, int> ath, int height)
    {
        var athAvg = (ath["Strength"] + ath["Speed"] + ath["Quickness"] + ath["FirstStep"] + ath["Vertical"]) / 5.0;
        // pathway weights from the realized allocation on the card itself
        var pm = FamilyMass(c, PERIM_FAMS);
        var qm = FamilyMass(c, POST_FAMS);
        var tilt = (pm + qm) > 0 ? pm / (pm + qm) : 0.5;
        var perimW = PATHWAY_W_FLOOR + (1.0 - PATHWAY_W_FLOOR) * tilt;
        var postW  = PATHWAY_W_FLOOR + (1.0 - PATHWAY_W_FLOOR) * (1.0 - tilt);
        // PERIMETER pathway (structure unchanged from the old locked oracle)
        var access       = Math.Max(c["BallHandling"], Math.Max(c["OffBallMovement"], c["Outside"])) / 99.0;
        var midEff       = c["Mid"] * Math.Min(1.0, access / 0.45);
        var entryP       = Math.Max(Math.Max((double)c["Outside"], c["BallHandling"]), midEff);
        var perimSupport = (c["Passing"] + c["Playmaking"] + c["SelfCreation"] + c["OffBallMovement"]) / 4.0;
        var perimDef     = (double)Math.Max(c["PerimeterDefense"], Math.Max(c["Steals"], c["OffBallDefense"]));
        var perimVal = Math.Max(0.0, entryP - 20) * (0.55 + 0.30 * perimSupport / 99 + 0.15 * perimDef / 99) + 0.14 * athAvg;
        // POST pathway; the rebounding read comes from the FAMILY on the skill card (D3)
        var postSkill    = (double)Math.Max(c["RimProtection"],
                               Math.Max(c["PostMoves"], Math.Max(c["Close"], Math.Max(c["Finishing"], c["PostDefense"]))));
        var postSupport  = (c["Screening"] + c["PostDefense"] + c["RimProtection"]) / 3.0;
        var heightFactor = Clamp(HF_LO + HF_RANGE / (1.0 + Math.Exp(-HF_STEEP * (height - HF_MID))), HF_LO, HF_HI);
        var skillVal     = Math.Max(0.0, postSkill - 24) * (0.60 + 0.40 * postSupport / 99) * heightFactor;
        var glass        = (c["OffensiveRebounding"] + c["DefensiveRebounding"]) / 2.0;
        var rebVal       = glass * 0.16 * Math.Min(1.0, postSkill / 45.0);
        var lowTaper     = Clamp((height - 40.0) / (LOW_TAPER_TOP - 40.0), LOW_TAPER_FLOOR, 1.0);
        var postVal = (skillVal + rebVal + 0.10 * athAvg * Math.Min(1.0, heightFactor)) * lowTaper;
        // DEFENSE pathway (S68 Emmett ruling)
        var stopSkill = (double)Math.Max(c["PerimeterDefense"], Math.Max(c["Steals"], c["OffBallDefense"]));
        var sizeGate  = Clamp((height - 51.0) / 12.0, 0.0, 1.0);
        var athGate   = Clamp((athAvg - 45.0) / 25.0, 0.0, 1.0);
        var viabGate  = Clamp((Math.Max(c["BallHandling"], c["Outside"]) - 15.0) / 20.0, 0.0, 1.0);
        var defVal    = Math.Max(0.0, stopSkill - 30.0) * 0.55 * sizeGate * athGate * viabGate + 0.10 * athAvg * sizeGate;
        double wperim = perimW * perimVal, wpost = postW * postVal;
        var best = Math.Max(Math.Max(wperim, wpost), defVal);
        var which = best == wperim ? "perim" : (best == wpost ? "post" : "defense");
        var parts = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["rscore"] = best, ["tilt"] = tilt,
            ["perim_val"] = perimVal, ["post_val"] = postVal, ["def_val"] = defVal,
            ["wperim"] = wperim, ["wpost"] = wpost, ["ath"] = athAvg,
        };
        return (best, which, parts);
    }

    // ========================================================================
    // Constants echo — the tripwire surface. Phase 69 asserts the fixture's echo
    // equals this map BEFORE running any replay; the oracle source stays the
    // single canonical home of the values.
    // ========================================================================
    public static Dictionary<string, object> ConstantsEcho() => new(StringComparer.Ordinal)
    {
        ["SEED"] = (double)SEED, ["N_CANDIDATE"] = (double)N_CANDIDATE, ["R_LINE"] = R_LINE,
        ["HEIGHT_MARGINAL"] = HEIGHT_MARGINAL,
        ["ATH_HEIGHT_CENTER"] = ATH_HEIGHT_CENTER, ["SIZE_COEF"] = SIZE_COEF, ["ATH_SIGMA"] = ATH_SIGMA,
        ["ATH_BASE_LO"] = ATH_BASE_LO, ["ATH_BASE_HI"] = ATH_BASE_HI,
        ["ATHQ_A"] = ATHQ_A, ["ATHQ_B"] = ATHQ_B,
        ["ARR_READY"] = ARR_READY, ["ARR_RAW"] = ARR_RAW, ["ARRB_LO"] = ARRB_LO, ["ARRB_HI"] = ARRB_HI,
        ["ARR_SIGMA"] = ARR_SIGMA, ["E_MIN"] = E_MIN, ["EXPR_BASELINE"] = EXPR_BASELINE,
        ["FT_CENTER"] = FT_CENTER, ["FT_OUT_ANCHOR"] = FT_OUT_ANCHOR, ["FT_OUT_SPAN"] = FT_OUT_SPAN,
        ["FT_OUT_SCALE"] = FT_OUT_SCALE,
        ["FT_HEIGHT_COEF"] = FT_HEIGHT_COEF, ["FT_MIN"] = FT_MIN, ["FT_MAX"] = FT_MAX, ["FT_SIGMA"] = FT_SIGMA,
        ["FAMILIES"] = FAMILIES, ["FAMILY_ORDER"] = FAMILY_ORDER, ["SPEND_SKILLS"] = SPEND_SKILLS,
        ["ATH_KEYS"] = ATH_KEYS, ["ROLES"] = ROLES,
        ["DEF_MID"] = DEF_MID, ["DEF_STEEP"] = DEF_STEEP, ["DEF_NOISE"] = DEF_NOISE,
        ["ROLE_PRIOR"] = ROLE_PRIOR, ["HFRAC_LO"] = HFRAC_LO, ["HFRAC_HI"] = HFRAC_HI,
        ["PERIM_DECAY"] = PERIM_DECAY, ["POST_DECAY"] = POST_DECAY,
        ["TALENT_A"] = TALENT_A, ["TALENT_B"] = TALENT_B,
        ["BUDGET_LO"] = BUDGET_LO, ["BUDGET_SPAN"] = BUDGET_SPAN, ["BUDGET_POW"] = BUDGET_POW,
        ["CONC_A"] = CONC_A, ["CONC_B"] = CONC_B,
        ["PULL_EPS"] = PULL_EPS, ["PULL_DICE_SIGMA"] = PULL_DICE_SIGMA,
        ["ROLE_FAM_PREF"] = ROLE_FAM_PREF, ["GLUE_PREF"] = GLUE_PREF,
        ["WITHIN_PREF"] = WITHIN_PREF, ["WITHIN_DICE_SIGMA"] = WITHIN_DICE_SIGMA,
        ["GAMMA_LO"] = GAMMA_LO, ["GAMMA_HI"] = GAMMA_HI,
        ["FLAT_BASE"] = FLAT_BASE, ["BASE_JITTER"] = BASE_JITTER,
        ["IDCAP_LO_H"] = IDCAP_LO_H, ["IDCAP_HI_H"] = IDCAP_HI_H,
        ["IDCAP_MIN"] = IDCAP_MIN, ["REBCAP_MIN"] = REBCAP_MIN, ["PRICE_TAU"] = PRICE_TAU,
        ["HF_LO"] = HF_LO, ["HF_HI"] = HF_HI, ["HF_RANGE"] = HF_RANGE,
        ["HF_STEEP"] = HF_STEEP, ["HF_MID"] = HF_MID,
        ["LOW_TAPER_FLOOR"] = LOW_TAPER_FLOOR, ["LOW_TAPER_TOP"] = LOW_TAPER_TOP,
        ["PATHWAY_W_FLOOR"] = PATHWAY_W_FLOOR,
        ["WS_NOISE_MEAN"] = WS_NOISE_MEAN, ["WS_NOISE_SIGMA"] = WS_NOISE_SIGMA,
        ["WEIGHT_NOISE_SIGMA"] = WEIGHT_NOISE_SIGMA,
    };
}

/// <summary>The raw recorded draws for one player — the fixture's <c>draws</c> block
/// (68 semantic slots; the contract's single home is the oracle's <c>_flat_draws</c>).
/// The live generator fills this same shape from <see cref="IRng"/>.</summary>
public sealed class Pass3Draws
{
    public double HeightU;         // height uniform (inverse-CDF input)
    public double WsNoise;         // mean 4.0 INCLUDED in the drawn value
    public double A;               // athletic-quality beta
    public Dictionary<string, double> AthNoise = new(StringComparer.Ordinal);          // per ATH_KEYS
    public double WeightNoise;
    public double DefNoise;        // def-plane gauss
    public double RoleU;           // role uniform
    public double Q;               // talent beta
    public double C;               // concentration beta
    public Dictionary<string, double> PullGauss = new(StringComparer.Ordinal);         // per family, pre-exp
    public Dictionary<string, double> WithinGauss = new(StringComparer.Ordinal);       // per member, pre-exp
    public Dictionary<string, double> BaseJitterGauss = new(StringComparer.Ordinal);   // per SPEND_SKILLS, pre-abs
    public double ArrivalRaw;      // pre-clamp (mean included in the drawn value)
    public double FtIdio;          // the ONE shared FT idiosyncrasy
}

/// <summary>Every checkpoint of one generated player — the shape the fixture records.
/// Deliberately NOT <see cref="Player"/>: the bridge-swap session owns the mapping onto
/// the Player card (the S44 dormant seats).</summary>
public sealed class Pass3Result
{
    public int Height, Wingspan, Weight;
    public Dictionary<string, int> Ath = new(StringComparer.Ordinal);
    public double DPlane;
    public string DCat = "", Role = "";
    public double Q, Budget, Conc, Gamma;
    public Dictionary<string, double> Pulls = new(StringComparer.Ordinal);
    public Dictionary<string, double> FamShare = new(StringComparer.Ordinal);
    public Dictionary<string, double> Spend = new(StringComparer.Ordinal);
    public Dictionary<string, double> Caps = new(StringComparer.Ordinal);
    public Dictionary<string, int> Latent = new(StringComparer.Ordinal);
    public Dictionary<string, int> Current = new(StringComparer.Ordinal);
    public Dictionary<string, int> Card = new(StringComparer.Ordinal);   // S70: BuildCard's output — the 33-key GenMapToPlayer shape
    public int LatentFt, CurrentFt;
    public double Arrival, E;
    public Dictionary<string, int> Runway = new(StringComparer.Ordinal);
    public int RunwayTotal;
    public double Rscore;
    public string RscoreWhich = "";
    public Dictionary<string, double> RscoreParts = new(StringComparer.Ordinal);
}
