namespace Content.Server._Onyx.Telecommunications.Components;

/// <summary>
/// Tracks the alignment and short-term traffic load of a telecommunications node.
/// </summary>
[RegisterComponent]
public sealed partial class TelecomCalibrationComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Calibration = 100f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DecayPerHour = 12f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Bandwidth = 10f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DamageLossMultiplier = 0.15f;

    [ViewVariables(VVAccess.ReadOnly)]
    public float CurrentLoad;

    [ViewVariables(VVAccess.ReadOnly)]
    public float TelemetryLoad;
}
