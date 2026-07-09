namespace Charm.Engine;

/// <summary>
/// C# port, Phase 1 (S43): the DETERMINISTIC MATH of the locked Pass-2 skill-first
/// player-generation oracle (<c>tools/gen_pass2_skillfirst_oracle.py</c>, LOCKED SPEC,
/// S42.1). Pure functions only — every transform takes its drawn value(s) as EXPLICIT
/// inputs and draws nothing. No RNG, no state, no <see cref="Player"/> — the parity
/// gate (Phase 59) compares against the plain result object below, and the Phase-2
/// live generator will draw from <see cref="IRng"/> and call THESE SAME functions, so
/// the math proven by fixture replay is the math that ships.
///
/// <para><b>The oracle wins.</b> Every constant and every expression here is a verbatim
/// transcription (oracle names preserved; oracle line cited per site). The committed
/// replay fixture <c>tools/gen_pass2_replay_fixture_s42_2.json</c> carries a constants
/// echo that the parity gate asserts against <see cref="ConstantsEcho"/> BEFORE any
/// replay runs — silent transcription drift is impossible to miss.</para>
///
/// <para><b>The three port traps, handled once here (S43 adversarial preamble):</b>
/// (A1) every float→int rounds HALF-TO-EVEN via <see cref="RoundHalfEven"/> — Python's
/// <c>round()</c> and <c>Math.Round(x, MidpointRounding.ToEven)</c> agree; never
/// <c>(int)(x+0.5)</c>. (A2) round/clamp ORDER is per-site: Height is round-AFTER-clamp
/// (oracle :311); every other integer site is clamp-AFTER-round. Each site mirrors its
/// oracle line exactly. (A3) both weapon argmaxes take the FIRST maximum in
/// DRAWN_SKILLS scan order — <see cref="FirstArgmax"/> replaces only on
/// strictly-greater, matching Python's <c>max()</c> tie rule.</para>
/// </summary>
public static class PlayerGenPass2
{
    // ========================================================================
    // FROZEN CONSTANTS — transcribed verbatim from the locked oracle (its names,
    // its comments abbreviated, its line numbers cited). The fixture's constants
    // echo is the tripwire on these transcriptions.
    // ========================================================================

    // --- Dial 1: 60/40 orientation lean (oracle :69-70) — Phase-2 draw shape ---
    public const double ORI_MEAN = 0.446;  // Beta mean; P(o<0.5) ~= 0.60 (perimeter share)
    public const double ORI_CONC = 4.5;    // Beta concentration ("hybrid density")

    // --- Dial 2: perimeter Height cliff (oracle :77-85) ---
    public const double HT_ORI_MID        = 0.54;  // o at which the ceiling is halfway up
    public const double HT_ORI_STEEP      = 15.0;  // steepness of the knee
    public const double HT_MU_PERIM       = 53.0;  // perimeter Height location (~5'11"/6'0")
    public const double HT_MU_POST        = 72.0;  // post Height location (~6'8")
    public const double HT_SIGMA_UP_PERIM = 7.6;   // perimeter upper-tail sigma -> the CLIFF
    public const double HT_SIGMA_UP_POST  = 6.0;   // post upper-tail sigma
    public const double HT_SCALE_DOWN     = 7.0;   // lower-tail scale, both orientations
    public const double HT_MIN            = 40.0;
    public const double HT_MAX            = 99.0;

    // --- Dial 3: size -> athleticism (oracle :88-97) ---
    public const double ATH_HEIGHT_CENTER = 60.0;
    public static readonly Dictionary<string, double> SIZE_COEF = new(StringComparer.Ordinal)
    {   // rating-points per Height-point away from center
        ["Strength"] = +0.42, ["Speed"] = -0.22, ["Quickness"] = -0.22, ["FirstStep"] = -0.22,
        ["Vertical"] = -0.02, ["Endurance"] = -0.06, ["Hustle"] = 0.00,
    };
    public static readonly Dictionary<string, double> ATH_SIGMA = new(StringComparer.Ordinal)
    {   // per-attribute spread (Phase-2 draw shape); burst=7 IS the freak-tail width
        ["Strength"] = 6.0, ["Speed"] = 7.0, ["Quickness"] = 7.0, ["FirstStep"] = 7.0,
        ["Vertical"] = 8.0, ["Endurance"] = 6.0, ["Hustle"] = 8.0,
    };
    public const double ATH_BASE_LO = 25.0;  // maps athletic-quality a in [0,1] -> center rating
    public const double ATH_BASE_HI = 85.0;

