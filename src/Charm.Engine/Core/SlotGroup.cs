namespace Charm.Engine;

/// <summary>Five on-court offense slots plus an unattributed bucket for one
/// possession-level counting stat. All ints default to 0. Replaces six scalar
/// fields per family in RoutingOutcome and PossessionRecord.</summary>
public readonly record struct SlotGroup(
    int S1 = 0, int S2 = 0, int S3 = 0, int S4 = 0, int S5 = 0,
    int Unattr = 0)
{
    public int Total => S1 + S2 + S3 + S4 + S5 + Unattr;
    public int this[int slot] => slot switch
    {
        1 => S1, 2 => S2, 3 => S3, 4 => S4, 5 => S5, _ => Unattr
    };
    public SlotGroup WithSlot(int slot, int delta) => slot switch
    {
        1 => this with { S1 = S1 + delta }, 2 => this with { S2 = S2 + delta },
        3 => this with { S3 = S3 + delta }, 4 => this with { S4 = S4 + delta },
        5 => this with { S5 = S5 + delta }, _ => this with { Unattr = Unattr + delta }
    };
}
