namespace Charm.Engine;

/// <summary>
/// Coaching configuration placeholder (Phase 9 scaffold). Exists so the
/// <see cref="CoachingPull.Apply"/> seam can take a real type from day one
/// instead of faking it with <c>Player?</c> (which would be a semantic lie
/// and would force every call site to change when coaching actually lands).
/// v1 has no fields; the future coaching session adds them (system
/// preferences, tempo lean, intentional-fouling thresholds, etc.) and
/// replaces the body of <see cref="CoachingPull.Apply"/>. The call sites in
/// <see cref="RollGGenerator"/> never change.
/// </summary>
public sealed record CoachProfile;