    // --- Dial 4: runway skew along the bend (oracle :100-106) ---
    public const double ARR_PERIM     = 0.72;  // mean arrival for pure perimeter (near-ready)
    public const double ARR_POST      = 0.42;  // mean arrival for pure post (raw project)
    public const double ARR_SIGMA     = 0.18;  // arrival spread (Phase-2 draw shape)
    public const double E_MIN         = 0.15;  // minimum expression
    public const double EXPR_BASELINE = 14.0;  // rating a raw skill decays toward
    public const double AGE_ARR_SPAN  = 4.0;   // age = 18 + AGE_ARR_SPAN*arrival + noise (PLACEHOLDER, S42.1)
    public const double AGE_NOISE     = 0.95;  // (Phase-2 draw shape)

    // --- Recruiting line + post-pathway height-access curve (oracle :116-134) ---
    public const double R_LINE   = 17.0;   // recruiting-line threshold on RScore
    public const double HF_LO    = 0.20;   // floor: very short interior rating cashes almost nothing
    public const double HF_HI    = 1.45;   // ceiling: extreme height amplifies a REAL interior tool
    public const double HF_RANGE = 1.25;   // HF_HI - HF_LO
    public const double HF_STEEP = 0.13;   // logistic steepness
    public const double HF_MID   = 59.0;   // inflection height (~6'2)
    public const double PERIM_OW = 0.45;   // perim_weight = 1.00 - PERIM_OW * o
    public const double POST_OW  = 0.45;   // post_weight  = (1-POST_OW) + POST_OW * o
    public const double LOW_TAPER_FLOOR = 0.10;  // sub-6'0 interior taper floor
    public const double LOW_TAPER_TOP   = 51.0;  // height (~6'0) at/above which the taper is released

    // --- FROZEN draw shapes (oracle :141-143) — Phase-2 samplers consume these ---
    public const double SKILL_Q_A = 2.3;
    public const double SKILL_Q_B = 2.7;
    public const double SPEC_A    = 2.0;
    public const double SPEC_B    = 2.0;
    public const double ATHQ_A    = 2.2;
    public const double ATHQ_B    = 2.2;

    // --- FROZEN latent-skill construction (oracle :146-176) ---
    public const double MISMATCH_STRENGTH   = 0.85;  // opposite-axis suppression strength
    public const double SKILL_NOISE         = 0.12;  // per-skill idiosyncrasy sigma (Phase-2 draw shape)
    public const double WEAPON_BUMP         = 0.62;  // weapon lift at s=1
    public const double SUPPORT_DRAIN       = 0.42;  // non-weapon drain at s=1
    public const double WEAPON_MISMATCH_MAX = 0.30;  // weapon-eligibility mismatch cap
    public static readonly string[] WEAPON_EXCLUDE = { "BasketballIQ", "Discipline", "HelpDefense" };
    // S42.1 weapon-census offsets — added to base[k] INSIDE THE ARGMAX ONLY (oracle :168-173);
    // base[k] and all downstream card math are untouched.
    public static readonly Dictionary<string, double> WEAPON_CENSUS_OFFSET = new(StringComparer.Ordinal)
    {
        ["Close"] = +0.013, ["Mid"] = -0.030, ["Outside"] = -0.018, ["Finishing"] = +0.018, ["FoulDrawing"] = -0.011,
        ["BallHandling"] = -0.006, ["Passing"] = -0.012, ["Playmaking"] = -0.005, ["SelfCreation"] = -0.008,
        ["PostMoves"] = +0.032, ["OffBallMovement"] = -0.017, ["Screening"] = +0.023, ["PerimeterDefense"] = -0.005,
        ["PostDefense"] = +0.027, ["RimProtection"] = +0.025, ["Steals"] = -0.010, ["OffBallDefense"] = -0.014,
    };
    public const double RATING_LO  = 18.0;  // t=0 -> ~18
    public const double RATING_SPAN = 70.0; // t=1 -> ~88
    public const int    HOLE_FLOOR = 8;     // rating-DOMAIN lower bound, applied at draw time (NOT a repair floor)

