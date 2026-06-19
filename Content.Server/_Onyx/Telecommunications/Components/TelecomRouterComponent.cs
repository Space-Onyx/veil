namespace Content.Server._Onyx.Telecommunications.Components;

[RegisterComponent]
public sealed partial class TelecomRouterComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MaxBuses = 1;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Standalone;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float QualityDropExponent = 2f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float QualityDropChanceMultiplier = 0.65f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float CongestionDropThreshold = 1f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float CongestionDropChanceMultiplier = 0.35f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MaxCongestionDropChance = 0.65f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MaxTotalDropChance = 0.8f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float GarbleQualityThreshold = 0.95f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float GarbleQualityChanceMultiplier = 0.25f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float GarbleLoadThreshold = 0.75f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float GarbleLoadChanceMultiplier = 0.12f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MaxGarbleChance = 0.7f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DegradedQualityThreshold = 0.9f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DegradedLoadThreshold = 0.75f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float BaseLatencyMilliseconds = 40f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float QualityLatencyMultiplier = 800f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float LoadLatencyThreshold = 0.5f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float LoadLatencyMultiplier = 1200f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> DisabledChannels = new();

    [ViewVariables(VVAccess.ReadOnly)]
    public bool Sabotaged;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool ResetArmed;
}
