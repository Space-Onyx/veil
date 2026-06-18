namespace Content.Server._Onyx.Telecommunications.Components;

[RegisterComponent]
public sealed partial class TelecomRouterComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MaxBuses = 1;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Standalone;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> DisabledChannels = new();

    [ViewVariables(VVAccess.ReadOnly)]
    public bool Sabotaged;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool ResetArmed;
}