    // --- FreeThrow derivation (oracle :180-188) ---
    public const double FT_CENTER      = 66.0;
    public const double FT_OUT_SPAN    = 10.0;
    public const double FT_OUT_SCALE   = 25.0;
    public const double FT_HEIGHT_COEF = 6.0;
    public const double FT_MIN         = 25.0;
    public const double FT_MAX         = 95.0;
    public const double FT_SIGMA       = 9.0;   // the ONE per-player idiosyncrasy sigma (Phase-2 draw shape)

    // --- Perimeter<->post axis of each drawn skill (oracle :208-214) ---
    public static readonly Dictionary<string, double> PAXIS = new(StringComparer.Ordinal)
    {
        ["Close"] = +0.30, ["Mid"] = 0.00, ["Outside"] = -0.15, ["Finishing"] = +0.40,
        ["FoulDrawing"] = +0.10, ["BallHandling"] = -0.70, ["Passing"] = -0.30, ["Playmaking"] = -0.60,
        ["SelfCreation"] = -0.60, ["PostMoves"] = +0.90, ["OffBallMovement"] = -0.20, ["Screening"] = +0.60,
        ["PerimeterDefense"] = -0.70, ["PostDefense"] = +0.80, ["RimProtection"] = +0.80, ["Steals"] = -0.40,
        ["HelpDefense"] = 0.00, ["OffBallDefense"] = -0.20, ["BasketballIQ"] = 0.00, ["Discipline"] = 0.00,
    };

    // ========================================================================
    // ATTRIBUTE TAXONOMY — the 33-key contract (oracle :193-205)
    // ========================================================================
    public static readonly string[] SIZE_KEYS =
        { "Height", "Wingspan", "Weight", "OffensiveRebounding", "DefensiveRebounding" };
    public static readonly string[] ATH_KEYS =
        { "Strength", "Speed", "Quickness", "FirstStep", "Vertical", "Endurance", "Hustle" };
    public static readonly string[] SKILL_KEYS =
    {
        "Close", "Mid", "Outside", "Finishing", "FreeThrow", "FoulDrawing",
        "BallHandling", "Passing", "Playmaking", "SelfCreation", "PostMoves",
        "OffBallMovement", "Screening", "PerimeterDefense", "PostDefense",
        "RimProtection", "Steals", "HelpDefense", "OffBallDefense",
        "BasketballIQ", "Discipline",
    };
    public static readonly string[] ALL_KEYS = BuildAllKeys();   // 5 + 7 + 21 = 33
    public static readonly string[] DRAWN_SKILLS = BuildDrawnSkills();  // SKILL_KEYS minus FreeThrow = 20

    private static string[] BuildAllKeys()
    {
        var all = new string[SIZE_KEYS.Length + ATH_KEYS.Length + SKILL_KEYS.Length];
        SIZE_KEYS.CopyTo(all, 0);
        ATH_KEYS.CopyTo(all, SIZE_KEYS.Length);
        SKILL_KEYS.CopyTo(all, SIZE_KEYS.Length + ATH_KEYS.Length);
        return all;
    }

    private static string[] BuildDrawnSkills()
    {
        var drawn = new List<string>(SKILL_KEYS.Length - 1);
        foreach (var k in SKILL_KEYS)
            if (k != "FreeThrow")
                drawn.Add(k);
        return drawn.ToArray();
    }

    // ========================================================================
    // Primitive helpers — the two port traps, centralized
    // ========================================================================

