namespace Content.Server._Onyx.Telecommunications.Components;

/// <summary>
/// Processes radio traffic accepted by a telecommunications receiver.
/// </summary>
[RegisterComponent]
public sealed partial class TelecomProcessorComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MaxBuses = 2;
}
