namespace Charm.Engine;

/// <summary>
/// Whether — and with what profile — the defending team is applying full-court
/// pressure on THIS specific possession. Stamped once per possession by
/// <see cref="Resolver.RunPossession"/> BEFORE invoking
/// <see cref="IRollAPieGenerator.Generate"/>, so the generator reads a finished
/// decision rather than rolling for it itself (the generator is a pure pie
/// builder; the RNG draw lives upstream in the Resolver).
///
/// <para><b>None</b> — the defense is not pressing this possession. Roll A uses
/// the flat config baseline, identical to a pre-Phase-14 possession.</para>
///
/// <para><b>Standard</b> — the defense is pressing in standard full-court mode.
/// Roll A applies <see cref="MatchupConfig.StandardLift"/> to all three
/// disruption arms and then bends the turnover arm through the three-gap matchup
/// (skill, athleticism, size) scaled by
/// <see cref="MatchupConfig.StandardGate"/>. DefFoul and OffFoul receive only the
/// fixed lift. CleanEntry absorbs the complement; JumpBall is pinned flat.</para>
///
/// <para><b>Desperate</b> — reserved for the late-game module (a score-and-clock-
/// aware end-game press: high-steal / high-back-end / high-foul). The late-game
/// module does not yet exist. This value MUST NOT be produced by any live code
/// path in Phase 15. <see cref="RollAGenerator"/> throws
/// <see cref="InvalidOperationException"/> if it ever receives this value, so a
/// mistaken stamp fails loud rather than silently falling back to baseline.</para>
/// </summary>
public enum PressMode
{
    /// <summary>No press. Generator uses the flat config baseline — identical to
    /// the pre-Phase-14 output.</summary>
    None,

    /// <summary>Standard full-court press. Generator applies
    /// <see cref="MatchupConfig.StandardLift"/> + <see cref="MatchupConfig.StandardGate"/>
    /// × (skill·skillShift + ath·athShift + size·sizeShift) for the turnover arm,
    /// and <see cref="MatchupConfig.StandardLift"/> only for DefFoul and OffFoul,
    /// all within the Standard ceiling/floor band.</summary>
    Standard,

    /// <summary>Reserved for the late-game desperate-foul / run-out-the-clock press.
    /// Never produced by any live code path in Phase 15. The generator throws
    /// <see cref="InvalidOperationException"/> if it receives this value.</summary>
    Desperate,
}