    /// <summary>The oracle's <c>clamp</c> (oracle :240), mirrored verbatim rather than
    /// <see cref="Math.Clamp"/> so the boundary semantics are self-evidently identical.</summary>
    public static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);

    /// <summary>Python's <c>round()</c>: HALF-TO-EVEN (banker's rounding). The ONLY legal
    /// float→int rounding in this port — never <c>(int)(x+0.5)</c>, which is round-half-up
    /// and silently disagrees on exact halves (A1).</summary>
    public static double RoundHalfEven(double x) => Math.Round(x, MidpointRounding.ToEven);

    /// <summary>The contractual argmax tie rule (A3, fixture <c>weapon_selection_contract</c>):
    /// scan in list order, replace the incumbent only on STRICTLY-greater — the FIRST
    /// maximum wins, matching Python's <c>max()</c>. A <c>&gt;=</c> here (or LINQ
    /// <c>MaxBy</c>) selects the LAST max and flips the identity on any tie.</summary>
    public static string FirstArgmax(IReadOnlyList<string> keys, Func<string, double> score)
    {
        var bestK = keys[0];
        var bestV = score(keys[0]);
        for (var i = 1; i < keys.Count; i++)
        {
            var v = score(keys[i]);
            if (v > bestV) { bestK = keys[i]; bestV = v; }
        }
        return bestK;
    }

    /// <summary>The orientation→Height shape (oracle :297-299): the logistic ceiling
    /// <c>oh</c>, the location <c>mu</c>, and the upper-tail sigma <c>sigma_up</c>, all
    /// pure functions of <c>o</c> and frozen constants. S44 extraction — lifted verbatim
    /// out of <see cref="BuildFromDraws"/> so the Phase-2 live draw loop (which needs
    /// <c>sigma_up</c> BEFORE drawing height noise) shares the exact same expressions.
    /// Draws nothing; changes no numerical output (Phase 59 re-proves it every run).</summary>
    public static (double Oh, double Mu, double SigmaUp) ComputeHeightShape(double o)
    {
        var oh      = 1.0 / (1.0 + Math.Exp(-HT_ORI_STEEP * (o - HT_ORI_MID)));
        var mu      = HT_MU_PERIM + oh * (HT_MU_POST - HT_MU_PERIM);
        var sigmaUp = HT_SIGMA_UP_PERIM + oh * (HT_SIGMA_UP_POST - HT_SIGMA_UP_PERIM);
        return (oh, mu, sigmaUp);
    }

    // ========================================================================
    // THE TRANSFORMS — one player from recorded draws (oracle generate_player,
    // :280-421, restructured only in that every draw arrives as a parameter)
    // ========================================================================

    /// <summary>Rebuild one player's full deterministic state from raw draws — the exact
    /// factoring of the oracle's <c>generate_player(r)</c> with the RNG removed, and the
    /// executable twin of <c>tools/gen_pass2_replay_check.py</c>'s <c>replay_player</c>.</summary>
    public static Pass2Result BuildFromDraws(Pass2Draws d)
    {
        double o = d.O, q = d.Q, a = d.A, s = d.S;

        // ---- orientation axis (oracle :291) ----
        var oaxis = 2.0 * o - 1.0;

        // ---- 2. orientation -> Height: logistic ceiling (oracle :297-311) ----
        // S44 pure extraction: the three shape lines live in ComputeHeightShape so the
        // Phase-2 live loop (which needs sigma_up BEFORE drawing the height noise) and
        // this transform can never drift. Same expressions, same order — Phase 59
        // re-proves this bit-for-bit against the fixture on every harness run.
        var (oh, mu, sigmaUp) = ComputeHeightShape(o);
        var branch  = d.HeightBranchSelectorRaw < 0.5 ? "upper_gauss" : "lower_exp";
        var hRaw    = branch == "upper_gauss" ? mu + Math.Abs(d.HeightNoiseRaw) : mu - d.HeightNoiseRaw;
        // Height is round-AFTER-clamp — the one site with this order (oracle :311, A2).
        // NOTE: the high clamp (Height == 99) is NOT exercised by the committed fixture on the
        // canonical seed (recorded NONE in its header); a future seed's fixture is the instrument
        // that would cover it. Do not synthesize a fake row for it.
        var height = (int)RoundHalfEven(Clamp(hRaw, HT_MIN, HT_MAX));

        // ---- 3. LATENT skill base from (o, q) + recorded noise (oracle :323-330) ----
        var bas = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var k in DRAWN_SKILLS)
        {
            var mismatch = Math.Max(0.0, -oaxis * PAXIS[k]);   // opposite-axis suppression 0..1
            var supp = MISMATCH_STRENGTH * mismatch;
            bas[k] = q - supp + d.SkillNoise[k];
        }

        // ---- weapon eligibility + the two argmaxes (oracle :337-343, S42.1 rule) ----
        var eligible = new List<string>();
        foreach (var k in DRAWN_SKILLS)
            if (Array.IndexOf(WEAPON_EXCLUDE, k) < 0 && Math.Max(0.0, -oaxis * PAXIS[k]) < WEAPON_MISMATCH_MAX)
                eligible.Add(k);
        // The empty-pool fallback mirrors the oracle but is UNREACHABLE: Mid (PAXIS 0.0,
        // not excluded) is always eligible — the fixture asserts non-emptiness per player.
        IReadOnlyList<string> pool = eligible.Count > 0 ? eligible : DRAWN_SKILLS;
        var weaponRaw = FirstArgmax(pool, k => bas[k]);
        var weapon = FirstArgmax(
            pool, k => bas[k] + (WEAPON_CENSUS_OFFSET.TryGetValue(k, out var off) ? off : 0.0));

        // ---- 4. size + athletic card, bypasses expression (oracle :348-373) ----
        var wingspan = (int)Clamp(RoundHalfEven(height + d.WingspanNoise), HT_MIN, 99.0);
        var athCenter = ATH_BASE_LO + a * (ATH_BASE_HI - ATH_BASE_LO);
        var athRaw = new Dictionary<string, double>(StringComparer.Ordinal);
        var ath = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in ATH_KEYS)
        {
            var raw = athCenter + SIZE_COEF[k] * (height - ATH_HEIGHT_CENTER) + d.AthNoise[k];
            athRaw[k] = raw;
            ath[k] = (int)Clamp(RoundHalfEven(raw), 8.0, 99.0);
        }
        var weight = (int)Clamp(RoundHalfEven(30 + 0.40 * height + 0.30 * ath["Strength"] + d.WeightNoise), 20.0, 99.0);
        var postBonus = 8.0 * o;
        var oreb = (int)Clamp(RoundHalfEven(20 + 0.34 * height + 0.14 * ath["Strength"] + postBonus + d.OrebNoise), 8.0, 99.0);
        var dreb = (int)Clamp(RoundHalfEven(22 + 0.36 * height + 0.18 * ath["Strength"] + postBonus + d.DrebNoise), 8.0, 99.0);

        // ---- 5. arrival stage + expression (oracle :376-380) ----
        var arrMean = ARR_PERIM - o * (ARR_PERIM - ARR_POST);
        var arrival = Clamp(d.ArrivalDrawRaw, 0.0, 1.0);
        var e = E_MIN + arrival * (1.0 - E_MIN);

        // ---- latent / current / FT / runway (oracle build_skill_state, :258-274) ----
        var latent = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in DRAWN_SKILLS)
        {
            var t = bas[k] + (k == weapon ? s * WEAPON_BUMP : -s * SUPPORT_DRAIN);
            latent[k] = (int)Clamp(RoundHalfEven(RATING_LO + t * RATING_SPAN), HOLE_FLOOR, 99.0);
        }
        var current = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in DRAWN_SKILLS)
        {
            var latentK = latent[k];
            current[k] = latentK <= EXPR_BASELINE
                ? latentK
                : (int)RoundHalfEven(EXPR_BASELINE + e * (latentK - EXPR_BASELINE));
        }
        // FreeThrow: the ONE shared ft_idio feeds BOTH derivations (S42.1 ruling) —
        // a trait, not a second development axis.
        latent["FreeThrow"] = DeriveFt(latent["Outside"], d.FtIdio, height);
        current["FreeThrow"] = DeriveFt(current["Outside"], d.FtIdio, height);
        var runway = new Dictionary<string, int>(StringComparer.Ordinal);
        var runwayTotal = 0;
        foreach (var k in SKILL_KEYS)
        {
            runway[k] = latent[k] - current[k];
            runwayTotal += runway[k];
        }

        // ---- 7. class / age — PLACEHOLDER (S42.1 ruling: replayed as output, NOT ported
        // as spec; arrival is the ruled mechanism, these labels are decoration on it.
        // The season layer owns the real population-structure question.) (oracle :392-401)
        var age = (int)Clamp(RoundHalfEven(18 + AGE_ARR_SPAN * arrival + d.AgeNoiseRaw), 17.0, 23.0);
        var cls = age <= 18 ? "Fr" : (age == 19 ? "So" : (age <= 21 ? "Jr" : "Sr"));

        // ---- assemble the 33-key card (current) (oracle :404-408) ----
        var card = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Height"] = height, ["Wingspan"] = wingspan, ["Weight"] = weight,
            ["OffensiveRebounding"] = oreb, ["DefensiveRebounding"] = dreb,
        };
        foreach (var kv in ath) card[kv.Key] = kv.Value;
        foreach (var k in SKILL_KEYS) card[k] = current[k];

        // ---- recruiting line, from the just-built card (oracle rscore_parts, :420-457) ----
        var (rscore, which, parts) = ComputeRscoreParts(current, card, height, o);

        return new Pass2Result
        {
            Oaxis = oaxis, Oh = oh, Mu = mu, SigmaUp = sigmaUp,
            HeightBranch = branch, HRaw = hRaw, Height = height,
            Base = bas, Eligible = eligible, WeaponRaw = weaponRaw, Weapon = weapon,
            AthCenter = athCenter, AthRaw = athRaw, Ath = ath,
            Wingspan = wingspan, Weight = weight,
            OffensiveRebounding = oreb, DefensiveRebounding = dreb,
            ArrMean = arrMean, Arrival = arrival, E = e,
            Latent = latent, Current = current, Runway = runway, RunwayTotal = runwayTotal,
            Age = age, Cls = cls, Card = card,
            Rscore = rscore, RscoreWhich = which, RscoreParts = parts,
        };
    }

    /// <summary>FreeThrow derivation (oracle <c>derive_ft</c>, :249-256): tanh on Outside,
    /// height penalty, plus the player's ONE persistent idiosyncrasy. Round-then-clamp.</summary>
    public static int DeriveFt(int outside, double ftIdio, int height)
    {
        var val = FT_CENTER + FT_OUT_SPAN * Math.Tanh((outside - 50.0) / FT_OUT_SCALE)
                - FT_HEIGHT_COEF * ((height - 55.0) / 40.0) + ftIdio;
        return (int)Clamp(RoundHalfEven(val), FT_MIN, FT_MAX);
    }

    /// <summary>The recruiting line (oracle <c>rscore_parts</c>, :420-457): value of the
    /// player's best viable PATHWAY to minutes, orientation-weighted before the max.
    /// Expressions transcribed operand-for-operand — evaluation order preserved so plain
    /// IEEE-754 arithmetic lands bit-identical to the oracle.</summary>
    public static (double Rscore, string Which, Dictionary<string, double> Parts) ComputeRscoreParts(
        Dictionary<string, int> c, Dictionary<string, int> cd, int height, double o)
    {
        var ath = (cd["Strength"] + cd["Speed"] + cd["Quickness"] + cd["FirstStep"] + cd["Vertical"]) / 5.0;
        // PERIMETER pathway: entry tool (handle/shot; Mid is access-gated), support, defense.
        var access       = Math.Max(c["BallHandling"], Math.Max(c["OffBallMovement"], c["Outside"])) / 99.0;
        var midEff       = c["Mid"] * Math.Min(1.0, access / 0.45);
        var entryP       = Math.Max(Math.Max(c["Outside"], c["BallHandling"]), midEff);
        var perimSupport = (c["Passing"] + c["Playmaking"] + c["SelfCreation"] + c["OffBallMovement"]) / 4.0;
        var perimDef     = (double)Math.Max(c["PerimeterDefense"], Math.Max(c["Steals"], c["OffBallDefense"]));
        var perimVal = Math.Max(0.0, entryP - 20) * (0.55 + 0.30 * perimSupport / 99 + 0.15 * perimDef / 99) + 0.14 * ath;
        // POST pathway: interior SKILL cashed through the continuous height-ACCESS logistic.
        var postSkill    = (double)Math.Max(c["RimProtection"],
                               Math.Max(c["PostMoves"], Math.Max(c["Close"], Math.Max(c["Finishing"], c["PostDefense"]))));
        var postSupport  = (c["Screening"] + c["PostDefense"] + c["RimProtection"]) / 3.0;
        var heightFactor = Clamp(HF_LO + HF_RANGE / (1.0 + Math.Exp(-HF_STEEP * (height - HF_MID))), HF_LO, HF_HI);
        var skillVal     = Math.Max(0.0, postSkill - 24) * (0.60 + 0.40 * postSupport / 99) * heightFactor;
        var glass        = (cd["OffensiveRebounding"] + cd["DefensiveRebounding"]) / 2.0;
        var rebVal       = glass * 0.16 * Math.Min(1.0, postSkill / 45.0);
        var lowTaper     = Clamp((height - 40.0) / (LOW_TAPER_TOP - 40.0), LOW_TAPER_FLOOR, 1.0);
        var postVal = (skillVal + rebVal + 0.10 * ath * Math.Min(1.0, heightFactor)) * lowTaper;
        // ORIENTATION-WEIGHTED SELECTION before the max — continuous, not a role table.
        var perimW = 1.00 - PERIM_OW * o;
        var postW  = (1.0 - POST_OW) + POST_OW * o;
        double wperim = perimW * perimVal, wpost = postW * postVal;
        var total = Math.Max(wperim, wpost);
        var which = wperim >= wpost ? "perim" : "post";
        var parts = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["rscore"] = total,
            ["entry_p"] = entryP, ["perim_support"] = perimSupport, ["perim_def"] = perimDef,
            ["perim_val"] = perimVal, ["post_skill"] = postSkill, ["glass"] = glass,
            ["reb_val"] = rebVal, ["skill_val"] = skillVal, ["post_support"] = postSupport,
            ["post_val"] = postVal, ["ath"] = ath, ["wperim"] = wperim, ["wpost"] = wpost, ["o"] = o,
        };
        return (total, which, parts);
    }

    // ========================================================================
    // Constants echo — the tripwire surface. The parity gate asserts the fixture's
    // 57-entry constants echo equals this map BEFORE running any replay (the S42.2
    // guardrail); the oracle source stays the single canonical home of the values.
    // ========================================================================
    public static Dictionary<string, object> ConstantsEcho() => new(StringComparer.Ordinal)
    {
        ["ORI_MEAN"] = ORI_MEAN, ["ORI_CONC"] = ORI_CONC,
        ["SKILL_Q_A"] = SKILL_Q_A, ["SKILL_Q_B"] = SKILL_Q_B,
        ["SPEC_A"] = SPEC_A, ["SPEC_B"] = SPEC_B, ["ATHQ_A"] = ATHQ_A, ["ATHQ_B"] = ATHQ_B,
        ["HT_ORI_MID"] = HT_ORI_MID, ["HT_ORI_STEEP"] = HT_ORI_STEEP,
        ["HT_MU_PERIM"] = HT_MU_PERIM, ["HT_MU_POST"] = HT_MU_POST,
        ["HT_SIGMA_UP_PERIM"] = HT_SIGMA_UP_PERIM, ["HT_SIGMA_UP_POST"] = HT_SIGMA_UP_POST,
        ["HT_SCALE_DOWN"] = HT_SCALE_DOWN, ["HT_MIN"] = HT_MIN, ["HT_MAX"] = HT_MAX,
        ["ATH_HEIGHT_CENTER"] = ATH_HEIGHT_CENTER, ["SIZE_COEF"] = SIZE_COEF, ["ATH_SIGMA"] = ATH_SIGMA,
        ["ATH_BASE_LO"] = ATH_BASE_LO, ["ATH_BASE_HI"] = ATH_BASE_HI,
        ["ARR_PERIM"] = ARR_PERIM, ["ARR_POST"] = ARR_POST, ["ARR_SIGMA"] = ARR_SIGMA,
        ["E_MIN"] = E_MIN, ["EXPR_BASELINE"] = EXPR_BASELINE,
        ["AGE_ARR_SPAN"] = AGE_ARR_SPAN, ["AGE_NOISE"] = AGE_NOISE,
        ["MISMATCH_STRENGTH"] = MISMATCH_STRENGTH, ["SKILL_NOISE"] = SKILL_NOISE,
        ["WEAPON_BUMP"] = WEAPON_BUMP, ["SUPPORT_DRAIN"] = SUPPORT_DRAIN,
        ["WEAPON_MISMATCH_MAX"] = WEAPON_MISMATCH_MAX,
        ["WEAPON_EXCLUDE"] = WEAPON_EXCLUDE, ["WEAPON_CENSUS_OFFSET"] = WEAPON_CENSUS_OFFSET,
        ["RATING_LO"] = RATING_LO, ["RATING_SPAN"] = RATING_SPAN, ["HOLE_FLOOR"] = (double)HOLE_FLOOR,
        ["FT_CENTER"] = FT_CENTER, ["FT_OUT_SPAN"] = FT_OUT_SPAN, ["FT_OUT_SCALE"] = FT_OUT_SCALE,
        ["FT_HEIGHT_COEF"] = FT_HEIGHT_COEF, ["FT_MIN"] = FT_MIN, ["FT_MAX"] = FT_MAX,
        ["FT_SIGMA"] = FT_SIGMA, ["PAXIS"] = PAXIS, ["R_LINE"] = R_LINE,
        ["HF_LO"] = HF_LO, ["HF_HI"] = HF_HI, ["HF_RANGE"] = HF_RANGE,
        ["HF_STEEP"] = HF_STEEP, ["HF_MID"] = HF_MID,
        ["PERIM_OW"] = PERIM_OW, ["POST_OW"] = POST_OW,
        ["LOW_TAPER_FLOOR"] = LOW_TAPER_FLOOR, ["LOW_TAPER_TOP"] = LOW_TAPER_TOP,
    };
}

/// <summary>The raw recorded draws for one player — the fixture's <c>draws</c> block
/// (16 entries; the 40 RNG slots grouped: skill_noise carries 20, ath_noise carries 7).
/// In Phase 2 the live generator fills this same shape from <see cref="IRng"/>.</summary>
public sealed class Pass2Draws
{
    public double O, Q, A, S;
    public double HeightBranchSelectorRaw;
    public double HeightNoiseRaw;   // upper branch: pre-abs gauss; lower branch: the expovariate value
    public Dictionary<string, double> SkillNoise = new(StringComparer.Ordinal);   // per DRAWN_SKILLS
    public double WingspanNoise;    // mean 4.0 INCLUDED in the drawn value
    public Dictionary<string, double> AthNoise = new(StringComparer.Ordinal);     // per ATH_KEYS
    public double WeightNoise, OrebNoise, DrebNoise;
    public double ArrivalDrawRaw;   // pre-clamp — the clamp destroys this when it binds
    public double FtIdio;           // the ONE shared FT idiosyncrasy
    public double AgeNoiseRaw;      // placeholder machinery (S42.1)
}

/// <summary>Every checkpoint, the 33-key card, latent/current/runway, and the recruiting
/// line for one player — the shape the fixture records. Deliberately NOT
/// <see cref="Player"/>: the Player data-shape decision is Phase 2's, made against the
/// ~46k-at-scale population question.</summary>
public sealed class Pass2Result
{
    public double Oaxis, Oh, Mu, SigmaUp, HRaw;
    public string HeightBranch = "";
    public int Height;
    public Dictionary<string, double> Base = new(StringComparer.Ordinal);
    public List<string> Eligible = new();
    public string WeaponRaw = "", Weapon = "";
    public double AthCenter;
    public Dictionary<string, double> AthRaw = new(StringComparer.Ordinal);
    public Dictionary<string, int> Ath = new(StringComparer.Ordinal);
    public int Wingspan, Weight, OffensiveRebounding, DefensiveRebounding;
    public double ArrMean, Arrival, E;
    public Dictionary<string, int> Latent = new(StringComparer.Ordinal);
    public Dictionary<string, int> Current = new(StringComparer.Ordinal);
    public Dictionary<string, int> Runway = new(StringComparer.Ordinal);
    public int RunwayTotal;
    public int Age;                 // PLACEHOLDER-output (S42.1 ruling)
    public string Cls = "";         // PLACEHOLDER-output (S42.1 ruling)
    public Dictionary<string, int> Card = new(StringComparer.Ordinal);
    public double Rscore;
    public string RscoreWhich = "";
    public Dictionary<string, double> RscoreParts = new(StringComparer.Ordinal);
}
